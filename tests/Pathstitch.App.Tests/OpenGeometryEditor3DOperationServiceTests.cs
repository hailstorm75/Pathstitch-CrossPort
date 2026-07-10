using System.Globalization;
using System.Text.Json;
using Domain.App.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class OpenGeometryEditor3DOperationServiceTests
{
    [Fact]
    public async Task LoadModelAsync_LoadsObjMeshIntoViewportScene()
    {
        using var workspace = TestWorkspace.Create();
        var objPath = workspace.WriteText("triangle.obj", TriangleObj);
        var service = CreateService();

        var result = await service.LoadModelAsync(objPath);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(objPath, result.SourceModelPath);
        Assert.NotNull(result.ViewportJson);
        Assert.NotNull(result.Bodies);
        Assert.Single(result.Bodies);
        Assert.Contains("\"bodies\"", result.ViewportJson, StringComparison.Ordinal);
        Assert.Contains("\"bbox\"", result.ViewportJson, StringComparison.Ordinal);
        Assert.Equal(0, result.Bodies[0].BodyIndex);
        Assert.Single(result.Bodies[0].Faces);
    }

    [Fact]
    public async Task LoadModelsAsync_PersistsCombinedMeshWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var first = workspace.WriteText("first.obj", TriangleObj);
        var second = workspace.WriteText(
            "second.obj",
            """
            v 0 0 0
            v 0 0 5
            v 0 5 0
            f 1 2 3
            """);
        var service = CreateService();

        var result = await service.LoadModelsAsync([first, second]);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.SourceModelPath);
        Assert.True(File.Exists(result.SourceModelPath));
        Assert.EndsWith(".json", result.SourceModelPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.Bodies?.Count);
        Assert.Equal([0, 1], result.Bodies!.Select(static body => body.BodyIndex).ToArray());
    }

    [Fact]
    public async Task ProjectEdgesAsync_UsesOpenGeometryWorkerAndWritesProjectedDxf()
    {
        using var workspace = TestWorkspace.Create();
        var objPath = workspace.WriteText("triangle.obj", TriangleObj);
        var service = CreateService();
        var load = await service.LoadModelAsync(objPath);

        var projection = await service.ProjectEdgesAsync(new EditorProjectionRequest(
            load.SourceModelPath,
            "XY",
            0,
            FaceIndex: null,
            FaceBodyIndex: null,
            VisibleBodyIndices: [0],
            BodyOffsets: []));

        Assert.True(projection.IsSuccess, projection.Message);
        Assert.Contains("through the OpenGeometry kernel", projection.Message, StringComparison.Ordinal);
        Assert.Contains("OpenGeometry wrote", projection.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(projection.OutputPath));
        var dxf = await File.ReadAllTextAsync(projection.OutputPath);
        var polylineCount = CountDxfEntities(dxf, "LWPOLYLINE");
        Assert.True(polylineCount > 0);
        Assert.Equal(polylineCount, CountClosedLwPolylines(dxf));
        Assert.Contains("OPEN_GEOMETRY_OFFSET", dxf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectEdgesAsync_LoadsRestoredJsonMeshWorkspaceWithoutTempFilePrefix()
    {
        using var workspace = TestWorkspace.Create();
        var first = workspace.WriteText("first.obj", TriangleObj);
        var second = workspace.WriteText(
            "second.obj",
            """
            v 0 0 0
            v 0 0 5
            v 0 5 0
            f 1 2 3
            """);
        var service = CreateService();
        var combined = await service.LoadModelsAsync([first, second]);
        var restoredPath = workspace.GetPath("active.json");
        File.Copy(combined.SourceModelPath!, restoredPath);

        var projection = await service.ProjectEdgesAsync(new EditorProjectionRequest(
            restoredPath,
            "XY",
            0,
            FaceIndex: null,
            FaceBodyIndex: null,
            VisibleBodyIndices: [0, 1],
            BodyOffsets: []));

        Assert.True(projection.IsSuccess, projection.Message);
        Assert.True(File.Exists(projection.OutputPath));
    }

    [Fact]
    public async Task ProjectEdgesAsync_ProjectsOntoSelectedMeshFace()
    {
        using var workspace = TestWorkspace.Create();
        var objPath = workspace.WriteText(
            "vertical-triangle.obj",
            """
            v 0 0 0
            v 10 0 0
            v 0 0 10
            f 1 2 3
            """);
        var service = CreateService();
        var load = await service.LoadModelAsync(objPath);

        var projection = await service.ProjectEdgesAsync(new EditorProjectionRequest(
            load.SourceModelPath,
            "face",
            0,
            FaceIndex: 0,
            FaceBodyIndex: 0,
            VisibleBodyIndices: [0],
            BodyOffsets: []));

        Assert.True(projection.IsSuccess, projection.Message);
        Assert.Contains("mesh face B1:F0", projection.Message, StringComparison.Ordinal);
        Assert.Contains("through the OpenGeometry kernel", projection.Message, StringComparison.Ordinal);
        Assert.Contains("OpenGeometry wrote", projection.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(projection.OutputPath));
        var dxf = await File.ReadAllTextAsync(projection.OutputPath);
        Assert.Contains("OPEN_GEOMETRY_OFFSET", dxf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnfoldAsync_WritesTrianglePreservingDxf()
    {
        using var workspace = TestWorkspace.Create();
        var objPath = workspace.WriteText("triangle.obj", TriangleObj);
        var service = CreateService();
        var load = await service.LoadModelAsync(objPath);

        var unfold = await service.UnfoldAsync(new EditorUnfoldRequest(
            load.SourceModelPath,
            SelectedFaces: [],
            VisibleBodyIndices: [0],
            WholeBody: true,
            DistortionMode: "conformal"));

        Assert.True(unfold.IsSuccess, unfold.Message);
        Assert.True(File.Exists(unfold.OutputPath));
        var dxf = await File.ReadAllTextAsync(unfold.OutputPath);
        Assert.Equal(1, CountDxfEntities(dxf, "LWPOLYLINE"));
        Assert.Contains("\n70\n1\n", dxf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadModelAsync_LoadsAsciiStlMesh()
    {
        using var workspace = TestWorkspace.Create();
        var stlPath = workspace.WriteText(
            "triangle.stl",
            """
            solid triangle
              facet normal 0 0 1
                outer loop
                  vertex 0 0 0
                  vertex 10 0 0
                  vertex 0 10 0
                endloop
              endfacet
            endsolid triangle
            """);
        var service = CreateService();

        var result = await service.LoadModelAsync(stlPath);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Single(result.Bodies!);
        Assert.Single(result.Bodies![0].Faces);
    }

    [Fact]
    public async Task LoadModelAsync_LoadsBinaryStlMesh()
    {
        using var workspace = TestWorkspace.Create();
        var stlPath = workspace.WriteBytes("triangle-binary.stl", CreateBinaryTriangleStl());
        var service = CreateService();

        var result = await service.LoadModelAsync(stlPath);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Single(result.Bodies!);
        Assert.Single(result.Bodies![0].Faces);
        Assert.Contains("triangle-binary", result.ViewportJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeFaceDistortionAsync_ReturnsNearZeroValuesForPlanarMeshChunk()
    {
        using var workspace = TestWorkspace.Create();
        var objPath = workspace.WriteText("triangle.obj", TriangleObj);
        var service = CreateService();
        var load = await service.LoadModelAsync(objPath);

        var result = await service.ComputeFaceDistortionAsync(
            load.SourceModelPath,
            new SelectedFace3D(0, 0),
            "balanced");

        Assert.True(result.IsSuccess, result.Message);
        var values = ReadDistortionValues(result.DistortionJson);
        Assert.Equal(3, values.Length);
        Assert.All(values, value => Assert.InRange(value, 0.0, 1e-9));
    }

    [Fact]
    public async Task ComputeFaceDistortionAsync_ReturnsMeasuredValuesForNonPlanarMeshChunk()
    {
        using var workspace = TestWorkspace.Create();
        var objPath = workspace.WriteText(
            "warped-quad.obj",
            """
            v 0 0 0
            v 10 0 0
            v 0 10 0
            v 10 10 5
            f 1 2 3
            f 2 4 3
            """);
        var service = CreateService();
        var load = await service.LoadModelAsync(objPath);

        var result = await service.ComputeFaceDistortionAsync(
            load.SourceModelPath,
            new SelectedFace3D(0, 0),
            "balanced");

        Assert.True(result.IsSuccess, result.Message);
        var values = ReadDistortionValues(result.DistortionJson);
        Assert.Equal(6, values.Length);
        Assert.Contains(values, value => value > 0.01);
        Assert.All(values, value => Assert.InRange(value, 0.0, 1.0));
    }

    [Fact]
    public async Task LoadModelAsync_ReportsTypedFailureWhenPackagedStepRuntimeIsMissing()
    {
        using var workspace = TestWorkspace.Create();
        var stepPath = workspace.WriteText("part.step", "ISO-10303-21;");
        var service = CreateService();

        var result = await service.LoadModelAsync(stepPath);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(GeometryKernelFailureCode.SourceUnavailable, result.Failure.Code);
        Assert.Equal(GeometryKernelOperation.Import, result.Failure.Operation);
        Assert.Contains("app-owned STEP geometry worker", result.Message, StringComparison.Ordinal);
    }

    private static OpenGeometryEditor3DOperationService CreateService()
    {
        var bridge = new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance);
        return new OpenGeometryEditor3DOperationService(
            NullLogger<OpenGeometryEditor3DOperationService>.Instance,
            bridge);
    }

    private static int CountDxfEntities(string dxf, string entityName)
    {
        var lines = dxf.Split('\n', StringSplitOptions.TrimEntries);
        var count = 0;
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (lines[i] == "0" && string.Equals(lines[i + 1], entityName, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    private static int CountClosedLwPolylines(string dxf)
    {
        var lines = dxf.Split('\n', StringSplitOptions.TrimEntries);
        var count = 0;
        var inLwPolyline = false;
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (lines[i] == "0")
            {
                inLwPolyline = string.Equals(lines[i + 1], "LWPOLYLINE", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inLwPolyline && lines[i] == "70" && lines[i + 1] == "1")
            {
                count++;
                inLwPolyline = false;
            }
        }

        return count;
    }

    private static double[] ReadDistortionValues(string? distortionJson)
    {
        Assert.False(string.IsNullOrWhiteSpace(distortionJson));
        using var document = JsonDocument.Parse(distortionJson);
        return document.RootElement
            .GetProperty("distortion")
            .EnumerateArray()
            .Select(static value => value.GetDouble())
            .ToArray();
    }

    private static byte[] CreateBinaryTriangleStl()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]);
        writer.Write(1u);
        WriteVector(writer, 0, 0, 1);
        WriteVector(writer, 0, 0, 0);
        WriteVector(writer, 10, 0, 0);
        WriteVector(writer, 0, 10, 0);
        writer.Write((ushort)0);
        return stream.ToArray();
    }

    private static void WriteVector(BinaryWriter writer, float x, float y, float z)
    {
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
    }

    private const string TriangleObj =
        """
        v 0 0 0
        v 10 0 0
        v 0 10 0
        f 1 2 3
        """;

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
                "Pathstitch-CrossPort-Tests",
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            System.IO.Directory.CreateDirectory(directory);
            return new TestWorkspace(directory);
        }

        public string WriteText(string fileName, string contents)
        {
            var path = GetPath(fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        public string WriteBytes(string fileName, byte[] contents)
        {
            var path = GetPath(fileName);
            File.WriteAllBytes(path, contents);
            return path;
        }

        public string GetPath(string fileName)
            => Path.Combine(Directory, fileName);

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
