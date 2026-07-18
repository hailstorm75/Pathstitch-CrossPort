using System.Globalization;
using System.Text.Json;
using Domain.App.Models;
using Domain.App.Services;
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
    public async Task LoadModelsAsync_AppendsMultipleStepDocumentsAndKeepsFinalCombinedSource()
    {
        using var workspace = TestWorkspace.Create();
        var existing = workspace.WriteText("existing.step", "existing");
        var second = workspace.WriteText("second.step", "second");
        var third = workspace.WriteText("third.stp", "third");
        var kernel = new RecordingStepKernel(workspace);
        var service = CreateService(kernel);

        var result = await service.LoadModelsAsync([second, third], existing);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, kernel.CombineCalls.Count);
        Assert.Equal((existing, second), kernel.CombineCalls[0]);
        Assert.Equal((kernel.CombinedPaths[0], third), kernel.CombineCalls[1]);
        Assert.Equal(kernel.CombinedPaths[1], result.SourceModelPath);
        Assert.Equal(kernel.CombinedPaths[1], kernel.ImportedPath);
        Assert.False(File.Exists(kernel.CombinedPaths[0]));
        Assert.True(File.Exists(kernel.CombinedPaths[1]));
        Assert.Equal(3, result.Bodies?.Count);
        Assert.Equal("existing", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task LoadModelsAsync_StepCombineFailureLeavesExistingSourceUntouched()
    {
        using var workspace = TestWorkspace.Create();
        var existing = workspace.WriteText("existing.step", "existing");
        var incoming = workspace.WriteText("incoming.step", "incoming");
        var kernel = new RecordingStepKernel(workspace) { FailCombine = true };
        var service = CreateService(kernel);

        var result = await service.LoadModelsAsync([incoming], existing);

        Assert.False(result.IsSuccess);
        Assert.Null(kernel.ImportedPath);
        Assert.Empty(kernel.CombinedPaths);
        Assert.Equal("existing", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task LoadModelsAsync_NormalizesMixedStepAndMeshThroughPackagedKernel()
    {
        using var workspace = TestWorkspace.Create();
        var step = workspace.WriteText("part.step", "step");
        var mesh = workspace.WriteText("part.obj", TriangleObj);
        var kernel = new RecordingStepKernel(workspace);
        var service = CreateService(kernel);

        var result = await service.LoadModelsAsync([step, mesh]);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal([(step, mesh)], kernel.CombineCalls);
        Assert.Single(kernel.CombinedPaths);
        Assert.Equal(kernel.CombinedPaths[0], kernel.ImportedPath);
        Assert.Equal(kernel.CombinedPaths[0], result.SourceModelPath);
        Assert.True(File.Exists(result.SourceModelPath));
        Assert.Equal(2, result.Bodies?.Count);
    }

    [Fact]
    public async Task LoadModelsAsync_RejectsLegacyJsonAndStepWithoutMutatingLegacyWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var first = workspace.WriteText("first.obj", TriangleObj);
        var second = workspace.WriteText("second.obj", TriangleObj);
        var legacyService = CreateService();
        var legacy = await legacyService.LoadModelsAsync([first, second]);
        var original = await File.ReadAllBytesAsync(legacy.SourceModelPath!);
        var step = workspace.WriteText("part.step", "step");
        var service = CreateService(new RecordingStepKernel(workspace));

        var result = await service.LoadModelsAsync([step], legacy.SourceModelPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(GeometryKernelFailureCode.InvalidInput, result.Failure?.Code);
        Assert.Contains("legacy mesh workspace", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, await File.ReadAllBytesAsync(legacy.SourceModelPath!));
    }
    [Fact]
    public async Task LoadModelAsync_RoutesObjThroughPackagedKernelWhenAvailable()
    {
        using var workspace = TestWorkspace.Create();
        var mesh = workspace.WriteText("part.obj", TriangleObj);
        var kernel = new RecordingStepKernel(workspace);
        var service = CreateService(kernel);

        var result = await service.LoadModelAsync(mesh);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(mesh, kernel.ImportedPath);
        Assert.Equal(mesh, result.SourceModelPath);
        Assert.Single(result.Bodies!);
    }

    [Fact]
    public async Task PackagedMeshOperations_RouteAllParityControlsToKernel()
    {
        using var workspace = TestWorkspace.Create();
        var mesh = workspace.WriteText("part.obj", TriangleObj);
        var kernel = new RecordingStepKernel(workspace);
        var service = CreateService(kernel);
        var projectionRequest = new EditorProjectionRequest(
            mesh,
            "XY",
            2.5,
            FaceIndex: null,
            FaceBodyIndex: null,
            VisibleBodyIndices: [0],
            BodyOffsets: [],
            VisibleBodyIds: ["body-0"]);
        var unfoldRequest = new EditorUnfoldRequest(
            mesh,
            SelectedFaces: [new SelectedFace3D(0, 0, "body-0", "face-0")],
            VisibleBodyIndices: [0],
            WholeBody: false,
            DistortionMode: "balanced",
            SelectedFaceIds: ["face-0"],
            VisibleBodyIds: ["body-0"],
            SeamControlMode: "manual",
            ForcedSeams: [new EditorSeamEdge3D(0, 2)],
            AnchorFace: new SelectedFace3D(0, 0, "body-0", "face-0"),
            SeamDecoration: "holes",
            SeamDecorations: [new EditorSeamDecoration3D(new EditorSeamEdge3D(0, 2), "tabs")],
            NetLayout: "connected",
            UnrollMode: "spanning",
            TabHeight: 7,
            HoleDiameter: 1.5,
            HoleSpacing: 6,
            HoleMargin: 3);
        var selectedFace = new SelectedFace3D(0, 0, "body-0", "face-0");

        var projection = await service.ProjectEdgesAsync(projectionRequest);
        var unfold = await service.UnfoldAsync(unfoldRequest);
        var distortion = await service.ComputeFaceDistortionAsync(mesh, selectedFace, "balanced");

        Assert.True(projection.IsSuccess);
        Assert.True(unfold.IsSuccess);
        Assert.True(distortion.IsSuccess);
        Assert.Same(projectionRequest, kernel.ProjectRequest);
        Assert.Same(unfoldRequest, kernel.UnfoldRequest);
        Assert.Equal((mesh, selectedFace, "balanced"), kernel.DistortionRequest);
        Assert.Equal("manual", kernel.UnfoldRequest!.SeamControlMode);
        Assert.Equal("connected", kernel.UnfoldRequest.NetLayout);
        Assert.Equal("holes", kernel.UnfoldRequest.SeamDecoration);
        Assert.Equal(7, kernel.UnfoldRequest.TabHeight);
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
    public async Task ProjectEdgesAsync_AppendsAfterExistingTwoDGeometryWithoutMutatingSource()
    {
        using var workspace = TestWorkspace.Create();
        var objPath = workspace.WriteText("triangle.obj", TriangleObj);
        var existingPath = workspace.GetPath("existing.dxf");
        WriteExistingLine(existingPath);
        var original = await File.ReadAllTextAsync(existingPath);
        var service = CreateService();
        var load = await service.LoadModelAsync(objPath);

        var projection = await service.ProjectEdgesAsync(new EditorProjectionRequest(
            load.SourceModelPath,
            "XY",
            0,
            FaceIndex: null,
            FaceBodyIndex: null,
            VisibleBodyIndices: [0],
            BodyOffsets: [],
            ExistingDxfPath: existingPath));

        Assert.True(projection.IsSuccess, projection.Message);
        var output = EditorDxfDocument.LoadPreviewDocument(projection.OutputPath!);
        Assert.True(output.Paths.Count > 1);
        AssertExistingLineAndAppendedGap(output);
        Assert.Equal(original, await File.ReadAllTextAsync(existingPath));
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
    public async Task UnfoldAsync_AppendsAfterExistingTwoDGeometryWithoutMutatingSource()
    {
        using var workspace = TestWorkspace.Create();
        var objPath = workspace.WriteText("triangle.obj", TriangleObj);
        var existingPath = workspace.GetPath("existing.dxf");
        WriteExistingLine(existingPath);
        var original = await File.ReadAllTextAsync(existingPath);
        var service = CreateService();
        var load = await service.LoadModelAsync(objPath);

        var unfold = await service.UnfoldAsync(new EditorUnfoldRequest(
            load.SourceModelPath,
            SelectedFaces: [],
            VisibleBodyIndices: [0],
            WholeBody: true,
            DistortionMode: "conformal",
            ExistingDxfPath: existingPath));

        Assert.True(unfold.IsSuccess, unfold.Message);
        var output = EditorDxfDocument.LoadPreviewDocument(unfold.OutputPath!);
        Assert.Equal(2, output.Paths.Count);
        AssertExistingLineAndAppendedGap(output);
        Assert.Equal(original, await File.ReadAllTextAsync(existingPath));
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
        Assert.Contains("app-owned OpenGeometry worker", result.Message, StringComparison.Ordinal);
    }

    private static OpenGeometryEditor3DOperationService CreateService(IStepGeometryKernelService? stepKernel = null)
    {
        var bridge = new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance);
        return new OpenGeometryEditor3DOperationService(
            NullLogger<OpenGeometryEditor3DOperationService>.Instance,
            bridge,
            stepKernel);
    }

    private static void WriteExistingLine(string path)
    {
        var line = new Editor2DPreviewPath("existing-line", "LINE", [new(0, 0), new(5, 0)], false, Start: new(0, 0));
        EditorDxfDocument.SavePreviewDocument(
            path,
            new Editor2DPreviewDocument(
                [line],
                new Editor2DBounds(0, 0, 5, 0),
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["LINE"] = 1 },
                []));
    }

    private static void AssertExistingLineAndAppendedGap(DxfPreviewDocument output)
    {
        var existing = Assert.Single(output.Paths, path => path.Points.Count == 2
            && path.Points[0] == new DxfPoint(0, 0)
            && path.Points[1] == new DxfPoint(5, 0));
        Assert.NotNull(existing);
        var appended = output.Paths.Where(path => !ReferenceEquals(path, existing)).ToArray();
        Assert.NotEmpty(appended);
        Assert.All(appended.SelectMany(path => path.Points), point => Assert.True(point.X >= 15.0 - 1e-6));
    }

    private sealed class RecordingStepKernel(TestWorkspace workspace) : IStepGeometryKernelService
    {
        public List<(string Existing, string Incoming)> CombineCalls { get; } = [];
        public List<string> CombinedPaths { get; } = [];
        public string? ImportedPath { get; private set; }
        public EditorProjectionRequest? ProjectRequest { get; private set; }
        public EditorUnfoldRequest? UnfoldRequest { get; private set; }
        public (string SourcePath, SelectedFace3D Face, string DistortionMode)? DistortionRequest { get; private set; }
        public bool FailCombine { get; init; }

        public Task<StepGeometryProtocolInfo> HandshakeAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StepGeometryCombineResult> CombineAsync(
            string existingSourcePath,
            string incomingSourcePath,
            CancellationToken cancellationToken = default)
        {
            CombineCalls.Add((existingSourcePath, incomingSourcePath));
            if (FailCombine)
            {
                return Task.FromResult(new StepGeometryCombineResult(
                    false,
                    "combine failed",
                    Failure: new GeometryKernelFailure(
                        GeometryKernelFailureCode.BackendFailure,
                        GeometryKernelOperation.Import,
                        "combine failed")));
            }
            var output = workspace.GetPath($"combined-{CombinedPaths.Count + 1}.step");
            File.WriteAllText(output, $"{existingSourcePath}|{incomingSourcePath}");
            CombinedPaths.Add(output);
            return Task.FromResult(new StepGeometryCombineResult(true, "combined", output, CombinedPaths.Count + 1));
        }

        public Task<StepGeometryImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
        {
            ImportedPath = sourcePath;
            var bodies = Enumerable.Range(0, CombineCalls.Count + 1)
                .Select(index => new Body3D(index, $"Body {index + 1}", []))
                .ToArray();
            return Task.FromResult(new StepGeometryImportResult(
                true,
                "imported",
                ViewportJson: "{}",
                ViewportBodies: bodies));
        }

        public Task<EditorOperationResult> ProjectAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default)
        {
            ProjectRequest = request;
            return Task.FromResult(new EditorOperationResult(true, "projected"));
        }

        public Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default)
        {
            UnfoldRequest = request;
            return Task.FromResult(new EditorOperationResult(true, "unfolded"));
        }

        public Task<EditorFaceDistortionResult> ComputeDistortionAsync(
            string sourcePath,
            SelectedFace3D face,
            string distortionMode,
            CancellationToken cancellationToken = default)
        {
            DistortionRequest = (sourcePath, face, distortionMode);
            return Task.FromResult(new EditorFaceDistortionResult(true, "distortion"));
        }
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
