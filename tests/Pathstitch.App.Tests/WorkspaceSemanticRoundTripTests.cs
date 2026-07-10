using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Tests;

public sealed class WorkspaceSemanticRoundTripTests
{
    [Fact]
    public async Task SaveReopen_PreservesSimultaneousTwoDThreeDAndExportStateWithoutCrossContamination()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-RoundTrip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "combined.stch");
            var outputPath = Path.Combine(directory, "output.dxf");
            var outputBytes = "0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n"u8.ToArray();
            await File.WriteAllBytesAsync(outputPath, outputBytes);
            var face = new Face3D(7, "Plane", 100) { BodyIndex = 3, IsSelected = true };
            var body = new Body3D(3, "Body", [face]) { IsSelected = true };
            var offset = new BodyOffset3D(3, 12.5, -2, 8);
            var selectedFace = new SelectedFace3D(3, 7);
            var selectedDetails = new SelectedFaceDetails(3, "Body", 7, "Plane", 100);
            var projection = new EditorProjectionWorkspaceState("face", null, 7, 3, 1.25);
            var unfold = new EditorUnfoldWorkspaceState(1, 2, 0, 0, 0, true, true, "5", "1", "4", "2");
            var threeD = new Editor3DWorkspaceState(
                Editor3DTool.Unfold,
                true,
                "{\"bodies\":[{\"body_index\":3}]}",
                null,
                [body],
                [offset],
                [selectedFace],
                [selectedDetails],
                3,
                projection,
                unfold);
            var path = new Editor2DPreviewPath(
                "cut-line",
                "LINE",
                [new Editor2DPoint(0, 0), new Editor2DPoint(20, 0)],
                false);
            var layer = new Editor2DLayer("cut", "Cut", [path.Id], IsVisible: true, IsLocked: false);
            var measurement = new Editor2DMeasurement("measure", path.Points[0], path.Points[1]);
            var twoD = Editor2DWorkspaceState.Empty with
            {
                IsInitialized = true,
                Document = Editor2DWorkspaceState.Empty.Document with { Paths = [path] },
                ActiveTool = Editor2DTool.Measure,
                SelectedPathIds = [path.Id],
                Measurements = [measurement],
                SelectedMeasurementId = measurement.Id,
                Layers = [layer],
                ActiveLayerId = layer.Id,
            };
            var shell = new EditorWorkspaceState(
                Editor3DTool.Unfold,
                true,
                ShowTwoDWorkspace: true,
                TwoDActiveTool: Editor2DTool.Measure,
                ActiveEditorMode: EditorMode.TwoD,
                ToolCustomizations: [new EditorToolCustomization("2d.measure", 0, "Q")]);
            var exportContext = new EditorGeneratedOutputContext(
                "Unfold",
                "Selected faces",
                "1 face",
                "Default",
                DateTimeOffset.Parse("2026-07-10T10:00:00Z"));
            var service = new Project3DStateService();

            await service.SaveAsync(
                projectPath,
                new Project3DState(
                    threeD.ViewportJson,
                    [body],
                    [offset],
                    GeneratedOutputPath: outputPath,
                    GeneratedOutputDataBase64: Convert.ToBase64String(outputBytes),
                    GeneratedOutputContext: exportContext,
                    UnfoldWorkspaceState: unfold,
                    ProjectionWorkspaceState: projection,
                    WorkspaceState: shell,
                    TwoDWorkspaceState: twoD,
                    ThreeDWorkspaceState: threeD));

            var restored = await service.LoadAsync(projectPath);

            Assert.Equal(EditorMode.TwoD, restored.WorkspaceState!.ActiveEditorMode);
            Assert.Equal(Editor2DTool.Measure, restored.WorkspaceState.TwoDActiveTool);
            Assert.Equal(Editor3DTool.Unfold, restored.ThreeDWorkspaceState!.ActiveTool);
            Assert.Equal(selectedFace, Assert.Single(restored.ThreeDWorkspaceState.SelectedFaces));
            Assert.Equal(offset, Assert.Single(restored.ThreeDWorkspaceState.BodyOffsets));
            Assert.Equal(path.Id, Assert.Single(restored.TwoDWorkspaceState!.SelectedPathIds!));
            Assert.Equal(layer.Id, Assert.Single(restored.TwoDWorkspaceState.Layers!).Id);
            Assert.Equal(measurement.Id, restored.TwoDWorkspaceState.SelectedMeasurementId);
            Assert.Equal(exportContext, restored.GeneratedOutputContext);
            Assert.True(restored.HasGeneratedOutput);
            Assert.Equal(outputBytes, await File.ReadAllBytesAsync(restored.GeneratedOutputPath!));
            Assert.DoesNotContain(
                restored.ThreeDWorkspaceState.SelectedFaces,
                selected => restored.TwoDWorkspaceState.SelectedPathIds!.Contains($"{selected.BodyIndex}:{selected.FaceIndex}"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
