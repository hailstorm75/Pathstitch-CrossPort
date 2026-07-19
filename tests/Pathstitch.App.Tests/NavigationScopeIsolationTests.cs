using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Models;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Pathstitch.App.Services;
using Pathstitch.App.Tests.Fixtures;

namespace Pathstitch.App.Tests;

public sealed class NavigationScopeIsolationTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public async Task CoordinatorApprovedWindowClose_DoesNotPromptDocumentAgain()
    {
        var messenger = new StrongReferenceMessenger();
        using var services = Services(messenger, "approved", new TestWindowContext());
        var shell = await _ui.RunAsync(() => new MainWindowShell(services));
        var prompts = 0;
        var recipient = new object();
        messenger.Register<PreviewApplicationClosingMessage>(recipient, (_, message) =>
        {
            prompts++;
            message.Reply(Completed(false));
        });
        await _ui.RunAsync(shell.Show);

        await _ui.RunAsync(() =>
        {
            shell.ApproveApplicationClose();
            shell.Close();
        });
        await WaitUntilAsync(() => !shell.IsVisible);

        Assert.Equal(0, prompts);
    }

    [Fact]
    public async Task TwoShells_KeepNavigationAndClosePreviewInsideOwningScope()
    {
        var messengerA = new StrongReferenceMessenger();
        var messengerB = new StrongReferenceMessenger();
        var contextA = new TestWindowContext();
        var contextB = new TestWindowContext();
        using var servicesA = Services(messengerA, "A", contextA);
        using var servicesB = Services(messengerB, "B", contextB);
        var shellA = await _ui.RunAsync(() => new MainWindowShell(servicesA));
        var shellB = await _ui.RunAsync(() => new MainWindowShell(servicesB));
        Assert.Same(shellA, contextA.Owner);
        Assert.Same(shellB, contextB.Owner);
        await _ui.RunAsync(() =>
        {
            shellA.Show();
            shellB.Show();
        });
        var closeA = 0;
        var closeB = 0;
        var recipientA = new object();
        var recipientB = new object();
        messengerA.Register<PreviewApplicationClosingMessage>(recipientA, (_, message) =>
        {
            closeA++;
            message.Reply(Completed(false));
        });
        messengerB.Register<PreviewApplicationClosingMessage>(recipientB, (_, message) =>
        {
            closeB++;
            message.Reply(Completed(false));
        });

        await _ui.RunAsync(() => messengerA.Send(
            new NavigationChangeRequestMessage("test")));
        await shellA.WhenNavigationIdleAsync();

        var pageA = await _ui.RunAsync(() => shellA.CurrentPageViewModel);
        var pageB = await _ui.RunAsync(() => shellB.CurrentPageViewModel);
        Assert.Equal("A", Assert.IsType<TestPageViewModel>(pageA).Name);
        Assert.Null(pageB);

        await _ui.RunAsync(shellA.Close);
        await WaitUntilAsync(() => !shellA.IsVisible);

        Assert.Equal(1, closeA);
        Assert.Equal(0, closeB);
        Assert.True(await _ui.RunAsync(() => shellB.IsVisible));

        await _ui.RunAsync(shellB.Close);
        await WaitUntilAsync(() => !shellB.IsVisible);
    }

    [Fact]
    public async Task DocumentWindowManager_OpensIndependentSessionScopes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-window-scopes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var dispositionPrompt = new RecordingDispositionPrompt();
            using var root = new ServiceCollection()
                .AddSingleton<IProjectFileDialogService, NullProjectFileDialogService>()
                .AddSingleton(new RecentProjectsService(Path.Combine(directory, "recent.json")))
                .AddScoped<ProjectSessionService>()
                .AddScoped<IDocumentWindowContext, DocumentWindowContext>()
                .AddSingleton<IProjectOpenDispositionPromptService>(dispositionPrompt)
                .AddScoped<IMessenger>(_ => new StrongReferenceMessenger())
                .AddScoped<INavigationManager>(services => new TestNavigationManager(
                    services.GetRequiredService<ProjectSessionService>().CurrentSession?.ProjectName ?? "missing"))
                .BuildServiceProvider();
            using var manager = new DesktopDocumentWindowManager(root);

            await manager.OpenDocumentAsync(ProjectLaunchRequest.ForProject(Session(directory, "A")));
            await manager.OpenDocumentAsync(ProjectLaunchRequest.ForProject(Session(directory, "B")));

            Assert.Equal(2, manager.DocumentCount);
            var windows = manager.DocumentWindows.ToArray();
            var pageA = await _ui.RunAsync(() => windows[0].CurrentPageViewModel);
            var pageB = await _ui.RunAsync(() => windows[1].CurrentPageViewModel);
            Assert.Equal("A", Assert.IsType<TestPageViewModel>(pageA).Name);
            Assert.Equal("B", Assert.IsType<TestPageViewModel>(pageB).Name);

            await _ui.RunAsync(windows[0].Close);
            await WaitUntilAsync(() => !windows[0].IsVisible);
            Assert.Equal(1, manager.DocumentCount);
            Assert.True(await _ui.RunAsync(() => windows[1].IsVisible));

            await _ui.RunAsync(windows[1].Close);
            await WaitUntilAsync(() => !windows[1].IsVisible);

            var firstProject = Path.Combine(directory, "first.stch");
            var secondProject = Path.Combine(directory, "second.stch");
            await File.WriteAllTextAsync(firstProject, "{\"projectName\":\"First\",\"templateId\":\"blank-project\"}");
            await File.WriteAllTextAsync(secondProject, "{\"projectName\":\"Second\",\"templateId\":\"blank-project\"}");

            await manager.OpenFilesAsync([firstProject, secondProject]);

            Assert.Equal(2, manager.DocumentCount);
            var activatedWindows = manager.DocumentWindows.ToArray();
            var activatedA = await _ui.RunAsync(() => activatedWindows[0].CurrentPageViewModel);
            var activatedB = await _ui.RunAsync(() => activatedWindows[1].CurrentPageViewModel);
            Assert.Equal("First", Assert.IsType<TestPageViewModel>(activatedA).Name);
            Assert.Equal("Second", Assert.IsType<TestPageViewModel>(activatedB).Name);
            Assert.Equal(1, dispositionPrompt.CallCount);
            await _ui.RunAsync(activatedWindows[0].Close);
            await _ui.RunAsync(activatedWindows[1].Close);
            await WaitUntilAsync(() => manager.DocumentCount == 0);

            var mixedProject = Path.Combine(directory, "mixed.stch");
            var mixedDrawing = Path.Combine(directory, "drawing.dxf");
            await File.WriteAllTextAsync(mixedProject, "{\"projectName\":\"Mixed\",\"templateId\":\"blank-project\"}");
            await File.WriteAllTextAsync(mixedDrawing, "0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n");

            await manager.OpenDocumentAsync(ProjectLaunchRequest.ForProject(Session(directory, "Owner")));
            await manager.OpenFilesAsync([mixedProject, mixedDrawing]);

            Assert.Equal(3, manager.DocumentCount);
            var mixedWindows = manager.DocumentWindows.ToArray();
            var mixedProjectPage = await _ui.RunAsync(() => mixedWindows[1].CurrentPageViewModel);
            var mixedImportPage = await _ui.RunAsync(() => mixedWindows[2].CurrentPageViewModel);
            Assert.Equal("Mixed", Assert.IsType<TestPageViewModel>(mixedProjectPage).Name);
            Assert.NotEqual("Mixed", Assert.IsType<TestPageViewModel>(mixedImportPage).Name);
            Assert.Equal(2, dispositionPrompt.CallCount);
            foreach (var window in mixedWindows)
                await _ui.RunAsync(window.Close);
            await WaitUntilAsync(() => manager.DocumentCount == 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DocumentWindowManager_BackgroundClosePreservesActiveDocument()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-active-window-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var root = new ServiceCollection()
                .AddSingleton<IProjectFileDialogService, NullProjectFileDialogService>()
                .AddSingleton(new RecentProjectsService(Path.Combine(directory, "recent.json")))
                .AddScoped<ProjectSessionService>()
                .AddScoped<IDocumentWindowContext, DocumentWindowContext>()
                .AddSingleton<IProjectOpenDispositionPromptService>(new RecordingDispositionPrompt())
                .AddScoped<IMessenger>(_ => new StrongReferenceMessenger())
                .AddScoped<INavigationManager>(services => new TestNavigationManager(
                    services.GetRequiredService<ProjectSessionService>().CurrentSession?.ProjectName ?? "missing"))
                .BuildServiceProvider();
            using var manager = new DesktopDocumentWindowManager(root);

            await manager.OpenDocumentAsync(ProjectLaunchRequest.ForProject(Session(directory, "A")));
            await manager.OpenDocumentAsync(ProjectLaunchRequest.ForProject(Session(directory, "B")));
            await manager.OpenDocumentAsync(ProjectLaunchRequest.ForProject(Session(directory, "C")));
            var windows = manager.DocumentWindows.ToArray();

            var privateInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var documentHandles = Assert.IsAssignableFrom<System.Collections.IList>(
                typeof(DesktopDocumentWindowManager).GetField("_documents", privateInstance)!.GetValue(manager));
            var backgroundHandle = documentHandles[0];
            var activeHandle = documentHandles[1];
            var activeDocumentField = typeof(DesktopDocumentWindowManager)
                .GetField("_activeDocument", privateInstance)!;
            activeDocumentField.SetValue(manager, activeHandle);

            typeof(DesktopDocumentWindowManager)
                .GetMethod("OnDocumentClosed", privateInstance)!
                .Invoke(manager, [backgroundHandle]);
            Assert.Equal(2, manager.DocumentCount);

            Assert.Same(activeHandle, activeDocumentField.GetValue(manager));

            await _ui.RunAsync(windows[0].Close);
            await _ui.RunAsync(windows[1].Close);
            await _ui.RunAsync(windows[2].Close);
            await WaitUntilAsync(() => manager.DocumentCount == 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceProvider Services(
        IMessenger messenger,
        string name,
        IDocumentWindowContext? windowContext = null)
        => new ServiceCollection()
            .AddSingleton(messenger)
            .AddSingleton<IDocumentWindowContext>(windowContext ?? new TestWindowContext())
            .AddSingleton<INavigationManager>(new TestNavigationManager(name))
            .BuildServiceProvider();

    private static TaskCompletionSource<bool> Completed(bool value)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult(value);
        return completion;
    }

    private static ProjectSession Session(string directory, string name)
        => new(
            Guid.NewGuid(),
            name,
            Path.Combine(directory, $"{name}.stch"),
            new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
            ProjectSessionOrigin.Created,
            DateTimeOffset.UtcNow);

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await _ui.RunAsync(condition))
                return;
            await Task.Delay(10);
        }
        Assert.True(await _ui.RunAsync(condition));
    }

    private sealed class TestNavigationManager(string name) : INavigationManager
    {
        public bool TryGetNavigablePage(
            string identifier,
            [NotNullWhen(true)] out INavigablePageView? page)
        {
            page = new TestPageView(name);
            return true;
        }
    }

    private sealed class TestPageView(string name) : UserControl, INavigablePageView
    {
        public INavigablePageViewModel ViewModel { get; } = new TestPageViewModel(name);

        public ValueTask<bool> ConfigureParametersAsync(
            IReadOnlyDictionary<string, object> parameters,
            CancellationToken cancellationToken)
            => ViewModel.ConfigureParametersAsync(parameters, cancellationToken);

        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class TestPageViewModel(string name) : INavigablePageViewModel
    {
        public string Name { get; } = name;

        public ValueTask<bool> ConfigureParametersAsync(
            IReadOnlyDictionary<string, object> parameters,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(true);

        public ValueTask LoadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class NullProjectFileDialogService : IProjectFileDialogService
    {
        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickNewProjectFileAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class TestWindowContext : IDocumentWindowContext
    {
        public Window? Owner { get; set; }
    }

    private sealed class RecordingDispositionPrompt : IProjectOpenDispositionPromptService
    {
        public int CallCount { get; private set; }

        public Task<ProjectOpenDisposition> PromptAsync(
            string incomingProjectName,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(ProjectOpenDisposition.NewWindow);
        }
    }
}
