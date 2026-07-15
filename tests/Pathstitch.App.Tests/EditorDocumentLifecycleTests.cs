using System.Reflection;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;

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
