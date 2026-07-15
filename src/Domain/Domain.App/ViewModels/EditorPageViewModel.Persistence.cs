using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private void Request3DStatePersistence(TimeSpan? delay = null)
        => MarkDocumentDirty();

    private Project3DState CaptureProjectState()
        => new(
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
            StepTopology: _stepTopology);

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
