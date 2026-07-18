namespace Domain.App.Models;

public enum PsdImportMode
{
    LoadAsIs = 0,
    LoadAsOneImage = 1,
    AutoVectorize = 2,
    MergeAndVectorize = 3,
}

public sealed record PsdRasterLayer(
    string Name,
    string PngDataBase64,
    int PixelWidth,
    int PixelHeight,
    double CenterX,
    double CenterY,
    bool IsVisible = true);

public sealed record PsdVectorEntity(
    IReadOnlyList<Editor2DPoint> Points,
    bool IsClosed);

public sealed record PsdVectorLayer(
    string Name,
    IReadOnlyList<PsdVectorEntity> Entities,
    bool IsVisible = true);

public sealed record Editor2DViewportPlacement(
    double PixelWidth,
    double PixelHeight,
    double Zoom,
    double OffsetX,
    double OffsetY);


public sealed record PsdImportData(
    string SourcePath,
    int CanvasWidth,
    int CanvasHeight,
    string CompositePngDataBase64,
    int CompositeWidth,
    int CompositeHeight,
    IReadOnlyList<PsdRasterLayer> RasterLayers,
    IReadOnlyList<PsdVectorLayer> VectorLayers)
{
    public int TotalLayerCount => RasterLayers.Count + VectorLayers.Count;
}
