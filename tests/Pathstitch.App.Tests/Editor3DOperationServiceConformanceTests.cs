using System.Globalization;
using System.Text.Json;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

/// <summary>
/// Kernel-neutral behavior contract. A future packaged OCCT adapter and the temporary
/// legacy-reference adapter should inherit this fixture and supply their service seam.
/// These mesh fixtures intentionally do not assert STEP/B-rep parity.
/// </summary>
public abstract class Editor3DOperationServiceConformanceTests
{
    protected abstract IEditor3DOperationService CreateService();

    [Fact]
    public async Task Import_ProducesStableDocumentScopedBodyAndFaceReferences()
    {
        using var fixture = KernelFixture.Create();
        var source = fixture.WriteText("planar.obj", PlanarQuadObj);
        var service = CreateService();

        var first = await service.LoadModelAsync(source);
        var second = await service.LoadModelAsync(source);

        AssertSuccess(first.IsSuccess, first.Message, first.Failure);
        AssertSuccess(second.IsSuccess, second.Message, second.Failure);
        Assert.Equal(source, first.SourceModelPath);
        Assert.NotNull(first.ViewportJson);
        Assert.NotNull(first.Bodies);
        Assert.Single(first.Bodies);
        Assert.Single(second.Bodies!);
        Assert.Single(first.Bodies[0].Faces);
        Assert.Equal(first.Bodies[0].BodyIndex, second.Bodies![0].BodyIndex);
        Assert.Equal(
            first.Bodies[0].Faces.Select(face => face.FaceIndex),
            second.Bodies[0].Faces.Select(face => face.FaceIndex));
    }

