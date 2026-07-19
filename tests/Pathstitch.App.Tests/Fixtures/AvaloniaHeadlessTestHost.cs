using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Pathstitch.App.Pages;

namespace Pathstitch.App.Tests.Fixtures;

public sealed class HeadlessTestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public static class AvaloniaHeadlessTestHost
{
    private static readonly ManualResetEventSlim Ready = new(false);
    private static readonly object Sync = new();
    private static Exception? _startupFailure;
    private static bool _started;

    public static void EnsureInitialized()
    {
        lock (Sync)
        {
            if (!_started)
            {
                _started = true;
                var uiThread = new Thread(RunUiThread)
                {
                    IsBackground = true,
                    Name = "Pathstitch Avalonia headless UI",
                };
                uiThread.Start();
            }
        }
        if (!Ready.Wait(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("Avalonia headless UI thread did not start.");
        if (_startupFailure is not null)
            throw new InvalidOperationException("Avalonia headless UI startup failed.", _startupFailure);
    }

    private static void RunUiThread()
    {
        try
        {
            AppBuilder.Configure<HeadlessTestApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            EditorViewportWebView.DisableNativeChildForAutomationTests = true;
            Ready.Set();
            Dispatcher.UIThread.MainLoop(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _startupFailure = exception;
            Ready.Set();
        }
    }
}
