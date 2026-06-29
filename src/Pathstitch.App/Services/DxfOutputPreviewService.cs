using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class DxfOutputPreviewService : IEditorOutputPreviewService
{
    public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            return Task.FromResult<Editor2DPreviewDocument?>(null);

        var previewDocument = EditorDxfDocument.LoadPreviewDocument(outputPath);
        var hasBounds = TryMeasureBounds(previewDocument.Paths, out var minX, out var minY, out var maxX, out var maxY);

        var document = new Editor2DPreviewDocument(
            previewDocument.Paths
                .Select(static path => new Editor2DPreviewPath(
                    Id: path.Id,
                    EntityType: path.EntityType,
                    Points: path.Points.Select(static point => new Editor2DPoint(point.X, point.Y)).ToArray(),
                    IsClosed: path.IsClosed,
                    IsAxisAlignedRectangle: path.IsAxisAlignedRectangle,
                    Start: path.Start is DxfPoint start ? new Editor2DPoint(start.X, start.Y) : null,
                    Text: path.Text,
                    TextHeight: path.TextHeight,
                    RotationDegrees: path.RotationDegrees,
                    WidthFactor: path.WidthFactor,
                    Center: path.Center is DxfPoint center ? new Editor2DPoint(center.X, center.Y) : null,
                    Radius: path.Radius,
                    StartAngleDegrees: path.StartAngleDegrees,
                    EndAngleDegrees: path.EndAngleDegrees))
                .ToArray(),
            hasBounds
                ? new Editor2DBounds(minX, minY, maxX, maxY)
                : new Editor2DBounds(0.0, 0.0, 0.0, 0.0),
            new Dictionary<string, int>(previewDocument.EntityCounts, StringComparer.OrdinalIgnoreCase),
            previewDocument.UnsupportedEntityTypes.ToArray());

        return Task.FromResult<Editor2DPreviewDocument?>(document);
    }

    public Task SavePreviewDocumentAsync(
        Editor2DPreviewDocument document,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EditorDxfDocument.SavePreviewDocument(outputPath, document);
        return Task.CompletedTask;
    }

    public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(outputPath))
            return Task.FromResult<EditorGeneratedOutputSummary?>(null);

        var fullOutputPath = Path.GetFullPath(outputPath);
        if (!File.Exists(fullOutputPath))
        {
            return Task.FromResult<EditorGeneratedOutputSummary?>(
                new EditorGeneratedOutputSummary(
                    fullOutputPath,
                    FileExists: false,
                    FileSizeBytes: 0,
                    LastModifiedUtc: null,
                    PreviewPathCount: 0,
                    ClosedPathCount: 0,
                    OpenPathCount: 0,
                    LineEntityCount: 0,
                    PolylineEntityCount: 0,
                    ArcEntityCount: 0,
                    CircleEntityCount: 0,
                    EllipseEntityCount: 0,
                    TextEntityCount: 0,
                    UnsupportedEntityCount: 0,
                    UnsupportedEntityTypes: string.Empty,
                    Width: 0.0,
                    Height: 0.0));
        }

        var fileInfo = new FileInfo(fullOutputPath);
        var previewDocument = EditorDxfDocument.LoadPreviewDocument(fullOutputPath);
        var closedPathCount = previewDocument.Paths.Count(static x => x.IsClosed);
        var openPathCount = previewDocument.Paths.Count - closedPathCount;
        var hasBounds = TryMeasureBounds(previewDocument.Paths, out var minX, out var minY, out var maxX, out var maxY);
        var width = hasBounds ? Math.Max(maxX - minX, 0.0) : 0.0;
        var height = hasBounds ? Math.Max(maxY - minY, 0.0) : 0.0;
        var lineEntityCount = GetEntityCount(previewDocument.EntityCounts, "LINE");
        var polylineEntityCount = GetEntityCount(previewDocument.EntityCounts, "LWPOLYLINE")
                                  + GetEntityCount(previewDocument.EntityCounts, "POLYLINE");
        var arcEntityCount = GetEntityCount(previewDocument.EntityCounts, "ARC");
        var circleEntityCount = GetEntityCount(previewDocument.EntityCounts, "CIRCLE");
        var ellipseEntityCount = GetEntityCount(previewDocument.EntityCounts, "ELLIPSE");
        var textEntityCount = GetEntityCount(previewDocument.EntityCounts, "TEXT");
        var supportedEntityCount = lineEntityCount + polylineEntityCount + arcEntityCount + circleEntityCount + ellipseEntityCount + textEntityCount;
        var unsupportedEntityCount = previewDocument.EntityCounts.Values.Sum() - supportedEntityCount;

        return Task.FromResult<EditorGeneratedOutputSummary?>(
            new EditorGeneratedOutputSummary(
                fullOutputPath,
                FileExists: true,
                FileSizeBytes: fileInfo.Length,
                LastModifiedUtc: fileInfo.LastWriteTimeUtc,
                PreviewPathCount: previewDocument.Paths.Count,
                ClosedPathCount: closedPathCount,
                OpenPathCount: openPathCount,
                LineEntityCount: lineEntityCount,
                PolylineEntityCount: polylineEntityCount,
                ArcEntityCount: arcEntityCount,
                CircleEntityCount: circleEntityCount,
                EllipseEntityCount: ellipseEntityCount,
                TextEntityCount: textEntityCount,
                UnsupportedEntityCount: Math.Max(unsupportedEntityCount, 0),
                UnsupportedEntityTypes: string.Join(", ", previewDocument.UnsupportedEntityTypes),
                Width: width,
                Height: height));
    }

    private static bool TryMeasureBounds(
        IReadOnlyList<DxfPreviewPath> paths,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = double.PositiveInfinity;
        minY = double.PositiveInfinity;
        maxX = double.NegativeInfinity;
        maxY = double.NegativeInfinity;

        foreach (var path in paths)
        {
            foreach (var point in path.Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        return double.IsFinite(minX)
            && double.IsFinite(minY)
            && double.IsFinite(maxX)
            && double.IsFinite(maxY);
    }

    private static int GetEntityCount(IReadOnlyDictionary<string, int> entityCounts, string entityType)
        => entityCounts.TryGetValue(entityType, out var count) ? count : 0;
}
