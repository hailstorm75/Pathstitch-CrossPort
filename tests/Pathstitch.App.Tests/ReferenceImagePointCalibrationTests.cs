using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Tests;

public sealed class ReferenceImagePointCalibrationTests
{
    [Fact]
    public void WorkspaceCalibration_UniformlyScalesImageAndSupportsUndoRedo()
    {
        var workspace = Workspace(out var layer);
        Assert.True(workspace.UpdateReferenceImageTransform(layer.Id, 7, 9, 120, 40, 25));
        workspace.ClearHistory();

        Assert.True(workspace.CalibrateReferenceImageDistance(layer.Id, measuredDistance: 5, targetDistance: 10));

        var calibrated = workspace.ActiveLayer!.ReferenceImage!;
        Assert.Equal(7, calibrated.X);
        Assert.Equal(9, calibrated.Y);
        Assert.Equal(240, calibrated.Width);
        Assert.Equal(80, calibrated.Height);
        Assert.Equal(2, calibrated.CalibrationUnitsPerPixel, 8);
        Assert.Equal(25, calibrated.RotationDegrees);
        Assert.True(workspace.Undo());
        Assert.Equal(120, workspace.ActiveLayer!.ReferenceImage!.Width);
        Assert.True(workspace.Redo());
        Assert.Equal(240, workspace.ActiveLayer!.ReferenceImage!.Width);
    }

    [Fact]
    public void WorkspaceCalibration_RejectsInvalidHiddenAndLockedInputs()
    {
        var workspace = Workspace(out var layer);

        Assert.False(workspace.CalibrateReferenceImageDistance(layer.Id, 0, 10));
        Assert.False(workspace.CalibrateReferenceImageDistance(layer.Id, 10, double.PositiveInfinity));
        Assert.False(workspace.CalibrateReferenceImageDistance(layer.Id, 1, double.MaxValue));
        Assert.True(workspace.ToggleLayerVisibility(layer.Id));
        Assert.False(workspace.CalibrateReferenceImageDistance(layer.Id, 10, 20));
        Assert.True(workspace.ToggleLayerVisibility(layer.Id));
        Assert.True(workspace.ToggleLayerLock(layer.Id));
        Assert.False(workspace.CalibrateReferenceImageDistance(layer.Id, 10, 20));
    }

    [Fact]
    public async Task PageCalibration_TransitionsValidatesCommitsAndCancelsOnLayerChange()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        await editor.SetActiveEditorModeAsync(EditorMode.TwoD);
        editor.TwoDWorkspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var reference = editor.TwoDWorkspace.ImportReferenceImage("reference.png", "data", 100, 50);
        editor.SelectTwoDLayer(reference.Id);

        Assert.True(editor.BeginTwoDReferencePointCalibration());
        Assert.True(editor.TwoDReferencePointCalibrationActive);
        editor.CaptureTwoDReferenceCalibrationPoints(new(0, 0), new(3, 4));
        Assert.False(editor.TwoDReferencePointCalibrationActive);
        Assert.True(editor.TwoDReferenceCalibrationAwaitingDistance);
        Assert.Equal("5", editor.TwoDReferenceCalibrationTargetText);
        editor.TwoDReferenceCalibrationTargetText = "bad";
        Assert.False(editor.CommitTwoDReferencePointCalibration());
        Assert.Equal(2, editor.TwoDReferenceCalibrationPoints.Count);
        editor.TwoDReferenceCalibrationTargetText = "10";
        Assert.True(editor.CommitTwoDReferencePointCalibration());
        Assert.Empty(editor.TwoDReferenceCalibrationPoints);
        Assert.False(editor.TwoDReferenceCalibrationAwaitingDistance);
        Assert.Equal(200, editor.TwoDWorkspace.Layers.Single(layer => layer.Id == reference.Id).ReferenceImage!.Width);

        editor.SelectTwoDLayer(reference.Id);
        Assert.True(editor.BeginTwoDReferencePointCalibration());
        var geometry = editor.TwoDWorkspace.Layers.First(layer => !layer.IsReferenceImage);
        editor.SelectTwoDLayer(geometry.Id);
        Assert.False(editor.TwoDReferencePointCalibrationActive);
        Assert.Empty(editor.TwoDReferenceCalibrationPoints);

        editor.SelectTwoDLayer(reference.Id);
        editor.TwoDWorkspace.ClearHistory();
        Assert.True(editor.BeginTwoDReferencePointCalibration());
        editor.CancelTwoDReferencePointCalibration();
        Assert.False(editor.TwoDReferencePointCalibrationActive);
        Assert.Empty(editor.TwoDReferenceCalibrationPoints);
        Assert.False(editor.TwoDWorkspace.CanUndo);
    }

    [Fact]
    public async Task CalibratedValues_PersistAcrossProjectRoundTrip()
    {
        var workspace = Workspace(out var layer);
        Assert.True(workspace.CalibrateReferenceImageDistance(layer.Id, 4, 10));
        var path = Path.Combine(Path.GetTempPath(), $"reference-calibration-{Guid.NewGuid():N}.stch");
        try
        {
            var service = new Project3DStateService();
            await service.SaveAsync(path, new Project3DState(null, [], [], TwoDWorkspaceState: workspace.State));
            var loaded = await service.LoadAsync(path);
            var image = loaded.TwoDWorkspaceState!.Layers!.Single(candidate => candidate.Id == layer.Id).ReferenceImage!;
            Assert.Equal(250, image.Width);
            Assert.Equal(125, image.Height);
            Assert.Equal(2.5, image.CalibrationUnitsPerPixel, 8);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CanvasCalibration_MeasuresMidpointAndExposesBindingsAndFocusedInput()
    {
        Assert.Equal(5, DxfCanvasReferenceCalibration.Distance(new(0, 0), new(3, 4)), 8);
        Assert.Equal(new Editor2DPoint(2, 3), DxfCanvasReferenceCalibration.Midpoint(new(0, 1), new(4, 5)));
        var view = ReadPage("Editor2DView.axaml");
        var panel = ReadPage("Editor2DLayersPanel.axaml");
        Assert.Contains("ReferenceCalibrationActive=\"{Binding TwoDReferencePointCalibrationActive, Mode=TwoWay}\"", view, StringComparison.Ordinal);
        Assert.Contains("ReferenceCalibrationPoints=\"{Binding TwoDReferenceCalibrationPoints, Mode=TwoWay}\"", view, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding TwoDReferenceCalibrationAwaitingDistance}\"", view, StringComparison.Ordinal);
        Assert.Contains("editor.canvas.2d.reference-calibration-input", view, StringComparison.Ordinal);
        Assert.Contains("OnReferenceCalibrationInputKeyDown", view, StringComparison.Ordinal);
        Assert.Contains("editor.2d.reference-calibration.begin", panel, StringComparison.Ordinal);
        var canvas = ReadSource("Controls", "DxfPreviewCanvas.cs");
        Assert.Contains("calibrationViewModel.CancelTwoDReferencePointCalibration()", canvas, StringComparison.Ordinal);
    }

    private static Editor2DWorkspaceViewModel Workspace(out Editor2DLayer layer)
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        layer = workspace.ImportReferenceImage("reference.png", "data", 100, 50);
        return workspace;
    }

    private static string ReadPage(string fileName)
        => ReadSource("Pages", fileName);

    private static string ReadSource(string folderName, string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Pathstitch.App", folderName, fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(fileName);
    }
}
