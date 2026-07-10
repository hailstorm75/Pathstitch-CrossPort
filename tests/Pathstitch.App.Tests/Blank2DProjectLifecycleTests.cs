using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class Blank2DProjectLifecycleTests
{
    [Fact]
    public async Task BlankProject_CanEditSaveCloseAndReopenWithoutOutputOrThreeDModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-Blank2D", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "blank-2d.stch");
            var workspace = new Editor2DWorkspaceViewModel();
            workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);

            var line = new Editor2DPreviewPath(
                "line-1",
                "LINE",
                [new Editor2DPoint(0, 0), new Editor2DPoint(25, 10)],
                IsClosed: false);
            workspace.Edit(state => state with
            {
                Document = state.Document with
                {
                    Paths = [line],
                    Bounds = new Editor2DBounds(0, 0, 25, 10),
                    EntityCounts = new Dictionary<string, int> { ["LINE"] = 1 },
                },
                Layers = [new Editor2DLayer("layer-1", "Sketch", [line.Id])],
                ActiveLayerId = "layer-1",
            });
            workspace.SetActiveTool(Editor2DTool.Measure);
            workspace.SetSelection([line.Id]);
            var measurement = new Editor2DMeasurement("measure-1", line.Points[0], line.Points[1]);
            workspace.SetMeasurements([measurement], measurement.Id);

            var persistence = new Project3DStateService();
            await persistence.SaveAsync(projectPath, new Project3DState(
                ViewportJson: null,
                Bodies: [],
                BodyOffsets: [],
                SourceModelPath: null,
                GeneratedOutputPath: null,
                GeneratedOutputDataBase64: null,
                TwoDWorkspaceState: workspace.State));

            workspace.ClearDocument();
            workspace.ClearHistory();
            Assert.False(workspace.IsInitialized);

            var saved = await persistence.LoadAsync(projectPath);
            var reopened = new Editor2DWorkspaceViewModel();
            reopened.Apply(Assert.IsType<Editor2DWorkspaceState>(saved.TwoDWorkspaceState), recordHistory: false);
            reopened.ClearHistory();

            Assert.Null(saved.ViewportJson);
            Assert.Empty(saved.Bodies);
            Assert.Null(saved.SourceModelPath);
            Assert.Null(saved.GeneratedOutputPath);
            Assert.Null(saved.GeneratedOutputDataBase64);
            Assert.True(reopened.IsInitialized);
            var reopenedLine = Assert.Single(reopened.Document.Paths);
            Assert.Equal(line.Id, reopenedLine.Id);
            Assert.Equal(line.EntityType, reopenedLine.EntityType);
            Assert.Equal(line.Points, reopenedLine.Points);
            Assert.Equal(Editor2DTool.Measure, reopened.ActiveTool);
            Assert.Equal(line.Id, Assert.Single(reopened.SelectedPathIds));
            Assert.Equal(measurement, Assert.Single(reopened.Measurements));
            Assert.Equal(measurement.Id, reopened.SelectedMeasurementId);
            Assert.False(reopened.CanUndo);
            Assert.False(reopened.CanRedo);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
