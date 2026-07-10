using Domain.App.Models;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private void Request3DStatePersistence(TimeSpan? delay = null)
    {
        if (ProjectSession is null)
            return;

        var nextCancellationTokenSource = new CancellationTokenSource();
        var previousCancellationTokenSource = _persist3DStateCancellationTokenSource;
        _persist3DStateCancellationTokenSource = nextCancellationTokenSource;
        previousCancellationTokenSource?.Cancel();
        previousCancellationTokenSource?.Dispose();

        _ = Persist3DStateAsync(nextCancellationTokenSource, delay ?? TimeSpan.Zero);
    }

    private async Task Persist3DStateAsync(CancellationTokenSource cancellationTokenSource, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationTokenSource.Token).ConfigureAwait(true);

            if (ProjectSession is null)
                return;

            await _project3DStateService.SaveAsync(
                ProjectSession.ProjectFilePath,
                new Project3DState(
                    ViewportJson: ViewportJsonContent,
                    Bodies: Bodies,
                    BodyOffsets: BodyOffsets,
                    SourceModelPath: _sourceModelPath,
                    GeneratedOutputPath: LastGeneratedOutputPath,
                    GeneratedOutputDataBase64: _generatedOutputDataBase64,
                    GeneratedOutputContext: GeneratedOutputContext,
                    UnfoldWorkspaceState: BuildPersistedUnfoldWorkspaceState(),
                    ProjectionWorkspaceState: BuildPersistedProjectionWorkspaceState(),
                    WorkspaceState: BuildPersistedEditorWorkspaceState(),
                    TwoDWorkspaceState: BuildPersistedTwoDWorkspaceState(),
                    ThreeDWorkspaceState: _threeDWorkspace.CaptureState(),
                    StepTopology: _stepTopology),
                cancellationTokenSource.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A newer workspace mutation superseded this persistence request.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist 3D project state for {ProjectPath}", ProjectSession?.ProjectFilePath);
        }
        finally
        {
            if (ReferenceEquals(_persist3DStateCancellationTokenSource, cancellationTokenSource))
                _persist3DStateCancellationTokenSource = null;

            cancellationTokenSource.Dispose();
        }
    }
}