    [Fact]
    public async Task Projection_ProducesAReadableNonEmptyTwoDArtifact()
    {
        using var fixture = KernelFixture.Create();
        var service = CreateService();
        var load = await service.LoadModelAsync(fixture.WriteText("projection.obj", PlanarQuadObj));
        AssertSuccess(load.IsSuccess, load.Message, load.Failure);

        var result = await service.ProjectEdgesAsync(new EditorProjectionRequest(
            load.SourceModelPath,
            "XY",
            Offset: 0,
            FaceIndex: null,
            FaceBodyIndex: null,
            VisibleBodyIndices: [0],
            BodyOffsets: []));

        AssertSuccess(result.IsSuccess, result.Message, result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.OutputPath));
        Assert.True(File.Exists(result.OutputPath));
        var dxf = await File.ReadAllTextAsync(result.OutputPath);
        // The mesh seam may retain or merge the triangulation diagonal. Both forms must
        // preserve the closed outer boundary and never lose all interior topology.
        var projectedLoopCount = CountDxfEntities(dxf, "LWPOLYLINE");
        Assert.InRange(projectedLoopCount, 2, 3);
        Assert.Equal(projectedLoopCount, CountClosedPolylines(dxf));
        var points = ReadDxfPolylines(dxf).SelectMany(polyline => polyline).ToArray();
        Assert.InRange(points.Min(point => point.X), -1e-9, 0.01);
        Assert.InRange(points.Min(point => point.Y), -1e-9, 0.01);
        Assert.InRange(points.Max(point => point.X), 9.99, 10.02);
        Assert.InRange(points.Max(point => point.Y), 9.99, 10.02);
    }

    [Fact]
    public async Task Unfold_PreservesEveryRepresentativeMeshTriangleAsAClosedLoop()
    {
        using var fixture = KernelFixture.Create();
        var service = CreateService();
        var load = await service.LoadModelAsync(fixture.WriteText("unfold.obj", PlanarQuadObj));
        AssertSuccess(load.IsSuccess, load.Message, load.Failure);

        var result = await service.UnfoldAsync(new EditorUnfoldRequest(
            load.SourceModelPath,
            SelectedFaces: [],
            VisibleBodyIndices: [0],
            WholeBody: true,
            DistortionMode: "balanced"));

        AssertSuccess(result.IsSuccess, result.Message, result.Failure);
        var dxf = await File.ReadAllTextAsync(result.OutputPath!);
        Assert.Equal(2, CountDxfEntities(dxf, "LWPOLYLINE"));
        Assert.Equal(2, CountClosedPolylines(dxf));
        const double fixtureArea = 100.0;
        const double dxfSerializationTolerance = 0.001;
        Assert.InRange(
            ReadDxfPolylines(dxf).Sum(PolygonArea),
            fixtureArea - dxfSerializationTolerance,
            fixtureArea + dxfSerializationTolerance);
    }

    [Fact]
    public async Task Distortion_ReturnsOneFiniteBoundedValuePerMeshVertexReference()
    {
        using var fixture = KernelFixture.Create();
        var service = CreateService();
        var load = await service.LoadModelAsync(fixture.WriteText("distortion.obj", WarpedQuadObj));
        AssertSuccess(load.IsSuccess, load.Message, load.Failure);

        var result = await service.ComputeFaceDistortionAsync(
            load.SourceModelPath,
            new SelectedFace3D(0, 0),
            "balanced");

        AssertSuccess(result.IsSuccess, result.Message, result.Failure);
        using var json = JsonDocument.Parse(result.DistortionJson!);
        var values = json.RootElement.GetProperty("distortion").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        Assert.Equal(6, values.Length);
        Assert.All(values, value => Assert.True(double.IsFinite(value)));
        Assert.All(values, value => Assert.InRange(value, 0.0, 1.0));
        Assert.Contains(values, value => value > 0.01);
    }

    [Fact]
    public async Task UnsupportedStepImport_ReturnsTypedFailureWithoutClaimingBrepParity()
    {
        using var fixture = KernelFixture.Create();
        var service = CreateService();
        var result = await service.LoadModelAsync(fixture.WriteText("unsupported.step", "ISO-10303-21;\nEND-ISO-10303-21;"));

        AssertFailure(result.IsSuccess, result.Message, result.Failure,
            GeometryKernelFailureCode.UnsupportedFormat, GeometryKernelOperation.Import);
        Assert.Null(result.SourceModelPath);
        Assert.Null(result.ViewportJson);
        Assert.Null(result.Bodies);
    }

    [Fact]
    public async Task MissingSources_ReturnTypedFailuresForEveryOperation()
    {
        var service = CreateService();
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.obj");

        var import = await service.LoadModelAsync(missing);
        AssertFailure(import.IsSuccess, import.Message, import.Failure,
            GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Import);

        var projection = await service.ProjectEdgesAsync(new EditorProjectionRequest(
            missing, "XY", 0, null, null, [0], []));
        AssertFailure(projection.IsSuccess, projection.Message, projection.Failure,
            GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Projection);

        var unfold = await service.UnfoldAsync(new EditorUnfoldRequest(missing, [], [0], true, "balanced"));
        AssertFailure(unfold.IsSuccess, unfold.Message, unfold.Failure,
            GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Unfold);

        var distortion = await service.ComputeFaceDistortionAsync(missing, new SelectedFace3D(0, 0), "balanced");
        AssertFailure(distortion.IsSuccess, distortion.Message, distortion.Failure,
            GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Distortion);
    }

    [Fact]
    public async Task PreCancelledRequests_UseTheDotNetCancellationContract()
    {
        using var fixture = KernelFixture.Create();
        var source = fixture.WriteText("cancel.obj", PlanarQuadObj);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().LoadModelAsync(source, cancellation.Token));
    }

    private static void AssertSuccess(bool isSuccess, string message, GeometryKernelFailure? failure)
    {
        Assert.True(isSuccess, message);
        Assert.Null(failure);
    }

    private static void AssertFailure(
        bool isSuccess,
        string message,
        GeometryKernelFailure? failure,
        GeometryKernelFailureCode expectedCode,
        GeometryKernelOperation expectedOperation)
    {
        Assert.False(isSuccess);
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.NotNull(failure);
        Assert.Equal(expectedCode, failure.Code);
        Assert.Equal(expectedOperation, failure.Operation);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
    }

    private static int CountDxfEntities(string dxf, string entityName)
    {
        var lines = dxf.Split('\n', StringSplitOptions.TrimEntries);
        return Enumerable.Range(0, Math.Max(0, lines.Length - 1))
            .Count(index => lines[index] == "0" && string.Equals(lines[index + 1], entityName, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountClosedPolylines(string dxf)
    {
        var lines = dxf.Split('\n', StringSplitOptions.TrimEntries);
        var count = 0;
        var inPolyline = false;
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (lines[index] == "0")
            {
                inPolyline = string.Equals(lines[index + 1], "LWPOLYLINE", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inPolyline && lines[index] == "70" && lines[index + 1] == "1")
            {
                count++;
                inPolyline = false;
            }
        }

        return count;
    }

    private static IReadOnlyList<IReadOnlyList<(double X, double Y)>> ReadDxfPolylines(string dxf)
    {
        var lines = dxf.Split('\n', StringSplitOptions.TrimEntries);
        var result = new List<IReadOnlyList<(double X, double Y)>>();
        List<(double X, double Y)>? current = null;
        for (var index = 0; index + 1 < lines.Length; index += 2)
        {
            var code = lines[index];
            var value = lines[index + 1];
            if (code == "0")
            {
                if (current is not null)
                    result.Add(current);
                current = string.Equals(value, "LWPOLYLINE", StringComparison.OrdinalIgnoreCase) ? [] : null;
                continue;
            }

            if (current is not null
                && code == "10"
                && index + 3 < lines.Length
                && lines[index + 2] == "20")
            {
                current.Add((
                    double.Parse(value, CultureInfo.InvariantCulture),
                    double.Parse(lines[index + 3], CultureInfo.InvariantCulture)));
                index += 2;
            }
        }

        if (current is not null)
            result.Add(current);
        return result;
    }

    private static double PolygonArea(IReadOnlyList<(double X, double Y)> points)
    {
        var twiceArea = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            twiceArea += points[index].X * next.Y - next.X * points[index].Y;
        }

        return Math.Abs(twiceArea) / 2.0;
    }

    private const string PlanarQuadObj = """
        v 0 0 0
        v 10 0 0
        v 10 10 0
        v 0 10 0
        f 1 2 3
        f 1 3 4
        """;

    private const string WarpedQuadObj = """
        v 0 0 0
        v 10 0 0
        v 0 10 0
        v 10 10 5
        f 1 2 3
        f 2 4 3
        """;

    protected sealed class KernelFixture : IDisposable
    {
        private KernelFixture(string directory) => Directory = directory;

        private string Directory { get; }

        public static KernelFixture Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Pathstitch-Kernel-Conformance",
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            System.IO.Directory.CreateDirectory(directory);
            return new KernelFixture(directory);
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
                // Best-effort fixture cleanup.
            }
        }
    }
}

public sealed class OpenGeometryEditor3DOperationServiceConformanceTests
    : Editor3DOperationServiceConformanceTests
{
    protected override IEditor3DOperationService CreateService()
        => new OpenGeometryEditor3DOperationService(
            NullLogger<OpenGeometryEditor3DOperationService>.Instance,
            new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance));
}
