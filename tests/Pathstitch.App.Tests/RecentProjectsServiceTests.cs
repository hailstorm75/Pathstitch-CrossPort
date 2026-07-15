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
