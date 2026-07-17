using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;

namespace Pathstitch.App.Tests;

public sealed class EditorFileCommandTests
{
    [Fact]
    public async Task OpenProject_NewWindowDispositionPreservesCurrentDocument()
    {
        var windows = new RecordingDocumentWindowService();
        var disposition = new RecordingDispositionPrompt(ProjectOpenDisposition.NewWindow);
        await using var fixture = await Fixture.CreateAsync(
            UnsavedChangesPromptResult.Cancel,
            documentWindowService: windows,
            dispositionPrompt: disposition);
        var other = Path.Combine(fixture.Directory, "other-window.stch");
        await new Project3DStateService().SaveAsync(other, Project3DState.Empty);
        fixture.Dialog.OpenPath = other;

        await fixture.ViewModel.OpenProjectAsync(CancellationToken.None);

        Assert.Equal(1, disposition.CallCount);
        Assert.Equal(fixture.Session.SessionId, fixture.ViewModel.ProjectSession!.SessionId);
        Assert.Equal(fixture.Session.SessionId, fixture.SessionService.CurrentSession!.SessionId);
        Assert.Equal(Path.GetFullPath(other), Assert.Single(windows.Requests).Session.ProjectFilePath);
        Assert.Empty(fixture.NavigationRequests);
    }

    [Fact]
    public async Task OpenProject_CancelDispositionDoesNothing()
    {
        var windows = new RecordingDocumentWindowService();
        var disposition = new RecordingDispositionPrompt(ProjectOpenDisposition.Cancel);
        await using var fixture = await Fixture.CreateAsync(
            UnsavedChangesPromptResult.Discard,
            documentWindowService: windows,
            dispositionPrompt: disposition);
        var other = Path.Combine(fixture.Directory, "cancel.stch");
        await new Project3DStateService().SaveAsync(other, Project3DState.Empty);
        fixture.Dialog.OpenPath = other;

        await fixture.ViewModel.OpenProjectAsync(CancellationToken.None);

        Assert.Equal(1, disposition.CallCount);
        Assert.Empty(windows.Requests);
        Assert.Equal(0, fixture.Prompt.CallCount);
        Assert.Equal(fixture.Session.SessionId, fixture.SessionService.CurrentSession!.SessionId);
    }

    [Fact]
    public async Task NewProject_WithDocumentWindowServicePreservesDirtyCurrentDocument()
    {
        var windows = new RecordingDocumentWindowService();
        await using var fixture = await Fixture.CreateAsync(
            UnsavedChangesPromptResult.Cancel,
            documentWindowService: windows);
        fixture.Dialog.NewPath = Path.Combine(fixture.Directory, "new-window.stch");
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);

        await fixture.ViewModel.NewProjectAsync(CancellationToken.None);

