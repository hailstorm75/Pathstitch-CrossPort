using System.Globalization;
using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Tests;

public sealed class ProjectSessionServiceTests
{
    [Fact]
    public async Task OpenWorkspaceFilesAsync_CreatesImportedWorkspaceForStepSource()
    {
        using var workspace = TestWorkspace.Create();
        var stepPath = workspace.WriteText("part.step", "ISO-10303-21;");
        var service = CreateService();

        var request = await service.OpenWorkspaceFilesAsync([stepPath]);

        Assert.NotNull(request);
        Assert.Equal(ProjectSessionOrigin.Imported, request.Session.Origin);
        Assert.Equal([Path.GetFullPath(stepPath)], request.PendingSourceModelPaths);
        File.Delete(request.Session.ProjectFilePath);
    }

    [Fact]
    public async Task OpenWorkspaceFilesAsync_CreatesImportedWorkspaceForObjAndStl()
    {
        using var workspace = TestWorkspace.Create();
        var objPath = workspace.WriteText("part.obj", "v 0 0 0");
        var stlPath = workspace.WriteText("part.stl", "solid empty");
        var service = CreateService();

        var request = await service.OpenWorkspaceFilesAsync([objPath, stlPath]);

        Assert.NotNull(request);
        Assert.Equal(ProjectSessionOrigin.Imported, request.Session.Origin);
        Assert.False(request.Session.TrackInRecentProjects);
        Assert.Equal([Path.GetFullPath(objPath), Path.GetFullPath(stlPath)], request.PendingSourceModelPaths);
        Assert.True(File.Exists(request.Session.ProjectFilePath));
        File.Delete(request.Session.ProjectFilePath);
    }

    [Fact]
    public async Task OpenWorkspaceFilesAsync_QueuesDxfForTwoDWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var dxfPath = workspace.WriteText("drawing.dxf", "0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n");
        var service = CreateService();

        var request = await service.OpenWorkspaceFilesAsync([dxfPath]);

        Assert.NotNull(request);
        Assert.Empty(request.PendingSourceModelPaths);
        Assert.Equal([Path.GetFullPath(dxfPath)], request.PendingTwoDFilePaths);
        File.Delete(request.Session.ProjectFilePath);
    }

    [Fact]
    public async Task OpenWorkspaceFilesAsync_QueuesSvgForTwoDWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var svgPath = workspace.WriteText("drawing.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"1\" y=\"2\" width=\"10\" height=\"5\" /></svg>");
        var service = CreateService();

        var request = await service.OpenWorkspaceFilesAsync([svgPath]);

        Assert.NotNull(request);
        Assert.Equal([Path.GetFullPath(svgPath)], request.PendingTwoDFilePaths);
        File.Delete(request.Session.ProjectFilePath);
    }

    [Fact]
    public async Task OpenWorkspaceFilesAsync_QueuesPdfForTwoDWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var pdfPath = workspace.WriteText("drawing.pdf", "%PDF-1.7");
        var service = CreateService();

        var request = await service.OpenWorkspaceFilesAsync([pdfPath]);

        Assert.NotNull(request);
        Assert.Empty(request.PendingSourceModelPaths);
        Assert.Equal([Path.GetFullPath(pdfPath)], request.PendingTwoDFilePaths);
        File.Delete(request.Session.ProjectFilePath);
    }

    [Fact]
    public async Task OpenWorkspaceFilesAsync_QueuesReferenceImageForTwoDWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var imagePath = workspace.WriteText("reference.png", "not decoded here");
        var service = CreateService();

        var request = await service.OpenWorkspaceFilesAsync([imagePath]);

        Assert.NotNull(request);
        Assert.Empty(request.PendingSourceModelPaths);
        Assert.Empty(request.PendingTwoDFilePaths);
        Assert.Equal([Path.GetFullPath(imagePath)], request.PendingReferenceImagePaths);
        File.Delete(request.Session.ProjectFilePath);
    }

    [Fact]
    public async Task OpenWorkspaceFilesAsync_QueuesWebpReferenceImageForTwoDWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var imagePath = workspace.WriteText("reference.webp", "not decoded here");
        var service = CreateService();

        var request = await service.OpenWorkspaceFilesAsync([imagePath]);

        Assert.NotNull(request);
        Assert.Equal([Path.GetFullPath(imagePath)], request.PendingReferenceImagePaths);
        File.Delete(request.Session.ProjectFilePath);
    }

    [Fact]
    public async Task OpenWorkspaceFilesAsync_PreservesAllTwoDDrawings()
    {
        using var workspace = TestWorkspace.Create();
        var firstPath = workspace.WriteText("first.dxf", "0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n");
        var secondPath = workspace.WriteText("second.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><line x=\"0\" y1=\"0\" x2=\"5\" y2=\"5\" /></svg>");
        var service = CreateService();

        var request = await service.OpenWorkspaceFilesAsync([firstPath, secondPath]);

        Assert.NotNull(request);
        Assert.Equal([Path.GetFullPath(firstPath), Path.GetFullPath(secondPath)], request.PendingTwoDFilePaths);
        File.Delete(request.Session.ProjectFilePath);
    }

    private static ProjectSessionService CreateService()
        => new(new FakeProjectFileDialogService(), new RecentProjectsService());

    private sealed class FakeProjectFileDialogService : IProjectFileDialogService
    {
        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string directory)
        {
            Directory = directory;
        }

        private string Directory { get; }

        public static TestWorkspace Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Pathstitch-CrossPort-ProjectSessionTests",
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            System.IO.Directory.CreateDirectory(directory);
            return new TestWorkspace(directory);
        }

        public string WriteText(string fileName, string contents)
        {
            var path = Path.Combine(Directory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // Test cleanup is best-effort; stale temp files do not affect assertions.
            }
        }
    }
}
