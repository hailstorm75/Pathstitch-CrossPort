using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Pathstitch.App.Services;

internal sealed class DesktopFileOpenRouter(Action<Exception>? errorHandler = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<string[]> _pendingBatches = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource _idle = CompletedSource();
    private Func<IReadOnlyList<string>, Task>? _handler;
    private bool _isPumping;
    private bool _isDisposed;

    public void Enqueue(IReadOnlyList<string>? filePaths)
    {
        var normalized = App.NormalizeStartupFileArguments(filePaths);
        if (normalized.Length == 0)
            return;

        var startPump = false;
        lock (_gate)
        {
            if (_isDisposed)
                return;

            var batch = normalized.Where(_pendingPaths.Add).ToArray();
            if (batch.Length == 0)
                return;

            if (_idle.Task.IsCompleted)
                _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingBatches.Enqueue(batch);
            startPump = TryStartPumpLocked();
        }

        if (startPump)
            _ = PumpAsync();
    }

    public void SetReady(Func<IReadOnlyList<string>, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var startPump = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _handler = handler;
            startPump = TryStartPumpLocked();
        }

        if (startPump)
            _ = PumpAsync();
    }

    internal Task WhenIdleAsync()
    {
        lock (_gate)
            return _idle.Task;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _handler = null;
            _pendingBatches.Clear();
            _pendingPaths.Clear();
            _idle.TrySetResult();
        }
    }

    private bool TryStartPumpLocked()
    {
        if (_isPumping || _handler is null || _pendingBatches.Count == 0)
            return false;

        _isPumping = true;
        return true;
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            string[] batch;
            Func<IReadOnlyList<string>, Task> handler;
            lock (_gate)
            {
                if (_isDisposed || _handler is null || _pendingBatches.Count == 0)
                {
                    _isPumping = false;
                    if (_pendingBatches.Count == 0)
                        _idle.TrySetResult();
                    return;
                }

                batch = _pendingBatches.Dequeue();
                handler = _handler;
            }

            try
            {
                await handler(batch).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                try
                {
                    errorHandler?.Invoke(ex);
                }
                catch
                {
                    // Reporting must not strand later file-open activations.
                }
            }
            finally
            {
                lock (_gate)
                {
                    foreach (var path in batch)
                        _pendingPaths.Remove(path);
                }
            }
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