        Assert.Equal(0, fixture.Prompt.CallCount);
        Assert.Equal(fixture.Session.SessionId, fixture.ViewModel.ProjectSession!.SessionId);
        Assert.Equal(fixture.Session.SessionId, fixture.SessionService.CurrentSession!.SessionId);
        Assert.Empty(fixture.NavigationRequests);
        Assert.NotEqual(fixture.Session.SessionId, Assert.Single(windows.Requests).Session.SessionId);
    }

    [Fact]
    public async Task CloseDocument_WithDocumentWindowServiceClosesOnlyOwningWindow()
    {
        var windows = new RecordingDocumentWindowService();
        await using var fixture = await Fixture.CreateAsync(
            UnsavedChangesPromptResult.Cancel,
            documentWindowService: windows);

        await fixture.ViewModel.CloseDocumentAsync();

        Assert.Equal(1, windows.CloseCount);
        Assert.Empty(fixture.NavigationRequests);
    }

    [Fact]
    public async Task NewProject_DiscardPromptsOnceAndPreapprovesExactNavigation()
    {
        await using var fixture = await Fixture.CreateAsync(UnsavedChangesPromptResult.Discard);
        fixture.Dialog.NewPath = Path.Combine(fixture.Directory, "new.stch");
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);

        await fixture.ViewModel.NewProjectAsync(CancellationToken.None);

        Assert.Equal(1, fixture.Prompt.CallCount);
        Assert.Equal(1, fixture.Dialog.NewCount);
        var request = Assert.Single(fixture.NavigationRequests);
        Assert.Equal(NavigationAddressBook.EditorPage, request.Value);
        Assert.NotEqual(fixture.Session.SessionId,
            Assert.IsType<ProjectSession>(request.Parameters[EditorNavigationParameterKeys.ProjectSession]).SessionId);
        Assert.False(await PreviewNavigationAsync(fixture.ViewModel, request));
        Assert.Equal(1, fixture.Prompt.CallCount);
    }

    [Fact]
    public async Task NewProject_CancelStopsBeforePickerAndOpenPickerCancelDoesNotPrompt()
    {
        await using var cancelled = await Fixture.CreateAsync(UnsavedChangesPromptResult.Cancel);
        cancelled.Dialog.NewPath = Path.Combine(cancelled.Directory, "new.stch");
        await cancelled.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);

        await cancelled.ViewModel.NewProjectAsync(CancellationToken.None);

        Assert.Equal(1, cancelled.Prompt.CallCount);
        Assert.Equal(0, cancelled.Dialog.NewCount);
        Assert.Empty(cancelled.NavigationRequests);

        await using var openCancelled = await Fixture.CreateAsync(UnsavedChangesPromptResult.Discard);
        await openCancelled.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        await openCancelled.ViewModel.OpenProjectAsync(CancellationToken.None);
        Assert.Equal(1, openCancelled.Dialog.OpenCount);
        Assert.Equal(0, openCancelled.Prompt.CallCount);
        Assert.Empty(openCancelled.NavigationRequests);
    }

    [Fact]
    public async Task OpenProject_PromptCancelDoesNotActivatePreparedSession()
    {
        await using var fixture = await Fixture.CreateAsync(UnsavedChangesPromptResult.Cancel);
        var other = Path.Combine(fixture.Directory, "other.stch");
        await new Project3DStateService().SaveAsync(other, Project3DState.Empty);
        fixture.Dialog.OpenPath = other;
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);

        await fixture.ViewModel.OpenProjectAsync(CancellationToken.None);

        Assert.Equal(1, fixture.Dialog.OpenCount);
        Assert.Equal(1, fixture.Prompt.CallCount);
        Assert.Equal(fixture.Session.SessionId, fixture.SessionService.CurrentSession!.SessionId);
        Assert.Empty(fixture.NavigationRequests);
    }

    [Fact]
    public async Task OpenProject_CorruptFilePreservesCurrentSessionAndEditorState()
    {
        await using var fixture = await Fixture.CreateAsync(UnsavedChangesPromptResult.Discard);
        var corrupt = Path.Combine(fixture.Directory, "corrupt.stch");
        await File.WriteAllTextAsync(corrupt, "not-json");
        fixture.Dialog.OpenPath = corrupt;
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        var existing = new Editor2DPreviewPath("existing", "LINE", [new(0, 0), new(5, 0)], false);
        fixture.ViewModel.TwoDDocument = Document(existing);
        var originalSessionId = fixture.ViewModel.ProjectSession!.SessionId;

        await fixture.ViewModel.OpenProjectAsync(CancellationToken.None);

        Assert.Equal(1, fixture.Dialog.OpenCount);
        Assert.Equal(0, fixture.Prompt.CallCount);
        Assert.Equal(originalSessionId, fixture.SessionService.CurrentSession!.SessionId);
        Assert.Equal(originalSessionId, fixture.ViewModel.ProjectSession.SessionId);
        Assert.Equal(existing, Assert.Single(fixture.ViewModel.TwoDDocument!.Paths));
        Assert.Empty(fixture.NavigationRequests);
        Assert.Equal("Project replacement failed", fixture.ViewModel.StatusText);
        Assert.Contains("corrupt.stch", fixture.ViewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportDrawing_AddsToCurrentSessionWithoutUnsavedPrompt()
    {
        var importedPath = Path.Combine(Path.GetTempPath(), $"import-{Guid.NewGuid():N}.dxf");
        await File.WriteAllTextAsync(importedPath, "DXF");
        try
        {
            var imported = Document(new Editor2DPreviewPath("imported", "LINE", [new(0, 0), new(2, 0)], false));
            await using var fixture = await Fixture.CreateAsync(
                UnsavedChangesPromptResult.Cancel,
                new MappingOutputService(importedPath, imported));
            fixture.Dialog.WorkspacePaths = [importedPath];
            await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
            var existing = new Editor2DPreviewPath("existing", "LINE", [new(0, 0), new(1, 0)], false);
            fixture.ViewModel.TwoDDocument = Document(existing);
            var sessionId = fixture.ViewModel.ProjectSession!.SessionId;

            await fixture.ViewModel.ImportFilesAsync(CancellationToken.None);

            Assert.Equal(0, fixture.Prompt.CallCount);
            Assert.Empty(fixture.NavigationRequests);
            Assert.Equal(sessionId, fixture.ViewModel.ProjectSession.SessionId);
            Assert.True(fixture.ViewModel.IsDirty);
            Assert.Contains(fixture.ViewModel.TwoDDocument!.Paths, path => path.Id == existing.Id);
            Assert.Equal(2, fixture.ViewModel.TwoDDocument.Paths.Count);
        }
        finally
        {
            File.Delete(importedPath);
        }
    }

    [Fact]
    public async Task ActivatedDrawing_ImportsIntoCurrentEditorWithoutUsingPicker()
    {
        var importedPath = Path.Combine(Path.GetTempPath(), $"activated-{Guid.NewGuid():N}.dxf");
        await File.WriteAllTextAsync(importedPath, "DXF");
        try
        {
            var imported = Document(new Editor2DPreviewPath("activated", "LINE", [new(0, 0), new(2, 0)], false));
            await using var fixture = await Fixture.CreateAsync(
                UnsavedChangesPromptResult.Cancel,
                new MappingOutputService(importedPath, imported));
            await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
            var sessionId = fixture.ViewModel.ProjectSession!.SessionId;

            await fixture.ViewModel.OpenActivatedFilesAsync([importedPath]);

            Assert.Equal(0, fixture.Dialog.WorkspaceCount);
            Assert.Equal(0, fixture.Prompt.CallCount);
            Assert.Equal(sessionId, fixture.ViewModel.ProjectSession.SessionId);
            Assert.True(fixture.ViewModel.IsDirty);
            Assert.Single(fixture.ViewModel.TwoDWorkspace.ImportGroups);
        }
        finally
        {
            File.Delete(importedPath);
        }
    }

    [Fact]
    public async Task ActivatedProject_RespectsUnsavedChangesCancellation()
    {
        await using var fixture = await Fixture.CreateAsync(UnsavedChangesPromptResult.Cancel);
        var other = Path.Combine(fixture.Directory, "activated.stch");
        await new Project3DStateService().SaveAsync(other, Project3DState.Empty);
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        var originalSessionId = fixture.ViewModel.ProjectSession!.SessionId;

        await fixture.ViewModel.OpenActivatedFilesAsync([other]);

        Assert.Equal(1, fixture.Prompt.CallCount);
        Assert.Equal(originalSessionId, fixture.ViewModel.ProjectSession.SessionId);
        Assert.Equal(originalSessionId, fixture.SessionService.CurrentSession!.SessionId);
        Assert.Empty(fixture.NavigationRequests);
    }

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
        => new(paths, new(0, 0, 10, 10), new Dictionary<string, int>(), []);

    private static async Task<bool> PreviewNavigationAsync(
        EditorPageViewModel viewModel,
        NavigationChangeRequestMessage request)
    {
        var message = new BeforeNavigationChangeMessage(request);
        var method = typeof(EditorPageViewModel).GetMethod(
            "BeforePageLeave",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(viewModel, [viewModel, message]);
        var response = await message.Response;
        return await response.Task;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly object _recipient = new();

        private Fixture(
            string directory,
            ProjectSession session,
            EditorPageViewModel viewModel,
            ProjectSessionService sessionService,
            RecordingDialog dialog,
            RecordingPrompt prompt)
        {
            Directory = directory;
            Session = session;
            ViewModel = viewModel;
            SessionService = sessionService;
            Dialog = dialog;
            Prompt = prompt;
            WeakReferenceMessenger.Default.Register<NavigationChangeRequestMessage>(
                _recipient,
                (_, message) => NavigationRequests.Add(message));
        }

        public string Directory { get; }
        public ProjectSession Session { get; }
        public EditorPageViewModel ViewModel { get; }
        public ProjectSessionService SessionService { get; }
        public RecordingDialog Dialog { get; }
        public RecordingPrompt Prompt { get; }
        public List<NavigationChangeRequestMessage> NavigationRequests { get; } = [];

        public static async Task<Fixture> CreateAsync(
            UnsavedChangesPromptResult promptResult,
            IEditorOutputPreviewService? outputService = null,
            IDocumentWindowService? documentWindowService = null,
            IProjectOpenDispositionPromptService? dispositionPrompt = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"editor-file-commands-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "source.stch");
            await new Project3DStateService().SaveAsync(source, Project3DState.Empty);
            var dialog = new RecordingDialog();
            var sessionService = new ProjectSessionService(
                dialog,
                new RecentProjectsService(Path.Combine(directory, "recent.json")));
            var session = Assert.IsType<ProjectSession>(await sessionService.OpenRecentProjectAsync(source));
            var prompt = new RecordingPrompt(promptResult);
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
                projectFileDialogService: dialog,
                outputPreviewService: outputService,
                unsavedChangesPromptService: prompt,
                projectSessionService: sessionService,
                documentWindowService: documentWindowService,
                projectOpenDispositionPromptService: dispositionPrompt);
            Assert.True(await viewModel.ConfigureParametersAsync(
                new Dictionary<string, object> { [EditorNavigationParameterKeys.ProjectSession] = session },
                CancellationToken.None));
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);
            return new(directory, session, viewModel, sessionService, dialog, prompt);
        }

        public ValueTask DisposeAsync()
        {
            WeakReferenceMessenger.Default.UnregisterAll(_recipient);
            ViewModel.Dispose();
            System.IO.Directory.Delete(Directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDocumentWindowService : IDocumentWindowService
    {
        public List<ProjectLaunchRequest> Requests { get; } = [];
        public int CloseCount { get; private set; }

        public Task OpenDocumentAsync(
            ProjectLaunchRequest launchRequest,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(launchRequest);
            return Task.CompletedTask;
        }

        public Task CloseCurrentDocumentAsync(CancellationToken cancellationToken = default)
        {
            CloseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPrompt(UnsavedChangesPromptResult result) : IUnsavedChangesPromptService
    {
        public int CallCount { get; private set; }
        public Task<UnsavedChangesPromptResult> PromptToSaveAsync(string documentName, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingDispositionPrompt(ProjectOpenDisposition result) : IProjectOpenDispositionPromptService
    {
        public int CallCount { get; private set; }

        public Task<ProjectOpenDisposition> PromptAsync(
            string incomingProjectName,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingDialog : IProjectFileDialogService
    {
        public string? NewPath { get; set; }
        public string? OpenPath { get; set; }
        public IReadOnlyList<string> WorkspacePaths { get; set; } = [];
        public int NewCount { get; private set; }
        public int OpenCount { get; private set; }
        public int WorkspaceCount { get; private set; }

        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default)
        {
            OpenCount++;
            return Task.FromResult(OpenPath);
        }
        public Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
        {
            NewCount++;
            return Task.FromResult(NewPath);
        }
        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default)
        {
            WorkspaceCount++;
            return Task.FromResult(WorkspacePaths);
        }
        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class MappingOutputService(string path, Editor2DPreviewDocument document) : IEditorOutputPreviewService
    {
        public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<Editor2DPreviewDocument?>(
                string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
                    ? document
                    : null);
        public Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default) => Task.FromResult<EditorGeneratedOutputSummary?>(null);
    }
}
