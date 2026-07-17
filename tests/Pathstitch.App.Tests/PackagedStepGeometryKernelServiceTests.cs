using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class PackagedStepGeometryKernelServiceTests
{
    [Fact]
    public async Task PinnedWorker_StartupAndRepresentativeImportStayWithinReleaseBudgets()
    {
        var runtime = TryFindPinnedRuntime();
        if (runtime is null)
            return;
        using var service = CreateService(runtime);
        var fixture = FindRepositoryFile("tests", "Pathstitch.App.Tests", "Fixtures", "box-cylinder.step");

        var timer = Stopwatch.StartNew();
        var handshake = await service.HandshakeAsync();
        var startup = timer.Elapsed;
        timer.Restart();
        var imported = await service.ImportAsync(fixture);
        var import = timer.Elapsed;

        Assert.Equal("packaged-pythonocc-occt", handshake.Backend);
        Assert.True(imported.IsSuccess, imported.Message);
        Assert.True(startup <= TimeSpan.FromSeconds(15), $"Worker startup took {startup.TotalSeconds:F3}s.");
        Assert.True(import <= TimeSpan.FromSeconds(45), $"Representative STEP import took {import.TotalSeconds:F3}s.");
    }

    [Fact]
    public async Task PinnedWorker_CombinesStepDocumentsAndReimportsAllBodies()
    {
        var runtime = TryFindPinnedRuntime();
        if (runtime is null)
            return;
        using var service = CreateService(runtime);
        var fixture = FindRepositoryFile("tests", "Pathstitch.App.Tests", "Fixtures", "box-cylinder.step");

        var handshake = await service.HandshakeAsync();
        var combined = await service.CombineAsync(fixture, fixture);
        try
        {
            Assert.Contains("step-combine", handshake.Capabilities);
            Assert.True(combined.IsSuccess, combined.Message);
            Assert.Equal(4, combined.BodyCount);
            Assert.True(File.Exists(combined.OutputPath));

            var imported = await service.ImportAsync(combined.OutputPath!);
            Assert.True(imported.IsSuccess, imported.Message);
            Assert.Equal(4, imported.ViewportBodies?.Count);
            Assert.Equal(4, imported.Document?.Bodies.Count);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(combined.OutputPath))
                File.Delete(combined.OutputPath);
        }
    }

    [Fact]
    public async Task PinnedWorker_ImportsStableExactTopologyAndRunsLifecycleOperations()
    {
        var runtime = TryFindPinnedRuntime();
        if (runtime is null)
            return; // The release/package job supplies this runtime and executes the same test.
        using var service = CreateService(runtime);
        var fixture = FindRepositoryFile("tests", "Pathstitch.App.Tests", "Fixtures", "box-cylinder.step");

        var handshake = await service.HandshakeAsync();
        Assert.Equal(1, handshake.ProtocolVersion);
        Assert.Equal("packaged-pythonocc-occt", handshake.Backend);
        Assert.Contains("brep-topology", handshake.Capabilities);
        Assert.Contains("exact-curves", handshake.Capabilities);
        Assert.Contains("pcurves", handshake.Capabilities);

        var first = await service.ImportAsync(fixture);
        var second = await service.ImportAsync(fixture);
        Assert.True(first.IsSuccess, first.Message);
        Assert.NotNull(first.Document);
        Assert.Equal(2, first.Document.Bodies.Count);
        Assert.Equal([6, 3], first.Document.Bodies.Select(body => body.Faces.Count).ToArray());
        Assert.Equal([12, 3], first.Document.Bodies.Select(body => body.Edges.Count).ToArray());
        Assert.All(first.Document.Bodies, body =>
        {
            Assert.NotEmpty(body.Shells);
            Assert.All(body.Shells, shell =>
            {
                Assert.NotEqual("unknown", shell.Orientation);
                Assert.NotEmpty(shell.FaceIds);
                Assert.All(shell.FaceIds, faceId => Assert.Contains(body.Faces, face => face.Id == faceId));
            });
        });
        Assert.Contains(first.Document.Bodies.SelectMany(body => body.Faces), face => face.SurfaceKind == "plane");
        Assert.Contains(first.Document.Bodies.SelectMany(body => body.Faces), face => face.SurfaceKind == "cylinder");
        Assert.Contains(first.Document.Bodies.SelectMany(body => body.Edges), edge => edge.CurveKind == "line");
        Assert.Contains(first.Document.Bodies.SelectMany(body => body.Edges), edge => edge.CurveKind == "circle");
        Assert.True(first.Document.Bodies.SelectMany(body => body.Edges).Sum(edge => edge.PCurves.Count) > 0);
        Assert.All(first.Document.Bodies.SelectMany(body => body.Edges), edge => Assert.NotEmpty(edge.AdjacentFaceIds));
        Assert.Equal(first.Document.DocumentId, second.Document!.DocumentId);
        Assert.Equal(
            first.Document.Bodies.SelectMany(body => body.Faces).Select(face => face.Id),
            second.Document.Bodies.SelectMany(body => body.Faces).Select(face => face.Id));
        Assert.Equal(2, first.ViewportBodies!.Count);

        var projection = await service.ProjectAsync(new EditorProjectionRequest(
            fixture, "XY", 0, null, null, [0, 1], []));
        Assert.True(projection.IsSuccess, projection.Message);
        Assert.True(File.Exists(projection.OutputPath));
        Assert.True(new FileInfo(projection.OutputPath!).Length > 100);
        Assert.NotNull(projection.Geometry);
        Assert.Equal(first.Document.DocumentId, projection.Geometry.DocumentId);
        Assert.NotEmpty(projection.Geometry.Curves);
        Assert.False(projection.Geometry.IsApproximation, projection.Geometry.ApproximationReason);
        Assert.Contains(projection.Geometry.Curves, curve => curve.Kind is "line" or "circle" or "ellipse" or "bspline");
        Assert.All(projection.Geometry.Curves, curve =>
        {
            Assert.StartsWith(first.Document.DocumentId, curve.Id, StringComparison.Ordinal);
            Assert.Equal(first.Document.DocumentId, curve.Provenance.DocumentId);
            Assert.NotEmpty(curve.Provenance.BodyIds);
            Assert.NotNull(curve.DisplayApproximation);
            Assert.NotEmpty(curve.DisplayApproximation.Points);
            Assert.NotEmpty(curve.Geometry.Scalars);
        });
        var repeatedProjection = await service.ProjectAsync(new EditorProjectionRequest(
            fixture, "XY", 0, null, null, [0, 1], []));
        Assert.Equal(
            projection.Geometry.Curves.Select(curve => curve.Id),
            repeatedProjection.Geometry!.Curves.Select(curve => curve.Id));

        var selectedFace = first.Document.Bodies[0].Faces[0];
        var unfold = await service.UnfoldAsync(new EditorUnfoldRequest(
            fixture, [new SelectedFace3D(0, 0, first.Document.Bodies[0].Id, selectedFace.Id)], [0],
            WholeBody: false, DistortionMode: "conformal", SelectedFaceIds: [selectedFace.Id],
            VisibleBodyIds: [first.Document.Bodies[0].Id]));
        Assert.True(unfold.IsSuccess, unfold.Message);
        Assert.True(File.Exists(unfold.OutputPath));
        Assert.True(new FileInfo(unfold.OutputPath!).Length > 100);
        Assert.NotNull(unfold.Geometry);
        Assert.NotEmpty(unfold.Geometry.Curves);
        Assert.NotEmpty(unfold.Geometry.Loops);
        Assert.Contains(unfold.Geometry.Curves, curve => curve.Kind is "line" or "arc" or "circle");
        Assert.All(unfold.Geometry.Curves, curve =>
        {
            Assert.Contains(selectedFace.Id, curve.Provenance.FaceIds);
            Assert.NotEmpty(curve.Provenance.EdgeIds);
        });
        Assert.All(unfold.Geometry.Loops, loop =>
        {
            Assert.True(loop.Closed);
            Assert.NotEmpty(loop.CurveIds);
            Assert.Contains(selectedFace.Id, loop.Provenance.FaceIds);
        });

        var distortion = await service.ComputeDistortionAsync(fixture, new SelectedFace3D(0, 0), "conformal");
        Assert.True(distortion.IsSuccess, distortion.Message);
        using var distortionJson = JsonDocument.Parse(distortion.DistortionJson!);
        var values = distortionJson.RootElement.GetProperty("distortion").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        Assert.NotEmpty(values);
        Assert.All(values, value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public async Task PinnedWorker_MalformedStepReturnsTypedInvalidInput()
    {
        var runtime = TryFindPinnedRuntime();
        if (runtime is null)
            return;
        var malformed = Path.Combine(Path.GetTempPath(), $"malformed-{Guid.NewGuid():N}.step");
        await File.WriteAllTextAsync(malformed, "this is not an ISO-10303-21 document");
        try
        {
            using var service = CreateService(runtime);
            var result = await service.ImportAsync(malformed);
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Failure);
            Assert.Equal(GeometryKernelFailureCode.InvalidInput, result.Failure.Code);
            Assert.Contains("could not be parsed", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(malformed);
        }
    }

    [Fact]
    public async Task StepWorkspace_SaveReopenPreservesOriginalBytesTopologyIdsAndOfflineOperations()
    {
        var runtime = TryFindPinnedRuntime();
        if (runtime is null)
            return;
        using var service = CreateService(runtime);
        var sourceCopy = Path.Combine(Path.GetTempPath(), $"step-source-{Guid.NewGuid():N}.step");
        var project = Path.Combine(Path.GetTempPath(), $"step-project-{Guid.NewGuid():N}.stch");
        File.Copy(FindRepositoryFile("tests", "Pathstitch.App.Tests", "Fixtures", "box-cylinder.step"), sourceCopy);
        try
        {
            var imported = await service.ImportAsync(sourceCopy);
            Assert.True(imported.IsSuccess, imported.Message);
            var selectedFace = new SelectedFace3D(
                0, 0, imported.Document!.Bodies[0].Id, imported.Document.Bodies[0].Faces[0].Id);
            var persistence = new Project3DStateService();
            await persistence.SaveAsync(project, new Project3DState(
                imported.ViewportJson,
                imported.ViewportBodies!,
                [],
                SourceModelPath: sourceCopy,
                ThreeDWorkspaceState: Editor3DWorkspaceState.Empty with { SelectedFaces = [selectedFace] },
                StepTopology: imported.Document));
            File.Delete(sourceCopy);

            var restored = await persistence.LoadAsync(project);
            Assert.NotNull(restored.StepTopology);
            Assert.Equal(imported.Document!.DocumentId, restored.StepTopology.DocumentId);
            Assert.Equal(selectedFace, Assert.Single(restored.ThreeDWorkspaceState!.SelectedFaces));
            Assert.True(File.Exists(restored.SourceModelPath));
            Assert.Equal(".step", Path.GetExtension(restored.SourceModelPath), ignoreCase: true);

            // Recreate the service to prove reopen/offline behavior is not relying on
            // the importing service's document cache or live worker process.
            using var reopenedService = CreateService(runtime);
            var reopenedImport = await reopenedService.ImportAsync(restored.SourceModelPath);
            Assert.True(reopenedImport.IsSuccess, reopenedImport.Message);
            Assert.Equal(imported.Document.DocumentId, reopenedImport.Document!.DocumentId);
            Assert.Equal(
                imported.Document.Bodies.SelectMany(body => body.Faces).Select(face => face.Id),
                reopenedImport.Document.Bodies.SelectMany(body => body.Faces).Select(face => face.Id));

            var projection = await reopenedService.ProjectAsync(new EditorProjectionRequest(
                restored.SourceModelPath, "XY", 0, selectedFace.FaceIndex, selectedFace.BodyIndex, [0, 1], [],
                FaceId: selectedFace.FaceId, VisibleBodyIds: restored.StepTopology.Bodies.Select(body => body.Id).ToArray()));
            Assert.True(projection.IsSuccess, projection.Message);
            var unfold = await reopenedService.UnfoldAsync(new EditorUnfoldRequest(
                restored.SourceModelPath, restored.ThreeDWorkspaceState.SelectedFaces, [0], false, "conformal",
                SelectedFaceIds: restored.ThreeDWorkspaceState.SelectedFaces.Select(face => face.FaceId!).ToArray(),
                VisibleBodyIds: [selectedFace.BodyId!]));
            Assert.True(unfold.IsSuccess, unfold.Message);
            Assert.Contains(selectedFace.FaceId!, unfold.Geometry!.Provenance.FaceIds);
        }
        finally
        {
            if (File.Exists(sourceCopy)) File.Delete(sourceCopy);
            if (File.Exists(project)) File.Delete(project);
        }
    }

    [Fact]
    public async Task PinnedWorker_PreservesConeInnerWireAndExactBSplineControlData()
    {
        var runtime = TryFindPinnedRuntime();
        if (runtime is null)
            return;
        using var service = CreateService(runtime);
        var analytic = await service.ImportAsync(FindRepositoryFile(
            "tests", "Pathstitch.App.Tests", "Fixtures", "analytic-multibody-hole.step"));
        Assert.True(analytic.IsSuccess, analytic.Message);
        Assert.Equal(4, analytic.Document!.Bodies.Count);
        Assert.Equal([6, 3, 3, 7], analytic.Document.Bodies.Select(body => body.Faces.Count).ToArray());
        Assert.Contains(analytic.Document.Bodies.SelectMany(body => body.Faces), face => face.SurfaceKind == "cone");
        Assert.Contains(analytic.Document.Bodies.SelectMany(body => body.Faces), face => face.WireIds.Count == 2);
        Assert.All(analytic.Document.Bodies.SelectMany(body => body.Wires), wire => Assert.NotEmpty(wire.Edges));

        var freeform = await service.ImportAsync(FindRepositoryFile(
            "tests", "Pathstitch.App.Tests", "Fixtures", "bspline-edge-face.step"));
        Assert.True(freeform.IsSuccess, freeform.Message);
        var spline = Assert.Single(freeform.Document!.Bodies.SelectMany(body => body.Edges), edge => edge.CurveKind == "bspline");
        Assert.Equal(5, spline.CurveData.Poles.Count);
        Assert.Equal([0.0, 1.0], spline.CurveData.Knots);
        Assert.Equal([5, 5], spline.CurveData.Multiplicities);
        Assert.Equal([1.0, 1.0, 1.0, 1.0, 1.0], spline.CurveData.Weights);
        Assert.Contains(spline.PCurves, pcurve => pcurve.CurveData.Poles.Count > 0 || pcurve.CurveData.Scalars.Count > 0);
    }

    [Fact]
    public async Task PinnedWorker_UnfoldsDevelopableAndFreeformBoundariesFromExactPCurves()
    {
        var runtime = TryFindPinnedRuntime();
        if (runtime is null)
            return;
        using var service = CreateService(runtime);
        var analyticPath = FindRepositoryFile("tests", "Pathstitch.App.Tests", "Fixtures", "analytic-multibody-hole.step");
        var analytic = await service.ImportAsync(analyticPath);
        Assert.True(analytic.IsSuccess, analytic.Message);

        async Task<EditorOperationResult> Unfold(int bodyIndex, StepFaceTopology face)
            => await service.UnfoldAsync(new EditorUnfoldRequest(
                analyticPath,
                [new SelectedFace3D(bodyIndex, analytic.Document!.Bodies[bodyIndex].Faces.ToList().IndexOf(face), analytic.Document.Bodies[bodyIndex].Id, face.Id)],
                [bodyIndex], false, "conformal", [face.Id], [analytic.Document.Bodies[bodyIndex].Id]));

        var plane = await Unfold(0, analytic.Document!.Bodies[0].Faces.Where(face => face.SurfaceKind == "plane").MaxBy(face => face.Area)!);
        Assert.False(plane.Geometry!.IsApproximation, plane.Geometry.ApproximationReason);
        Assert.All(plane.Geometry.Curves, curve => Assert.Equal("line", curve.Kind));
        var longestPlaneLine = plane.Geometry.Curves.Max(CurveChordLength);
        Assert.Equal(100.0, longestPlaneLine, 3);

        var cylinder = await Unfold(1, analytic.Document.Bodies[1].Faces.First(face => face.SurfaceKind == "cylinder"));
        Assert.False(cylinder.Geometry!.IsApproximation, cylinder.Geometry.ApproximationReason);
        Assert.All(cylinder.Geometry.Curves, curve => Assert.Equal("line", curve.Kind));
        Assert.Equal(2.0 * Math.PI * 20.0, cylinder.Geometry.Curves.Max(CurveChordLength), 3);

        var cone = await Unfold(2, analytic.Document.Bodies[2].Faces.First(face => face.SurfaceKind == "cone"));
        Assert.False(cone.Geometry!.IsApproximation, cone.Geometry.ApproximationReason);
        Assert.Equal(2, cone.Geometry.Curves.Count(curve => curve.Kind == "arc"));
        Assert.All(cone.Geometry.Curves.Where(curve => curve.Kind == "arc"),
            curve => Assert.True(curve.Geometry.Scalars["radius"] > 0));

        var holeFace = analytic.Document.Bodies[3].Faces.First(face => face.WireIds.Count == 2);
        var hole = await Unfold(3, holeFace);
        Assert.False(hole.Geometry!.IsApproximation, hole.Geometry.ApproximationReason);
        var holeEllipse = Assert.Single(hole.Geometry.Curves, curve => curve.Kind == "ellipse");
        Assert.Equal(10.0, Length(holeEllipse.Geometry.Scalars["xAxisX"], holeEllipse.Geometry.Scalars["xAxisY"]), 3);
        Assert.Contains(holeFace.Id, holeEllipse.Provenance.FaceIds);
        Assert.NotEmpty(holeEllipse.Provenance.EdgeIds);

        var freeformPath = FindRepositoryFile("tests", "Pathstitch.App.Tests", "Fixtures", "bspline-edge-face.step");
        var freeform = await service.ImportAsync(freeformPath);
        var freeformFace = freeform.Document!.Bodies[0].Faces[0];
        var freeformResult = await service.UnfoldAsync(new EditorUnfoldRequest(
            freeformPath, [new SelectedFace3D(0, 0, freeform.Document.Bodies[0].Id, freeformFace.Id)], [0], false,
            "conformal", [freeformFace.Id], [freeform.Document.Bodies[0].Id]));
        Assert.False(freeformResult.Geometry!.IsApproximation, freeformResult.Geometry.ApproximationReason);
        var spline = Assert.Single(freeformResult.Geometry.Curves, curve => curve.Kind == "bspline");
        Assert.Equal(4.0, spline.Geometry.Scalars["degree"]);
        Assert.Equal(5, spline.Geometry.Poles.Count);
        Assert.Equal([0.0, 1.0], spline.Geometry.Knots);
        Assert.Equal([5, 5], spline.Geometry.Multiplicities);
        Assert.Equal([1.0, 1.0, 1.0, 1.0, 1.0], spline.Geometry.Weights);
    }

    [Fact]
    public async Task PinnedWorker_ProtocolMatchesLegacyReferenceWithinDocumentedTolerances()
    {
        var runtime = TryFindPinnedRuntime();
        if (runtime is null)
            return;
        var evidencePath = Path.Combine(Path.GetTempPath(), $"step-parity-{Guid.NewGuid():N}.json");
        try
        {
            var start = new ProcessStartInfo(runtime.PythonExecutable)
            {
                WorkingDirectory = runtime.ModuleRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-B");
            start.ArgumentList.Add(FindRepositoryFile("tests", "Pathstitch.App.Tests", "Fixtures", "verify_step_parity.py"));
            start.ArgumentList.Add("--fixtures");
            start.ArgumentList.Add(Path.GetDirectoryName(FindRepositoryFile(
                "tests", "Pathstitch.App.Tests", "Fixtures", "box-cylinder.step"))!);
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(evidencePath);
            start.Environment["PYTHONPATH"] = runtime.ModuleRoot;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, $"Parity harness failed.\n{await stdout}\n{await stderr}");

            using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(evidencePath));
            var root = evidence.RootElement;
            Assert.Equal(0.0, root.GetProperty("importAreaMaxDeviation").GetDouble());
            Assert.Equal(0.0, root.GetProperty("distortionMaxDeviation").GetDouble());
            Assert.All(root.GetProperty("unfold").EnumerateArray(), item =>
                Assert.True(item.GetProperty("lengthDeviation").GetDouble() <= 1e-6));
            Assert.Contains(root.GetProperty("unfold").EnumerateArray(), item =>
                item.GetProperty("surfaceKind").GetString() == "bspline-boundary"
                && item.GetProperty("canonicalCurveKinds").EnumerateArray().Any(kind => kind.GetString() == "bspline"));
            Assert.NotEmpty(root.GetProperty("acceptedDifferences").EnumerateArray());
        }
        finally
        {
            if (File.Exists(evidencePath)) File.Delete(evidencePath);
        }
    }

    private static double CurveChordLength(StepCurve2D curve)
    {
        var values = curve.Geometry.Scalars;
        return Length(values["endX"] - values["startX"], values["endY"] - values["startY"]);
    }

    private static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

    [Fact]
    public async Task EditorOperationSeam_RoutesStepImportToPackagedWorker()
    {
        var runtime = TryFindPinnedRuntime();
        if (runtime is null)
            return;
        using var step = CreateService(runtime);
        var editor = new OpenGeometryEditor3DOperationService(
            NullLogger<OpenGeometryEditor3DOperationService>.Instance,
            new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance),
            step);

        var result = await editor.LoadModelAsync(FindRepositoryFile(
            "tests", "Pathstitch.App.Tests", "Fixtures", "analytic-multibody-hole.step"));

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.StepTopology);
        Assert.Equal(4, result.Bodies!.Count);
        Assert.Equal(4, result.StepTopology.Bodies.Count);
    }

    [Fact]
    public void RuntimePackaging_IsLockedForBothReleaseRidsAndNeverUsesPathPython()
    {
        var windowsLock = File.ReadAllText(FindRepositoryFile("geometry-worker", "locks", "win-x64.lock"));
        var macLock = File.ReadAllText(FindRepositoryFile("geometry-worker", "locks", "osx-arm64.lock"));
        var resolver = File.ReadAllText(FindRepositoryFile("src", "Pathstitch.App", "Services", "PackagedStepGeometryKernelService.cs"));
        var project = File.ReadAllText(FindRepositoryFile("src", "Pathstitch.App", "Pathstitch.App.csproj"));
        var runtimeSpec = File.ReadAllText(FindRepositoryFile("geometry-worker", "runtime-spec.json"));

        Assert.Contains("@EXPLICIT", windowsLock, StringComparison.Ordinal);
        Assert.Contains("pythonocc-core", windowsLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@EXPLICIT", macLock, StringComparison.Ordinal);
        Assert.Contains("pythonocc-core", macLock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetEnvironmentVariable(\"PATH\")", resolver, StringComparison.Ordinal);
        Assert.Contains("GeometryWorker", project, StringComparison.Ordinal);
        Assert.Contains("step-combine", runtimeSpec, StringComparison.Ordinal);
    }

    private static PackagedStepGeometryKernelService CreateService(GeometryWorkerRuntime runtime)
        => new(
            NullLogger<PackagedStepGeometryKernelService>.Instance,
            new FixedRuntimeResolver(runtime));

    private static GeometryWorkerRuntime? TryFindPinnedRuntime()
    {
        var repository = FindRepositoryDirectory();
        var moduleRootOverride = Environment.GetEnvironmentVariable("PATHSTITCH_STEP_TEST_MODULE_ROOT");
        var rid = RuntimeInformation.RuntimeIdentifier;
        var roots = new[]
        {
            Path.Combine(repository, "artifacts", "geometry-worker", rid),
            Path.Combine(Path.GetTempPath(), "pathstitch-geometry-worker-win-x64"),
            Path.Combine(Path.GetTempPath(), "pathstitch-occt-env"),
        };
        foreach (var root in roots)
        {
            var python = OperatingSystem.IsWindows()
                ? Path.Combine(root, "python.exe")
                : Path.Combine(root, "bin", "python3.11");
            if (File.Exists(python)
                && File.Exists(Path.Combine(root, "pathstitch_core", "geometry_worker.py")))
                return new GeometryWorkerRuntime(python,
                    string.IsNullOrWhiteSpace(moduleRootOverride) ? root : moduleRootOverride);
        }

        return null;
    }

    private static string FindRepositoryDirectory()
        => Path.GetDirectoryName(FindRepositoryFile("PathstitchCross.slnx"))!;

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(pathParts));
    }

    private sealed class FixedRuntimeResolver(GeometryWorkerRuntime runtime) : IGeometryWorkerRuntimeResolver
    {
        public GeometryWorkerRuntime? Resolve() => runtime;
    }
}
