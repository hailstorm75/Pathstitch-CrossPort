using System.Text.Json;
using Domain.App.Models;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class EditorExportPreferencePersistenceTests
{
    [Fact]
    public void ExportPreferencesRoundTripWithWorkspaceState()
    {
        var preferences = new Editor2DExportPreferences(
            ExportSelectedOnly: true,
            IncludeMeasurementLines: true,
            SvgPrecisionText: "5",
            SvgStrokeWidthText: "0.25",
            DxfVersion: "R2000",
            PngLongestEdgeText: "4096",
            PngTransparent: false);
        var state = Editor2DWorkspaceState.Empty with { ExportPreferences = preferences };

        var restored = JsonSerializer.Deserialize<Editor2DWorkspaceState>(JsonSerializer.Serialize(state));

        Assert.Equal(preferences, restored!.ExportPreferences);
    }

    [Fact]
    public void LegacyWorkspaceWithoutExportPreferencesUsesDefaults()
    {
        var restored = JsonSerializer.Deserialize<Editor2DWorkspaceState>(
            JsonSerializer.Serialize(Editor2DWorkspaceState.Empty));

        Assert.Null(restored!.ExportPreferences);
        Assert.Equal(new Editor2DExportPreferences(), restored.ExportPreferences ?? new Editor2DExportPreferences());
    }

    [Fact]
    public void EditorRestoresExportPreferencesFromWorkspace()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        editor.TwoDExportSelectedOnly = true;
        editor.TwoDExportMeasurementLines = true;
        editor.TwoDSvgPrecisionText = "6";
        editor.TwoDSvgStrokeWidthText = "0.2";
        editor.TwoDDxfVersion = "R2000";
        editor.TwoDPngLongestEdgeText = "3072";
        editor.TwoDPngTransparent = false;
        var persisted = editor.TwoDWorkspace.State;

        var reopened = EditorPageViewModelModeTests.CreateViewModelForTests();
        reopened.ApplyPersistedTwoDWorkspaceState(persisted);

        Assert.True(reopened.TwoDExportSelectedOnly);
        Assert.True(reopened.TwoDExportMeasurementLines);
        Assert.Equal("6", reopened.TwoDSvgPrecisionText);
        Assert.Equal("0.2", reopened.TwoDSvgStrokeWidthText);
        Assert.Equal("R2000", reopened.TwoDDxfVersion);
        Assert.Equal("3072", reopened.TwoDPngLongestEdgeText);
        Assert.False(reopened.TwoDPngTransparent);
    }
}
