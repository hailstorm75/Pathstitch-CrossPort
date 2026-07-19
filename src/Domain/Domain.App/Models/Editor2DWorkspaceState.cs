using System.Text.Json.Serialization;

namespace Domain.App.Models;

/// <summary>
/// Persisted, editable 2D workspace state. This is intentionally independent
/// from generated/exported files so a blank sketch can be saved on its own.
/// </summary>
public sealed record Editor2DWorkspaceState(
    [property: JsonPropertyName("document")] Editor2DPreviewDocument Document,
    [property: JsonPropertyName("activeTool")] Editor2DTool ActiveTool = Editor2DTool.Select,
    [property: JsonPropertyName("selectedPathIds")] IReadOnlyList<string>? SelectedPathIds = null,
    [property: JsonPropertyName("measurements")] IReadOnlyList<Editor2DMeasurement>? Measurements = null,
    [property: JsonPropertyName("selectedMeasurementId")] string? SelectedMeasurementId = null,
    [property: JsonPropertyName("polygonSides")] int PolygonSides = 6,
    [property: JsonPropertyName("viewportZoom")] double ViewportZoom = 0.0,
    [property: JsonPropertyName("viewportOffsetX")] double ViewportOffsetX = 0.0,
    [property: JsonPropertyName("viewportOffsetY")] double ViewportOffsetY = 0.0,
    [property: JsonPropertyName("expandedRectanglePathIds")] IReadOnlyList<string>? ExpandedRectanglePathIds = null,
    [property: JsonPropertyName("isInitialized")] bool IsInitialized = true,
    [property: JsonPropertyName("layers")] IReadOnlyList<Editor2DLayer>? Layers = null,
    [property: JsonPropertyName("activeLayerId")] string? ActiveLayerId = null,
    [property: JsonPropertyName("cornerParameters")] IReadOnlyList<Editor2DCornerParameter>? CornerParameters = null,
    [property: JsonPropertyName("sewingHoleParameters")] Editor2DSewingHoleParameters? SewingHoleParameters = null,
    [property: JsonPropertyName("sewingHoleOperations")] IReadOnlyList<Editor2DSewingHoleOperation>? SewingHoleOperations = null,
    [property: JsonPropertyName("snapEnabled")] bool SnapEnabled = true,
    [property: JsonPropertyName("gridVisible")] bool GridVisible = true,
    [property: JsonPropertyName("chainSelectionEnabled")] bool ChainSelectionEnabled = false,
    [property: JsonPropertyName("folders")] IReadOnlyList<Editor2DLayerFolder>? Folders = null,
    [property: JsonPropertyName("convertLineGroups")] IReadOnlyList<Editor2DConvertLineGroup>? ConvertLineGroups = null,
    [property: JsonPropertyName("importGroups")] IReadOnlyList<Editor2DImportGroup>? ImportGroups = null,
    [property: JsonPropertyName("baseUnsupportedEntityTypes")] IReadOnlyList<string>? BaseUnsupportedEntityTypes = null,
    [property: JsonPropertyName("exportPreferences")] Editor2DExportPreferences? ExportPreferences = null)
{
    public static Editor2DWorkspaceState Empty { get; } = new(
        new Editor2DPreviewDocument(
            [],
            new Editor2DBounds(0, 0, 0, 0),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            []),
        IsInitialized: false);

    public static Editor2DWorkspaceState NewProject { get; } = Empty with
    {
        IsInitialized = true,
        Layers = [new Editor2DLayer("layer-1", "Layer 1", [], ColorHex: "#4D7FFF")],
        ActiveLayerId = "layer-1",
    };
}

public sealed record Editor2DExportPreferences(
    bool ExportSelectedOnly = false,
    bool IncludeMeasurementLines = false,
    string SvgPrecisionText = "3",
    string SvgStrokeWidthText = "0.5",
    string DxfVersion = "R2010",
    string PngLongestEdgeText = "2048",
    bool PngTransparent = true);
