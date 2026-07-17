using System.Reflection;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class EditorDocumentLifecycleTests
{
    [Fact]
    public async Task LoadedSession_StartsClean_AndMutationBecomesDirtyWithoutAutosave()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();

        Assert.False(fixture.ViewModel.IsDirty);

        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        await Task.Delay(250);

        Assert.True(fixture.ViewModel.IsDirty);
        var persisted = await new Project3DStateService().LoadAsync(fixture.ProjectPath);
        Assert.Null(persisted.WorkspaceState);
    }

    [Fact]
    public async Task SaveDocument_WritesCurrentStateAndEstablishesCleanRevision()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);

        await fixture.ViewModel.SaveDocumentAsync();

        Assert.False(fixture.ViewModel.IsDirty);
        var persisted = await new Project3DStateService().LoadAsync(fixture.ProjectPath);
        Assert.Equal(EditorMode.Batch, persisted.WorkspaceState?.ActiveEditorMode);
    }

    [Fact]
    public async Task SaveAndReopen_RestoresEmbeddedBatchInputsSettingsAndSelection()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var directory = Path.GetDirectoryName(fixture.ProjectPath)!;
        var dxfPath = Path.Combine(directory, "first.dxf");
        var svgPath = Path.Combine(directory, "second.svg");
        var outputDirectory = Path.Combine(directory, "batch-output");
        await File.WriteAllTextAsync(
            dxfPath,
            "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n10\n0\nENDSEC\n0\nEOF\n");
        await File.WriteAllTextAsync(
            svgPath,
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0 L10 0\"/></svg>");
        Assert.Equal(2, fixture.ViewModel.BatchWorkspace.AddFiles([dxfPath, svgPath]));
        fixture.ViewModel.BatchWorkspace.Items[1].IsSelected = false;
        fixture.ViewModel.BatchWorkspace.ContinueOnError = false;
        fixture.ViewModel.BatchWorkspace.OutputDirectory = outputDirectory;
        fixture.ViewModel.BatchWorkspace.ExportSelectedOnly = true;
        fixture.ViewModel.BatchWorkspace.SelectedExportFormat = EditorBatchExportFormat.Dxf;
        fixture.ViewModel.BatchWorkspace.SelectedNamingOption = EditorBatchNamingOption.CustomIndex;
        fixture.ViewModel.BatchWorkspace.CustomExportName = "Saved Batch";
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        Assert.True(fixture.ViewModel.IsDirty);

        await fixture.ViewModel.SaveDocumentAsync();
        File.Delete(dxfPath);
        File.Delete(svgPath);

        var reopened = EditorPageViewModelModeTests.CreateViewModelForTests();
        try
        {
            var session = new ProjectSession(
                Guid.NewGuid(),
                "Lifecycle",
                fixture.ProjectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Opened,
                DateTimeOffset.UtcNow);
            Assert.True(await reopened.ConfigureParametersAsync(
                new Dictionary<string, object> { [EditorNavigationParameterKeys.ProjectSession] = session },
                CancellationToken.None));
            await ((INavigablePageViewModel)reopened).LoadAsync(CancellationToken.None);

            Assert.False(reopened.IsDirty);
            Assert.Equal(EditorMode.Batch, reopened.ActiveEditorMode);
            Assert.Equal(["first.dxf", "second.svg"], reopened.BatchWorkspace.Items.Select(item => item.FileName));
            Assert.All(reopened.BatchWorkspace.Items, item => Assert.True(File.Exists(item.FilePath)));
            Assert.DoesNotContain(reopened.BatchWorkspace.Items, item =>
                item.FilePath.Equals(dxfPath, StringComparison.OrdinalIgnoreCase)
                || item.FilePath.Equals(svgPath, StringComparison.OrdinalIgnoreCase));
            Assert.True(reopened.BatchWorkspace.Items[0].IsSelected);
            Assert.False(reopened.BatchWorkspace.Items[1].IsSelected);
            Assert.False(reopened.BatchWorkspace.ContinueOnError);
            Assert.True(reopened.BatchWorkspace.ExportSelectedOnly);
            Assert.Equal(outputDirectory, reopened.BatchWorkspace.OutputDirectory);
            Assert.Equal(EditorBatchNamingOption.CustomIndex, reopened.BatchWorkspace.SelectedNamingOption);
            Assert.Equal("Saved Batch", reopened.BatchWorkspace.CustomExportName);

            await reopened.BatchWorkspace.ExportDxfAsync(new DxfOutputPreviewService());

            Assert.True(File.Exists(reopened.BatchWorkspace.Items[0].OutputPath));
            Assert.Equal("Saved Batch_1.dxf", Path.GetFileName(reopened.BatchWorkspace.Items[0].OutputPath));
            Assert.Equal("Saved Batch", Path.GetFileName(Path.GetDirectoryName(reopened.BatchWorkspace.Items[0].OutputPath)));
            Assert.Null(reopened.BatchWorkspace.Items[1].OutputPath);
        }
        finally
        {
            reopened.Dispose();
        }
    }

    [Fact]
    public async Task NavigationPreview_CancelCancelsNavigationAndKeepsDirty()
    {
        var prompt = new StubUnsavedChangesPromptService(UnsavedChangesPromptResult.Cancel);
        await using var fixture = await LifecycleFixture.CreateAsync(prompt);
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);

        var cancelNavigation = await PreviewNavigationAsync(fixture.ViewModel);

        Assert.True(cancelNavigation);
        Assert.True(fixture.ViewModel.IsDirty);
        Assert.Equal(1, prompt.CallCount);
    }

    [Fact]
    public async Task NavigationPreview_DiscardAllowsNavigationButKeepsDirtyUntilPageLeaves()
    {
        var prompt = new StubUnsavedChangesPromptService(UnsavedChangesPromptResult.Discard);
        await using var fixture = await LifecycleFixture.CreateAsync(prompt);
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);

        var cancelNavigation = await PreviewNavigationAsync(fixture.ViewModel);

        Assert.False(cancelNavigation);
        Assert.True(fixture.ViewModel.IsDirty);
    }

    [Fact]
    public async Task ApplicationClosingPreview_SavePersistsAndAllowsShutdown()
    {
        var prompt = new StubUnsavedChangesPromptService(UnsavedChangesPromptResult.Save);
        await using var fixture = await LifecycleFixture.CreateAsync(prompt);
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);

        var cancelShutdown = await PreviewApplicationClosingAsync(fixture.ViewModel);

        Assert.False(cancelShutdown);
        Assert.False(fixture.ViewModel.IsDirty);
        var persisted = await new Project3DStateService().LoadAsync(fixture.ProjectPath);
        Assert.Equal(EditorMode.Batch, persisted.WorkspaceState?.ActiveEditorMode);
    }

    [Fact]
    public async Task SaveAs_RetargetsWithoutResetAndFutureSaveUsesNewProject()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-save-as-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "source.stch");
            var targetPath = Path.Combine(directory, "renamed.stch");
            await File.WriteAllTextAsync(sourcePath, "{\"projectName\":\"Source\",\"templateId\":\"blank\"}");
            await new Project3DStateService().SaveAsync(sourcePath, Project3DState.Empty);
            var dialog = new SaveAsDialogService(targetPath);
            var sessionService = new ProjectSessionService(
                dialog,
                new RecentProjectsService(Path.Combine(directory, "recent.json")));
            var session = Assert.IsType<ProjectSession>(await sessionService.OpenRecentProjectAsync(sourcePath));
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
                projectFileDialogService: dialog,
                projectSessionService: sessionService);
            Assert.True(await viewModel.ConfigureParametersAsync(
                new Dictionary<string, object> { [EditorNavigationParameterKeys.ProjectSession] = session },
                CancellationToken.None));
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);
            await viewModel.SetActiveEditorModeAsync(EditorMode.Batch);

            await viewModel.SaveDocumentAsAsync();

            Assert.False(viewModel.IsDirty);
            Assert.Equal(targetPath, viewModel.ProjectSession!.ProjectFilePath);
            Assert.Equal("renamed", viewModel.ProjectName);
            Assert.Equal(targetPath, viewModel.ProjectSubtitle);
            Assert.Equal(targetPath, sessionService.CurrentSession!.ProjectFilePath);
            Assert.Equal(EditorMode.Batch, (await new Project3DStateService().LoadAsync(targetPath)).WorkspaceState!.ActiveEditorMode);
            Assert.Null((await new Project3DStateService().LoadAsync(sourcePath)).WorkspaceState);

            await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
            await viewModel.SaveDocumentAsync();
            Assert.Equal(EditorMode.TwoD, (await new Project3DStateService().LoadAsync(targetPath)).WorkspaceState!.ActiveEditorMode);
            Assert.Null((await new Project3DStateService().LoadAsync(sourcePath)).WorkspaceState);
            viewModel.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAs_CancelLeavesSessionAndDirtyRevisionUntouched()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await fixture.ViewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        var originalSession = fixture.ViewModel.ProjectSession;

        await fixture.ViewModel.SaveDocumentAsAsync();

        Assert.Same(originalSession, fixture.ViewModel.ProjectSession);
        Assert.True(fixture.ViewModel.IsDirty);
    }

    private static async Task<bool> PreviewNavigationAsync(EditorPageViewModel viewModel)
    {
        var message = new BeforeNavigationChangeMessage(
            new NavigationChangeRequestMessage(NavigationAddressBook.HomePage));
        InvokeHandler(viewModel, "BeforePageLeave", message);
        var response = await message.Response;
        return await response.Task;
    }

    private static async Task<bool> PreviewApplicationClosingAsync(EditorPageViewModel viewModel)
    {
        var message = new PreviewApplicationClosingMessage();
        InvokeHandler(viewModel, "OnPreviewApplicationClosing", message);
        var response = await message.Response;
        return await response.Task;
    }

    private static void InvokeHandler(EditorPageViewModel viewModel, string methodName, object message)
    {
        var method = typeof(EditorPageViewModel).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(EditorPageViewModel).FullName, methodName);
        method.Invoke(viewModel, [viewModel, message]);
    }

    private sealed class StubUnsavedChangesPromptService(UnsavedChangesPromptResult result)
        : IUnsavedChangesPromptService
    {
        public int CallCount { get; private set; }

        public Task<UnsavedChangesPromptResult> PromptToSaveAsync(
            string documentName,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class SaveAsDialogService(string? saveAsPath) : IProjectFileDialogService
    {
        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickProjectSaveAsFileAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult(saveAsPath);
        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class LifecycleFixture : IAsyncDisposable
    {
        private readonly string _directory;

        private LifecycleFixture(string directory, string projectPath, EditorPageViewModel viewModel)
        {
            _directory = directory;
            ProjectPath = projectPath;
            ViewModel = viewModel;
        }

        public string ProjectPath { get; }

        public EditorPageViewModel ViewModel { get; }

        public static async Task<LifecycleFixture> CreateAsync(IUnsavedChangesPromptService? prompt = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-lifecycle-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var projectPath = Path.Combine(directory, "lifecycle.stch");
            await new Project3DStateService().SaveAsync(projectPath, Project3DState.Empty);

            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
                unsavedChangesPromptService: prompt);
            var session = new ProjectSession(
                Guid.NewGuid(),
                "Lifecycle",
                projectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Opened,
                DateTimeOffset.UtcNow);
            var configured = await viewModel.ConfigureParametersAsync(
                new Dictionary<string, object>
                {
                    [EditorNavigationParameterKeys.ProjectSession] = session,
                },
                CancellationToken.None);
            Assert.True(configured);
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);
            return new LifecycleFixture(directory, projectPath, viewModel);
        }

        public ValueTask DisposeAsync()
        {
            ViewModel.Dispose();
            Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
