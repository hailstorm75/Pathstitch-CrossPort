using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging;
using Occt;

namespace Pathstitch.App.Services;

public sealed class OcctEditor3DOperationService(
    ILogger<OcctEditor3DOperationService> logger) : IEditor3DOperationService
{
    private const double BodyMeshDeflection = 0.05;
    private const double ProjectionGapMillimeters = 20.0;
    private const double OutputGapMillimeters = 10.0;

    private readonly ILogger<OcctEditor3DOperationService> _logger = logger;

    public Task<EditorModelLoadResult> LoadModelAsync(string sourceModelPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sourceModelPath) || !File.Exists(sourceModelPath))
            return Task.FromResult(new EditorModelLoadResult(false, "The selected 3D source model could not be found."));

        try
        {
            var scene = BuildViewportScene(sourceModelPath);
            if (scene.Bodies.Count == 0)
                return Task.FromResult(new EditorModelLoadResult(false, "The 3D source model did not contain any importable bodies."));

            return Task.FromResult(new EditorModelLoadResult(
                true,
                $"Loaded {scene.Bodies.Count} body/bodies from {Path.GetFileName(sourceModelPath)}.",
                SourceModelPath: sourceModelPath,
                StepJson: scene.ViewportJson,
                Bodies: scene.Bodies));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load 3D source model {SourceModelPath} through Occt.NET", sourceModelPath);
            return Task.FromResult(new EditorModelLoadResult(false, $"3D model load failed: {ex.Message}"));
        }
    }

    public async Task<EditorModelLoadResult> LoadModelsAsync(
        IReadOnlyList<string> sourceModelPaths,
        string? existingSourceModelPath = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPaths = sourceModelPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPaths.Length == 0)
            return new EditorModelLoadResult(false, "No valid 3D source models were selected.");

        if (normalizedPaths.Length == 1
            && (string.IsNullOrWhiteSpace(existingSourceModelPath) || !File.Exists(existingSourceModelPath)))
        {
            return await LoadModelAsync(normalizedPaths[0], cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var existingSourceExists = !string.IsNullOrWhiteSpace(existingSourceModelPath) && File.Exists(existingSourceModelPath);
            var currentCombinedPath = existingSourceExists
                ? existingSourceModelPath!
                : normalizedPaths[0];

            var incomingIndex = existingSourceExists ? 0 : 1;
            for (var i = incomingIndex; i < normalizedPaths.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentCombinedPath = CombineModels(currentCombinedPath, normalizedPaths[i]);
            }

            var loadResult = await LoadModelAsync(currentCombinedPath, cancellationToken).ConfigureAwait(false);
            if (!loadResult.IsSuccess)
                return loadResult;

            var message = existingSourceExists
                ? $"Appended {normalizedPaths.Length} model(s) into the current 3D workspace."
                : $"Loaded {normalizedPaths.Length} model(s) into the 3D workspace.";

            return loadResult with
            {
                SourceModelPath = currentCombinedPath,
                Message = message,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to combine {ModelCount} 3D source models through Occt.NET", normalizedPaths.Length);
            return new EditorModelLoadResult(false, $"3D model import failed: {ex.Message}");
        }
    }

    public Task<EditorFaceDistortionResult> ComputeFaceDistortionAsync(
        string? sourceModelPath,
        SelectedFace3D selectedFace,
        string distortionMode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sourceModelPath) || !File.Exists(sourceModelPath))
        {
            return Task.FromResult(new EditorFaceDistortionResult(
                false,
                "No usable native 3D source asset is available for distortion analysis. Re-import the model or reopen a .stch with embedded 3D data."));
        }

        try
        {
            var shape = LoadShape(sourceModelPath);
            var bodies = GetSolidBodies(shape);
            if (selectedFace.BodyIndex < 0 || selectedFace.BodyIndex >= bodies.Count)
            {
                return Task.FromResult(new EditorFaceDistortionResult(
                    false,
                    $"Body index {selectedFace.BodyIndex} is out of range."));
            }

            var body = bodies[selectedFace.BodyIndex];
            var face = TryGetFaceByIndex(body, selectedFace.FaceIndex);
            if (face is null)
            {
                return Task.FromResult(new EditorFaceDistortionResult(
                    false,
                    $"Face index {selectedFace.FaceIndex} was not found on body {selectedFace.BodyIndex}."));
            }

            var surfaceType = GetSurfaceTypeLabel(face);
            var distortion = surfaceType is "Plane" or "Cylinder" or "Cone"
                ? CreateZeroDistortion(face, body)
                : CreateFaceDistortion(face, body, distortionMode);

            var payloadJson = JsonSerializer.Serialize(new
            {
                body_index = selectedFace.BodyIndex,
                face_index = selectedFace.FaceIndex,
                distortion,
            });

            return Task.FromResult(new EditorFaceDistortionResult(
                true,
                "Distortion analysis updated.",
                payloadJson));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to compute native distortion for body {BodyIndex}, face {FaceIndex}.",
                selectedFace.BodyIndex,
                selectedFace.FaceIndex);
            return Task.FromResult(new EditorFaceDistortionResult(false, $"3D distortion analysis failed: {ex.Message}"));
        }
    }

    public Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanUseNativeSeparateFaceUnfold(request))
            return Task.FromResult(BuildMissingNativeUnfoldSelectionResult(request));

        return Task.FromResult(TryUnfoldSeparateFacesNative(request));
    }

    public Task<EditorOperationResult> ProjectEdgesAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.SourceModelPath) || !File.Exists(request.SourceModelPath))
        {
            return Task.FromResult(new EditorOperationResult(
                false,
                "No usable native 3D source asset is available for projection. Re-import the model or reopen a .stch with embedded 3D data."));
        }

        try
        {
            var shape = LoadShape(request.SourceModelPath);
            var bodies = GetSolidBodies(shape)
                .Select((body, index) => ApplyBodyOffset(body, request.BodyOffsets, index))
                .ToArray();

            var targetBodies = ResolveTargetBodies(bodies, request.VisibleBodyIndices);
            if (targetBodies.Count == 0)
                return Task.FromResult(new EditorOperationResult(false, "No solid bodies found to project."));

            if (!TryBuildProjectionBasis(bodies, request, out var basis, out var basisError))
                return Task.FromResult(new EditorOperationResult(false, basisError));

            var polylines = new List<DxfPolyline>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var anyIntersection = false;

            foreach (var body in targetBodies)
            {
                var section = new BRepAlgoAPI_Section(body, ToPlane(basis));
                section.Build();
                if (!section.IsDone)
                    continue;

                var edgeExplorer = new TopExp_Explorer(section.Shape, TopAbs_ShapeEnum.TopAbs_EDGE);
                while (edgeExplorer.More)
                {
                    anyIntersection = true;
                    var edge = AsEdge(edgeExplorer.Current);
                    var projected = ProjectEdgeToBasis(edge, basis);
                    AddUniquePolyline(polylines, seenKeys, projected, isClosed: false);
                    edgeExplorer.Next();
                }
            }

            if (polylines.Count == 0 && !anyIntersection)
                AddSilhouetteFallbackPolylines(targetBodies, basis, polylines, seenKeys);

            if (polylines.Count == 0)
                return Task.FromResult(new EditorOperationResult(false, "No projectable edges found."));

            var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, $"projected_{Guid.NewGuid():N}.dxf");
            EditorDxfDocument.SaveLwPolylines(outputPath, "PROJECTED_SKETCH", ArrangePolylinesForOutput(polylines));

            return Task.FromResult(new EditorOperationResult(
                true,
                $"Projected {polylines.Count} sketch curve(s) from the visible workspace bodies.",
                outputPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute native Occt.NET projection for {SourceModelPath}.", request.SourceModelPath);
            return Task.FromResult(new EditorOperationResult(false, $"3D projection failed: {ex.Message}"));
        }
    }

    private OcctViewportScene BuildViewportScene(string sourceModelPath)
    {
        var shape = LoadShape(sourceModelPath);
        var solidBodies = GetSolidBodies(shape);
        var viewportBodies = new List<ViewportBodyPayload>(solidBodies.Count);
        var bodies = new List<Body3D>(solidBodies.Count);

        var min = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var max = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };

        for (var bodyIndex = 0; bodyIndex < solidBodies.Count; bodyIndex++)
        {
            var body = solidBodies[bodyIndex];
            var geometry = BuildBodyGeometry(body, bodyIndex, min, max);
            viewportBodies.Add(geometry.ViewportBody);
            bodies.Add(new Body3D(bodyIndex, geometry.Name, geometry.Faces)
            {
                Visible = true,
            });
        }

        if (!double.IsFinite(min[0]))
        {
            min = [0.0, 0.0, 0.0];
            max = [0.0, 0.0, 0.0];
        }

        var viewportJson = JsonSerializer.Serialize(new
        {
            bodies = viewportBodies,
            bbox = new
            {
                min,
                max,
                center = new[]
                {
                    (min[0] + max[0]) / 2.0,
                    (min[1] + max[1]) / 2.0,
                    (min[2] + max[2]) / 2.0,
                },
            },
        });

        return new OcctViewportScene(viewportJson, bodies);
    }

    private BodyGeometryResult BuildBodyGeometry(
        TopoDS_Shape body,
        int bodyIndex,
        double[] min,
        double[] max)
    {
        _ = new BRepMesh_IncrementalMesh(body, BodyMeshDeflection, false, 0.5, false);

        var edgeMap = new TopTools_IndexedMapOfShape();
        var edgeExplorer = new TopExp_Explorer(body, TopAbs_ShapeEnum.TopAbs_EDGE);
        while (edgeExplorer.More)
        {
            edgeMap.Add(edgeExplorer.Current);
            edgeExplorer.Next();
        }

        var edgeToFaces = new Dictionary<int, List<int>>();
        var faceSummaries = new List<Face3D>();
        var viewportFaces = new List<ViewportFacePayload>();
        var faceExplorer = new TopExp_Explorer(body, TopAbs_ShapeEnum.TopAbs_FACE);
        var faceIndex = 0;

        while (faceExplorer.More)
        {
            var face = AsFace(faceExplorer.Current);
            faceExplorer.Next();

            var faceEdgeExplorer = new TopExp_Explorer(face, TopAbs_ShapeEnum.TopAbs_EDGE);
            while (faceEdgeExplorer.More)
            {
                var edgeId = edgeMap.Add(faceEdgeExplorer.Current);
                if (!edgeToFaces.TryGetValue(edgeId, out var adjacentFaces))
                {
                    adjacentFaces = [];
                    edgeToFaces[edgeId] = adjacentFaces;
                }

                adjacentFaces.Add(faceIndex);
                faceEdgeExplorer.Next();
            }

            var surfaceType = GetSurfaceTypeLabel(face);
            var area = GetFaceArea(face);
            var vertices = new List<double>();
            var indices = new List<int>();
            var location = new TopLoc_Location();
            var triangulation = BRep_Tool.Triangulation(face, out location);

            if (triangulation is not null)
            {
                var transform = location.Transformation;
                for (var nodeIndex = 1; nodeIndex <= triangulation.NbNodes; nodeIndex++)
                {
                    var point = triangulation.Node(nodeIndex).Transformed(transform);
                    vertices.Add(point.X);
                    vertices.Add(point.Y);
                    vertices.Add(point.Z);

                    min[0] = Math.Min(min[0], point.X);
                    min[1] = Math.Min(min[1], point.Y);
                    min[2] = Math.Min(min[2], point.Z);
                    max[0] = Math.Max(max[0], point.X);
                    max[1] = Math.Max(max[1], point.Y);
                    max[2] = Math.Max(max[2], point.Z);
                }

                for (var triangleIndex = 1; triangleIndex <= triangulation.NbTriangles; triangleIndex++)
                {
                    var triangle = triangulation.Triangle(triangleIndex);
                    var a = 0;
                    var b = 0;
                    var c = 0;
                    triangle.Get(out a, out b, out c);
                    indices.Add(a - 1);
                    indices.Add(b - 1);
                    indices.Add(c - 1);
                }
            }

            faceSummaries.Add(new Face3D(faceIndex, surfaceType, area) { BodyIndex = bodyIndex });
            viewportFaces.Add(new ViewportFacePayload(faceIndex, surfaceType, area, vertices, indices));
            faceIndex++;
        }

        var viewportEdges = new List<ViewportEdgePayload>();
        for (var edgeIndex = 1; edgeIndex <= edgeMap.Extent; edgeIndex++)
        {
            var edge = AsEdge(edgeMap.FindKey(edgeIndex));
            viewportEdges.Add(new ViewportEdgePayload(
                edgeIndex,
                Flatten(DiscretizeEdge(edge)),
                edgeToFaces.TryGetValue(edgeIndex, out var adjacentFaces) ? adjacentFaces : []));
        }

        return new BodyGeometryResult(
            $"Body {bodyIndex + 1}",
            faceSummaries,
            new ViewportBodyPayload(bodyIndex, $"Body {bodyIndex + 1}", viewportFaces, viewportEdges));
    }

    private static double[] CreateZeroDistortion(TopoDS_Face face, TopoDS_Shape body)
    {
        var mesh = BuildFaceTriangulation(face, body);
        return mesh.Points3D.Count == 0
            ? []
            : Enumerable.Repeat(0.0, mesh.Points3D.Count).ToArray();
    }

    private EditorOperationResult TryUnfoldSeparateFacesNative(EditorUnfoldRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceModelPath) || !File.Exists(request.SourceModelPath))
        {
            return new EditorOperationResult(
                false,
                "No usable native 3D source asset is available for unfolding. Re-import the model or reopen a .stch with embedded 3D data.");
        }

        try
        {
            var shape = LoadShape(request.SourceModelPath);
            var bodies = GetSolidBodies(shape);
            var targetFaces = ResolveSeparateFaceTargets(bodies, request);
            if (targetFaces.Count == 0)
            {
                return new EditorOperationResult(
                    false,
                    request.WholeBody
                        ? "No faces were found for native whole-body separate-piece flattening."
                        : "No faces were selected for native separate-piece flattening.");
            }

            var outputPolylines = new List<DxfPolyline>();
            var currentXOffset = 0.0;
            var unfoldedCount = 0;
            var skippedCount = 0;
            var skippedSurfaceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var targetFace in targetFaces)
            {
                var polylines = BuildSeparateFacePolylines(targetFace.Face, targetFace.Body, request.DistortionMode);
                if (polylines.Count == 0 || polylines.All(polyline => polyline.Points.Count == 0))
                {
                    skippedCount++;
                    skippedSurfaceTypes.Add(GetSurfaceTypeLabel(targetFace.Face));
                    continue;
                }

                var minX = polylines.SelectMany(polyline => polyline.Points).Min(point => point.X);
                var maxX = polylines.SelectMany(polyline => polyline.Points).Max(point => point.X);
                var minY = polylines.SelectMany(polyline => polyline.Points).Min(point => point.Y);

                foreach (var polyline in polylines)
                {
                    var translated = polyline.Points
                        .Select(point => new DxfPoint(
                            point.X - minX + currentXOffset,
                            point.Y - minY))
                        .ToArray();

                    outputPolylines.Add(new DxfPolyline(translated, polyline.IsClosed));
                }

                currentXOffset += (maxX - minX) + OutputGapMillimeters;
                unfoldedCount++;
            }

            if (unfoldedCount == 0 || outputPolylines.Count == 0)
            {
                return new EditorOperationResult(
                    false,
                    request.WholeBody
                        ? "No faces could be flattened into native separate pieces for the loaded bodies."
                        : "The selected faces could not be flattened into native separate pieces.");
            }

            var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, $"unfold_{Guid.NewGuid():N}.dxf");
            EditorDxfDocument.SaveLwPolylines(outputPath, "UNFOLDED_3D", outputPolylines);

            var skippedSummary = skippedCount == 0
                ? string.Empty
                : $" Skipped {skippedCount} face(s) with no usable triangulation or boundary loops ({string.Join(", ", skippedSurfaceTypes.OrderBy(type => type, StringComparer.OrdinalIgnoreCase))}).";

            return new EditorOperationResult(
                true,
                request.WholeBody
                    ? $"Flattened {unfoldedCount} face(s) from the visible bodies into native separate pieces through OpenCASCADE .NET.{skippedSummary}"
                    : $"Flattened {unfoldedCount} selected face(s) into native separate pieces through OpenCASCADE .NET.{skippedSummary}",
                outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute native separate-face unfolding for {SourceModelPath}.", request.SourceModelPath);
            return new EditorOperationResult(false, $"3D unfolding failed: {ex.Message}");
        }
    }

    private static bool CanUseNativeSeparateFaceUnfold(EditorUnfoldRequest request)
        => request.WholeBody || request.SelectedFaces.Count > 0;

    private static EditorOperationResult BuildMissingNativeUnfoldSelectionResult(EditorUnfoldRequest request)
        => new(
            false,
            request.WholeBody
                ? "Load a model to flatten the whole body natively."
                : "Select one or more faces to flatten them natively as separate pieces.");

    private static IReadOnlyList<SeparateFaceTarget> ResolveSeparateFaceTargets(
        IReadOnlyList<TopoDS_Shape> bodies,
        EditorUnfoldRequest request)
    {
        if (request.WholeBody)
        {
            var visibleBodyIndices = request.VisibleBodyIndices.Count == 0
                ? null
                : request.VisibleBodyIndices.ToHashSet();
            var targets = new List<SeparateFaceTarget>();
            for (var bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                if (visibleBodyIndices is not null && !visibleBodyIndices.Contains(bodyIndex))
                    continue;

                var faceExplorer = new TopExp_Explorer(bodies[bodyIndex], TopAbs_ShapeEnum.TopAbs_FACE);
                while (faceExplorer.More)
                {
                    targets.Add(new SeparateFaceTarget(bodies[bodyIndex], AsFace(faceExplorer.Current)));
                    faceExplorer.Next();
                }
            }

            return targets;
        }

        var selectedTargets = new List<SeparateFaceTarget>(request.SelectedFaces.Count);
        foreach (var selectedFace in request.SelectedFaces)
        {
            if (selectedFace.BodyIndex < 0 || selectedFace.BodyIndex >= bodies.Count)
                return [];

            var body = bodies[selectedFace.BodyIndex];
            var face = TryGetFaceByIndex(body, selectedFace.FaceIndex);
            if (face is null)
                return [];

            selectedTargets.Add(new SeparateFaceTarget(body, face));
        }

        return selectedTargets;
    }

    private static IReadOnlyList<DxfPolyline> BuildSeparateFacePolylines(
        TopoDS_Face face,
        TopoDS_Shape body,
        string distortionMode)
    {
        var mesh = BuildFaceTriangulation(face, body);
        if (mesh.Points3D.Count == 0 || mesh.Triangles.Count == 0)
            return [];

        var flattenedPoints = BuildFlattenedFacePoints(face, mesh, distortionMode);
        if (flattenedPoints.Count != mesh.Points3D.Count)
            return [];

        var loops = BuildBoundaryLoops(mesh.Triangles);
        if (loops.Count == 0)
            return [];

        return loops
            .Select(loop => new DxfPolyline(
                loop.Select(index => new DxfPoint(flattenedPoints[index].X, flattenedPoints[index].Y)).ToArray(),
                IsClosed: true))
            .Where(polyline => polyline.Points.Count >= 3)
            .ToArray();
    }

    private static double[] CreateFaceDistortion(TopoDS_Face face, TopoDS_Shape body, string distortionMode)
    {
        var mesh = BuildFaceTriangulation(face, body);
        if (mesh.Points3D.Count == 0 || mesh.Triangles.Count == 0)
            return [];

        var flattenedPoints = BuildFlattenedFacePoints(face, mesh, distortionMode);
        if (flattenedPoints.Count != mesh.Points3D.Count)
            return Enumerable.Repeat(0.0, mesh.Points3D.Count).ToArray();

        var triangleDistortion = new List<double>[mesh.Points3D.Count];
        for (var i = 0; i < triangleDistortion.Length; i++)
            triangleDistortion[i] = [];

        foreach (var triangle in mesh.Triangles)
        {
            if (!TryComputeTriangleMetrics(mesh.Points3D, flattenedPoints, triangle, out var metrics))
                continue;

            var distortion = distortionMode switch
            {
                "equal-area" => metrics.AreaDistortion,
                "equidistant" => metrics.EdgeDistortion,
                "balanced" => (metrics.ConformalDistortion + metrics.AreaDistortion) / 2.0,
                _ => metrics.ConformalDistortion,
            };

            triangleDistortion[triangle.A].Add(distortion);
            triangleDistortion[triangle.B].Add(distortion);
            triangleDistortion[triangle.C].Add(distortion);
        }

        return triangleDistortion
            .Select(values => values.Count == 0 ? 0.0 : values.Average())
            .ToArray();
    }

    private static FaceTriangulationData BuildFaceTriangulation(TopoDS_Face face, TopoDS_Shape body)
    {
        _ = new BRepMesh_IncrementalMesh(body, BodyMeshDeflection, false, 0.5, false);

        var location = new TopLoc_Location();
        var triangulation = BRep_Tool.Triangulation(face, out location);
        if (triangulation is null)
            return new FaceTriangulationData([], [], []);

        var transform = location.Transformation;
        var points3D = new List<Vector3d>(triangulation.NbNodes);
        var parameterPoints = new List<Vector2d>(triangulation.NbNodes);

        for (var nodeIndex = 1; nodeIndex <= triangulation.NbNodes; nodeIndex++)
        {
            var point = triangulation.Node(nodeIndex).Transformed(transform);
            points3D.Add(new Vector3d(point.X, point.Y, point.Z));

            var uvNode = triangulation.UVNode(nodeIndex);
            parameterPoints.Add(new Vector2d(uvNode.X, uvNode.Y));
        }

        var triangles = new List<MeshTriangle>(triangulation.NbTriangles);
        for (var triangleIndex = 1; triangleIndex <= triangulation.NbTriangles; triangleIndex++)
        {
            var triangle = triangulation.Triangle(triangleIndex);
            var a = 0;
            var b = 0;
            var c = 0;
            triangle.Get(out a, out b, out c);
            triangles.Add(new MeshTriangle(a - 1, b - 1, c - 1));
        }

        return new FaceTriangulationData(points3D, parameterPoints, triangles);
    }

    private static IReadOnlyList<Vector2d> BuildFlattenedFacePoints(
        TopoDS_Face face,
        FaceTriangulationData mesh,
        string distortionMode)
    {
        var mappedPoints = GetSurfaceTypeLabel(face) switch
        {
            "Plane" => mesh.ParameterPoints,
            "Cylinder" => MapCylindricalPoints(face, mesh.ParameterPoints),
            "Cone" => MapConicalPoints(face, mesh.ParameterPoints),
            _ => mesh.ParameterPoints.Count == mesh.Points3D.Count && mesh.ParameterPoints.Count > 0
                ? mesh.ParameterPoints
                : ProjectPointsToBestFitPlane(mesh.Points3D),
        };

        var scale = DetermineFlatteningScale(mesh, mappedPoints, distortionMode);
        if (!double.IsFinite(scale) || Math.Abs(scale) < 1e-9)
            scale = 1.0;

        return mappedPoints
            .Select(point => new Vector2d(point.X * scale, point.Y * scale))
            .ToArray();
    }

    private static IReadOnlyList<Vector2d> MapCylindricalPoints(TopoDS_Face face, IReadOnlyList<Vector2d> parameterPoints)
    {
        var radius = new BRepAdaptor_Surface(face).Cylinder.Radius;
        return parameterPoints
            .Select(point => new Vector2d(radius * point.X, point.Y))
            .ToArray();
    }

    private static IReadOnlyList<Vector2d> MapConicalPoints(TopoDS_Face face, IReadOnlyList<Vector2d> parameterPoints)
    {
        var cone = new BRepAdaptor_Surface(face).Cone;
        var radius = cone.RefRadius;
        var semiAngle = cone.SemiAngle;
        if (Math.Abs(semiAngle) < 1e-9)
            return parameterPoints.Select(point => new Vector2d(radius * point.X, point.Y)).ToArray();

        var slantOffset = radius / Math.Sin(semiAngle);
        return parameterPoints
            .Select(point =>
            {
                var slantLength = slantOffset + point.Y;
                var theta = point.X * Math.Sin(semiAngle);
                return new Vector2d(
                    slantLength * Math.Cos(theta),
                    slantLength * Math.Sin(theta));
            })
            .ToArray();
    }

    private static IReadOnlyList<Vector2d> ProjectPointsToBestFitPlane(IReadOnlyList<Vector3d> points)
    {
        if (points.Count == 0)
            return [];

        var origin = points[0];
        var uAxis = new Vector3d(1.0, 0.0, 0.0);
        var vAxis = new Vector3d(0.0, 1.0, 0.0);

        if (points.Count >= 3)
        {
            var edgeA = points[1] - origin;
            var edgeB = points[2] - origin;
            if (edgeA.Length > 1e-9)
                uAxis = edgeA.Normalize();

            var normal = Vector3d.Cross(edgeA, edgeB);
            if (normal.Length > 1e-9)
            {
                normal = normal.Normalize();
                var candidateVAxis = Vector3d.Cross(normal, uAxis);
                if (candidateVAxis.Length > 1e-9)
                    vAxis = candidateVAxis.Normalize();
            }
        }

        return points
            .Select(point =>
            {
                var delta = point - origin;
                return new Vector2d(Vector3d.Dot(delta, uAxis), Vector3d.Dot(delta, vAxis));
            })
            .ToArray();
    }

    private static double DetermineFlatteningScale(
        FaceTriangulationData mesh,
        IReadOnlyList<Vector2d> flattenedPoints,
        string distortionMode)
    {
        var uniqueEdges = GetUniqueEdges(mesh.Triangles);
        if (uniqueEdges.Count == 0)
            return 1.0;

        var total3DEdgeLength = 0.0;
        var total2DEdgeLength = 0.0;
        foreach (var edge in uniqueEdges)
        {
            total3DEdgeLength += (mesh.Points3D[edge.A] - mesh.Points3D[edge.B]).Length;
            total2DEdgeLength += (flattenedPoints[edge.A] - flattenedPoints[edge.B]).Length;
        }

        var total3DArea = 0.0;
        var total2DArea = 0.0;
        foreach (var triangle in mesh.Triangles)
        {
            total3DArea += TriangleArea(mesh.Points3D[triangle.A], mesh.Points3D[triangle.B], mesh.Points3D[triangle.C]);
            total2DArea += TriangleArea(flattenedPoints[triangle.A], flattenedPoints[triangle.B], flattenedPoints[triangle.C]);
        }

        var edgeScale = total2DEdgeLength > 1e-9 ? total3DEdgeLength / total2DEdgeLength : 1.0;
        var areaScale = total2DArea > 1e-9 ? Math.Sqrt(total3DArea / total2DArea) : edgeScale;

        return distortionMode switch
        {
            "equal-area" => areaScale,
            "balanced" => Math.Sqrt(Math.Max(1e-9, edgeScale * areaScale)),
            _ => edgeScale,
        };
    }

    private static IReadOnlyList<MeshEdge> GetUniqueEdges(IReadOnlyList<MeshTriangle> triangles)
    {
        var seen = new HashSet<MeshEdge>();
        foreach (var triangle in triangles)
        {
            seen.Add(new MeshEdge(Math.Min(triangle.A, triangle.B), Math.Max(triangle.A, triangle.B)));
            seen.Add(new MeshEdge(Math.Min(triangle.B, triangle.C), Math.Max(triangle.B, triangle.C)));
            seen.Add(new MeshEdge(Math.Min(triangle.C, triangle.A), Math.Max(triangle.C, triangle.A)));
        }

        return seen.ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<int>> BuildBoundaryLoops(IReadOnlyList<MeshTriangle> triangles)
    {
        var edgeCounts = new Dictionary<MeshEdge, int>();
        foreach (var triangle in triangles)
        {
            IncrementEdgeCount(edgeCounts, triangle.A, triangle.B);
            IncrementEdgeCount(edgeCounts, triangle.B, triangle.C);
            IncrementEdgeCount(edgeCounts, triangle.C, triangle.A);
        }

        var next = new Dictionary<int, int>();
        foreach (var triangle in triangles)
        {
            AddBoundaryEdge(next, edgeCounts, triangle.A, triangle.B);
            AddBoundaryEdge(next, edgeCounts, triangle.B, triangle.C);
            AddBoundaryEdge(next, edgeCounts, triangle.C, triangle.A);
        }

        var loops = new List<IReadOnlyList<int>>();
        var visited = new HashSet<int>();
        foreach (var start in next.Keys.ToArray())
        {
            if (visited.Contains(start))
                continue;

            var loop = new List<int> { start };
            visited.Add(start);

            var hasNext = next.TryGetValue(start, out var current);
            while (hasNext && current != start && !visited.Contains(current))
            {
                loop.Add(current);
                visited.Add(current);
                hasNext = next.TryGetValue(current, out current);
            }

            if (loop.Count >= 3)
                loops.Add(loop);
        }

        return loops
            .OrderByDescending(loop => loop.Count)
            .ToArray();
    }

    private static void IncrementEdgeCount(IDictionary<MeshEdge, int> edgeCounts, int start, int end)
    {
        var edge = new MeshEdge(Math.Min(start, end), Math.Max(start, end));
        edgeCounts.TryGetValue(edge, out var count);
        edgeCounts[edge] = count + 1;
    }

    private static void AddBoundaryEdge(
        IDictionary<int, int> next,
        IReadOnlyDictionary<MeshEdge, int> edgeCounts,
        int start,
        int end)
    {
        var edge = new MeshEdge(Math.Min(start, end), Math.Max(start, end));
        if (!edgeCounts.TryGetValue(edge, out var count) || count != 1)
            return;

        next[start] = end;
    }

    private static bool TryComputeTriangleMetrics(
        IReadOnlyList<Vector3d> points3D,
        IReadOnlyList<Vector2d> flattenedPoints,
        MeshTriangle triangle,
        out TriangleMetrics metrics)
    {
        var p0 = points3D[triangle.A];
        var p1 = points3D[triangle.B];
        var p2 = points3D[triangle.C];
        var q0 = flattenedPoints[triangle.A];
        var q1 = flattenedPoints[triangle.B];
        var q2 = flattenedPoints[triangle.C];

        var edgeA3D = p1 - p0;
        var edgeB3D = p2 - p0;
        var edgeA2D = q1 - q0;
        var edgeB2D = q2 - q0;

        var xAxisLength = edgeA3D.Length;
        if (xAxisLength < 1e-9)
        {
            metrics = default;
            return false;
        }

        var xAxis = edgeA3D.Normalize();
        var normal = Vector3d.Cross(edgeA3D, edgeB3D);
        if (normal.Length < 1e-9)
        {
            metrics = default;
            return false;
        }

        var yAxis = Vector3d.Cross(normal.Normalize(), xAxis);
        if (yAxis.Length < 1e-9)
        {
            metrics = default;
            return false;
        }

        yAxis = yAxis.Normalize();

        var localX2 = Vector3d.Dot(edgeB3D, xAxis);
        var localY2 = Vector3d.Dot(edgeB3D, yAxis);
        if (Math.Abs(localY2) < 1e-9)
        {
            metrics = default;
            return false;
        }

        var area3D = TriangleArea(p0, p1, p2);
        var area2D = TriangleArea(q0, q1, q2);
        var areaDistortion = SymmetricRatio(area2D, area3D);

        var edgeDistortion = (
            SymmetricRatio((q1 - q0).Length, (p1 - p0).Length)
            + SymmetricRatio((q2 - q1).Length, (p2 - p1).Length)
            + SymmetricRatio((q0 - q2).Length, (p0 - p2).Length))
            / 3.0;

        var j11 = edgeA2D.X / xAxisLength;
        var j21 = edgeA2D.Y / xAxisLength;
        var j12 = (edgeB2D.X - (j11 * localX2)) / localY2;
        var j22 = (edgeB2D.Y - (j21 * localX2)) / localY2;

        var a = (j11 * j11) + (j21 * j21);
        var b = (j11 * j12) + (j21 * j22);
        var c = (j12 * j12) + (j22 * j22);
        var trace = a + c;
        var determinant = Math.Max(0.0, (a * c) - (b * b));
        var discriminant = Math.Max(0.0, ((trace * trace) / 4.0) - determinant);
        var root = Math.Sqrt(discriminant);
        var sigmaMax = Math.Sqrt(Math.Max(0.0, (trace / 2.0) + root));
        var sigmaMin = Math.Sqrt(Math.Max(0.0, (trace / 2.0) - root));
        var conformalDistortion = sigmaMin <= 1e-9
            ? sigmaMax
            : Math.Max(sigmaMax / sigmaMin, sigmaMin / sigmaMax) - 1.0;

        metrics = new TriangleMetrics(conformalDistortion, areaDistortion, edgeDistortion);
        return true;
    }

    private static double TriangleArea(Vector3d a, Vector3d b, Vector3d c)
        => 0.5 * Vector3d.Cross(b - a, c - a).Length;

    private static double TriangleArea(Vector2d a, Vector2d b, Vector2d c)
        => 0.5 * Math.Abs(((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y)));

    private static double SymmetricRatio(double left, double right)
    {
        var safeLeft = Math.Max(Math.Abs(left), 1e-9);
        var safeRight = Math.Max(Math.Abs(right), 1e-9);
        var ratio = Math.Max(safeLeft / safeRight, safeRight / safeLeft);
        return ratio - 1.0;
    }

    private string CombineModels(string existingSourceModelPath, string incomingSourceModelPath)
    {
        var existingBodies = GetSolidBodies(LoadShape(existingSourceModelPath));
        var incomingBodies = GetSolidBodies(LoadShape(incomingSourceModelPath));

        if (existingBodies.Count == 0)
            throw new InvalidOperationException($"Existing model produced no bodies: {existingSourceModelPath}");
        if (incomingBodies.Count == 0)
            throw new InvalidOperationException($"Incoming model produced no bodies: {incomingSourceModelPath}");

        var existingMaxX = existingBodies
            .Select(GetBounds)
            .Where(bounds => bounds is not null)
            .Max(bounds => bounds!.MaxX);
        var incomingMinX = incomingBodies
            .Select(GetBounds)
            .Where(bounds => bounds is not null)
            .Min(bounds => bounds!.MinX);
        var translationX = (existingMaxX + ProjectionGapMillimeters) - incomingMinX;

        var translatedIncomingBodies = incomingBodies
            .Select(body => TranslateShape(body, translationX, 0.0, 0.0))
            .ToArray();

        var builder = new BRep_Builder();
        var compound = new TopoDS_Compound();
        builder.MakeCompound(out compound);

        foreach (var body in existingBodies)
            builder.Add(compound, body);

        foreach (var body in translatedIncomingBodies)
            builder.Add(compound, body);

        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"combined_{Guid.NewGuid():N}.step");

        var writer = new STEPControl_Writer();
        var transferStatus = writer.Transfer(compound, STEPControl_StepModelType.STEPControl_AsIs);
        if (transferStatus != IFSelect_ReturnStatus.IFSelect_RetDone)
            throw new InvalidOperationException("OpenCASCADE could not transfer the combined model into STEP output.");

        var writeStatus = writer.Write(outputPath);
        if (writeStatus != IFSelect_ReturnStatus.IFSelect_RetDone)
            throw new InvalidOperationException("OpenCASCADE could not write the combined STEP file.");

        return outputPath;
    }

    private static List<TopoDS_Shape> ResolveTargetBodies(IReadOnlyList<TopoDS_Shape> bodies, IReadOnlyList<int> visibleBodyIndices)
    {
        if (visibleBodyIndices.Count == 0)
            return [];

        var results = new List<TopoDS_Shape>(visibleBodyIndices.Count);
        foreach (var bodyIndex in visibleBodyIndices.Distinct())
        {
            if (bodyIndex >= 0 && bodyIndex < bodies.Count)
                results.Add(bodies[bodyIndex]);
        }

        return results;
    }

    private static IReadOnlyList<DxfPolyline> ArrangePolylinesForOutput(IReadOnlyList<DxfPolyline> polylines)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;

        foreach (var polyline in polylines)
        {
            foreach (var point in polyline.Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
            }
        }

        if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX))
            return polylines;

        var shiftX = -minX;
        var shiftY = -minY;

        return polylines
            .Select(polyline => new DxfPolyline(
                polyline.Points
                    .Select(point => new DxfPoint(point.X + shiftX, point.Y + shiftY))
                    .ToArray(),
                polyline.IsClosed))
            .ToArray();
    }

    private static void AddSilhouetteFallbackPolylines(
        IReadOnlyList<TopoDS_Shape> targetBodies,
        ProjectionBasis basis,
        ICollection<DxfPolyline> polylines,
        ISet<string> seenKeys)
    {
        var projector = new HLRAlgo_Projector(new gp_Ax2(ToPoint(basis.Origin), ToDirection(basis.Normal), ToDirection(basis.UAxis)));
        var hlr = new HLRBRep_Algo();
        foreach (var body in targetBodies)
            hlr.Add(body);

        hlr.Projector = projector;
        hlr.Update();
        hlr.Hide();

        var hlrToShape = new HLRBRep_HLRToShape(hlr);
        foreach (var compound in new[] { hlrToShape.VCompound(), hlrToShape.OutLineVCompound() })
        {
            if (compound is null || compound.IsNull)
                continue;

            var edgeExplorer = new TopExp_Explorer(compound, TopAbs_ShapeEnum.TopAbs_EDGE);
            while (edgeExplorer.More)
            {
                var edge = AsEdge(edgeExplorer.Current);
                var points = DiscretizeEdge(edge)
                    .Select(point =>
                    {
                        var projected = new gp_Pnt2d();
                        projector.Project(ToPoint(point), projected);
                        return new DxfPoint(projected.X, projected.Y);
                    })
                    .ToArray();
                AddUniquePolyline(polylines, seenKeys, points, isClosed: false);
                edgeExplorer.Next();
            }
        }
    }

    private static IReadOnlyList<DxfPoint> ProjectEdgeToBasis(TopoDS_Edge edge, ProjectionBasis basis)
        => DiscretizeEdge(edge)
            .Select(point =>
            {
                var delta = point - basis.Origin;
                return new DxfPoint(Vector3d.Dot(delta, basis.UAxis), Vector3d.Dot(delta, basis.VAxis));
            })
            .ToArray();

    private static void AddUniquePolyline(
        ICollection<DxfPolyline> polylines,
        ISet<string> seenKeys,
        IReadOnlyList<DxfPoint> points,
        bool isClosed)
    {
        if (points.Count < 2)
            return;

        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        if ((maxX - minX) < 1e-6 && (maxY - minY) < 1e-6)
            return;

        var forward = string.Join(";", points.Select(point => $"{RoundKey(point.X)},{RoundKey(point.Y)}"));
        var reverse = string.Join(";", points.Reverse().Select(point => $"{RoundKey(point.X)},{RoundKey(point.Y)}"));
        var key = string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse;
        if (!seenKeys.Add(key))
            return;

        polylines.Add(new DxfPolyline(points.ToArray(), isClosed));
    }

    private static string RoundKey(double value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

    private static IReadOnlyList<double> Flatten(IReadOnlyList<Vector3d> points)
    {
        var values = new double[points.Count * 3];
        for (var i = 0; i < points.Count; i++)
        {
            values[(i * 3) + 0] = points[i].X;
            values[(i * 3) + 1] = points[i].Y;
            values[(i * 3) + 2] = points[i].Z;
        }

        return values;
    }

    private static IReadOnlyList<Vector3d> DiscretizeEdge(TopoDS_Edge edge)
    {
        var curve = new BRepAdaptor_Curve(edge);
        var samples = curve.Type == GeomAbs_CurveType.GeomAbs_Line ? 1 : 24;
        var points = new List<Vector3d>(samples + 1);
        var start = curve.FirstParameter;
        var end = curve.LastParameter;
        for (var sample = 0; sample <= samples; sample++)
        {
            var t = start + ((end - start) * sample / samples);
            points.Add(ToVector(curve.Value(t)));
        }

        return points;
    }

    private static bool TryBuildProjectionBasis(
        IReadOnlyList<TopoDS_Shape> bodies,
        EditorProjectionRequest request,
        out ProjectionBasis basis,
        out string error)
    {
        var origin = new Vector3d(0.0, 0.0, 0.0);
        var normal = new Vector3d(0.0, 0.0, 1.0);
        var uAxis = new Vector3d(1.0, 0.0, 0.0);
        var vAxis = new Vector3d(0.0, 1.0, 0.0);

        switch (request.PlaneType)
        {
            case "XY":
                break;
            case "XZ":
                normal = new Vector3d(0.0, 1.0, 0.0);
                vAxis = new Vector3d(0.0, 0.0, 1.0);
                break;
            case "YZ":
                normal = new Vector3d(1.0, 0.0, 0.0);
                uAxis = new Vector3d(0.0, 1.0, 0.0);
                vAxis = new Vector3d(0.0, 0.0, 1.0);
                break;
            case "face":
            {
                if (request.FaceIndex is null || request.FaceBodyIndex is null)
                {
                    basis = default;
                    error = "Select a planar face before confirming the projection.";
                    return false;
                }

                if (request.FaceBodyIndex < 0 || request.FaceBodyIndex >= bodies.Count)
                {
                    basis = default;
                    error = $"Body index {request.FaceBodyIndex} is out of range for the selected projection face.";
                    return false;
                }

                var face = TryGetFaceByIndex(bodies[request.FaceBodyIndex.Value], request.FaceIndex.Value);
                if (face is null)
                {
                    basis = default;
                    error = $"Face index {request.FaceIndex} was not found on body {request.FaceBodyIndex}.";
                    return false;
                }

                var surfaceType = GetSurfaceTypeLabel(face);
                if (!string.Equals(surfaceType, "Plane", StringComparison.OrdinalIgnoreCase))
                {
                    basis = default;
                    error = $"Face-based projection currently requires a planar face. The selected face is {surfaceType}.";
                    return false;
                }

                if (!TryBuildFaceBasis(face, out origin, out normal, out uAxis, out vAxis))
                {
                    basis = default;
                    error = "The selected face could not provide a stable projection plane.";
                    return false;
                }

                break;
            }
            default:
                basis = default;
                error = $"Unsupported projection plane type: {request.PlaneType}.";
                return false;
        }

        origin += normal * request.Offset;
        basis = new ProjectionBasis(origin, normal, uAxis, vAxis);
        error = string.Empty;
        return true;
    }

    private static bool TryBuildFaceBasis(
        TopoDS_Face face,
        out Vector3d origin,
        out Vector3d normal,
        out Vector3d uAxis,
        out Vector3d vAxis)
    {
        var surface = new BRepAdaptor_Surface(face);
        var point = new gp_Pnt();
        var uVector = new gp_Vec();
        var vVector = new gp_Vec();
        var uMid = (surface.FirstUParameter + surface.LastUParameter) / 2.0;
        var vMid = (surface.FirstVParameter + surface.LastVParameter) / 2.0;
        surface.D1(uMid, vMid, out point, out uVector, out vVector);

        var resolvedNormal = ToVector(uVector.Crossed(vVector));
        if (resolvedNormal.Length <= 1e-6)
            resolvedNormal = new Vector3d(0.0, 0.0, 1.0);
        else
            resolvedNormal = resolvedNormal.Normalize();

        var resolvedUAxis = ToVector(uVector);
        if (resolvedUAxis.Length <= 1e-6)
            resolvedUAxis = new Vector3d(1.0, 0.0, 0.0);
        else
            resolvedUAxis = resolvedUAxis.Normalize();

        var resolvedVAxis = Vector3d.Cross(resolvedNormal, resolvedUAxis);
        if (resolvedVAxis.Length <= 1e-6)
            resolvedVAxis = new Vector3d(0.0, 1.0, 0.0);
        else
            resolvedVAxis = resolvedVAxis.Normalize();

        origin = ToVector(point);
        normal = resolvedNormal;
        uAxis = resolvedUAxis;
        vAxis = resolvedVAxis;
        return true;
    }

    private static TopoDS_Face? TryGetFaceByIndex(TopoDS_Shape body, int faceIndex)
    {
        var explorer = new TopExp_Explorer(body, TopAbs_ShapeEnum.TopAbs_FACE);
        var currentIndex = 0;
        while (explorer.More)
        {
            var face = AsFace(explorer.Current);
            if (currentIndex == faceIndex)
                return face;

            currentIndex++;
            explorer.Next();
        }

        return null;
    }

    private static TopoDS_Shape ApplyBodyOffset(TopoDS_Shape body, IReadOnlyList<BodyOffset3D> offsets, int bodyIndex)
    {
        var offset = offsets.FirstOrDefault(candidate => candidate.BodyIndex == bodyIndex);
        return offset is null
            ? body
            : TranslateShape(body, offset.X, offset.Y, offset.Z);
    }

    private static TopoDS_Shape TranslateShape(TopoDS_Shape shape, double x, double y, double z)
    {
        if (Math.Abs(x) < 1e-9 && Math.Abs(y) < 1e-9 && Math.Abs(z) < 1e-9)
            return shape;

        var transform = new gp_Trsf();
        transform.SetTranslation(new gp_Vec(x, y, z));
        var moved = new BRepBuilderAPI_Transform(shape, transform, true);
        return moved.Shape;
    }

    private static Bounds3D? GetBounds(TopoDS_Shape shape)
    {
        var bounds = shape.BoundingBox;
        if (bounds is null || bounds.IsVoid)
            return null;

        var minX = 0.0;
        var minY = 0.0;
        var minZ = 0.0;
        var maxX = 0.0;
        var maxY = 0.0;
        var maxZ = 0.0;
        bounds.Get(out minX, out minY, out minZ, out maxX, out maxY, out maxZ);
        return new Bounds3D(minX, minY, minZ, maxX, maxY, maxZ);
    }

    private static double GetFaceArea(TopoDS_Face face)
    {
        var properties = new GProp_GProps();
        BRepGProp.SurfaceProperties(face, out properties);
        return properties.Mass;
    }

    private static string GetSurfaceTypeLabel(TopoDS_Face face)
        => new BRepAdaptor_Surface(face).Type switch
        {
            GeomAbs_SurfaceType.GeomAbs_Plane => "Plane",
            GeomAbs_SurfaceType.GeomAbs_Cylinder => "Cylinder",
            GeomAbs_SurfaceType.GeomAbs_Cone => "Cone",
            _ => "Other",
        };

    private static List<TopoDS_Shape> GetSolidBodies(TopoDS_Shape shape)
    {
        var bodies = ExploreShapes(shape, TopAbs_ShapeEnum.TopAbs_SOLID);
        if (bodies.Count > 0)
            return bodies;

        bodies = ExploreShapes(shape, TopAbs_ShapeEnum.TopAbs_SHELL);
        if (bodies.Count > 0)
            return bodies;

        if (ExploreShapes(shape, TopAbs_ShapeEnum.TopAbs_FACE).Count > 0)
            return [shape];

        return [];
    }

    private static List<TopoDS_Shape> ExploreShapes(TopoDS_Shape shape, TopAbs_ShapeEnum shapeType)
    {
        var results = new List<TopoDS_Shape>();
        var explorer = new TopExp_Explorer(shape, shapeType);
        while (explorer.More)
        {
            results.Add(explorer.Current);
            explorer.Next();
        }

        return results;
    }

    private static TopoDS_Shape LoadShape(string sourceModelPath)
        => Path.GetExtension(sourceModelPath).ToLowerInvariant() switch
        {
            ".stl" => LoadStlShape(sourceModelPath),
            ".obj" => LoadObjShape(sourceModelPath),
            _ => LoadStepShape(sourceModelPath),
        };

    private static TopoDS_Shape LoadStepShape(string sourceModelPath)
    {
        var reader = new STEPControl_Reader();
        var status = reader.ReadFile(sourceModelPath);
        if (status != IFSelect_ReturnStatus.IFSelect_RetDone)
            throw new InvalidOperationException($"STEP control reader failed to read {Path.GetFileName(sourceModelPath)}.");

        reader.TransferRoots();
        var shape = reader.OneShape();
        if (shape.IsNull)
            throw new InvalidOperationException($"STEP import produced an empty shape for {Path.GetFileName(sourceModelPath)}.");

        return shape;
    }

    private static TopoDS_Shape LoadStlShape(string sourceModelPath)
    {
        var shape = TopoDS_Shape.Cast(IntPtr.Zero);
        var reader = new StlAPI_Reader();
        if (!reader.Read(out shape, sourceModelPath) || shape is null || shape.IsNull)
            throw new InvalidOperationException($"Failed to read STL file {Path.GetFileName(sourceModelPath)}.");

        return shape;
    }

    private static TopoDS_Shape LoadObjShape(string sourceModelPath)
    {
        var builder = new BRep_Builder();
        var compound = new TopoDS_Compound();
        builder.MakeCompound(out compound);

        var vertices = new List<gp_Pnt>();
        var hasFaces = false;

        foreach (var line in File.ReadLines(sourceModelPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            if (string.Equals(parts[0], "v", StringComparison.OrdinalIgnoreCase) && parts.Length >= 4)
            {
                if (TryParseDouble(parts[1], out var x)
                    && TryParseDouble(parts[2], out var y)
                    && TryParseDouble(parts[3], out var z))
                {
                    vertices.Add(new gp_Pnt(x, y, z));
                }

                continue;
            }

            if (!string.Equals(parts[0], "f", StringComparison.OrdinalIgnoreCase))
                continue;

            var faceVertices = new List<gp_Pnt>();
            foreach (var token in parts.Skip(1))
            {
                var indexToken = token.Split('/')[0];
                if (!int.TryParse(indexToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vertexIndex))
                    continue;

                var resolvedIndex = vertexIndex > 0
                    ? vertexIndex - 1
                    : vertices.Count + vertexIndex;

                if (resolvedIndex >= 0 && resolvedIndex < vertices.Count)
                    faceVertices.Add(vertices[resolvedIndex]);
            }

            if (faceVertices.Count < 3)
                continue;

            var polygon = new BRepBuilderAPI_MakePolygon();
            foreach (var vertex in faceVertices)
                polygon.Add(vertex);

            polygon.Close();
            var makeFace = new BRepBuilderAPI_MakeFace(polygon.Wire);
            if (!makeFace.IsDone)
                continue;

            builder.Add(compound, makeFace.Face);
            hasFaces = true;
        }

        if (!hasFaces)
            throw new InvalidOperationException($"No valid faces could be parsed from OBJ file {Path.GetFileName(sourceModelPath)}.");

        return compound;
    }

    private static bool TryParseDouble(string raw, out double value)
        => double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private static gp_Pln ToPlane(ProjectionBasis basis)
        => new(ToPoint(basis.Origin), ToDirection(basis.Normal));

    private static gp_Pnt ToPoint(Vector3d value)
        => new(value.X, value.Y, value.Z);

    private static gp_Dir ToDirection(Vector3d value)
        => new(value.X, value.Y, value.Z);

    private static Vector3d ToVector(gp_Pnt value)
        => new(value.X, value.Y, value.Z);

    private static Vector3d ToVector(gp_Vec value)
        => new(value.X, value.Y, value.Z);

    private static unsafe TopoDS_Face AsFace(TopoDS_Shape shape)
        => TopoDS_Face.Cast(new IntPtr(shape.NativeInstance));

    private static unsafe TopoDS_Edge AsEdge(TopoDS_Shape shape)
        => TopoDS_Edge.Cast(new IntPtr(shape.NativeInstance));

    private sealed record OcctViewportScene(string ViewportJson, IReadOnlyList<Body3D> Bodies);

    private sealed record BodyGeometryResult(
        string Name,
        IReadOnlyList<Face3D> Faces,
        ViewportBodyPayload ViewportBody);

    private sealed record ViewportBodyPayload(
        int body_index,
        string name,
        IReadOnlyList<ViewportFacePayload> faces,
        IReadOnlyList<ViewportEdgePayload> edges);

    private sealed record ViewportFacePayload(
        int face_index,
        string type,
        double area,
        IReadOnlyList<double> vertices,
        IReadOnlyList<int> indices);

    private sealed record ViewportEdgePayload(
        int edge_index,
        IReadOnlyList<double> vertices,
        IReadOnlyList<int> faces);

    private sealed record Bounds3D(
        double MinX,
        double MinY,
        double MinZ,
        double MaxX,
        double MaxY,
        double MaxZ);

    private sealed record SeparateFaceTarget(
        TopoDS_Shape Body,
        TopoDS_Face Face);

    private readonly record struct ProjectionBasis(
        Vector3d Origin,
        Vector3d Normal,
        Vector3d UAxis,
        Vector3d VAxis);

    private readonly record struct Vector3d(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public Vector3d Normalize()
        {
            var length = Length;
            return length <= 1e-9
                ? new Vector3d(0.0, 0.0, 0.0)
                : new Vector3d(X / length, Y / length, Z / length);
        }

        public static Vector3d Cross(Vector3d left, Vector3d right)
            => new(
                (left.Y * right.Z) - (left.Z * right.Y),
                (left.Z * right.X) - (left.X * right.Z),
                (left.X * right.Y) - (left.Y * right.X));

        public static double Dot(Vector3d left, Vector3d right)
            => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

        public static Vector3d operator +(Vector3d left, Vector3d right)
            => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        public static Vector3d operator -(Vector3d left, Vector3d right)
            => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static Vector3d operator *(Vector3d left, double scalar)
            => new(left.X * scalar, left.Y * scalar, left.Z * scalar);
    }

    private readonly record struct Vector2d(double X, double Y)
    {
        public double Length => Math.Sqrt((X * X) + (Y * Y));

        public static Vector2d operator -(Vector2d left, Vector2d right)
            => new(left.X - right.X, left.Y - right.Y);
    }

    private sealed record FaceTriangulationData(
        IReadOnlyList<Vector3d> Points3D,
        IReadOnlyList<Vector2d> ParameterPoints,
        IReadOnlyList<MeshTriangle> Triangles);

    private readonly record struct MeshTriangle(int A, int B, int C);

    private readonly record struct MeshEdge(int A, int B);

    private readonly record struct TriangleMetrics(
        double ConformalDistortion,
        double AreaDistortion,
        double EdgeDistortion);
}
