using System.Security.Cryptography;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class DxfBinaryTransportConversionTests
{
    [Fact]
    public async Task PreviewAndUnitInspection_ConvertBinaryTransportAndDeleteTemporaryAscii()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-binary-routing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.dxf");
        var templatePath = Path.Combine(directory, "template.dxf");
        await File.WriteAllBytesAsync(sourcePath, "AutoCAD Binary DXF\r\n\u001A\0fixture"u8.ToArray());
        await new DxfOutputPreviewService().SavePreviewDocumentAsync(
            new Editor2DPreviewDocument(
                [new("line", "LINE", [new(-2, 3), new(8, 13)], false)],
                new(-2, 3, 8, 13),
                new Dictionary<string, int> { ["LINE"] = 1 },
                []),
            templatePath);
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));
        var converter = new CopyingConverter(templatePath);
        var service = new DxfOutputPreviewService(dxfTransportConversionService: converter);

        try
        {
            var preview = await service.LoadPreviewDocumentAsync(sourcePath);
            var units = await service.InspectImportUnitsAsync(sourcePath);

            Assert.Single(preview!.Paths);
            Assert.Equal(4, units!.InsUnitsCode);
            Assert.Equal(Path.GetFullPath(sourcePath), units.SourcePath);
            Assert.Equal(2, converter.Calls);
            Assert.All(converter.OutputPaths, path => Assert.False(File.Exists(path)));
            Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AsciiDxf_BypassesBinaryTransportConverter()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ascii-routing-{Guid.NewGuid():N}.dxf");
        var converter = new RejectingConverter();
        try
        {
            await new DxfOutputPreviewService().SavePreviewDocumentAsync(
                new Editor2DPreviewDocument(
                    [new("line", "LINE", [new(0, 0), new(1, 1)], false)],
                    new(0, 0, 1, 1),
                    new Dictionary<string, int> { ["LINE"] = 1 },
                    []),
                path);
            var service = new DxfOutputPreviewService(dxfTransportConversionService: converter);

            Assert.NotNull(await service.LoadPreviewDocumentAsync(path));
            Assert.NotNull(await service.InspectImportUnitsAsync(path));
            Assert.Equal(0, converter.Calls);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertedAscii_IsDeletedWhenParsingFails()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"pathstitch-binary-invalid-{Guid.NewGuid():N}.dxf");
        await File.WriteAllBytesAsync(sourcePath, "AutoCAD Binary DXF\r\n\u001A\0fixture"u8.ToArray());
        var converter = new InvalidAsciiConverter();
        try
        {
            var service = new DxfOutputPreviewService(dxfTransportConversionService: converter);

            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadPreviewDocumentAsync(sourcePath));
            Assert.NotNull(converter.OutputPath);
            Assert.False(File.Exists(converter.OutputPath));
        }
        finally
        {
            File.Delete(sourcePath);
            if (converter.OutputPath is not null)
                File.Delete(converter.OutputPath);
        }
    }

    [Fact]
    public async Task PackagedConverter_LoadsCommittedBinaryFixtureWhenRuntimeIsAvailable()
    {
        var packagedRuntime = new AppOwnedGeometryWorkerRuntimeResolver().Resolve();
        if (packagedRuntime is null)
            return;
        var sourcePath = FindRepositoryFile(
            "tests", "Pathstitch.App.Tests", "Fixtures", "binary-dxf", "r2010-unicode-ocs.dxf");
        var repositoryRoot = FindRepositoryRoot();
        var resolver = new FixedRuntimeResolver(new GeometryWorkerRuntime(
            packagedRuntime.PythonExecutable,
            repositoryRoot));
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));
        using var workerClient = new PathstitchDxfWorkerClient(
            NullLogger<PathstitchDxfWorkerClient>.Instance,
            resolver);
        var converter = new PackagedDxfTransportConversionService(workerClient);
        var service = new DxfOutputPreviewService(dxfTransportConversionService: converter);

        var preview = await service.LoadPreviewDocumentAsync(sourcePath);
        var units = await service.InspectImportUnitsAsync(sourcePath);

        Assert.Contains(preview!.Paths, path => path.EntityType == "LINE");
        var text = Assert.Single(preview.Paths, path => path.EntityType == "TEXT");
        Assert.Equal("Größe 東京", text.Text);
        Assert.Equal(4, units!.InsUnitsCode);
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
    }

    [Fact]
    public async Task PackagedConverter_MissingRuntimeFailsActionably()
    {
        var sourcePath = FindRepositoryFile(
            "tests", "Pathstitch.App.Tests", "Fixtures", "binary-dxf", "r2010-unicode-ocs.dxf");
        using var workerClient = new PathstitchDxfWorkerClient(
            NullLogger<PathstitchDxfWorkerClient>.Instance,
            new MissingRuntimeResolver());
        var converter = new PackagedDxfTransportConversionService(workerClient);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => converter.ConvertBinaryToAsciiAsync(sourcePath));

        Assert.Contains("GeometryWorker", error.Message, StringComparison.Ordinal);
    }
    [Fact]
    public void RuntimeCapabilityContract_DeclaresBinaryDxfConversion()
    {
        var runtimeSpec = File.ReadAllText(FindRepositoryFile("geometry-worker", "runtime-spec.json"));
        Assert.Contains("binary-dxf-conversion", runtimeSpec, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] segments)
        => Path.Combine([FindRepositoryRoot(), .. segments]);

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

    private sealed class MissingRuntimeResolver : IGeometryWorkerRuntimeResolver
    {
        public GeometryWorkerRuntime? Resolve() => null;
    }
    private sealed class FixedRuntimeResolver(GeometryWorkerRuntime runtime) : IGeometryWorkerRuntimeResolver
    {
        public GeometryWorkerRuntime? Resolve() => runtime;
    }
    private sealed class CopyingConverter(string templatePath) : IDxfTransportConversionService
    {
        public int Calls { get; private set; }
        public List<string> OutputPaths { get; } = [];

        public Task<string> ConvertBinaryToAsciiAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-converted-{Guid.NewGuid():N}.dxf");
            File.Copy(templatePath, outputPath);
            OutputPaths.Add(outputPath);
            return Task.FromResult(outputPath);
        }
    }

    private sealed class RejectingConverter : IDxfTransportConversionService
    {
        public int Calls { get; private set; }

        public Task<string> ConvertBinaryToAsciiAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("ASCII input must bypass conversion.");
        }
    }

    private sealed class InvalidAsciiConverter : IDxfTransportConversionService
    {
        public string? OutputPath { get; private set; }

        public async Task<string> ConvertBinaryToAsciiAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            OutputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-invalid-ascii-{Guid.NewGuid():N}.dxf");
            await File.WriteAllTextAsync(OutputPath, "0\nSECTION\n2", cancellationToken);
            return OutputPath;
        }
    }
}