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
        var twoDWorkspaceState = GetPersistableTwoDWorkspaceState();
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

    private async Task<Project3DState> PrepareProjectStateForPersistenceAsync(
        Project3DState state,
        CancellationToken cancellationToken)
    {
        if (state.TwoDWorkspaceState?.Document is { } twoDDocument
            && !string.IsNullOrWhiteSpace(state.GeneratedOutputPath))
        {
            var preserveSource = CanPreserveGeneratedDxfSource(
                twoDDocument,
                requiredDxfVersion: null,
                requireFullExportOptions: false);
            if (preserveSource)
            {
                await _editorOutputPreviewService
                    .CopyDxfPreservingStructureAsync(
                        _preservableGeneratedDxfSourceBytes!,
                        state.GeneratedOutputPath,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
            else
            {
                var exportDocument = BuildFullExportDocument(twoDDocument);
                var dxfOptions = new Editor2DExportOptions(
                    DxfVersion: _preservableGeneratedDxfVersion ?? Editor2DExportOptions.Defaults.DxfVersion);
                var mergeResult = EditorDxfMergeResult.NotSupported;
                if (CanAttemptGeneratedDxfMerge(
                        twoDDocument,
                        requiredDxfVersion: null,
                        requireFullExportOptions: false))
                {
                    mergeResult = await _editorOutputPreviewService
                        .TrySaveMergedDxfDocumentAsync(
                            _preservableGeneratedDxfSourceBytes!,
                            exportDocument,
                            state.GeneratedOutputPath,
                            dxfOptions,
                            cancellationToken)
                        .ConfigureAwait(true);
                }
                if (mergeResult.Succeeded && mergeResult.ProvenanceByPathId.Count > 0
                    && state.TwoDWorkspaceState is { } workspaceState)
                {
                    var persistedPaths = twoDDocument.Paths.Select(path =>
                    {
                        var canonicalPath = mergeResult.CanonicalPathsByPathId?.TryGetValue(
                            path.Id,
                            out var resolvedCanonicalPath) == true
                            ? resolvedCanonicalPath
                            : path;
                        return mergeResult.ProvenanceByPathId.TryGetValue(path.Id, out var provenance)
                            ? canonicalPath with
                            {
                                SourceEntityHandle = provenance.Handle,
                                SourceLayerName = provenance.LayerName,
                            }
                            : canonicalPath;
                    }).ToArray();
                    state = state with
                    {
                        GeneratedOutputDataBase64 = mergeResult.OutputData is { Length: > 0 }
                            ? Convert.ToBase64String(mergeResult.OutputData)
                            : state.GeneratedOutputDataBase64,
                        TwoDWorkspaceState = workspaceState with
                        {
                            Document = CreateUpdatedTwoDDocument(twoDDocument, persistedPaths),
                        },
                    };
                }
                if (!mergeResult.Succeeded)
                {
                    await _editorOutputPreviewService
                        .SaveExportDocumentAsync(
                            exportDocument,
                            state.GeneratedOutputPath,
                            dxfOptions,
                            cancellationToken)
                        .ConfigureAwait(true);
                    state = state with
                    {
                        GeneratedOutputDataBase64 = Convert.ToBase64String(
                            await File.ReadAllBytesAsync(state.GeneratedOutputPath, cancellationToken).ConfigureAwait(true)),
                    };
                }
            }
        }

        return state;
    }

    private async Task<Project3DState> PersistDocumentAsync(
        Project3DState state,
        CancellationToken cancellationToken)
    {
        if (ProjectSession is null)
            throw new InvalidOperationException("Cannot save without an active project session.");

        var preparedState = await PrepareProjectStateForPersistenceAsync(state, cancellationToken).ConfigureAwait(true);
        await _project3DStateService
            .SaveAsync(ProjectSession.ProjectFilePath, preparedState, cancellationToken)
            .ConfigureAwait(true);
        return preparedState;
    }

    private void PromotePersistedProjectState(Project3DState state)
    {
        if (state.TwoDWorkspaceState is { } workspaceState)
        {
            var livePathsById = _twoDWorkspace.State.Document.Paths.ToDictionary(
                path => path.Id,
                StringComparer.Ordinal);
            var persistedPathChanged = livePathsById.Count != workspaceState.Document.Paths.Count
                || workspaceState.Document.Paths.Any(path =>
                    !livePathsById.TryGetValue(path.Id, out var livePath)
                    || !EqualityComparer<Editor2DPreviewPath>.Default.Equals(livePath, path));
            if (persistedPathChanged)
            {
                using var dirtyTrackingSuppression = SuppressDocumentDirtyTracking();
                _twoDWorkspace.Apply(workspaceState, recordHistory: false, rebuildMeasurementCaches: false);
            }
        }
        _generatedOutputDataBase64 = state.GeneratedOutputDataBase64;
        CaptureGeneratedDxfPreservationBaseline(
            state.TwoDWorkspaceState?.Document,
            state.GeneratedOutputPath,
            state.GeneratedOutputDataBase64);
    }
}
