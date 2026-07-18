using System.IO.Compression;
using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Tests;

public sealed class RecentProjectsServiceTests
{
    [Fact]
    public void GetRecentProjects_ReadsEmbeddedPreviewPngFromStchArchive()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("preview.stch");
        var storagePath = workspace.GetPath("recents.json");
        var previewBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
        CreateProjectArchive(projectPath, previewBytes);
        var service = new RecentProjectsService(storagePath);

        service.RecordProject(new ProjectSession(
            Guid.NewGuid(),
            "Preview Project",
            projectPath,
            new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
            ProjectSessionOrigin.Created,
            DateTimeOffset.UtcNow));

        var recent = Assert.Single(service.GetRecentProjects());

        Assert.True(recent.HasThumbnail);
        Assert.Equal(Convert.ToBase64String(previewBytes), recent.ThumbnailDataBase64);
    }

    [Fact]
    public async Task DiscoveryMerge_PreservesPersistedMetadataMissingEntriesAndNewestOrder()
    {
        using var workspace = TestWorkspace.Create();
        var persistedPath = workspace.GetPath("persisted.stch");
        var discoveredPath = workspace.GetPath("discovered.stch");
        var missingPath = workspace.GetPath("missing.stch");
        File.WriteAllText(persistedPath, "{}");
        File.WriteAllText(discoveredPath, "{}");
        File.WriteAllText(missingPath, "{}");
        var now = DateTimeOffset.UtcNow;
        var provider = new FakeProjectDiscoveryProvider(
        [
            new DiscoveredProject(persistedPath, now.AddMinutes(2)),
            new DiscoveredProject(discoveredPath, now.AddMinutes(1)),
            new DiscoveredProject(discoveredPath, now),
        ]);
        var service = new RecentProjectsService(provider, workspace.GetPath("recents.json"));
        service.RecordProject(CreateSession("Persisted metadata", persistedPath));
        service.RecordProject(CreateSession("Missing metadata", missingPath));
        File.Delete(missingPath);

        var recent = await service.GetRecentProjectsWithDiscoveryAsync();

        Assert.Equal(3, recent.Count);
        Assert.Equal(Path.GetFullPath(persistedPath), recent[0].ProjectFilePath);
        Assert.Equal("Persisted metadata", recent[0].ProjectName);
        Assert.Equal("blank", recent[0].TemplateId);
        Assert.Contains(recent, project =>
            project.ProjectFilePath == Path.GetFullPath(missingPath)
            && project.IsMissing
            && project.ProjectName == "Missing metadata");
        Assert.Single(recent, project => project.ProjectFilePath == Path.GetFullPath(discoveredPath));
    }

    [Fact]
    public async Task RemovedDiscoveredProject_StaysHiddenUntilReopened()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("spotlight.stch");
        File.WriteAllText(projectPath, "{}");
        var provider = new FakeProjectDiscoveryProvider(
        [
            new DiscoveredProject(projectPath, DateTimeOffset.UtcNow),
        ]);
        var service = new RecentProjectsService(provider, workspace.GetPath("recents.json"));

        Assert.Single(await service.GetRecentProjectsWithDiscoveryAsync());
        Assert.Single(service.GetRecentProjectsIncludingDiscovery());
        service.RemoveProject(projectPath);
        Assert.Empty(service.GetRecentProjectsIncludingDiscovery());
        Assert.Empty(await service.GetRecentProjectsWithDiscoveryAsync());

        service.RecordProject(CreateSession("Reopened", projectPath));
        var reopened = Assert.Single(service.GetRecentProjectsIncludingDiscovery());
        Assert.Equal("Reopened", reopened.ProjectName);
    }

    [Fact]
    public async Task DiscoveryMerge_CapsNewestProjectsAtTwenty()
    {
        using var workspace = TestWorkspace.Create();
        var baseline = DateTimeOffset.UtcNow.AddHours(-1);
        var discovered = Enumerable.Range(0, 25)
            .Select(index =>
            {
                var path = workspace.GetPath($"project-{index:00}.stch");
                File.WriteAllText(path, "{}");
                return new DiscoveredProject(path, baseline.AddMinutes(index));
            })
            .ToArray();
        var service = new RecentProjectsService(
            new FakeProjectDiscoveryProvider(discovered),
            workspace.GetPath("recents.json"));

        var recent = await service.GetRecentProjectsWithDiscoveryAsync();

        Assert.Equal(20, recent.Count);
        Assert.Equal("project-24", recent[0].ProjectName);
        Assert.DoesNotContain(recent, project => project.ProjectName == "project-00");
    }

    private static ProjectSession CreateSession(string name, string path)
        => new(
            Guid.NewGuid(),
            name,
            path,
            new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
            ProjectSessionOrigin.Opened,
            DateTimeOffset.UtcNow);

    private sealed class FakeProjectDiscoveryProvider(
        IReadOnlyList<DiscoveredProject> projects) : IProjectDiscoveryProvider
    {
        public Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(projects);
        }
    }

    private static void CreateProjectArchive(string path, byte[] previewBytes)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var projectEntry = archive.CreateEntry("project.json");
        using (var writer = new StreamWriter(projectEntry.Open()))
            writer.Write("{\"projectName\":\"Preview Project\",\"templateId\":\"blank\"}");

        var previewEntry = archive.CreateEntry("preview.png");
        using var stream = previewEntry.Open();
        stream.Write(previewBytes);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string directory) => Directory = directory;

        private string Directory { get; }

        public static TestWorkspace Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-recents-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            return new TestWorkspace(directory);
        }

        public string GetPath(string fileName) => Path.Combine(Directory, fileName);

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
