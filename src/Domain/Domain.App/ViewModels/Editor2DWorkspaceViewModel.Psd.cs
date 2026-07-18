using Domain.App.Models;
using Domain.App.Services;

namespace Domain.App.ViewModels;

public sealed partial class Editor2DWorkspaceViewModel
{
    public Editor2DWorkspaceOperationResult ImportPsd(
        PsdImportData import,
        PsdImportMode mode,
        Editor2DPoint? insertionPoint = null,
        Editor2DViewportPlacement? viewport = null)
    {
        if (import.CanvasWidth <= 0 || import.CanvasHeight <= 0 || import.TotalLayerCount == 0)
            return Editor2DWorkspaceOperationResult.Failure("The Photoshop file contains no importable layers");
        var vectorize = mode is PsdImportMode.AutoVectorize or PsdImportMode.MergeAndVectorize;
        if (vectorize && _referenceImageTraceService is null)
            return Editor2DWorkspaceOperationResult.Failure("Raster vectorization is unavailable");

        var hasViewport = viewport is { } currentViewport
            && double.IsFinite(currentViewport.PixelWidth)
            && double.IsFinite(currentViewport.PixelHeight)
            && double.IsFinite(currentViewport.Zoom)
            && double.IsFinite(currentViewport.OffsetX)
            && double.IsFinite(currentViewport.OffsetY)
            && currentViewport.PixelWidth > 0.0
            && currentViewport.PixelHeight > 0.0
            && currentViewport.Zoom > 0.001;
        var fit = hasViewport
            ? Math.Max(
                0.0001,
                Math.Min(
                    ((viewport!.PixelWidth / viewport.Zoom) * 0.8) / import.CanvasWidth,
                    ((viewport.PixelHeight / viewport.Zoom) * 0.8) / import.CanvasHeight))
            : Math.Min(1.0, 240.0 / Math.Max(import.CanvasWidth, import.CanvasHeight));
        var placement = insertionPoint ?? (hasViewport
            ? new Editor2DPoint(-viewport!.OffsetX / viewport.Zoom, viewport.OffsetY / viewport.Zoom)
            : new Editor2DPoint(0.0, 0.0));
        var layers = Layers.OrderBy(layer => layer.Order).ToList();
        var additions = new List<Editor2DPreviewPath>();
        var selectedIds = new List<string>();
        var traceLayerIds = new List<string>();
        var sourceName = Path.GetFileNameWithoutExtension(import.SourcePath);
        var baseName = $"PSD_{(string.IsNullOrWhiteSpace(sourceName) ? "Import" : sourceName)}";

        void AddVectorLayer(PsdVectorLayer vector)
        {
            var paths = vector.Entities
                .Where(entity => entity.Points.Count >= 2)
                .Select(entity => new Editor2DPreviewPath(
                    $"psd-vector-{Guid.NewGuid():N}",
                    "LWPOLYLINE",
                    entity.Points.Select(point => new Editor2DPoint(
                        (point.X * fit) + placement.X,
                        (point.Y * fit) + placement.Y)).ToArray(),
                    entity.IsClosed))
                .ToArray();
            if (paths.Length == 0)
                return;
            additions.AddRange(paths);
            selectedIds.AddRange(paths.Select(path => path.Id));
            layers.Add(new Editor2DLayer(
                Guid.NewGuid().ToString("N"),
                $"PSD_{NormalizePsdLayerName(vector.Name)}",
                paths.Select(path => path.Id).ToArray(),
                IsVisible: vector.IsVisible,
                Order: layers.Count));
        }

        void AddRasterLayer(
            string name,
            string dataBase64,
            int pixelWidth,
            int pixelHeight,
            double centerX,
            double centerY,
            bool isVisible,
            bool queueForTrace)
        {
            if (string.IsNullOrWhiteSpace(dataBase64) || pixelWidth <= 0 || pixelHeight <= 0)
                return;
            var id = Guid.NewGuid().ToString("N");
            var fileName = $"{NormalizePsdLayerName(name)}.png";
            var image = new Editor2DReferenceImage(
                id,
                fileName,
                dataBase64,
                pixelWidth,
                pixelHeight,
                (centerX * fit) + placement.X,
                (centerY * fit) + placement.Y,
                pixelWidth * fit,
                pixelHeight * fit,
                Opacity: 1.0,
                CalibrationUnitsPerPixel: fit);
            layers.Add(new Editor2DLayer(
                id,
                name,
                [],
                IsVisible: isVisible,
                Order: layers.Count,
                Kind: Editor2DLayerKind.ReferenceImage,
                ReferenceImage: image));
            if (queueForTrace)
                traceLayerIds.Add(id);
        }

        switch (mode)
        {
            case PsdImportMode.LoadAsOneImage:
                AddRasterLayer(
                    baseName,
                    import.CompositePngDataBase64,
                    import.CompositeWidth,
                    import.CompositeHeight,
                    0,
                    0,
                    true,
                    queueForTrace: false);
                break;
            case PsdImportMode.MergeAndVectorize:
                AddRasterLayer(
                    baseName,
                    import.CompositePngDataBase64,
                    import.CompositeWidth,
                    import.CompositeHeight,
                    0,
                    0,
                    true,
                    queueForTrace: true);
                break;
            default:
                foreach (var vector in import.VectorLayers)
                    AddVectorLayer(vector);
                foreach (var raster in import.RasterLayers.Reverse())
                {
                    AddRasterLayer(
                        $"PSD_{NormalizePsdLayerName(raster.Name)}",
                        raster.PngDataBase64,
                        raster.PixelWidth,
                        raster.PixelHeight,
                        raster.CenterX,
                        raster.CenterY,
                        raster.IsVisible,
                        queueForTrace: mode == PsdImportMode.AutoVectorize);
                }
                break;
        }

        var addedLayerCount = layers.Count - Layers.Count;
        if (addedLayerCount == 0)
            return Editor2DWorkspaceOperationResult.Failure("The Photoshop file contains no importable layer data");
        CommitReferenceImageTransformEdit();
        Apply(_state with
        {
            Document = RebuildDocument(Document, Document.Paths.Concat(additions).ToArray()),
            IsInitialized = true,
            Layers = layers,
            ActiveLayerId = traceLayerIds.FirstOrDefault() ?? layers[^1].Id,
            SelectedPathIds = selectedIds,
            SelectedMeasurementId = null,
        });

        if (traceLayerIds.Count > 0)
        {
            BeginReferenceImageTraceBatch(traceLayerIds);
        }
        else
        {
            var activeLayer = layers[^1];
            if (activeLayer.IsReferenceImage && activeLayer.IsVisible && !activeLayer.IsLocked)
                BeginReferenceImageTransformEdit(activeLayer.Id);
        }

        var message = $"Imported {addedLayerCount} layer{(addedLayerCount == 1 ? string.Empty : "s")} from {Path.GetFileName(import.SourcePath)}";
        if (traceLayerIds.Count > 0)
            message += $"; {traceLayerIds.Count} raster layer{(traceLayerIds.Count == 1 ? string.Empty : "s")} ready to vectorize";
        return Editor2DWorkspaceOperationResult.Success(message);
    }

    private static string NormalizePsdLayerName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "Layer" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "Layer" : normalized;
    }
}
