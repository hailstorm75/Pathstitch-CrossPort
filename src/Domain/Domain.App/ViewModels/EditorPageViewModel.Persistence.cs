using Domain.App.Models;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private void Request3DStatePersistence(TimeSpan? delay = null)
        => MarkDocumentDirty();

    private async Task<Project3DState> CaptureProjectStateAsync(CancellationToken cancellationToken)
    {
        var batchWorkspaceState = await _batchWorkspace
            .CaptureStateAsync(ProjectSession?.ProjectFilePath, cancellationToken)
            .ConfigureAwait(true);
        var twoDWorkspaceState = BuildPersistedTwoDWorkspaceState();
        byte[]? previewImageData = null;
        if (_projectPreviewRenderer is not null)
        {
            try
            {
                previewImageData = await _projectPreviewRenderer
                    .RenderAsync(BuildProjectPreviewDocument(twoDWorkspaceState), cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Could not render the project preview thumbnail.");
            }
        }
        return new(
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
            TwoDWorkspaceState: twoDWorkspaceState,
            ThreeDWorkspaceState: _threeDWorkspace.CaptureState(),
            StepTopology: _stepTopology,
            BatchWorkspaceState: batchWorkspaceState,
            ActivityLog: ActivityLog,
            LearnModeEnabled: LearnModeEnabled,
            PreviewImageData: previewImageData);
    }

    private Editor2DExportDocument? BuildProjectPreviewDocument(Editor2DWorkspaceState twoDWorkspaceState)
    {
        if (!twoDWorkspaceState.IsInitialized)
            return null;

        var document = twoDWorkspaceState.Document;
        var geometryLayers = (twoDWorkspaceState.Layers ?? [])
            .Where(layer => layer.Kind == Editor2DLayerKind.Geometry)
            .ToArray();
        var assignedPathIds = geometryLayers
            .SelectMany(layer => layer.PathIds)
            .ToHashSet(StringComparer.Ordinal);
        var visiblePathIds = geometryLayers
            .Where(layer => layer.IsVisible)
            .SelectMany(layer => layer.PathIds)
            .ToHashSet(StringComparer.Ordinal);
        var visiblePaths = document.Paths
            .Where(path => !assignedPathIds.Contains(path.Id) || visiblePathIds.Contains(path.Id))
            .ToArray();
        var visibleDocument = CreateUpdatedTwoDDocument(document, visiblePaths);
        return BuildExportDocument(visibleDocument);
    }

    private async Task PersistDocumentAsync(Project3DState state, CancellationToken cancellationToken)
    {
        if (ProjectSession is null)
            throw new InvalidOperationException("Cannot save without an active project session.");

        if (state.TwoDWorkspaceState?.Document is { } twoDDocument
            && !string.IsNullOrWhiteSpace(state.GeneratedOutputPath))
        {
            await _editorOutputPreviewService
                .SavePreviewDocumentAsync(twoDDocument, state.GeneratedOutputPath, cancellationToken)
                .ConfigureAwait(true);
        }

        await _project3DStateService
            .SaveAsync(ProjectSession.ProjectFilePath, state, cancellationToken)
            .ConfigureAwait(true);
    }
}
