using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pathstitch.App.Tests;

public sealed class HomePageViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptProjectOpen_ShowsErrorAndPreservesCurrentSession(bool openAsRecent)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-home-corrupt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var recipient = new object();
        try
        {
            var validPath = Path.Combine(directory, "current.stch");
            var corruptPath = Path.Combine(directory, "broken.stch");
            await File.WriteAllTextAsync(
                validPath,
                "{\"projectName\":\"Current\",\"templateId\":\"blank-project\"}");
            await File.WriteAllTextAsync(corruptPath, "not-json");
            var sessionService = new ProjectSessionService(
                new StubProjectFileDialogService(),
                new RecentProjectsService(Path.Combine(directory, "recent.json")));
            var current = Assert.IsType<ProjectSession>(
                await sessionService.OpenRecentProjectAsync(validPath));
            var viewModel = new HomePageViewModel(
                NullLogger<HomePageViewModel>.Instance,
                sessionService,
                new StubGeometryKernelDescriptorProvider());
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);
            var navigationRequests = new List<NavigationChangeRequestMessage>();
            WeakReferenceMessenger.Default.Register<NavigationChangeRequestMessage>(
                recipient,
                (_, message) => navigationRequests.Add(message));

            if (openAsRecent)
            {
                await viewModel.OpenRecentProjectCardAsync(new RecentProjectSummary(
                    "Broken",
                    corruptPath,
                    "blank-project",
                    "Blank project",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    IsAvailable: true));
            }
            else
            {
                await viewModel.OpenFilesAsync([corruptPath]);
            }

            Assert.Contains("broken.stch", viewModel.HomeStatusText, StringComparison.Ordinal);
            Assert.Contains("not a valid Pathstitch project", viewModel.HomeStatusText, StringComparison.Ordinal);
            Assert.Same(current, sessionService.CurrentSession);
            Assert.Same(current, viewModel.CurrentSession);
            Assert.Empty(navigationRequests);
            Assert.False(viewModel.IsLoading);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubProjectFileDialogService : IProjectFileDialogService
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
}
