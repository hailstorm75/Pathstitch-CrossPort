using System.Text.Json;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;

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

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task LegacyMeasurementLineExportSetting_MigratesOnlyWithoutCanonicalPreference(
        bool hasCanonicalPreference,
        bool expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-legacy-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "legacy-export.stch");
        var canonicalWorkspace = Editor2DWorkspaceState.Empty with
        {
            ExportPreferences = new Editor2DExportPreferences(IncludeMeasurementLines: false),
        };
        object payload = hasCanonicalPreference
            ? new { exportMeasurementLines = true, savedTwoDWorkspaceState = canonicalWorkspace }
            : new { exportMeasurementLines = true };
        await File.WriteAllTextAsync(projectPath, JsonSerializer.Serialize(payload));
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        try
        {
            var session = new ProjectSession(
                Guid.NewGuid(),
                "Legacy export",
                projectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Opened,
                DateTimeOffset.UtcNow);
            Assert.True(await editor.ConfigureParametersAsync(
                new Dictionary<string, object> { [EditorNavigationParameterKeys.ProjectSession] = session },
                CancellationToken.None));

            await ((INavigablePageViewModel)editor).LoadAsync(CancellationToken.None);

            Assert.Equal(expected, editor.TwoDExportMeasurementLines);
            editor.TwoDExportSelectedOnly = true;
            await editor.SaveDocumentAsync();
            var saved = await new Project3DStateService().LoadAsync(projectPath);
            var preferences = Assert.IsType<Editor2DExportPreferences>(saved.TwoDWorkspaceState?.ExportPreferences);
            Assert.Equal(expected, preferences.IncludeMeasurementLines);
        }
        finally
        {
            editor.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
