using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class Project3DStateServiceTests
{
    [Fact]
    public async Task LoadAsync_MapsLegacySavedStepJsonToViewportJson()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.WriteText(
            "legacy-project.json",
            """
            {
              "savedStepJson": "{\"bodies\":[],\"bbox\":{}}",
              "savedBodies3D": []
            }
            """);
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.Equal("{\"bodies\":[],\"bbox\":{}}", state.ViewportJson);
        Assert.True(state.HasModel);
    }

    [Fact]
    public async Task SaveAsync_WritesViewportJsonAndLegacyCompatibilityAlias()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("project.stch");
        var service = new Project3DStateService();
        var body = new Body3D(0, "Body", [new Face3D(0, "Mesh", 50.0) { BodyIndex = 0 }]);

        await service.SaveAsync(
            projectPath,
            new Project3DState(
                ViewportJson: "{\"bodies\":[{\"body_index\":0}],\"bbox\":{}}",
                Bodies: [body],
                BodyOffsets: []));

        await using var fileStream = File.OpenRead(projectPath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("project.json");
        Assert.NotNull(entry);
        await using var entryStream = entry.Open();
        using var document = await JsonDocument.ParseAsync(entryStream);
        var root = document.RootElement;

        Assert.Equal("{\"bodies\":[{\"body_index\":0}],\"bbox\":{}}", root.GetProperty("savedViewportJson").GetString());
        Assert.Equal("{\"bodies\":[{\"body_index\":0}],\"bbox\":{}}", root.GetProperty("savedStepJson").GetString());
    }

    [Fact]
    public async Task SaveAndLoadAsync_RestoresJsonMeshWorkspaceAsActiveSourceAsset()
    {
        using var workspace = TestWorkspace.Create();
        var sourcePath = workspace.WriteText(
            "mesh_workspace.json",
            """
            {"bodies":[]}
            """);
        var projectPath = workspace.GetPath("combined.stch");
        var service = new Project3DStateService();

        await service.SaveAsync(
            projectPath,
            new Project3DState(
                ViewportJson: "{\"bodies\":[],\"bbox\":{}}",
                Bodies: [],
                BodyOffsets: [],
                SourceModelPath: sourcePath));

        var restored = await service.LoadAsync(projectPath);

        Assert.NotNull(restored.SourceModelPath);
        Assert.True(File.Exists(restored.SourceModelPath));
        Assert.Equal(".json", Path.GetExtension(restored.SourceModelPath));
        Assert.Equal("{\"bodies\":[]}", await File.ReadAllTextAsync(restored.SourceModelPath));
    }

    [Fact]
    public async Task LoadAsync_RestoresEmbeddedStepAsActiveSourceAsset()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("legacy-step.stch");
        await using (var fileStream = File.Create(projectPath))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var projectEntry = archive.CreateEntry("project.json");
            await using (var projectStream = projectEntry.Open())
            await using (var writer = new StreamWriter(projectStream))
            {
                await writer.WriteAsync(
                    """
                    {
                      "savedViewportJson": "{\"bodies\":[],\"bbox\":{}}",
                      "savedBodies3D": []
                    }
                    """);
            }

            var sourceEntry = archive.CreateEntry("active.step");
            await using var sourceStream = sourceEntry.Open();
            await using var sourceWriter = new StreamWriter(sourceStream);
            await sourceWriter.WriteAsync("ISO-10303-21;");
        }
        var service = new Project3DStateService();

        var restored = await service.LoadAsync(projectPath);

        Assert.NotNull(restored.SourceModelPath);
        Assert.True(File.Exists(restored.SourceModelPath));
        Assert.Equal(".step", Path.GetExtension(restored.SourceModelPath), ignoreCase: true);
        Assert.Equal("ISO-10303-21;", await File.ReadAllTextAsync(restored.SourceModelPath));
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsBlankIndependentTwoDWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("blank-2d.stch");
        var service = new Project3DStateService();
        var line = new Editor2DPreviewPath(
            "line-1",
            "LINE",
            [new Editor2DPoint(1, 2), new Editor2DPoint(3, 4)],
            IsClosed: false);
        var document = Editor2DWorkspaceState.Empty.Document with
        {
            Paths = [line],
            Bounds = new Editor2DBounds(1, 2, 3, 4),
            EntityCounts = new Dictionary<string, int> { ["LINE"] = 1 },
        };
        var twoDState = new Editor2DWorkspaceState(
            document,
            ActiveTool: Editor2DTool.Move,
            SelectedPathIds: [line.Id],
            PolygonSides: 8,
            ViewportZoom: 2.5,
            ViewportOffsetX: 12,
            ViewportOffsetY: -4);

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], TwoDWorkspaceState: twoDState));

        var restored = await service.LoadAsync(projectPath);

        Assert.False(restored.HasGeneratedOutput);
        Assert.NotNull(restored.TwoDWorkspaceState);
        Assert.Equal(Editor2DTool.Move, restored.TwoDWorkspaceState.ActiveTool);
        var restoredLine = Assert.Single(restored.TwoDWorkspaceState.Document.Paths);
        Assert.Equal(line.Id, restoredLine.Id);
        Assert.Equal(line.EntityType, restoredLine.EntityType);
        Assert.Equal(line.Points, restoredLine.Points);
        Assert.Equal([line.Id], restored.TwoDWorkspaceState.SelectedPathIds);
        Assert.Equal(2.5, restored.TwoDWorkspaceState.ViewportZoom);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsTwoDLayersAndMembership()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("layers.stch");
        var service = new Project3DStateService();
        var path = new Editor2DPreviewPath("path", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(5, 0)], false);
        var layer = new Editor2DLayer("cut", "Cut", [path.Id], IsVisible: false, IsLocked: true);
        var state = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Document = Editor2DWorkspaceState.Empty.Document with { Paths = [path] },
            Layers = [layer],
            ActiveLayerId = layer.Id,
        };

        await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: state));
        var restored = await service.LoadAsync(projectPath);

        var restoredLayer = Assert.Single(restored.TwoDWorkspaceState!.Layers!);
        Assert.Equal(layer.Id, restoredLayer.Id);
        Assert.Equal(layer.Name, restoredLayer.Name);
        Assert.Equal(layer.PathIds, restoredLayer.PathIds);
        Assert.False(restoredLayer.IsVisible);
        Assert.True(restoredLayer.IsLocked);
        Assert.Equal(layer.Id, restored.TwoDWorkspaceState.ActiveLayerId);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsNestedTwoDFoldersAndLayerMembership()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("nested-layer-folders.stch");
        var service = new Project3DStateService();
        var path = new Editor2DPreviewPath("path", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(5, 0)], false);
        var parent = new Editor2DLayerFolder("production", "Production");
        var child = new Editor2DLayerFolder("cut-folder", "Cut", parent.Id);
        var layer = new Editor2DLayer("cut", "Cut lines", [path.Id], ParentFolderId: child.Id);
        var state = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Document = Editor2DWorkspaceState.Empty.Document with { Paths = [path] },
            Layers = [layer],
            ActiveLayerId = layer.Id,
            Folders = [parent, child],
        };

        await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: state));
        var restored = await service.LoadAsync(projectPath);

        Assert.Equal([parent, child], restored.TwoDWorkspaceState!.Folders);
        Assert.Equal(child.Id, Assert.Single(restored.TwoDWorkspaceState.Layers!).ParentFolderId);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsReferenceImageLayerWithoutCreatingGeometry()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("reference-image.stch");
        var service = new Project3DStateService();
        var referenceImage = new Editor2DReferenceImage(
            "reference-1",
            "pattern.png",
            Convert.ToBase64String([1, 2, 3, 4]),
            800,
            600,
            X: 12.5,
            Y: -8.5,
            Width: 200,
            Height: 150,
            RotationDegrees: 17,
            Opacity: 0.35,
            CalibrationUnitsPerPixel: 0.25,
            TraceThreshold: 0.72,
            Depth: Editor2DReferenceImageDepth.Front);
        var referenceLayer = new Editor2DLayer(
            referenceImage.Id,
            "Pattern reference",
            [],
            IsLocked: true,
            Kind: Editor2DLayerKind.ReferenceImage,
            ReferenceImage: referenceImage);
        var twoDState = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Layers = [new Editor2DLayer("geometry", "Geometry", []), referenceLayer],
            ActiveLayerId = referenceLayer.Id,
        };

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], TwoDWorkspaceState: twoDState));
        var restored = await service.LoadAsync(projectPath);

        Assert.False(restored.HasGeneratedOutput);
        Assert.NotNull(restored.TwoDWorkspaceState);
        Assert.Empty(restored.TwoDWorkspaceState.Document.Paths);
        var restoredLayer = Assert.Single(
            restored.TwoDWorkspaceState.Layers!,
            layer => layer.Kind == Editor2DLayerKind.ReferenceImage);
        Assert.Empty(restoredLayer.PathIds);
        Assert.True(restoredLayer.IsLocked);
        Assert.Equal(referenceImage, restoredLayer.ReferenceImage);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsEditableCornerSourceAndValue()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("corners.stch");
        var service = new Project3DStateService();
        var path = new Editor2DPreviewPath(
            "shape",
            "LWPOLYLINE",
            [new Editor2DPoint(0, 0), new Editor2DPoint(10, 0), new Editor2DPoint(10, 10)],
            true);
        var parameter = new Editor2DCornerParameter("shape:1", path.Id, 1, Editor2DCornerKind.Chamfer, 2.5, path.Points);
        var state = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Document = Editor2DWorkspaceState.Empty.Document with { Paths = [path] },
            CornerParameters = [parameter],
        };

        await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: state));
        var restored = await service.LoadAsync(projectPath);

        var restoredParameter = Assert.Single(restored.TwoDWorkspaceState!.CornerParameters!);
        Assert.Equal(parameter.Id, restoredParameter.Id);
        Assert.Equal(2.5, restoredParameter.Value);
        Assert.Equal(parameter.SourcePoints, restoredParameter.SourcePoints);
        var reopenedWorkspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
        reopenedWorkspace.Apply(restored.TwoDWorkspaceState, recordHistory: false);
        Assert.True(reopenedWorkspace.UpdateCornerParameter(restoredParameter.Id, 4));
        Assert.Equal(4, reopenedWorkspace.CornerParameters.Single().Value);
        Assert.NotEqual(parameter.SourcePoints.Count, reopenedWorkspace.Document.Paths.Single().Points.Count);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsDrivenRectangleDimensionsAndCornerSource()
    {
        using var files = TestWorkspace.Create();
        var projectPath = files.GetPath("driven-rectangle.stch");
        var service = new Project3DStateService();
        var editor = new Editor2DWorkspaceViewModel();
        editor.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var pathId = editor.CreateRectangle(new(30, 40), new(10, 20), initialFilletRadius: 2)!;
        var widthId = $"{pathId}:width";
        Assert.True(editor.TrySetMeasurementValue(widthId, 35, out var error), error);

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], TwoDWorkspaceState: editor.State));
        var restored = await service.LoadAsync(projectPath);
        var reopened = new Editor2DWorkspaceViewModel();
        reopened.Apply(restored.TwoDWorkspaceState!, recordHistory: false);

        Assert.Equal(35, reopened.Measurements.Single(item => item.Id == widthId).Distance, 8);
        Assert.All(
            reopened.CornerParameters.Where(parameter => parameter.PathId == pathId),
            parameter =>
            {
                Assert.Equal(2, parameter.Value);
                Assert.Equal(new Editor2DPoint(30, 40), parameter.SourcePoints[0]);
                Assert.Equal(new Editor2DPoint(-5, 20), parameter.SourcePoints[2]);
            });
        Assert.Equal(pathId, reopened.Document.Paths.Single(path => path.Id == pathId).Id);
        Assert.Contains(pathId, reopened.Layers.Single(layer => layer.PathIds.Contains(pathId)).PathIds);
        Assert.True(reopened.TrySetMeasurementValue($"{pathId}:height", 30, out error), error);
        Assert.Equal(30, reopened.Measurements.Single(item => item.Id == $"{pathId}:height").Distance, 8);
    }

    [Fact]
    public async Task BlankTwoDProject_CanCreateEditSaveCloseAndReopenWithoutTwoDState()
    {
        using var files = TestWorkspace.Create();
        var projectPath = files.GetPath("blank-editable-2d.stch");
        var service = new Project3DStateService();
        var workspace = new Editor2DWorkspaceViewModel();
        var line = new Editor2DPreviewPath(
            "blank-line",
            "LINE",
            [new Editor2DPoint(2, 3), new Editor2DPoint(14, 3)],
            IsClosed: false);
        var measurement = new Editor2DMeasurement(
            "blank-measurement",
            line.Points[0],
            line.Points[1]);
        workspace.Edit(state => state with
        {
            Document = state.Document with { Paths = [line] },
            IsInitialized = true,
            ActiveTool = Editor2DTool.Move,
            SelectedPathIds = [line.Id],
            Measurements = [measurement],
        });

        await service.SaveAsync(
            projectPath,
            new Project3DState(
                ViewportJson: null,
                Bodies: [],
                BodyOffsets: [],
                GeneratedOutputPath: null,
                GeneratedOutputDataBase64: null,
                TwoDWorkspaceState: workspace.State));

        workspace = null!; // Close the editing session; only the project archive remains.
        var restoredProject = await service.LoadAsync(projectPath);
        var reopenedWorkspace = new Editor2DWorkspaceViewModel();
        reopenedWorkspace.Apply(restoredProject.TwoDWorkspaceState!, recordHistory: false);

        Assert.False(restoredProject.HasGeneratedOutput);
        Assert.Null(restoredProject.GeneratedOutputPath);
        Assert.Null(restoredProject.GeneratedOutputDataBase64);
        Assert.True(reopenedWorkspace.IsInitialized);
        Assert.Equal(Editor2DTool.Move, reopenedWorkspace.ActiveTool);
        var reopenedLine = Assert.Single(reopenedWorkspace.Document.Paths);
        Assert.Equal(line.Id, reopenedLine.Id);
        Assert.Equal(line.EntityType, reopenedLine.EntityType);
        Assert.Equal(line.Points, reopenedLine.Points);
        Assert.Equal([line.Id], reopenedWorkspace.SelectedPathIds);
        Assert.Equal(measurement, Assert.Single(reopenedWorkspace.Measurements));
        Assert.False(reopenedWorkspace.CanUndo);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsToolCustomizationInEditorWorkspaceState()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("customized-tools.stch");
        var service = new Project3DStateService();
        var editorState = new EditorWorkspaceState(
            Editor3DTool.Select,
            ThreeDOrthographic: false,
            ShowTwoDWorkspace: false,
            ToolCustomizations:
            [
                new EditorToolCustomization("2d.circle", -10, "G"),
                new EditorToolCustomization("3d.project", 2, "P"),
            ]);

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], WorkspaceState: editorState));

        var restored = await service.LoadAsync(projectPath);

        Assert.NotNull(restored.WorkspaceState);
        Assert.Equal(editorState.ToolCustomizations, restored.WorkspaceState.ToolCustomizations);
    }

    [Fact]
    public async Task SaveAsAsync_PreservesSourceArchiveEntriesAndReplacesProjectName()
    {
        using var workspace = TestWorkspace.Create();
        var source = workspace.WriteText(
            "source.stch",
            "{\"projectName\":\"Source\",\"templateId\":\"blank\",\"customMetadata\":42}");
        var target = workspace.GetPath("renamed.stch");
        var service = new Project3DStateService();
        await service.SaveAsync(source, Project3DState.Empty);
        using (var archive = ZipFile.Open(source, ZipArchiveMode.Update))
        {
            var entry = archive.CreateEntry("custom/data.bin");
            await using var stream = entry.Open();
            await stream.WriteAsync(new byte[] { 1, 2, 3, 4 });
        }
        var state = Project3DState.Empty with
        {
            WorkspaceState = new EditorWorkspaceState(
                Editor3DTool.Select,
                ThreeDOrthographic: false,
                ShowTwoDWorkspace: false,
                ActiveEditorMode: EditorMode.Batch),
        };

        await service.SaveAsAsync(source, target, state);

        using var targetArchive = ZipFile.OpenRead(target);
        Assert.NotNull(targetArchive.GetEntry("custom/data.bin"));
        await using var projectStream = targetArchive.GetEntry("project.json")!.Open();
        using var projectJson = await JsonDocument.ParseAsync(projectStream);
        Assert.Equal("renamed", projectJson.RootElement.GetProperty("projectName").GetString());
        Assert.Equal(42, projectJson.RootElement.GetProperty("customMetadata").GetInt32());
        Assert.Equal(EditorMode.Batch, (await service.LoadAsync(target)).WorkspaceState!.ActiveEditorMode);
        Assert.Null((await service.LoadAsync(source)).WorkspaceState);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string directory)
        {
            Directory = directory;
        }

        private string Directory { get; }

        public static TestWorkspace Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Pathstitch-CrossPort-ProjectStateTests",
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            System.IO.Directory.CreateDirectory(directory);
            return new TestWorkspace(directory);
        }

        public string GetPath(string fileName)
            => Path.Combine(Directory, fileName);

        public string WriteText(string fileName, string contents)
        {
            var path = GetPath(fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // Test cleanup is best-effort; stale temp files do not affect assertions.
            }
        }
    }
}
