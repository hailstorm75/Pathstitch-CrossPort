using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pathstitch.App.Tests;

public sealed class HomeRecentProjectsLiveDiscoveryTests
{
    [Fact]
    public async Task LiveSnapshots_UpdateOrderAvailabilitySelectionAndStopOnDispose()
    {
        using var workspace = TestWorkspace.Create();
        var persistedPath = workspace.CreateProject("persisted.stch");
        var firstDiscoveredPath = workspace.CreateProject("first.stch");
        var secondDiscoveredPath = workspace.CreateProject("second.stch");
        var provider = new ControlledDiscoveryProvider();
        var recentService = new RecentProjectsService(provider, workspace.GetPath("recents.json"));
        recentService.RecordProject(CreateSession("Persisted", persistedPath));
        var sessionService = new ProjectSessionService(new StubProjectFileDialogService(), recentService);
        var viewModel = new HomePageViewModel(
            NullLogger<HomePageViewModel>.Instance,
            sessionService,
            new StubGeometryKernelDescriptorProvider());

        await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var initial = Assert.Single(viewModel.RecentProjects);
        Assert.Equal(Path.GetFullPath(persistedPath), initial.ProjectFilePath);

        var baseline = DateTimeOffset.UtcNow.AddHours(1);
        var firstSnapshot = new[]
        {
            new DiscoveredProject(firstDiscoveredPath, baseline),
        };
        await provider.PublishAsync(firstSnapshot);
        Assert.Equal(2, viewModel.RecentProjects.Count);
        var selected = viewModel.RecentProjects.Single(project =>
            PathEquals(project.ProjectFilePath, firstDiscoveredPath));
        viewModel.SelectRecentProjectCard(selected);
        Assert.True(viewModel.SelectedRecentProject?.IsSelected);

        var reorderedSnapshot = new[]
        {
            new DiscoveredProject(secondDiscoveredPath, baseline.AddMinutes(2)),
            new DiscoveredProject(firstDiscoveredPath, baseline),
        };
        await provider.PublishAsync(reorderedSnapshot);
        Assert.Equal(Path.GetFullPath(secondDiscoveredPath), viewModel.RecentProjects[0].ProjectFilePath);
        Assert.Equal(Path.GetFullPath(firstDiscoveredPath), viewModel.SelectedRecentProject?.ProjectFilePath);
        Assert.True(viewModel.RecentProjects.Single(project =>
            PathEquals(project.ProjectFilePath, firstDiscoveredPath)).IsSelected);

        var recentChanges = 0;
        viewModel.PropertyChanged += CountRecentChanges;
        var stableCollection = viewModel.RecentProjects;
        await provider.PublishAsync(reorderedSnapshot);
        Assert.Same(stableCollection, viewModel.RecentProjects);
        Assert.Equal(0, recentChanges);

        File.Delete(firstDiscoveredPath);
        await provider.PublishAsync(
        [
            new DiscoveredProject(secondDiscoveredPath, baseline.AddMinutes(2)),
        ]);
        Assert.DoesNotContain(viewModel.RecentProjects, project =>
            PathEquals(project.ProjectFilePath, firstDiscoveredPath));
        Assert.Null(viewModel.SelectedRecentProject);

        File.Delete(persistedPath);
        await provider.PublishAsync(
        [
            new DiscoveredProject(secondDiscoveredPath, baseline.AddMinutes(2)),
        ]);
        var missingPersisted = Assert.Single(viewModel.RecentProjects, project =>
            PathEquals(project.ProjectFilePath, persistedPath));
        Assert.True(missingPersisted.IsMissing);

        var beforeDispose = viewModel.RecentProjects;
        viewModel.Dispose();
        await provider.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(beforeDispose, viewModel.RecentProjects);

        void CountRecentChanges(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(HomePageViewModel.RecentProjects))
                recentChanges++;
        }
    }

    [Fact]
    public async Task LoadCancellation_StopsEnumerationAndRejectsLateSnapshots()
    {
        using var workspace = TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var latePath = workspace.CreateProject("late.stch");
        var provider = new ControlledDiscoveryProvider();
        var recentService = new RecentProjectsService(provider, workspace.GetPath("recents.json"));
        var viewModel = new HomePageViewModel(
            NullLogger<HomePageViewModel>.Instance,
            new ProjectSessionService(new StubProjectFileDialogService(), recentService),
            new StubGeometryKernelDescriptorProvider());

        await ((INavigablePageViewModel)viewModel).LoadAsync(cancellation.Token);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await provider.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        provider.Queue(
        [
            new DiscoveredProject(latePath, DateTimeOffset.UtcNow),
        ]);
        await Task.Delay(50);
        Assert.Empty(viewModel.RecentProjects);
        viewModel.Dispose();
    }

    [Fact]
    public async Task ProviderFailure_PreservesPersistedCardsAndStopsCleanly()
    {
        using var workspace = TestWorkspace.Create();
        var persistedPath = workspace.CreateProject("persisted.stch");
        var provider = new ControlledDiscoveryProvider();
        var recentService = new RecentProjectsService(provider, workspace.GetPath("recents.json"));
        recentService.RecordProject(CreateSession("Persisted", persistedPath));
        var viewModel = new HomePageViewModel(
            NullLogger<HomePageViewModel>.Instance,
            new ProjectSessionService(new StubProjectFileDialogService(), recentService),
            new StubGeometryKernelDescriptorProvider());

        await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        provider.Fail(new IOException("Synthetic discovery failure"));
        await provider.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var recent = Assert.Single(viewModel.RecentProjects);
        Assert.Equal(Path.GetFullPath(persistedPath), recent.ProjectFilePath);
        viewModel.Dispose();
    }

    private static bool PathEquals(string first, string second)
        => string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);

    private static ProjectSession CreateSession(string name, string path)
        => new(
            Guid.NewGuid(),
            name,
            path,
            new ProjectTemplateDefinition("blank-project", "Blank project", "Untitled Project"),
            ProjectSessionOrigin.Opened,
            DateTimeOffset.UtcNow);

    private sealed class ControlledDiscoveryProvider : IProjectDiscoveryProvider
    {
        private readonly Channel<SnapshotEnvelope> _snapshots =
            Channel.CreateUnbounded<SnapshotEnvelope>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DiscoveredProject>>([]);

        public async IAsyncEnumerable<IReadOnlyList<DiscoveredProject>> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await foreach (var envelope in _snapshots.Reader
                    .ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return envelope.Snapshot;
                    envelope.Applied.TrySetResult();
                }
            }
            finally
            {
                Stopped.TrySetResult();
            }
        }

        public async Task PublishAsync(IReadOnlyList<DiscoveredProject> snapshot)
        {
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _snapshots.Writer.WriteAsync(new SnapshotEnvelope(snapshot, applied));
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Queue(IReadOnlyList<DiscoveredProject> snapshot)
            => _snapshots.Writer.TryWrite(new SnapshotEnvelope(
                snapshot,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)));

        public void Fail(Exception exception) => _snapshots.Writer.TryComplete(exception);

        private sealed record SnapshotEnvelope(
            IReadOnlyList<DiscoveredProject> Snapshot,
            TaskCompletionSource Applied);
    }

    private sealed class StubProjectFileDialogService : IProjectFileDialogService
    {
        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickNewProjectFileAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSourceModelFileAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class StubGeometryKernelDescriptorProvider : IGeometryKernelDescriptorProvider
    {
        public GeometryKernelDescriptor Current { get; } = new(
            "Test kernel",
            "Test implementation",
            "Test runtime",
            "Test capabilities",
            "Test requirements",
            "Test models");
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string directory) => Directory = directory;

        private string Directory { get; }

        public static TestWorkspace Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"pathstitch-live-recents-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            return new TestWorkspace(directory);
        }

        public string GetPath(string fileName) => Path.Combine(Directory, fileName);

        public string CreateProject(string fileName)
        {
            var path = GetPath(fileName);
            File.WriteAllText(path, "{}");
            return path;
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}