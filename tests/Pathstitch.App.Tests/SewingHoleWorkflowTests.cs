using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class SewingHoleWorkflowTests
{
    [Fact]
    public void Geometry_SymmetricPitchCentersTheRunAndAppliesMargin()
    {
        var line = Line("source", 0, 0, 10, 0);
        var document = Document(line);
        var parameters = new Editor2DSewingHoleParameters(Diameter: 1, Pitch: 4, Margin: 2, SymmetricDistribution: true);

        var preview = Editor2DSewingHoleGeometry.BuildPreview(document, [line.Id], parameters, "test");

        Assert.Equal(3, preview.Count);
        Assert.Equal([0, 5, 10], preview.Select(path => path.Center!.X).ToArray());
        Assert.All(preview, path => Assert.Equal(2, path.Center!.Y, 8));
        Assert.All(preview, path => Assert.Equal(0.5, path.Radius));
    }

    [Fact]
    public void Geometry_CornerModesAndAvoidanceChangePlacementDeterministically()
    {
        var square = new Editor2DPreviewPath(
            "square", "LWPOLYLINE",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)], IsClosed: true);
        var keepout = Line("keepout", 5, -5, 5, 15);
        var document = Document(square, keepout);
        var baseline = new Editor2DSewingHoleParameters(Pitch: 6, Margin: 1, CornerMode: Editor2DSewingCornerMode.Continuous);

        var continuous = Editor2DSewingHoleGeometry.BuildPreview(document, [square.Id], baseline, "continuous");
        var corners = Editor2DSewingHoleGeometry.BuildPreview(
            document, [square.Id], baseline with { CornerMode = Editor2DSewingCornerMode.IncludeCorners }, "corners");
        var avoided = Editor2DSewingHoleGeometry.BuildPreview(
            document, [square.Id], baseline with
            {
                AvoidanceEnabled = true,
                AvoidanceClearance = 2,
                AvoidPathIds = [keepout.Id],
            }, "avoid");

        Assert.True(corners.Count > continuous.Count);
        Assert.True(avoided.Count < continuous.Count);
        Assert.DoesNotContain(avoided, hole => Math.Abs(hole.Center!.X - 5) < 2.5);
    }

    [Fact]
    public void Workspace_PreviewsNonDestructivelyThenCommitsAndReEditsOperation()
    {
        var workspace = CreateWorkspace(Line("source", 0, 0, 12, 0));
        var original = workspace.Document;
        workspace.SewingHolePitch = 4;
        workspace.SewingHoleMargin = 2;

        Assert.True(workspace.RefreshSewingHolePreview());
        Assert.Equal(original, workspace.Document);
        Assert.True(workspace.HasSewingHolePreview);
        Assert.True(workspace.CommitSewingHolePreview());

        var operation = Assert.Single(workspace.SewingHoleOperations);
        Assert.Contains(workspace.Document.Paths, path => path.Id == "source");
        Assert.Equal(operation.GeneratedPathIds.Count, workspace.Document.Paths.Count(path => path.Id.StartsWith("sew-", StringComparison.Ordinal)));
        Assert.All(operation.GeneratedPathIds, id => Assert.Contains(id, workspace.ActiveLayer!.PathIds));

        var previousGeneratedIds = operation.GeneratedPathIds.ToArray();
        Assert.True(workspace.BeginEditSewingHoleOperation(operation.Id));
        workspace.SewingHolePitch = 3;
        Assert.True(workspace.CommitSewingHolePreview());
        var edited = Assert.Single(workspace.SewingHoleOperations);
        Assert.Equal(3, edited.Parameters.Pitch);
        Assert.DoesNotContain(workspace.Document.Paths, path => previousGeneratedIds.Contains(path.Id) && !edited.GeneratedPathIds.Contains(path.Id));
        Assert.Contains(workspace.Document.Paths, path => path.Id == "source");
    }

    [Fact]
    public void Workspace_SelectModeReentryIsVisibleAndReplacementIsOneUndoStep()
    {
        var workspace = CreateWorkspace(Line("source", 0, 0, 12, 0));
        Assert.True(workspace.IsSewingHoleInspectorVisible);
        Assert.True(workspace.RefreshSewingHolePreview());
        Assert.True(workspace.CommitSewingHolePreview());
        var originalOperation = Assert.Single(workspace.SewingHoleOperations);
        var originalDocument = workspace.Document;

        workspace.SetActiveTool(Editor2DTool.Select);

        Assert.False(workspace.IsSewingHoleToolActive);
        Assert.True(workspace.IsSewingHoleInspectorVisible);
        workspace.SelectedSewingHoleOperation = null;
        workspace.SelectedSewingHoleOperation = originalOperation;
        Assert.Equal(["source"], workspace.SelectedPathIds);
        Assert.True(workspace.HasSewingHolePreview);

        workspace.ClearHistory();
        workspace.SewingHolePitch = 3;
        Assert.True(workspace.CommitSewingHolePreview());

        var edited = Assert.Single(workspace.SewingHoleOperations);
        Assert.Equal(originalOperation.Id, edited.Id);
        Assert.Equal(3, edited.Parameters.Pitch);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.False(workspace.CanUndo);
        var restored = Assert.Single(workspace.SewingHoleOperations);
        Assert.Equal(originalOperation.Id, restored.Id);
        Assert.Equal(originalOperation.SourcePathIds, restored.SourcePathIds);
        Assert.Equal(originalOperation.GeneratedPathIds, restored.GeneratedPathIds);
        Assert.Equal(originalOperation.Parameters.Pitch, restored.Parameters.Pitch);
        Assert.Equal(originalDocument.Paths, workspace.Document.Paths);
    }

    [Fact]
    public void Workspace_BlankSelectHidesSewingInspectorUntilToolActivation()
    {
        var workspace = CreateWorkspace(Line("source", 0, 0, 12, 0));
        workspace.SetActiveTool(Editor2DTool.Select);

        Assert.Empty(workspace.SewingHoleOperations);
        Assert.False(workspace.IsSewingHoleInspectorVisible);

        workspace.SetActiveTool(Editor2DTool.AddSewingHoles);

        Assert.True(workspace.IsSewingHoleInspectorVisible);
    }

    [Fact]
    public void Workspace_ReEditsPatternSideAndSaddleSpacingWithoutDuplicatingOperation()
    {
        var workspace = CreateWorkspace(Line("source", 0, 0, 12, 0));
        Assert.True(workspace.RefreshSewingHolePreview());
        Assert.True(workspace.CommitSewingHolePreview());
        var original = Assert.Single(workspace.SewingHoleOperations);
        var originalGeneratedIds = original.GeneratedPathIds.ToArray();

        Assert.True(workspace.BeginEditSewingHoleOperation(original.Id));
        var singlePreviewCount = workspace.SewingHolePreviewCount;
        workspace.SewingPattern = Editor2DSewingPattern.Saddle;
        Assert.True(workspace.SewingHolePreviewCount > singlePreviewCount);
        workspace.SewingSide = Editor2DSewingSide.Right;
        workspace.SewingSaddleSpacing = 5.5;
        Assert.True(workspace.HasSewingHolePreview);
        Assert.True(workspace.CommitSewingHolePreview());

        var edited = Assert.Single(workspace.SewingHoleOperations);
        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(Editor2DSewingPattern.Saddle, edited.Parameters.Pattern);
        Assert.Equal(Editor2DSewingSide.Right, edited.Parameters.Side);
        Assert.Equal(5.5, edited.Parameters.SaddleSpacing);
        Assert.DoesNotContain(
            workspace.Document.Paths,
            path => originalGeneratedIds.Contains(path.Id) && !edited.GeneratedPathIds.Contains(path.Id));
    }

    [Fact]
    public void Workspace_NormalizesLegacyNegativeMarginsAndPreservesBothSide()
    {
        var source = Line("source", 0, 0, 12, 0);
        var operation = new Editor2DSewingHoleOperation(
            "operation",
            [source.Id],
            [],
            new Editor2DSewingHoleParameters(Margin: -3, Side: Editor2DSewingSide.Right));
        var workspace = new Editor2DWorkspaceViewModel();

        workspace.Apply(new Editor2DWorkspaceState(
            Document(source),
            SewingHoleParameters: new Editor2DSewingHoleParameters(Margin: -2, Side: Editor2DSewingSide.Left),
            SewingHoleOperations: [operation]));

        Assert.Equal(2, workspace.SewingHoleMargin);
        Assert.Equal(Editor2DSewingSide.Right, workspace.SewingSide);
        var normalizedOperation = Assert.Single(workspace.SewingHoleOperations);
        Assert.Equal(3, normalizedOperation.Parameters.Margin);
        Assert.Equal(Editor2DSewingSide.Left, normalizedOperation.Parameters.Side);

        workspace.Apply(workspace.State with
        {
            SewingHoleParameters = new Editor2DSewingHoleParameters(Margin: -4, Side: Editor2DSewingSide.Both),
        });
        Assert.Equal(4, workspace.SewingHoleMargin);
        Assert.Equal(Editor2DSewingSide.Both, workspace.SewingSide);
    }

    [Fact]
    public void Workspace_SelectionTransitionsPreserveAdaptiveSideMeaning()
    {
        var line = Line("line", 0, 0, 12, 0);
        var circle = new Editor2DPreviewPath(
            "circle", "CIRCLE", [], true, Center: new Editor2DPoint(0, 0), Radius: 10);
        var workspace = CreateWorkspace(line, circle);

        workspace.SewingSide = Editor2DSewingSide.Left;
        Assert.Equal("Left", workspace.SewingSideSelection);
        Assert.False(workspace.SewingUsesRadialSideVocabulary);
        workspace.SetSelection([circle.Id]);
        Assert.True(workspace.SewingUsesRadialSideVocabulary);
        Assert.Equal(Editor2DSewingSide.Right, workspace.SewingSide);
        Assert.Equal("Outer", workspace.SewingSideSelection);

        workspace.SewingSideSelection = "Inner";
        workspace.SetSelection([line.Id]);
        Assert.Equal(Editor2DSewingSide.Right, workspace.SewingSide);
        Assert.Equal("Right", workspace.SewingSideSelection);

        workspace.SewingSide = Editor2DSewingSide.Both;
        workspace.SetSelection([circle.Id]);
        Assert.Equal(Editor2DSewingSide.Both, workspace.SewingSide);
        Assert.Equal("Both", workspace.SewingSideSelection);
    }

    [Fact]
    public async Task ProjectPersistence_RoundTripsEditableSewingParametersAndOperationLinks()
    {
        var workspace = CreateWorkspace(
            Line("source", 0, 0, 12, 0),
            Line("keepout", 100, 100, 110, 100));
        workspace.SetSelection(["keepout"]);
        workspace.UseSelectionAsSewingAvoidancePaths();
        workspace.SetSelection(["source"]);
        workspace.SewingHoleDiameter = 1.25;
        workspace.SewingHolePitch = 3.5;
        workspace.SewingHoleMargin = 2;
        workspace.SewingPattern = Editor2DSewingPattern.Saddle;
        workspace.SewingSide = Editor2DSewingSide.Right;
        workspace.SewingSaddleSpacing = 4.5;
        workspace.SewingCornerMode = Editor2DSewingCornerMode.AvoidCorners;
        workspace.SewingCornerClearance = 1.5;
        workspace.SewingSymmetricDistribution = false;
        Assert.True(workspace.RefreshSewingHolePreview());
        Assert.True(workspace.CommitSewingHolePreview());
        var projectPath = Path.Combine(Path.GetTempPath(), $"sewing-{Guid.NewGuid():N}.stch");

        try
        {
            var service = new Project3DStateService();
            await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: workspace.State));
            var restoredProject = await service.LoadAsync(projectPath);
            var restored = new Editor2DWorkspaceViewModel();
            restored.Apply(restoredProject.TwoDWorkspaceState!, recordHistory: false);

            Assert.Equal(Editor2DSewingPattern.Saddle, restored.SewingPattern);
            Assert.Equal(Editor2DSewingSide.Right, restored.SewingSide);
            Assert.Equal(4.5, restored.SewingSaddleSpacing);
            var operation = Assert.Single(restored.SewingHoleOperations);
            Assert.Equal(1.25, operation.Parameters.Diameter);
            Assert.Equal(3.5, operation.Parameters.Pitch);
            Assert.Equal(2, operation.Parameters.Margin);
            Assert.Equal(Editor2DSewingPattern.Saddle, operation.Parameters.Pattern);
            Assert.Equal(Editor2DSewingSide.Right, operation.Parameters.Side);
            Assert.Equal(4.5, operation.Parameters.SaddleSpacing);
            Assert.Equal(Editor2DSewingCornerMode.AvoidCorners, operation.Parameters.CornerMode);
            Assert.Equal(1.5, operation.Parameters.CornerClearance);
            Assert.False(operation.Parameters.SymmetricDistribution);
            Assert.True(operation.Parameters.AvoidanceEnabled);
            Assert.Equal(["keepout"], operation.Parameters.AvoidPathIds);
            Assert.True(restored.BeginEditSewingHoleOperation(operation.Id));
            Assert.True(restored.HasSewingHolePreview);
            Assert.Equal(Editor2DSewingPattern.Saddle, restored.SewingPattern);
            Assert.Equal(Editor2DSewingSide.Right, restored.SewingSide);
            Assert.Equal(4.5, restored.SewingSaddleSpacing);
        }
        finally
        {
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }

    [Fact]
    public void Inspector_ExposesPreviewCommitAndAllRequiredMatureParameters()
    {
        var inspector = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DInspector.axaml");
        var viewport = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DView.axaml");

        Assert.Contains("SewingHolePitch", inspector, StringComparison.Ordinal);
        Assert.Contains("SewingHoleMargin", inspector, StringComparison.Ordinal);
        Assert.Contains("SewingCornerMode", inspector, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SewingPatterns}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SewingPattern, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SewingSideOptionItems}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SewingSideSelection, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsSaddleSewingPattern}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding SewingSaddleSpacing, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.sewing.pattern", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.sewing.side", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.sewing.saddle-spacing", inspector, StringComparison.Ordinal);
        Assert.Contains("SewingAvoidanceEnabled", inspector, StringComparison.Ordinal);
        Assert.Contains("SewingSymmetricDistribution", inspector, StringComparison.Ordinal);
        Assert.Contains("OnPreviewSewingHolesClicked", inspector, StringComparison.Ordinal);
        Assert.Contains("OnCommitSewingHolesClicked", inspector, StringComparison.Ordinal);
        Assert.Contains("SelectedSewingHoleOperation", inspector, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsSewingHoleInspectorVisible}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.sewing.inspector", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.sewing.operation", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.sewing.preview", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.sewing.commit", inspector, StringComparison.Ordinal);
        Assert.Contains("PreviewPaths=\"{Binding TwoDWorkspace.SewingHolePreviewPaths}\"", viewport, StringComparison.Ordinal);
    }

    private static Editor2DWorkspaceViewModel CreateWorkspace(params Editor2DPreviewPath[] paths)
    {
        var source = paths[0];
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(new Editor2DWorkspaceState(
            Document(paths),
            ActiveTool: Editor2DTool.AddSewingHoles,
            SelectedPathIds: [source.Id],
            Layers: [new Editor2DLayer("layer", "Pattern", paths.Select(path => path.Id).ToArray(), Order: 0)],
            ActiveLayerId: "layer"));
        return workspace;
    }

    private static Editor2DPreviewPath Line(string id, double x1, double y1, double x2, double y2)
        => new(id, "LINE", [new(x1, y1), new(x2, y2)], IsClosed: false);

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
        => new(paths, new Editor2DBounds(-10, -10, 20, 20), new Dictionary<string, int>(), []);

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(pathParts));
    }
}
