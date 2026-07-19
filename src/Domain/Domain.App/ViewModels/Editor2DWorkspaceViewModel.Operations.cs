using Domain.App.Models;
using Domain.App.Services;
using System.Globalization;

namespace Domain.App.ViewModels;

public sealed partial class Editor2DWorkspaceViewModel
{
    public Editor2DWorkspaceOperationResult ApplyStrokeToFill()
    {
        var selected = SelectedPaths(path => path.IsClosed && !path.IsFilled);
        if (selected.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select one or more closed stroke paths before converting to fill");

        var ids = selected.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        var nextPaths = Document.Paths
            .Select(path => ids.Contains(path.Id) ? path with { IsFilled = true } : path)
            .ToArray();
        CommitDocumentEdit(RebuildDocument(Document, nextPaths), SelectedPathIds);
        return Editor2DWorkspaceOperationResult.Success(
            selected.Count == 1 ? "Converted 1 stroke path to fill" : $"Converted {selected.Count} stroke paths to fill");
    }

    public Editor2DWorkspaceOperationResult ApplyFillToStroke()
    {
        var selected = SelectedPaths(static path => path.IsFilled);
        if (selected.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select one or more filled paths before converting to stroke");

        var replacements = selected.ToDictionary(
            static path => path.Id,
            Editor2DGeometry.ConvertFillToStrokePaths,
            StringComparer.Ordinal);
        var nextPaths = Document.Paths.SelectMany(path => replacements.TryGetValue(path.Id, out var strokes)
                ? strokes
                : [path])
            .ToArray();
        var nextSelection = SelectedPathIds.SelectMany(id => replacements.TryGetValue(id, out var strokes)
                ? strokes.Select(static stroke => stroke.Id)
                : [id])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var nextLayers = Layers.Select(layer => layer with
            {
                PathIds = layer.PathIds.SelectMany(id => replacements.TryGetValue(id, out var strokes)
                        ? strokes.Select(static stroke => stroke.Id)
                        : [id])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            })
            .ToArray();

        Apply(_state with
        {
            Document = RebuildDocument(Document, nextPaths),
            IsInitialized = true,
            SelectedPathIds = nextSelection,
            SelectedMeasurementId = null,
            Layers = nextLayers,
        });
        var strokeCount = replacements.Values.Sum(static strokes => strokes.Count);
        return Editor2DWorkspaceOperationResult.Success(
            selected.Count == 1
                ? $"Converted 1 fill to {strokeCount} stroke{(strokeCount == 1 ? "" : "s")}"
                : $"Converted {selected.Count} fills to {strokeCount} strokes");
    }

    public Editor2DWorkspaceOperationResult ApplyExplodeCompoundPaths()
    {
        var selected = SelectedPaths(path => path.IsClosed
            && (path.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
                || path.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase)));
        if (selected.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select a closed polyline before exploding compound paths");

        var replacements = new Dictionary<string, IReadOnlyList<Editor2DPreviewPath>>(StringComparer.Ordinal);
        foreach (var path in selected)
        {
            var loops = Editor2DGeometry.ExplodeCompoundPath(path);
            if (loops.Count > 1)
                replacements[path.Id] = loops;
        }

        if (replacements.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Nothing to explode — the selection is a single loop");

        var selectedIds = new List<string>();
        var nextPaths = new List<Editor2DPreviewPath>();
        foreach (var path in Document.Paths)
        {
            if (!replacements.TryGetValue(path.Id, out var loops))
            {
                nextPaths.Add(path);
                if (SelectedPathIds.Contains(path.Id, StringComparer.Ordinal))
                    selectedIds.Add(path.Id);
                continue;
            }

            nextPaths.AddRange(loops);
            selectedIds.AddRange(loops.Select(loop => loop.Id));
        }

        CommitDocumentEdit(RebuildDocument(Document, nextPaths), selectedIds);
        var loopCount = replacements.Values.Sum(loops => loops.Count);
        return Editor2DWorkspaceOperationResult.Success(
            $"Exploded {replacements.Count} compound path{(replacements.Count == 1 ? "" : "s")} into {loopCount} loops");
    }

    public async Task<Editor2DWorkspaceOperationResult> ApplyCurveOffsetAsync(
        IEditor2DGeometryKernelService kernel,
        double distance,
        bool outward,
        CancellationToken token = default,
        bool construction = false)
    {
        var result = await BuildCurveOffsetPreviewAsync(kernel, distance, outward, token, construction).ConfigureAwait(true);
        if (!result.IsSuccess)
            return Editor2DWorkspaceOperationResult.Failure($"OpenGeometry Offset failed: {result.Error ?? "unknown OpenGeometry worker failure"}");
        if (result.Paths.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("OpenGeometry did not produce an offset path for the selected geometry");
        return CommitCurveOffsetPreview(result.Paths, outward);
    }

    public async Task<Editor2DGeometryKernelResult> BuildCurveOffsetPreviewAsync(
        IEditor2DGeometryKernelService kernel,
        double distance,
        bool outward,
        CancellationToken token = default,
        bool construction = false)
    {
        var sources = SelectedPaths(Editor2DGeometry.IsCurveOffsettablePath).ToArray();
        if (sources.Length == 0)
            return Editor2DGeometryKernelResult.Failure("Select line, polyline, circle, or arc geometry before applying Offset");
        var result = await kernel.BuildCurveOffsetPathsAsync(sources, distance, outward, token).ConfigureAwait(true);
        return result.IsSuccess
            ? result with { Paths = result.Paths.Select(path => path with { IsConstruction = construction }).ToArray() }
            : result;
    }

    public Editor2DWorkspaceOperationResult CommitCurveOffsetPreview(
        IReadOnlyList<Editor2DPreviewPath> previewPaths,
        bool outward)
    {
        if (previewPaths.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("OpenGeometry did not produce an offset path for the selected geometry");
        CommitOffsetPreviewPaths(previewPaths);
        return Editor2DWorkspaceOperationResult.Success(previewPaths.Count == 1
            ? $"OpenGeometry created 1 {(outward ? "outward" : "inward")} offset path"
            : $"OpenGeometry created {previewPaths.Count} {(outward ? "outward" : "inward")} offset paths");
    }

    private void CommitOffsetPreviewPaths(IReadOnlyList<Editor2DPreviewPath> previewPaths)
    {
        var constructionIds = previewPaths
            .Where(path => path.IsConstruction)
            .Select(path => path.Id)
            .ToArray();
        if (constructionIds.Length == 0)
        {
            AppendAndSelect(previewPaths);
            return;
        }

        var layers = Layers.OrderBy(layer => layer.Order).ToList();
        var constructionIndex = layers.FindIndex(layer =>
            layer.Kind == Editor2DLayerKind.Geometry
            && string.Equals(layer.Name, "CONSTRUCTION", StringComparison.OrdinalIgnoreCase));
        if (constructionIndex < 0)
        {
            var layerId = layers.Any(layer => string.Equals(layer.Id, "layer-construction", StringComparison.Ordinal))
                ? $"layer-construction-{Guid.NewGuid():N}"
                : "layer-construction";
            layers.Add(new Editor2DLayer(
                layerId,
                "CONSTRUCTION",
                constructionIds,
                Order: layers.Count,
                ColorHex: "#808080"));
        }
        else
        {
            layers[constructionIndex] = layers[constructionIndex] with
            {
                Name = "CONSTRUCTION",
                ColorHex = "#808080",
                PathIds = layers[constructionIndex].PathIds
                    .Concat(constructionIds)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            };
        }

        Apply(_state with
        {
            Document = RebuildDocument(Document, Document.Paths.Concat(previewPaths).ToArray()),
            IsInitialized = true,
            SelectedPathIds = previewPaths.Select(path => path.Id).ToArray(),
            SelectedMeasurementId = null,
            Layers = layers,
            ActiveLayerId = ActiveLayerId,
        });
    }

    public async Task<Editor2DWorkspaceOperationResult> ApplyBooleanAsync(
        IEditor2DGeometryKernelService kernel,
        Editor2DBooleanOperation operation,
        CancellationToken token = default)
    {
        var selected = SelectedPaths(static path => path.IsClosed && path.Points.Count >= 3);
        if (selected.Count < 2)
            return Editor2DWorkspaceOperationResult.Failure("Select at least two closed paths before applying a boolean operation");

        var result = await kernel.BuildBooleanPathsAsync(selected, operation, token).ConfigureAwait(true);
        if (!result.IsSuccess || result.Paths.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure($"OpenGeometry boolean failed: {result.Error ?? "no result"}");

        var selectedIds = selected.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        var nextPaths = Document.Paths.Where(path => !selectedIds.Contains(path.Id)).Concat(result.Paths).ToArray();
        CommitDocumentEdit(RebuildDocument(Document, nextPaths), result.Paths.Select(path => path.Id).ToArray());
        return Editor2DWorkspaceOperationResult.Success($"OpenGeometry {operation.ToString().ToLowerInvariant()} created {result.Paths.Count} path{(result.Paths.Count == 1 ? "" : "s")}");
    }

    public Editor2DWorkspaceOperationResult ApplyBoundingBoxOffset(double distance, double cornerRadius)
    {
        var selected = SelectedPaths();
        if (selected.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select one or more 2D entities before applying BBox Offset");
        var path = Editor2DGeometry.BuildBoundingBoxOffsetPath(selected, distance, cornerRadius);
        if (path is null)
            return Editor2DWorkspaceOperationResult.Failure("The current selection could not produce a BBox offset");
        AppendAndSelect([path]);
        return Editor2DWorkspaceOperationResult.Success("Created a new BBox offset profile");
    }

    public async Task<Editor2DWorkspaceOperationResult> ApplyThicknessAsync(
        IEditor2DGeometryKernelService kernel,
        double thickness,
        CancellationToken token = default)
    {
        var sources = SelectedPathIds.Count == 0
            ? Document.Paths.Where(Editor2DGeometry.IsThicknessSourcePath).ToArray()
            : SelectedPaths(Editor2DGeometry.IsThicknessSourcePath);
        if (sources.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("No eligible open line or polyline centerlines are available for Add Thickness");
        var result = await kernel.BuildThicknessOutlinesAsync(sources, thickness, token).ConfigureAwait(true);
        if (!result.IsSuccess)
            return Editor2DWorkspaceOperationResult.Failure($"OpenGeometry Add Thickness failed: {result.Error ?? "unknown OpenGeometry worker failure"}");
        if (result.Paths.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("OpenGeometry did not produce a thickened outline for the selected geometry");
        AppendAndSelect(result.Paths);
        return Editor2DWorkspaceOperationResult.Success(result.Paths.Count == 1
            ? "OpenGeometry created 1 thickened outline"
            : $"OpenGeometry created {result.Paths.Count} thickened outlines");
    }

    public Editor2DWorkspaceOperationResult ApplyCleanup(double tolerance)
    {
        var sources = Document.Paths.Where(Editor2DGeometry.IsCleanupSourcePath).ToArray();
        if (sources.Length == 0)
            return Editor2DWorkspaceOperationResult.Failure("No open line or polyline geometry is available for Join/Cleanup");
        var cleaned = Editor2DGeometry.BuildCleanupPaths(sources, tolerance);
        if (cleaned.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Join/Cleanup could not build any cleaned paths");
        var sourceIds = sources.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        CommitDocumentEdit(RebuildDocument(Document, Document.Paths.Where(path => !sourceIds.Contains(path.Id)).Concat(cleaned).ToArray()),
            cleaned.Select(path => path.Id).ToArray());
        return Editor2DWorkspaceOperationResult.Success(cleaned.Count == 1
            ? "Join/Cleanup produced 1 cleaned path"
            : $"Join/Cleanup produced {cleaned.Count} cleaned paths");
    }

    public Editor2DWorkspaceOperationResult ApplyRectangularPattern(int copiesX, int copiesY, double spacingX, double spacingY)
    {
        var selected = SelectedPaths();
        if (selected.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select one or more 2D entities before applying a rectangular pattern");
        if (copiesX == 1 && copiesY == 1)
            return Editor2DWorkspaceOperationResult.Failure("Increase the rectangular pattern counts to create at least one duplicate");
        var additions = new List<Editor2DPreviewPath>();
        for (var row = 0; row < copiesY; row++)
        for (var column = 0; column < copiesX; column++)
        {
            if (row == 0 && column == 0) continue;
            additions.AddRange(selected.Select(path => Editor2DGeometry.TranslatePath(
                path, column * spacingX, row * spacingY,
                $"{path.Id}:pattern:{row}:{column}:{Guid.NewGuid():N}")));
        }
        if (additions.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("The selected geometry could not be patterned");
        AppendAndSelect(additions);
        return Editor2DWorkspaceOperationResult.Success(additions.Count == 1
            ? "Created 1 rectangular pattern duplicate"
            : $"Created {additions.Count} rectangular pattern duplicates");
    }

    public Editor2DWorkspaceOperationResult ApplyCircularPattern(int totalCount, double totalAngle, Editor2DPoint? pivot = null)
    {
        var selected = SelectedPaths();
        if (selected.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select one or more 2D entities before applying a circular pattern");
        if (totalCount <= 1)
            return Editor2DWorkspaceOperationResult.Failure("Increase the circular pattern count to create at least one duplicate");
        var points = selected.SelectMany(path => path.Points).ToArray();
        if (points.Length == 0)
            return Editor2DWorkspaceOperationResult.Failure("The current selection does not have enough geometry to compute a circular pattern pivot");
        pivot ??= new Editor2DPoint((points.Min(p => p.X) + points.Max(p => p.X)) / 2,
            (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2);
        var fullCircle = Math.Abs(Math.Abs(totalAngle) - 360) <= 1e-6;
        var step = fullCircle ? totalAngle / totalCount : totalAngle / Math.Max(totalCount - 1, 1);
        var additions = Enumerable.Range(1, totalCount - 1)
            .SelectMany(index => selected.Select(path => Editor2DGeometry.RotatePath(
                path, pivot, step * index, $"{path.Id}:pattern:circular:{index}:{Guid.NewGuid():N}")))
            .ToArray();
        AppendAndSelect(additions);
        return Editor2DWorkspaceOperationResult.Success(additions.Length == 1
            ? "Created 1 circular pattern duplicate"
            : $"Created {additions.Length} circular pattern duplicates");
    }

    public Editor2DWorkspaceOperationResult ApplyPreciseTransform(double deltaX, double deltaY, double rotationDegrees)
    {
        var selected = SelectedPaths();
        if (selected.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select one or more 2D entities before applying a precise transform");
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) || !double.IsFinite(rotationDegrees))
            return Editor2DWorkspaceOperationResult.Failure("Enter finite transform values");
        if (Math.Abs(deltaX) < 1e-12 && Math.Abs(deltaY) < 1e-12 && Math.Abs(rotationDegrees) < 1e-12)
            return Editor2DWorkspaceOperationResult.Failure("Enter a non-zero transform");

        var points = selected.SelectMany(path => path.Points).ToArray();
        if (points.Length == 0)
            return Editor2DWorkspaceOperationResult.Failure("The selected geometry has no editable points");
        var pivot = new Editor2DPoint(
            (points.Min(point => point.X) + points.Max(point => point.X)) / 2.0,
            (points.Min(point => point.Y) + points.Max(point => point.Y)) / 2.0);
        var transform = Editor2DAffineTransform.CreateRotation(pivot, rotationDegrees)
            .Then(Editor2DAffineTransform.CreateTranslation(deltaX, deltaY));
        ApplySelectionTransform(transform);
        return Editor2DWorkspaceOperationResult.Success("Applied precise 2D transform");
    }

    public Editor2DWorkspaceOperationResult ApplyScale(double factor, bool fromCenter, Editor2DPoint? customPivot = null)
    {
        var selected = SelectedPaths();
        if (selected.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select one or more 2D entities before applying Scale");
        if (!double.IsFinite(factor) || factor <= 0.0)
            return Editor2DWorkspaceOperationResult.Failure("Enter a positive finite scale factor");

        var points = selected.SelectMany(path => path.Points).ToArray();
        if (points.Length == 0)
            return Editor2DWorkspaceOperationResult.Failure("The selected geometry has no editable bounds");

        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var pivot = customPivot ?? (fromCenter
            ? new Editor2DPoint((minX + points.Max(point => point.X)) / 2.0, (minY + points.Max(point => point.Y)) / 2.0)
            : new Editor2DPoint(minX, minY));
        ApplySelectionTransform(Editor2DAffineTransform.CreateScale(pivot, factor));
        return Editor2DWorkspaceOperationResult.Success($"Scaled selected geometry by {factor:0.###}");
    }

    public Editor2DWorkspaceOperationResult ApplyMirror(
        Editor2DPoint axisStart,
        Editor2DPoint axisEnd,
        bool keepLink = true,
        bool flip = true)
    {
        var selected = SelectedPaths();
        if (selected.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select one or more 2D entities before applying Mirror");
        if (Distance(axisStart, axisEnd) <= 1e-8)
            return Editor2DWorkspaceOperationResult.Failure("Pick two distinct points for the mirror axis");

        var copies = selected
            .Select(path => Editor2DGeometry.CreateMirrorCopy(
                path,
                axisStart,
                axisEnd,
                flip,
                $"{path.Id}:mirror:{Guid.NewGuid():N}"))
            .ToArray();
        AppendAndSelect(copies);
        if (keepLink)
            AddMirrorLinks(selected, copies, axisStart, axisEnd, flip);
        return Editor2DWorkspaceOperationResult.Success(
            $"Mirrored {copies.Length} selected {(copies.Length == 1 ? "entity" : "entities")}");
    }

    public Editor2DWorkspaceOperationResult ApplyPathPattern(string guidePathId, int copyCount, double spacing)
    {
        var guide = Document.Paths.FirstOrDefault(path => path.Id == guidePathId);
        var selected = SelectedPaths().Where(path => path.Id != guidePathId).ToArray();
        if (guide is null || guide.Points.Count < 2)
            return Editor2DWorkspaceOperationResult.Failure("Select a LINE or polyline guide path before applying a path pattern");
        if (selected.Length == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select source geometry separately from the path-pattern guide");
        if (copyCount <= 1 || spacing <= 0)
            return Editor2DWorkspaceOperationResult.Failure("Increase path pattern copies and spacing to create duplicates");

        var segments = guide.Points.Zip(guide.Points.Skip(1), (start, end) => (start, end, length: Distance(start, end)))
            .Where(segment => segment.length > 1e-8)
            .ToArray();
        var totalLength = segments.Sum(segment => segment.length);
        var availableCopies = Math.Min(copyCount - 1, (int)Math.Floor(totalLength / spacing));
        if (availableCopies < 1)
            return Editor2DWorkspaceOperationResult.Failure("Guide path is too short for requested path-pattern spacing");

        var sourcePoints = selected.SelectMany(path => path.Points).ToArray();
        var pivot = new Editor2DPoint(
            (sourcePoints.Min(point => point.X) + sourcePoints.Max(point => point.X)) / 2.0,
            (sourcePoints.Min(point => point.Y) + sourcePoints.Max(point => point.Y)) / 2.0);
        var additions = new List<Editor2DPreviewPath>();
        for (var copyIndex = 1; copyIndex <= availableCopies; copyIndex++)
        {
            if (!TrySamplePath(segments, copyIndex * spacing, out var target, out var tangentDegrees))
                break;

            foreach (var path in selected)
            {
                var rotated = Editor2DGeometry.RotatePath(path, pivot, tangentDegrees);
                var translated = Editor2DGeometry.TranslatePath(
                    rotated,
                    target.X - pivot.X,
                    target.Y - pivot.Y,
                    $"{path.Id}:pattern:path:{copyIndex}:{Guid.NewGuid():N}");
                additions.Add(translated);
            }
        }

        if (additions.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("The selected geometry could not be patterned along guide path");
        AppendAndSelect(additions);
        return Editor2DWorkspaceOperationResult.Success(additions.Count == 1
            ? "Created 1 path pattern duplicate"
            : $"Created {additions.Count} path pattern duplicates");
    }

    public IReadOnlyList<Editor2DPreviewPath> GetPatternPreviewPaths(Editor2DPoint? circularPivot = null, string? guidePathId = null)
    {
        var selected = SelectedPaths();
        if (selected.Count == 0)
            return [];

        var preview = new List<Editor2DPreviewPath>();
        if (string.Equals(PatternMode, "Rectangular", StringComparison.Ordinal)
            && TryGetEffectiveRectangularPattern(
                out var copiesX,
                out var copiesY,
                out var spacingX,
                out var spacingY,
                out _))
        {
            for (var row = 0; row < copiesY; row++)
            for (var column = 0; column < copiesX; column++)
            {
                if (row == 0 && column == 0) continue;
                preview.AddRange(selected.Select(path => Editor2DGeometry.TranslatePath(
                    path, column * spacingX, row * spacingY,
                    $"preview:{path.Id}:{row}:{column}")));
            }
            return preview;
        }

        if (string.Equals(PatternMode, "Circular", StringComparison.Ordinal)
            && TryParsePositiveInt(PatternCircularCountText, out var totalCount)
            && TryParseFinite(PatternCircularAngleText, out var totalAngle)
            && totalCount > 1)
        {
            var points = selected.SelectMany(path => path.Points).ToArray();
            if (points.Length == 0) return [];
            var pivot = circularPivot ?? new Editor2DPoint(
                (points.Min(point => point.X) + points.Max(point => point.X)) / 2.0,
                (points.Min(point => point.Y) + points.Max(point => point.Y)) / 2.0);
            var fullCircle = Math.Abs(Math.Abs(totalAngle) - 360) <= 1e-6;
            var step = fullCircle ? totalAngle / totalCount : totalAngle / Math.Max(totalCount - 1, 1);
            for (var index = 1; index < totalCount; index++)
                preview.AddRange(selected.Select(path => Editor2DGeometry.RotatePath(
                    path, pivot, step * index, $"preview:{path.Id}:circular:{index}")));
            return preview;
        }

        if (string.Equals(PatternMode, "Path", StringComparison.Ordinal)
            && guidePathId is not null
            && TryParsePositiveInt(PatternPathCopiesText, out var copyCount)
            && TryParseFinite(PatternPathSpacingText, out var spacing)
            && spacing > 0)
        {
            var guide = Document.Paths.FirstOrDefault(path => path.Id == guidePathId);
            var sources = selected.Where(path => path.Id != guidePathId).ToArray();
            if (guide is null || guide.Points.Count < 2 || sources.Length == 0) return [];
            var segments = guide.Points.Zip(guide.Points.Skip(1), (start, end) => (start, end, length: Distance(start, end)))
                .Where(segment => segment.length > 1e-8).ToArray();
            var sourcePoints = sources.SelectMany(path => path.Points).ToArray();
            if (segments.Length == 0 || sourcePoints.Length == 0) return [];
            var pivot = new Editor2DPoint(
                (sourcePoints.Min(point => point.X) + sourcePoints.Max(point => point.X)) / 2.0,
                (sourcePoints.Min(point => point.Y) + sourcePoints.Max(point => point.Y)) / 2.0);
            var availableCopies = Math.Min(copyCount - 1, (int)Math.Floor(segments.Sum(segment => segment.length) / spacing));
            for (var index = 1; index <= availableCopies; index++)
            {
                if (!TrySamplePath(segments, index * spacing, out var target, out var tangentDegrees)) break;
                foreach (var path in sources)
                {
                    var rotated = Editor2DGeometry.RotatePath(path, pivot, tangentDegrees, $"preview:{path.Id}:path:{index}");
                    preview.Add(Editor2DGeometry.TranslatePath(rotated, target.X - pivot.X, target.Y - pivot.Y, rotated.Id));
                }
            }
        }
        return preview;
    }

    internal bool TryGetEffectiveRectangularPattern(
        out int copiesX,
        out int copiesY,
        out double spacingX,
        out double spacingY,
        out string errorMessage)
    {
        copiesX = 0;
        copiesY = 0;
        spacingX = 0;
        spacingY = 0;

        if (!TryParsePositiveInt(PatternCopiesXText, out copiesX))
        {
            errorMessage = "Enter a valid pattern X count";
            return false;
        }
        if (!TryParsePositiveInt(PatternCopiesYText, out copiesY))
        {
            errorMessage = "Enter a valid pattern Y count";
            return false;
        }

        var extentMode = string.Equals(PatternDistanceMode, "Extent", StringComparison.Ordinal);
        var xText = extentMode ? PatternExtentXText : PatternSpacingXText;
        var yText = extentMode ? PatternExtentYText : PatternSpacingYText;
        var valueLabel = extentMode ? "extent" : "spacing";
        if (!TryParseFinite(xText, out var x) || (!extentMode && x < 0))
        {
            errorMessage = $"Enter a valid pattern X {valueLabel}";
            return false;
        }
        if (!TryParseFinite(yText, out var y) || (!extentMode && y < 0))
        {
            errorMessage = $"Enter a valid pattern Y {valueLabel}";
            return false;
        }

        spacingX = copiesX > 1 ? (extentMode ? x / (copiesX - 1) : x) : 0;
        spacingY = copiesY > 1 ? (extentMode ? y / (copiesY - 1) : y) : 0;
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryParsePositiveInt(string text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;

    private static bool TryParseFinite(string text, out double value)
        => (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
           && double.IsFinite(value);

    private static bool TrySamplePath(
        IReadOnlyList<(Editor2DPoint start, Editor2DPoint end, double length)> segments,
        double distance,
        out Editor2DPoint point,
        out double tangentDegrees)
    {
        var remaining = distance;
        foreach (var segment in segments)
        {
            if (remaining <= segment.length)
            {
                var factor = remaining / segment.length;
                point = new Editor2DPoint(
                    segment.start.X + ((segment.end.X - segment.start.X) * factor),
                    segment.start.Y + ((segment.end.Y - segment.start.Y) * factor));
                tangentDegrees = Math.Atan2(segment.end.Y - segment.start.Y, segment.end.X - segment.start.X) * 180.0 / Math.PI;
                return true;
            }

            remaining -= segment.length;
        }

        point = new Editor2DPoint(0, 0);
        tangentDegrees = 0;
        return false;
    }

    private static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));

    public Editor2DWorkspaceOperationResult ApplyCreases()
        => ReplaceSelectedConvertibleLines("dashed", new Dictionary<string, double> { ["dash_length"] = 2, ["gap"] = 1 },
            "Select line or polyline geometry before applying dashed creases",
            count => count == 1 ? "Converted 1 selected entity to dashed crease geometry" : $"Converted {count} selected entities to dashed crease geometry");

    public Editor2DWorkspaceOperationResult ApplyConvertedLines(string style, IReadOnlyDictionary<string, double> settings)
        => CreateConvertedLineGroup(style, settings);

    public IReadOnlyList<Editor2DPreviewPath> BuildConvertedLinePreview(
        Editor2DPreviewPath source,
        string style,
        IReadOnlyDictionary<string, double> settings)
        => Editor2DGeometry.BuildConvertedLinePaths(source, style, settings);

    public Editor2DConvertLineGroup? FindConvertLineGroupForPath(string pathId)
        => ConvertLineGroups.FirstOrDefault(group => group.GeneratedPathIds.Contains(pathId, StringComparer.Ordinal));

    public Editor2DWorkspaceOperationResult RestyleConvertedLines(
        string groupId,
        string style,
        IReadOnlyDictionary<string, double> settings)
    {
        var group = ConvertLineGroups.FirstOrDefault(candidate => candidate.Id == groupId);
        if (group is null)
            return Editor2DWorkspaceOperationResult.Failure("The converted-line group no longer exists");
        if (!TryNormalizeConvertLineSettings(style, settings, out var normalizedStyle, out var normalizedSettings))
            return Editor2DWorkspaceOperationResult.Failure("Convert Lines settings must be finite values");

        var rebuiltSources = BuildConvertedSources(group.Id, group.Sources, normalizedStyle, normalizedSettings);
        if (rebuiltSources.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("The saved source geometry could not be converted");

        var nextDocumentPaths = ReplaceGeneratedPaths(
            Document.Paths, group.Sources, rebuiltSources, normalizedStyle, normalizedSettings);
        var nextLayers = ReplaceGeneratedPathsInLayers(Layers, group.Sources, rebuiltSources);
        var updated = group with { Style = normalizedStyle, Settings = normalizedSettings, Sources = rebuiltSources };
        Apply(_state with
        {
            Document = RebuildDocument(Document, nextDocumentPaths),
            Layers = nextLayers,
            ConvertLineGroups = ConvertLineGroups.Select(candidate => candidate.Id == group.Id ? updated : candidate).ToArray(),
            SelectedPathIds = updated.GeneratedPathIds,
        });
        return Editor2DWorkspaceOperationResult.Success($"Updated converted-line group to {normalizedStyle} geometry");
    }

    public Editor2DWorkspaceOperationResult ApplyGlueTabs(double height, string type, string side, double startOffset, double endOffset)
    {
        var sources = SelectedPaths(Editor2DGeometry.IsGlueTabSourcePath);
        if (sources.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select LINE entities before applying glue tabs");
        var additions = new List<Editor2DPreviewPath>();
        foreach (var path in sources)
            if (Editor2DGeometry.TryBuildGlueTabPath(path, height, type, side, startOffset, endOffset, out var tab))
                additions.Add(tab);
        if (additions.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("The selected LINE entities could not produce glue tabs");
        AppendAndSelect(additions);
        return Editor2DWorkspaceOperationResult.Success(additions.Count == 1 ? "Created 1 glue tab outline" : $"Created {additions.Count} glue tab outlines");
    }

    public IReadOnlyList<Editor2DPreviewPath> GetGlueTabPreviewPaths(
        double height,
        string type,
        string side,
        double startOffset,
        double endOffset)
    {
        if (SelectedPathIds.Count != 1)
            return [];
        var source = Document.Paths.FirstOrDefault(path => path.Id == SelectedPathIds[0]);
        return source is not null
               && Editor2DGeometry.TryBuildGlueTabPath(source, height, type, side, startOffset, endOffset, out var preview)
            ? [preview with { Id = $"{source.Id}:glue-tab:preview" }]
            : [];
    }

    public Editor2DWorkspaceOperationResult ApplySelectedText(
        string text, double height, string font, double spacing, bool bold, bool italic, bool underline,
        string fitMode = "None")
    {
        var selected = SelectedPaths(path => path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase));
        if (selected.Count != 1)
            return Editor2DWorkspaceOperationResult.Failure(string.Empty);
        var source = selected[0];
        var start = source.Start ?? source.Points.FirstOrDefault() ?? new Editor2DPoint(0, 0);
        var normalizedText = string.IsNullOrWhiteSpace(text) ? "Label" : text.Replace("\r\n", "\n");
        var normalizedHeight = Math.Max(height, .1);
        var normalizedFitMode = fitMode.Trim() is "Height" or "Width" or "Both" ? fitMode.Trim() : "None";
        var widthFactor = source.WidthFactor ?? 1.0;
        if (normalizedFitMode is "Height" or "Both")
        {
            var targetHeight = GetPathExtent(source.Points, axis: 1);
            var lineCount = Math.Max(normalizedText.Split('\n').Length, 1);
            if (targetHeight > 0.1)
                normalizedHeight = Math.Max(0.1, targetHeight / (lineCount * 1.2));
        }
        if (normalizedFitMode is "Width" or "Both")
        {
            var targetWidth = GetPathExtent(source.Points, axis: 0);
            var naturalBounds = Editor2DGeometry.BuildTextBoundsPoints(
                start, normalizedText, normalizedHeight, source.RotationDegrees ?? 0, 1.0, spacing);
            var naturalWidth = GetPathExtent(naturalBounds, axis: 0);
            if (targetWidth > 0.1 && naturalWidth > 0.1)
                widthFactor = Math.Max(0.1, targetWidth / naturalWidth);
        }
        Editor2DTextBasis? updatedTextBasis = null;
        if (source.TextBasis is { } sourceBasis)
        {
            var sourceBasisHeight = Math.Sqrt((sourceBasis.Vx * sourceBasis.Vx) + (sourceBasis.Vy * sourceBasis.Vy));
            var sourceProjectedWidth = Math.Max(Math.Abs(source.WidthFactor ?? 1.0), 1e-9);
            var heightScale = normalizedHeight / sourceBasisHeight;
            var widthScale = Math.Abs(widthFactor) / sourceProjectedWidth;
            updatedTextBasis = new Editor2DTextBasis(
                sourceBasis.Ux * heightScale * widthScale,
                sourceBasis.Uy * heightScale * widthScale,
                sourceBasis.Vx * heightScale,
                sourceBasis.Vy * heightScale);
        }
        var updated = source with
        {
            Start = start, Text = normalizedText, TextHeight = normalizedHeight, FontFamily = font,
            CharacterSpacing = spacing, IsBold = bold, IsItalic = italic, IsUnderline = underline,
            WidthFactor = widthFactor,
            TextBasis = updatedTextBasis,
            Points = Editor2DGeometry.BuildTextBoundsPoints(start, normalizedText, normalizedHeight,
                source.RotationDegrees ?? 0, widthFactor, spacing, updatedTextBasis),
        };
        CommitDocumentEdit(RebuildDocument(Document, Document.Paths.Select(path => path.Id == source.Id ? updated : path).ToArray()), SelectedPathIds);
        return Editor2DWorkspaceOperationResult.Success("Updated the selected text entity", normalizedText, normalizedHeight);
    }

    private static double GetPathExtent(IReadOnlyList<Editor2DPoint> points, int axis)
    {
        if (points.Count == 0)
            return 0.0;
        var values = points.Select(point => axis == 0 ? point.X : point.Y).ToArray();
        return values.Max() - values.Min();
    }

    private Editor2DWorkspaceOperationResult ReplaceSelectedConvertibleLines(
        string style, IReadOnlyDictionary<string, double> settings, string emptyMessage, Func<int, string> successMessage)
    {
        var selectedIds = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var next = new List<Editor2DPreviewPath>();
        var nextSelection = new List<string>();
        var converted = 0;
        foreach (var path in Document.Paths)
        {
            if (!selectedIds.Contains(path.Id) || !Editor2DGeometry.IsConvertibleLinePath(path))
            {
                next.Add(path);
                continue;
            }
            var replacements = Editor2DGeometry.BuildConvertedLinePaths(path, style, settings);
            if (replacements.Count == 0) { next.Add(path); continue; }
            next.AddRange(replacements);
            nextSelection.AddRange(replacements.Select(item => item.Id));
            converted++;
        }
        if (converted == 0)
            return Editor2DWorkspaceOperationResult.Failure(emptyMessage);
        CommitDocumentEdit(RebuildDocument(Document, next), nextSelection);
        return Editor2DWorkspaceOperationResult.Success(successMessage(converted));
    }

    private Editor2DWorkspaceOperationResult CreateConvertedLineGroup(
        string style,
        IReadOnlyDictionary<string, double> settings)
    {
        if (!TryNormalizeConvertLineSettings(style, settings, out var normalizedStyle, out var normalizedSettings))
            return Editor2DWorkspaceOperationResult.Failure("Convert Lines settings must be finite values");

        var selectedIds = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var selectedSources = Document.Paths
            .Select((path, index) => (path, index))
            .Where(item => selectedIds.Contains(item.path.Id) && Editor2DGeometry.IsConvertibleLinePath(item.path))
            .ToArray();
        if (selectedSources.Length == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select line or polyline geometry before applying Convert Lines");

        var groupId = Guid.NewGuid().ToString("N");
        var sourceSeeds = selectedSources.Select(item =>
        {
            var layer = Layers.FirstOrDefault(candidate => candidate.PathIds.Contains(item.path.Id, StringComparer.Ordinal));
            return new Editor2DConvertLineSource(
                item.path,
                layer?.Id,
                layer is null
                    ? 0
                    : layer.PathIds.Select((id, index) => (id, index))
                        .First(candidate => candidate.id == item.path.Id).index,
                item.index,
                []);
        }).ToArray();
        var convertedSources = BuildConvertedSources(groupId, sourceSeeds, normalizedStyle, normalizedSettings);
        if (convertedSources.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("The selected source geometry could not be converted");

        var sourceById = convertedSources.ToDictionary(source => source.SourcePath.Id, StringComparer.Ordinal);
        var generatedById = convertedSources.ToDictionary(
            source => source.SourcePath.Id,
            source => BuildGeneratedPaths(source, normalizedStyle, normalizedSettings),
            StringComparer.Ordinal);
        var paths = new List<Editor2DPreviewPath>();
        foreach (var path in Document.Paths)
        {
            if (sourceById.ContainsKey(path.Id))
                paths.AddRange(generatedById[path.Id]);
            else
                paths.Add(path);
        }

        var layers = Layers.Select(layer => ReplaceSourceIdsInLayer(layer, convertedSources)).ToArray();
        var group = new Editor2DConvertLineGroup(groupId, normalizedStyle, normalizedSettings, convertedSources);
        Apply(_state with
        {
            Document = RebuildDocument(Document, paths),
            Layers = layers,
            ConvertLineGroups = ConvertLineGroups.Append(group).ToArray(),
            SelectedPathIds = group.GeneratedPathIds,
        });
        return Editor2DWorkspaceOperationResult.Success(selectedSources.Length == 1
            ? $"Converted 1 selected entity to {normalizedStyle} geometry"
            : $"Converted {selectedSources.Length} selected entities to {normalizedStyle} geometry");
    }

    private static IReadOnlyList<Editor2DConvertLineSource> BuildConvertedSources(
        string groupId,
        IReadOnlyList<Editor2DConvertLineSource> sources,
        string style,
        IReadOnlyDictionary<string, double> settings)
        => sources.Select((source, sourceIndex) =>
        {
            var generated = Editor2DGeometry.BuildConvertedLinePaths(source.SourcePath, style, settings);
            return generated.Count == 0
                ? null
                : source with
                {
                    GeneratedPathIds = generated.Select((_, generatedIndex) =>
                        $"convert-{groupId}-{sourceIndex}-{generatedIndex}").ToArray(),
                };
        }).Where(source => source is not null).Cast<Editor2DConvertLineSource>().ToArray();

    private static IReadOnlyList<Editor2DPreviewPath> BuildGeneratedPaths(
        Editor2DConvertLineSource source,
        string style,
        IReadOnlyDictionary<string, double> settings)
    {
        var generated = Editor2DGeometry.BuildConvertedLinePaths(source.SourcePath, style, settings);
        return generated.Select((path, index) => path with { Id = source.GeneratedPathIds[index] }).ToArray();
    }

    private static IReadOnlyList<Editor2DPreviewPath> ReplaceGeneratedPaths(
        IReadOnlyList<Editor2DPreviewPath> documentPaths,
        IReadOnlyList<Editor2DConvertLineSource> previousSources,
        IReadOnlyList<Editor2DConvertLineSource> rebuiltSources,
        string style,
        IReadOnlyDictionary<string, double> settings)
    {
        var previousByGeneratedId = previousSources
            .SelectMany(source => source.GeneratedPathIds.Select(id => (id, source.SourcePath.Id)))
            .ToDictionary(item => item.id, item => item.Id, StringComparer.Ordinal);
        var rebuiltBySourceId = rebuiltSources.ToDictionary(source => source.SourcePath.Id, StringComparer.Ordinal);
        var inserted = new HashSet<string>(StringComparer.Ordinal);
        var next = new List<Editor2DPreviewPath>();
        foreach (var path in documentPaths)
        {
            if (!previousByGeneratedId.TryGetValue(path.Id, out var sourceId))
            {
                next.Add(path);
                continue;
            }
            if (inserted.Add(sourceId) && rebuiltBySourceId.TryGetValue(sourceId, out var rebuilt))
                next.AddRange(BuildGeneratedPaths(rebuilt, style, settings));
        }
        return next;
    }

    private static IReadOnlyList<Editor2DLayer> ReplaceGeneratedPathsInLayers(
        IReadOnlyList<Editor2DLayer> layers,
        IReadOnlyList<Editor2DConvertLineSource> previousSources,
        IReadOnlyList<Editor2DConvertLineSource> rebuiltSources)
    {
        var replacements = previousSources.ToDictionary(
            source => source.SourcePath.Id,
            source => rebuiltSources.FirstOrDefault(rebuilt => rebuilt.SourcePath.Id == source.SourcePath.Id),
            StringComparer.Ordinal);
        var oldOwner = previousSources.SelectMany(source => source.GeneratedPathIds.Select(id => (id, source.SourcePath.Id)))
            .ToDictionary(item => item.id, item => item.Id, StringComparer.Ordinal);
        return layers.Select(layer =>
        {
            var inserted = new HashSet<string>(StringComparer.Ordinal);
            var ids = new List<string>();
            foreach (var id in layer.PathIds)
            {
                if (!oldOwner.TryGetValue(id, out var sourceId)) { ids.Add(id); continue; }
                if (inserted.Add(sourceId) && replacements[sourceId] is { } replacement)
                    ids.AddRange(replacement.GeneratedPathIds);
            }
            return layer with { PathIds = ids };
        }).ToArray();
    }

    private static Editor2DLayer ReplaceSourceIdsInLayer(
        Editor2DLayer layer,
        IReadOnlyList<Editor2DConvertLineSource> sources)
    {
        var replacements = sources.Where(source => source.SourceLayerId == layer.Id)
            .ToDictionary(source => source.SourcePath.Id, source => source.GeneratedPathIds, StringComparer.Ordinal);
        var ids = layer.PathIds.SelectMany(id => replacements.TryGetValue(id, out var generated) ? generated : [id]).ToArray();
        return layer with { PathIds = ids };
    }

    private static bool TryNormalizeConvertLineSettings(
        string style,
        IReadOnlyDictionary<string, double> settings,
        out string normalizedStyle,
        out IReadOnlyDictionary<string, double> normalizedSettings)
    {
        normalizedStyle = string.IsNullOrWhiteSpace(style) ? "dashed" : style.Trim().ToLowerInvariant();
        if (!SupportedConvertLineStyles.Contains(normalizedStyle)
            || settings.Any(setting => string.IsNullOrWhiteSpace(setting.Key) || !double.IsFinite(setting.Value)))
        {
            normalizedSettings = new Dictionary<string, double>();
            return false;
        }
        normalizedSettings = CanonicalizeConvertLineSettings(normalizedStyle, settings);
        return true;
    }

    public Editor2DWorkspaceOperationResult AddImportedDrawings(
        IReadOnlyList<Editor2DImportedDrawing> drawings,
        double spacing = 20.0)
    {
        var valid = drawings.Where(drawing =>
                drawing is not null
                && !string.IsNullOrWhiteSpace(drawing.SourceFilePath)
                && double.IsFinite(drawing.AppliedUnitScale)
                && drawing.AppliedUnitScale > 0.0
                && drawing.Document is { Paths.Count: > 0 })
            .ToArray();
        if (valid.Length == 0)
            return Editor2DWorkspaceOperationResult.Failure("No imported drawing geometry was supplied");

        var normalizedSpacing = double.IsFinite(spacing) ? Math.Max(0.0, spacing) : 20.0;
        var cursorX = Document.Paths.Count == 0 ? 0.0 : Document.Bounds.MaxX + normalizedSpacing;
        var appendedPaths = new List<Editor2DPreviewPath>();
        var appendedLayers = new List<Editor2DLayer>();
        var groups = new List<Editor2DImportGroup>();
        var existingLayers = Document.Paths.Count == 0
            && ImportGroups.Count == 0
            && Layers is [{ Id: "layer-1", PathIds.Count: 0 }]
                ? []
                : Layers;
        var nextLayerOrder = existingLayers.Select(layer => layer.Order).DefaultIfEmpty(-1).Max() + 1;
        foreach (var drawing in valid)
        {
            var groupId = Guid.NewGuid().ToString("N");
            var bounds = MeasureImportBounds(drawing.Document.Paths);
            var deltaX = cursorX - bounds.MinX;
            var deltaY = -bounds.MinY;
            var generated = drawing.Document.Paths.Select((path, index) =>
                Editor2DGeometry.TranslatePath(path, deltaX, deltaY, $"import-{groupId}-{index}")).ToArray();
            var generatedIds = generated.Select(path => path.Id).ToArray();
            var fallbackLayerName = Path.GetFileNameWithoutExtension(drawing.SourceFilePath.Trim());
            if (string.IsNullOrWhiteSpace(fallbackLayerName))
                fallbackLayerName = "Imported Drawing";
            var generatedLayerIds = new List<string>();
            foreach (var sourceLayer in generated.GroupBy(
                path => ImportedSourceLayerName(path, fallbackLayerName),
                StringComparer.Ordinal))
            {
                var layerId = $"import-layer-{groupId}-{generatedLayerIds.Count}";
                generatedLayerIds.Add(layerId);
                appendedLayers.Add(new Editor2DLayer(
                    layerId,
                    sourceLayer.Key,
                    sourceLayer.Select(path => path.Id).ToArray(),
                    Order: nextLayerOrder++));
            }
            groups.Add(new Editor2DImportGroup(
                groupId,
                drawing.SourceFilePath.Trim(),
                drawing.AppliedUnitScale,
                generatedIds,
                generatedLayerIds[0],
                0,
                Document.Paths.Count + appendedPaths.Count,
                drawing.Document.UnsupportedEntityTypes,
                generatedLayerIds));
            appendedPaths.AddRange(generated);
            cursorX += bounds.Width + normalizedSpacing;
        }

        var selectedIds = groups.SelectMany(group => group.GeneratedPathIds).ToArray();
        var unsupportedEntityTypes = Document.UnsupportedEntityTypes
            .Concat(valid.SelectMany(drawing => drawing.Document.UnsupportedEntityTypes))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Apply(_state with
        {
            Document = RebuildDocument(
                Document with { UnsupportedEntityTypes = unsupportedEntityTypes },
                Document.Paths.Concat(appendedPaths).ToArray()),
            Layers = existingLayers.Concat(appendedLayers).ToArray(),
            ActiveLayerId = existingLayers.Any(layer => layer.Id == ActiveLayerId)
                ? ActiveLayerId
                : appendedLayers[0].Id,
            ImportGroups = ImportGroups.Concat(groups).ToArray(),
            BaseUnsupportedEntityTypes = _state.BaseUnsupportedEntityTypes ?? Document.UnsupportedEntityTypes,
            SelectedPathIds = selectedIds,
            IsInitialized = true,
        });
        return Editor2DWorkspaceOperationResult.Success(groups.Count == 1
            ? "Imported 1 drawing"
            : $"Imported {groups.Count} drawings side by side");
    }

    private static string ImportedSourceLayerName(Editor2DPreviewPath path, string fallbackLayerName)
        => string.IsNullOrWhiteSpace(path.SourceLayerName)
            ? fallbackLayerName
            : path.SourceLayerName.Trim();
    public Editor2DWorkspaceOperationResult ReloadImportGroups(
        IReadOnlyDictionary<string, Editor2DPreviewDocument> documentsByGroupId)
    {
        if (documentsByGroupId.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("No imported drawing groups were supplied for reload");
        var replacements = new Dictionary<string, (
            Editor2DImportGroup Existing,
            Editor2DImportGroup Updated,
            IReadOnlyList<Editor2DPreviewPath> Paths)>(StringComparer.Ordinal);
        foreach (var (groupId, sourceDocument) in documentsByGroupId)
        {
            var group = ImportGroups.FirstOrDefault(candidate => candidate.Id == groupId);
            if (group is null || sourceDocument is not { Paths.Count: > 0 })
                return Editor2DWorkspaceOperationResult.Failure("An imported drawing group could not be reloaded");
            var currentPaths = Document.Paths.Where(path => group.GeneratedPathIds.Contains(path.Id, StringComparer.Ordinal)).ToArray();
            if (currentPaths.Length == 0)
                return Editor2DWorkspaceOperationResult.Failure("An imported drawing group no longer has reloadable geometry");

            var currentBounds = MeasureImportBounds(currentPaths);
            var sourceBounds = MeasureImportBounds(sourceDocument.Paths);
            var deltaX = currentBounds.CenterX - sourceBounds.CenterX;
            var deltaY = currentBounds.CenterY - sourceBounds.CenterY;
            var reloadId = Guid.NewGuid().ToString("N");
            var generated = sourceDocument.Paths.Select((path, index) =>
                Editor2DGeometry.TranslatePath(path, deltaX, deltaY, $"import-{group.Id}-{reloadId}-{index}")).ToArray();
            replacements[group.Id] = (
                group,
                group with
                {
                    GeneratedPathIds = generated.Select(path => path.Id).ToArray(),
                    UnsupportedEntityTypes = sourceDocument.UnsupportedEntityTypes,
                },
                generated);
        }
        if (replacements.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("No matching imported drawing groups could be reloaded");

        var oldOwnerByPathId = replacements.Values
            .SelectMany(item => item.Existing.GeneratedPathIds.Select(id => (id, item.Existing.Id)))
            .ToDictionary(item => item.id, item => item.Id, StringComparer.Ordinal);
        var insertedDocumentGroups = new HashSet<string>(StringComparer.Ordinal);
        var nextPaths = new List<Editor2DPreviewPath>();
        foreach (var path in Document.Paths)
        {
            if (!oldOwnerByPathId.TryGetValue(path.Id, out var groupId))
            {
                nextPaths.Add(path);
                continue;
            }
            if (insertedDocumentGroups.Add(groupId))
                nextPaths.AddRange(replacements[groupId].Paths);
        }

        var nextLayers = Layers
            .Select(layer => layer with
            {
                PathIds = layer.PathIds.Where(pathId => !oldOwnerByPathId.ContainsKey(pathId)).ToArray(),
            })
            .ToList();
        var layerAssignments = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var nextLayerOrder = nextLayers.Select(layer => layer.Order).DefaultIfEmpty(-1).Max() + 1;
        foreach (var replacement in replacements.Values.OrderBy(item => item.Existing.DocumentPathIndex))
        {
            var fallbackLayerName = Path.GetFileNameWithoutExtension(replacement.Existing.SourceFilePath);
            if (string.IsNullOrWhiteSpace(fallbackLayerName))
                fallbackLayerName = "Imported Drawing";
            var reusableLayerIds = (replacement.Existing.GeneratedLayerIds ?? [replacement.Existing.OwningLayerId])
                .ToHashSet(StringComparer.Ordinal);
            var assignedLayerIds = new List<string>();
            foreach (var sourceLayer in replacement.Paths.GroupBy(
                path => ImportedSourceLayerName(path, fallbackLayerName),
                StringComparer.Ordinal))
            {
                var layerIndex = nextLayers.FindIndex(layer =>
                    reusableLayerIds.Contains(layer.Id)
                    && !assignedLayerIds.Contains(layer.Id, StringComparer.Ordinal)
                    && layer.Name.Equals(sourceLayer.Key, StringComparison.Ordinal));
                if (layerIndex < 0)
                {
                    layerIndex = nextLayers.FindIndex(layer =>
                        reusableLayerIds.Contains(layer.Id)
                        && !assignedLayerIds.Contains(layer.Id, StringComparer.Ordinal));
                }

                var sourcePathIds = sourceLayer.Select(path => path.Id).ToArray();
                if (layerIndex >= 0)
                {
                    var existingLayer = nextLayers[layerIndex];
                    nextLayers[layerIndex] = existingLayer with
                    {
                        Name = sourceLayer.Key,
                        PathIds = existingLayer.PathIds.Concat(sourcePathIds).ToArray(),
                    };
                    assignedLayerIds.Add(existingLayer.Id);
                }
                else
                {
                    var layerId = $"import-layer-{replacement.Existing.Id}-{Guid.NewGuid():N}";
                    nextLayers.Add(new Editor2DLayer(
                        layerId,
                        sourceLayer.Key,
                        sourcePathIds,
                        Order: nextLayerOrder++));
                    assignedLayerIds.Add(layerId);
                }
            }
            var assignedLayers = assignedLayerIds
                .Select(id => nextLayers.Single(layer => layer.Id == id))
                .ToArray();
            var assignedIndices = assignedLayerIds
                .Select(id => nextLayers.FindIndex(layer => layer.Id == id))
                .Where(index => index >= 0)
                .ToArray();
            var insertionIndex = assignedIndices.DefaultIfEmpty(nextLayers.Count).Min();
            nextLayers.RemoveAll(layer => assignedLayerIds.Contains(layer.Id, StringComparer.Ordinal));
            nextLayers.InsertRange(Math.Min(insertionIndex, nextLayers.Count), assignedLayers);
            layerAssignments[replacement.Existing.Id] = assignedLayerIds;
        }

        var assignedLayerIdSet = layerAssignments.Values.SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);
        var staleGeneratedLayerIds = replacements.Values
            .SelectMany(item => item.Existing.GeneratedLayerIds ?? [item.Existing.OwningLayerId])
            .Where(id => !assignedLayerIdSet.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        nextLayers.RemoveAll(layer => staleGeneratedLayerIds.Contains(layer.Id) && layer.PathIds.Count == 0);
        nextLayers = nextLayers.Select((layer, order) => layer with { Order = order }).ToList();

        var nextGroups = ImportGroups.Select(group =>
        {
            if (!replacements.TryGetValue(group.Id, out var replacement))
                return group;
            var generatedLayerIds = layerAssignments[group.Id];
            return replacement.Updated with
            {
                OwningLayerId = generatedLayerIds[0],
                GeneratedLayerIds = generatedLayerIds,
            };
        }).ToArray();
        var selectedIds = nextGroups
            .Where(group => replacements.ContainsKey(group.Id))
            .SelectMany(group => group.GeneratedPathIds)
            .ToArray();
        var previousImportDiagnostics = ImportGroups
            .SelectMany(group => group.UnsupportedEntityTypes ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseUnsupportedEntityTypes = _state.BaseUnsupportedEntityTypes
            ?? Document.UnsupportedEntityTypes.Where(type => !previousImportDiagnostics.Contains(type)).ToArray();
        var unsupportedEntityTypes = baseUnsupportedEntityTypes
            .Concat(nextGroups.SelectMany(group => group.UnsupportedEntityTypes ?? []))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Apply(_state with
        {
            Document = RebuildDocument(Document with { UnsupportedEntityTypes = unsupportedEntityTypes }, nextPaths),
            Layers = nextLayers,
            ImportGroups = nextGroups,
            BaseUnsupportedEntityTypes = baseUnsupportedEntityTypes,
            SelectedPathIds = selectedIds,
        });
        return Editor2DWorkspaceOperationResult.Success(replacements.Count == 1
            ? "Reloaded 1 imported drawing"
            : $"Reloaded {replacements.Count} imported drawings");
    }

    private static Editor2DBounds MeasureImportBounds(IReadOnlyList<Editor2DPreviewPath> paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        return points.Length == 0
            ? new Editor2DBounds(0, 0, 0, 0)
            : new Editor2DBounds(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.X),
                points.Max(point => point.Y));
    }

    private IReadOnlyList<Editor2DPreviewPath> SelectedPaths(Func<Editor2DPreviewPath, bool>? predicate = null)
    {
        var ids = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        return Document.Paths.Where(path => ids.Contains(path.Id) && (predicate is null || predicate(path))).ToArray();
    }

    private void AppendAndSelect(IReadOnlyList<Editor2DPreviewPath> additions)
        => CommitDocumentEdit(RebuildDocument(Document, Document.Paths.Concat(additions).ToArray()), additions.Select(path => path.Id).ToArray());
}

public sealed record Editor2DWorkspaceOperationResult(
    bool IsSuccess,
    string Message,
    string? NormalizedText = null,
    double? NormalizedTextHeight = null)
{
    public static Editor2DWorkspaceOperationResult Success(string message, string? text = null, double? height = null)
        => new(true, message, text, height);
    public static Editor2DWorkspaceOperationResult Failure(string message) => new(false, message);
}
