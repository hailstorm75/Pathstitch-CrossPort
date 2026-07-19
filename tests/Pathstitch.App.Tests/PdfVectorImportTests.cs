using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class PdfVectorImportTests
{
    [Fact]
    public async Task PreviewService_ConvertsPdfAndRemovesTemporaryDxf()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pdfPath = Path.Combine(directory, "drawing.pdf");
        var convertedPath = Path.Combine(directory, "converted.dxf");
        await File.WriteAllBytesAsync(pdfPath, "%PDF-1.7"u8.ToArray());
        var expected = new Editor2DPreviewDocument(
            [new("line", "LINE", [new(1, 2), new(11, 2)], false)],
            new(1, 2, 11, 2),
            new Dictionary<string, int> { ["LINE"] = 1 },
            []);
        await new DxfOutputPreviewService().SavePreviewDocumentAsync(expected, convertedPath);
        var converter = new RecordingPdfConverter(convertedPath);

        try
        {
            var loaded = await new DxfOutputPreviewService(converter).LoadPreviewDocumentAsync(pdfPath);

            var line = Assert.Single(loaded!.Paths);
            Assert.Equal(expected.Paths[0].Points, line.Points);
            Assert.Equal(Path.GetFullPath(pdfPath), converter.SourcePath);
            Assert.False(File.Exists(convertedPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BatchWorkspace_TreatsPdfAsDrawingInput()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-batch-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, "%PDF-1.7"u8.ToArray());
        try
        {
            var workspace = new EditorBatchWorkspaceViewModel();
            Assert.Equal(1, workspace.AddFiles([path]));
            Assert.True(workspace.CanExport);
            Assert.True(workspace.CanOperateSelected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PackagedWorker_ImportsGeneratedPdfVectorsWhenRuntimeIsAvailable()
    {
        var resolver = new AppOwnedGeometryWorkerRuntimeResolver();
        if (resolver.Resolve() is null)
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-pdf-worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pdfPath = Path.Combine(directory, "drawing.pdf");
        var source = new Editor2DPreviewDocument(
            [new("box", "LWPOLYLINE", [new(0, 0), new(20, 0), new(20, 10), new(0, 10)], true)],
            new(0, 0, 20, 10),
            new Dictionary<string, int> { ["LWPOLYLINE"] = 1 },
            []);
        await new DxfOutputPreviewService().SavePreviewDocumentAsync(source, pdfPath);

        try
        {
            using var workerClient = new PathstitchDxfWorkerClient(
                NullLogger<PathstitchDxfWorkerClient>.Instance,
                resolver);
            var converter = new PackagedPdfVectorImportService(workerClient);
            var imported = await new DxfOutputPreviewService(converter).LoadPreviewDocumentAsync(pdfPath);

            Assert.NotNull(imported);
            Assert.NotEmpty(imported.Paths);
            Assert.True(imported.Bounds.Width > 0);
            Assert.True(imported.Bounds.Height > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DesktopRoutingAndPackagedRuntime_IncludePdfImport()
    {
        var root = FindRepositoryRoot();
        var picker = File.ReadAllText(Path.Combine(root, "src", "Pathstitch.App", "Services", "ProjectFileDialogService.cs"));
        var import = File.ReadAllText(Path.Combine(root, "src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.Import.cs"));
        var runtimeBuild = File.ReadAllText(Path.Combine(root, "geometry-worker", "build-runtime.ps1"));
        var windowsLock = File.ReadAllText(Path.Combine(root, "geometry-worker", "locks", "win-x64.lock"));
        var macLock = File.ReadAllText(Path.Combine(root, "geometry-worker", "locks", "osx-arm64.lock"));

        Assert.Contains("*.pdf", picker, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\".pdf\"", import, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pdfplumber", runtimeBuild, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pdfplumber", windowsLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pdfplumber", macLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pypdfium2", windowsLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pypdfium2", macLock, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PathstitchCross.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class RecordingPdfConverter(string convertedPath) : IPdfVectorImportService
    {
        public string? SourcePath { get; private set; }

        public Task<string> ConvertToDxfAsync(string sourcePath, CancellationToken cancellationToken = default)
        {
            SourcePath = Path.GetFullPath(sourcePath);
            return Task.FromResult(convertedPath);
        }
    }
}
