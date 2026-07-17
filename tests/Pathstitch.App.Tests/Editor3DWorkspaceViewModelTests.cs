using System.Reflection;
using System.Text.Json;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class Editor3DWorkspaceViewModelTests
{
    [Fact]
    public void ShellDoesNotDeclareMutableThreeDWorkspaceFields()
    {
        var shellFields = typeof(EditorPageViewModel)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);

        var movedFields = new[]
        {
            "_activeTool",
            "_bodies",
            "_selectedFaces",
            "_selectedFaceDetails",
            "_selectedBodyIndex",
            "_bodyOffsets",
            "_viewportJsonContent",
            "_sourceModelPath",
            "_isPlaneSelectionActive",
            "_selectedProjectionPlane",
            "_distortionModeIndex",
            "_liveRecomputeEnabled",
            "_pendingViewportScripts",
        };

        Assert.All(movedFields, field => Assert.DoesNotContain(field, shellFields));

        var workspaceFields = typeof(Editor3DWorkspaceViewModel)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(movedFields, field => Assert.Contains(field, workspaceFields));
    }

    [Fact]
    public void WorkspaceOwnsToolBodiesAndSelectionAndRaisesNarrowCoordinationEvents()
    {
        var workspace = CreateWorkspace();
        var events = new List<Editor3DWorkspaceCoordinationEventArgs>();
        workspace.CoordinationRequested += (_, args) => events.Add(args);
        var body = CreateBody();
        var face = new SelectedFace3D(0, 0);
        var details = new SelectedFaceDetails(0, "Body 1", 0, "PLANAR", 12);

        workspace.ActivateTool(Editor3DTool.Move);
        workspace.ReplaceBodies([body], "{\"bodies\":[]}", "model.obj");
        workspace.SetFaceSelection([face], [details]);

        Assert.Equal(Editor3DTool.Move, workspace.ActiveTool);
        Assert.Same(body, Assert.Single(workspace.Bodies));
        Assert.Equal(face, Assert.Single(workspace.SelectedFaces));
        Assert.Equal(details, Assert.Single(workspace.SelectedFaceDetails));
        Assert.Equal(
            [
                Editor3DWorkspaceCoordinationKind.PersistState,
                Editor3DWorkspaceCoordinationKind.PersistState,
                Editor3DWorkspaceCoordinationKind.SelectionChanged,
            ],
            events.Select(item => item.Kind));
    }

    [Fact]
    public void WorkspaceStateJsonRoundTripRestoresSemanticThreeDState()
    {
        var original = CreateWorkspace();
        original.ActivateTool(Editor3DTool.Unfold);
        original.ReplaceBodies([CreateBody()], "{\"scene\":true}", "sample.obj");
        original.SetFaceSelection(
            [new SelectedFace3D(0, 0)],
            [new SelectedFaceDetails(0, "Body 1", 0, "PLANAR", 12)]);
        var projection = new EditorProjectionWorkspaceState("face", "face", 0, 0, 2.5);
        var unfold = new EditorUnfoldWorkspaceState(0, 2, 2, 0, 0, true, false, "5", "1", "4", "2");

        original.RestoreState(original.CaptureState() with { Projection = projection, Unfold = unfold });
        var json = JsonSerializer.Serialize(original.CaptureState());
        var persisted = JsonSerializer.Deserialize<Editor3DWorkspaceState>(json);
        Assert.NotNull(persisted);
        var restored = CreateWorkspace();
        restored.RestoreState(persisted);
        var roundTripped = restored.CaptureState();

        Assert.Equal(Editor3DTool.Unfold, roundTripped.ActiveTool);
        Assert.Equal("sample.obj", roundTripped.SourceModelPath);
        Assert.Equal("{\"scene\":true}", roundTripped.ViewportJson);
        Assert.Equal("Body 1", Assert.Single(roundTripped.Bodies).Name);
        Assert.Equal(new SelectedFace3D(0, 0), Assert.Single(roundTripped.SelectedFaces));
        Assert.Equal(projection, roundTripped.Projection);
        Assert.Equal(unfold, roundTripped.Unfold);
    }

    [Fact]
    public void ViewportScriptsAreExposedByWorkspaceInsteadOfShellOwnedState()
    {
        var workspace = CreateWorkspace();
        string? requestedScript = null;
        Editor3DWorkspaceCoordinationEventArgs? coordination = null;
        workspace.ViewportScriptRequested += script => requestedScript = script;
        workspace.CoordinationRequested += (_, args) => coordination = args;

        workspace.RequestViewportScript("frameHome();");

        Assert.Equal("frameHome();", requestedScript);
        Assert.Equal(Editor3DWorkspaceCoordinationKind.ViewportScript, coordination?.Kind);
        Assert.Equal("frameHome();", coordination?.ViewportScript);
    }

    [Fact]
    public void SeamControl_TogglesForcedCutsAndForbiddenFoldsByMode()
    {
        var workspace = CreateWorkspace();

        workspace.RestoreState(workspace.CaptureState() with
        {
            Unfold = workspace.CaptureState().Unfold with { SeamControlModeIndex = 1 },
        });
        workspace.ToggleSeamEdge(2, 7);
        Assert.Equal([new EditorSeamEdge3D(2, 7)], workspace.ForcedSeams);
        workspace.ToggleSeamEdge(2, 7);
        Assert.Empty(workspace.ForcedSeams);

        workspace.RestoreState(workspace.CaptureState() with
        {
            Unfold = workspace.CaptureState().Unfold with { SeamControlModeIndex = 2 },
        });
        workspace.ToggleSeamEdge(1, 4);
        Assert.Equal([new EditorSeamEdge3D(1, 4)], workspace.ForbiddenSeams);
        workspace.ClearActiveSeamOverrides();
        Assert.Empty(workspace.ForbiddenSeams);
    }

    [Fact]
    public void UnfoldDecorationAndAnchorState_RoundTrips()
    {
        var workspace = CreateWorkspace();
        var anchor = new SelectedFace3D(1, 3);
        var edge = new EditorSeamEdge3D(1, 4);
        var decoration = new EditorSeamDecoration3D(edge, "holes");
        var state = workspace.CaptureState() with
        {
            Unfold = workspace.CaptureState().Unfold with
            {
                GlobalSeamDecorationIndex = 2,
                AnchorFace = anchor,
                SeamDecorations = [decoration],
            },
        };

        workspace.RestoreState(state);

        Assert.Equal(2, workspace.CaptureState().Unfold.GlobalSeamDecorationIndex);
        Assert.Equal(anchor, workspace.CaptureState().Unfold.AnchorFace);
        Assert.Equal([decoration], workspace.CaptureState().Unfold.SeamDecorations);
    }

    [Fact]
    public void SeamDecoration_ClearRestoresInheritanceWithoutRewritingExplicitPlain()
    {
        var edge = new EditorSeamEdge3D(1, 4);
        var original = CreateWorkspace();
        original.SetSeamDecoration(edge, "none");
        var persisted = JsonSerializer.Deserialize<Editor3DWorkspaceState>(
            JsonSerializer.Serialize(original.CaptureState()));
        Assert.NotNull(persisted);
        var restored = CreateWorkspace();

        restored.RestoreState(persisted);

        Assert.Equal("none", Assert.Single(restored.SeamDecorations).Decoration);
        restored.ClearSeamDecoration(edge);
        Assert.Empty(restored.SeamDecorations);
        Assert.Null(restored.CaptureState().Unfold.SeamDecorations);
    }

    [Fact]
    public async Task ProjectPersistenceRoundTripPreservesOwnedThreeDWorkspaceState()
    {
        var workspace = CreateWorkspace();
        workspace.ActivateTool(Editor3DTool.Measure);
        workspace.ReplaceBodies([CreateBody()], "{\"scene\":true}", "persisted.obj");
        workspace.SetFaceSelection(
            [new SelectedFace3D(0, 0)],
            [new SelectedFaceDetails(0, "Body 1", 0, "PLANAR", 12)]);
        var projection = new EditorProjectionWorkspaceState("XY", "XY", null, null, 3);
        var unfold = new EditorUnfoldWorkspaceState(1, 1, 0, 0, 0, true, false, "5", "1", "4", "2");
        workspace.RestoreState(workspace.CaptureState() with { Projection = projection, Unfold = unfold });
        var state = workspace.CaptureState();
        var projectPath = Path.Combine(
            Path.GetTempPath(),
            $"pathstitch-3d-workspace-{Guid.NewGuid():N}.stch");
        var persistence = new Project3DStateService();

        try
        {
            await persistence.SaveAsync(
                projectPath,
                new Project3DState(
                    state.ViewportJson,
                    state.Bodies,
                    state.BodyOffsets,
                    SourceModelPath: state.SourceModelPath,
                    ThreeDWorkspaceState: state));
            var loaded = await persistence.LoadAsync(projectPath);

            Assert.NotNull(loaded.ThreeDWorkspaceState);
            Assert.Equal(Editor3DTool.Measure, loaded.ThreeDWorkspaceState.ActiveTool);
            Assert.Equal("persisted.obj", loaded.ThreeDWorkspaceState.SourceModelPath);
            Assert.Equal("Body 1", Assert.Single(loaded.ThreeDWorkspaceState.Bodies).Name);
            Assert.Equal(new SelectedFace3D(0, 0), Assert.Single(loaded.ThreeDWorkspaceState.SelectedFaces));
            Assert.Equal(projection, loaded.ThreeDWorkspaceState.Projection);
            Assert.Equal(unfold, loaded.ThreeDWorkspaceState.Unfold);
        }
        finally
        {
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }

    private static Editor3DWorkspaceViewModel CreateWorkspace()
        => new(
            new Stub3DOperationService(),
            "<html></html>",
            new Uri("file:///viewport/"),
            new GeometryKernelDescriptor("test", "test", "test", "test", "test", "test"));

    private static Body3D CreateBody()
        => new(
            0,
            "Body 1",
            [new Face3D(0, "PLANAR", 12) { BodyIndex = 0 }]);

    private sealed class Stub3DOperationService : IEditor3DOperationService
    {
        public Task<EditorModelLoadResult> LoadModelAsync(string sourceModelPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorModelLoadResult(false, "Not used."));

        public Task<EditorModelLoadResult> LoadModelsAsync(IReadOnlyList<string> sourceModelPaths, string? existingSourceModelPath = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorModelLoadResult(false, "Not used."));

        public Task<EditorFaceDistortionResult> ComputeFaceDistortionAsync(string? sourceModelPath, SelectedFace3D selectedFace, string distortionMode, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorFaceDistortionResult(false, "Not used."));

        public Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorOperationResult(false, "Not used."));

        public Task<EditorOperationResult> ProjectEdgesAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorOperationResult(false, "Not used."));
    }
}
