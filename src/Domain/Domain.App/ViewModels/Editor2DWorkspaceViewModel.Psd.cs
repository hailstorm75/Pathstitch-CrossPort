using Domain.App.Models;
using Domain.App.Services;

namespace Domain.App.ViewModels;

public sealed partial class Editor2DWorkspaceViewModel
{
    public Editor2DWorkspaceOperationResult ImportPsd(PsdImportData import, PsdImportMode mode)
    {
        if (import.CanvasWidth <= 0 || import.CanvasHeight <= 0 || import.TotalLayerCount == 0)
            return Editor2DWorkspaceOperationResult.Failure("The Photoshop file contains no importable layers");
        var vectorize = mode is PsdImportMode.AutoVectorize or PsdImportMode.MergeAndVectorize;
        if (vectorize && _referenceImageTraceService is null)
            return Editor2DWorkspaceOperationResult.Failure("Raster vectorization is unavailable");

        var fit = Math.Min(1.0, 240.0 / Math.Max(import.CanvasWidth, import.CanvasHeight));
        var layers = Layers.OrderBy(layer => layer.Order).ToList();
        var additions = new List<Editor2DPreviewPath>();
        var selectedIds = new List<string>();
        var sourceName = Path.GetFileNameWithoutExtension(import.SourcePath);
        var baseName = $"PSD_{(string.IsNullOrWhiteSpace(sourceName) ? "Import" : sourceName)}";

        void AddVectorLayer(PsdVectorLayer vector)
        {
            var paths = vector.Entities
                .Where(entity => entity.Points.Count >= 2)
                .Select(entity => new Editor2DPreviewPath(
                    $"psd-vector-{Guid.NewGuid():N}",
                    "LWPOLYLINE",
                    entity.Points.Select(point => new Editor2DPoint(point.X * fit, point.Y * fit)).ToArray(),
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
            bool trace)
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
                centerX * fit,
                centerY * fit,
                pixelWidth * fit,
                pixelHeight * fit,
                Opacity: 0.5,
                CalibrationUnitsPerPixel: fit);
            layers.Add(new Editor2DLayer(
                id,
                name,
                [],
                IsVisible: isVisible,
                Order: layers.Count,
                Kind: Editor2DLayerKind.ReferenceImage,
                ReferenceImage: image));
            if (!trace)
                return;

            var contours = _referenceImageTraceService!.TraceContours(
                dataBase64,
                new Editor2DReferenceImageTraceOptions(0.5, 50.0, 50.0, 50.0, false));
            var traces = contours
                .Where(contour => contour.Count >= 3)
                .Select(contour => new Editor2DPreviewPath(
                    $"psd-trace-{Guid.NewGuid():N}",
                    "REFERENCE_TRACE",
                    contour.Select(point => new Editor2DPoint(
                        image.X + (((point.X / pixelWidth) - 0.5) * image.Width),
                        image.Y + (((point.Y / pixelHeight) - 0.5) * image.Height))).ToArray(),
                    IsClosed: true))
                .ToArray();
            if (traces.Length == 0)
                return;
            additions.AddRange(traces);
            selectedIds.AddRange(traces.Select(path => path.Id));
            layers.Add(new Editor2DLayer(
                Guid.NewGuid().ToString("N"),
                $"{name}_traced",
                traces.Select(path => path.Id).ToArray(),
                IsVisible: isVisible,
                Order: layers.Count,
                ColorHex: "#22C55E"));
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
                    trace: false);
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
                    trace: true);
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
                        trace: mode == PsdImportMode.AutoVectorize);
                }
                break;
        }

        var addedLayerCount = layers.Count - Layers.Count;
        if (addedLayerCount == 0)
            return Editor2DWorkspaceOperationResult.Failure("The Photoshop file contains no importable layer data");
        Apply(_state with
        {
            Document = RebuildDocument(Document, Document.Paths.Concat(additions).ToArray()),
            IsInitialized = true,
            Layers = layers,
            ActiveLayerId = layers[^1].Id,
            SelectedPathIds = selectedIds,
            SelectedMeasurementId = null,
        });
        return Editor2DWorkspaceOperationResult.Success(
            $"Imported {addedLayerCount} layer{(addedLayerCount == 1 ? string.Empty : "s")} from {Path.GetFileName(import.SourcePath)}");
    }

    private static string NormalizePsdLayerName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "Layer" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "Layer" : normalized;
    }
}
