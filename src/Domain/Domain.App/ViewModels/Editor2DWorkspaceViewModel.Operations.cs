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

        var ids = selected.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        var nextPaths = Document.Paths
            .Select(path => ids.Contains(path.Id) ? path with { IsFilled = false } : path)
            .ToArray();
        CommitDocumentEdit(RebuildDocument(Document, nextPaths), SelectedPathIds);
        return Editor2DWorkspaceOperationResult.Success(
            selected.Count == 1 ? "Converted 1 fill to stroke" : $"Converted {selected.Count} fills to stroke");
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
        CancellationToken token = default)
    {
        var sources = SelectedPaths(Editor2DGeometry.IsCurveOffsettablePath);
        if (sources.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("Select line, polyline, circle, or arc geometry before applying Offset");
        var result = await kernel.BuildCurveOffsetPathsAsync(sources, distance, outward, token).ConfigureAwait(true);
        if (!result.IsSuccess)
            return Editor2DWorkspaceOperationResult.Failure($"OpenGeometry Offset failed: {result.Error ?? "unknown OpenGeometry worker failure"}");
        if (result.Paths.Count == 0)
            return Editor2DWorkspaceOperationResult.Failure("OpenGeometry did not produce an offset path for the selected geometry");
        AppendAndSelect(result.Paths);
        return Editor2DWorkspaceOperationResult.Success(result.Paths.Count == 1
            ? $"OpenGeometry created 1 {(outward ? "outward" : "inward")} offset path"
            : $"OpenGeometry created {result.Paths.Count} {(outward ? "outward" : "inward")} offset paths");
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
        var transformed = Document.Paths
            .Select(path => selected.Any(candidate => candidate.Id == path.Id)
                ? Editor2DGeometry.TranslatePath(
                    Editor2DGeometry.RotatePath(path, pivot, rotationDegrees, path.Id),
                    deltaX,
                    deltaY,
                    path.Id)
                : path)
            .ToArray();
        Edit(state => state with
        {
            Document = state.Document with { Paths = transformed },
            SelectedPathIds = SelectedPathIds.ToArray(),
        });
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
        var selectedIds = selected.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        var transformed = Document.Paths
            .Select(path => selectedIds.Contains(path.Id)
                ? Editor2DGeometry.ScalePath(path, pivot, factor, path.Id)
                : path)
            .ToArray();
        Edit(state => state with
        {
            Document = state.Document with { Paths = transformed },
            SelectedPathIds = SelectedPathIds.ToArray(),
        });
        return Editor2DWorkspaceOperationResult.Success($"Scaled selected geometry by {factor:0.###}");
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
            && TryParsePositiveInt(PatternCopiesXText, out var copiesX)
            && TryParsePositiveInt(PatternCopiesYText, out var copiesY)
            && TryParseFinite(PatternSpacingXText, out var spacingX)
            && TryParseFinite(PatternSpacingYText, out var spacingY))
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
        => ReplaceSelectedConvertibleLines(style, settings,
            "Select line or polyline geometry before applying Convert Lines",
            count => count == 1 ? $"Converted 1 selected entity to {style} geometry" : $"Converted {count} selected entities to {style} geometry");

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
        var updated = source with
        {
            Start = start, Text = normalizedText, TextHeight = normalizedHeight, FontFamily = font,
            CharacterSpacing = spacing, IsBold = bold, IsItalic = italic, IsUnderline = underline,
            WidthFactor = widthFactor,
            Points = Editor2DGeometry.BuildTextBoundsPoints(start, normalizedText, normalizedHeight,
                source.RotationDegrees ?? 0, widthFactor, spacing),
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
