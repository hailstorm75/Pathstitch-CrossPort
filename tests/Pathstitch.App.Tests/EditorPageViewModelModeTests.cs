using System.Text.Json;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pathstitch.App.Tests;

public sealed class EditorPageViewModelModeTests
{
    public static IEnumerable<object[]> CatalogShortcutDescriptors()
        => EditorToolCatalog.All
            .Where(descriptor => descriptor.ShortcutText is not null)
            .Select(descriptor => new object[] { descriptor });

    [Fact]
    public void EditorMode_HasStableValuesIncludingReservedBatchMode()
    {
        Assert.Equal(0, (int)EditorMode.TwoD);
        Assert.Equal(1, (int)EditorMode.ThreeD);
        Assert.Equal(2, (int)EditorMode.Batch);
        Assert.Equal([EditorMode.TwoD, EditorMode.ThreeD, EditorMode.Batch], Enum.GetValues<EditorMode>());
    }

    [Fact]
    public void PatternGuide_IsClearedWhenPatternSessionChanges()
    {
        var viewModel = CreateViewModel();
        viewModel.TwoDPatternGuidePathId = "stale-guide";

        viewModel.TwoDActiveTool = Editor2DTool.Patterning;

        Assert.Null(viewModel.TwoDPatternGuidePathId);
    }

    [Fact]
    public void CornerToolLifecycle_SeedsConfirmsOnLeaveAndCancelsToExactGeometry()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath(
            "square", "LWPOLYLINE", [new(0, 0), new(20, 0), new(20, 20), new(0, 20)], true);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDWorkspace.ClearHistory();

        viewModel.TwoDActiveTool = Editor2DTool.Fillet;

        Assert.Equal(4, viewModel.TwoDCornerParameters.Count);
        Assert.NotNull(viewModel.TwoDSelectedCornerParameterId);
        viewModel.TwoDActiveTool = Editor2DTool.Pan;
        Assert.True(viewModel.TwoDWorkspace.CanUndo);
        Assert.Equal(Editor2DTool.Pan, viewModel.TwoDActiveTool);

        viewModel.TwoDWorkspace.ClearHistory();
        var committed = viewModel.TwoDDocument;
        viewModel.TwoDActiveTool = Editor2DTool.Chamfer;
        Assert.True(viewModel.CancelTwoDCornerToolSession(exitTool: true));
        Assert.Equal(committed, viewModel.TwoDDocument);
        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    [Fact]
    public async Task ThreeDBodyMove_CanUndoAndRedoOffsetChanges()
    {
        var viewModel = CreateViewModelForTests();
        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        viewModel.ThreeDWorkspace.ReplaceBodies([new Body3D(0, "body", [])], "{}", "body.obj");
        viewModel.ActivateMoveTool();
        viewModel.SelectBodyFromPanel(0);

        viewModel.NudgeSelectedBody(axis: 0, direction: 1);

        Assert.Equal(1.0, Assert.Single(viewModel.BodyOffsets).X);
        Assert.True(viewModel.CanUndoThreeDBodyMove);
        Assert.True(viewModel.UndoThreeDBodyMove());
        Assert.Empty(viewModel.BodyOffsets);
        Assert.True(viewModel.CanRedoThreeDBodyMove);
        Assert.True(viewModel.RedoThreeDBodyMove());
        Assert.Equal(1.0, Assert.Single(viewModel.BodyOffsets).X);
    }

    [Fact]
    public void PatternPreview_IsComputedWithoutMutatingDocument()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(1, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Patterning;
        viewModel.TwoDPatternMode = "Circular";
        viewModel.TwoDPatternCircularCountText = "3";
        viewModel.TwoDPatternCircularAngleText = "180";
        viewModel.TwoDPatternPivot = new Editor2DPoint(0, 0);

        var preview = viewModel.TwoDPatternPreviewPaths;

        Assert.Equal(2, preview.Count);
        Assert.Single(viewModel.TwoDDocument.Paths);
        Assert.Equal(new Editor2DPoint(0, 0), preview[0].Points[0]);
        Assert.Equal(-1, preview[1].Points[1].X, 6);
        Assert.Equal(0, preview[1].Points[1].Y, 6);
    }

    [Fact]
    public void DimensionParameters_RefreshForDependenciesDrivenStateAndHistory()
    {
        var viewModel = CreateViewModel();
        viewModel.TwoDActiveTool = Editor2DTool.Dimension;
        viewModel.TwoDMeasurements =
        [
            new("first", new(0, 0), new(10, 0), VarName: "d1", Expression: "10", IsParametric: true),
            new("second", new(0, 10), new(20, 10), VarName: "d2", Expression: "d1 * 2", IsParametric: true),
            new("driven", new(0, 15), new(20, 15), VarName: "d4", Expression: "d1 * 3", Driven: true, IsParametric: true),
            new("manual", new(0, 20), new(3, 20)),
            new("auto", new(0, 30), new(4, 30), IsAutoDimension: true, VarName: "d3", Expression: "4", IsParametric: true),
        ];
        viewModel.TwoDSelectedMeasurementId = "first";
        viewModel.TwoDWorkspace.ClearHistory();
        var notifications = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.TwoDDimensionParameters))
                notifications++;
        };

        var initial = viewModel.TwoDDimensionParameters;

        Assert.Equal(["d1", "d2", "d4"], initial.Select(item => item.Name).ToArray());
        Assert.Equal(string.Empty, initial[0].ExpressionDisplay);
        Assert.Equal("10.00", initial[0].ValueDisplay);
        Assert.Equal("= d1 * 2", initial[1].ExpressionDisplay);
        Assert.Equal("20.00", initial[1].ValueDisplay);
        Assert.False(initial[1].IsDriven);
        Assert.Equal("(20.00)", initial[2].ValueDisplay);
        Assert.True(initial[2].IsDriven);

        viewModel.TwoDSelectedMeasurementExpressionText = "5";

        Assert.Equal("5.00", viewModel.TwoDDimensionParameters[0].ValueDisplay);
        Assert.Equal("10.00", viewModel.TwoDDimensionParameters[1].ValueDisplay);
        Assert.Equal("(20.00)", viewModel.TwoDDimensionParameters[2].ValueDisplay);
        Assert.True(notifications > 0);
        Assert.True(viewModel.UndoTwoDWorkspace());
        Assert.Equal("10.00", viewModel.TwoDDimensionParameters[0].ValueDisplay);
        Assert.True(viewModel.RedoTwoDWorkspace());
        Assert.Equal("5.00", viewModel.TwoDDimensionParameters[0].ValueDisplay);

        viewModel.TwoDWorkspace.ClearHistory();
        viewModel.TwoDSelectedMeasurementId = "first";
        var beforeInvalid = viewModel.TwoDMeasurements.ToArray();
        viewModel.TwoDSelectedMeasurementExpressionText = "d2";
        Assert.Equal(beforeInvalid, viewModel.TwoDMeasurements);
        Assert.False(viewModel.CanUndoTwoDWorkspace);
        Assert.Contains("positive number or arithmetic expression", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DimensionParameters_RebuildFromPersistedMeasurementState()
    {
        var measurement = new Editor2DMeasurement(
            "dimension", new(0, 0), new(12, 0), VarName: "d1", Expression: "6 * 2",
            Driven: true, IsParametric: true);
        var projectPath = Path.Combine(Path.GetTempPath(), $"dimension-table-{Guid.NewGuid():N}.stch");
        try
        {
            var state = Editor2DWorkspaceState.Empty with { Measurements = [measurement] };
            var service = new Project3DStateService();
            await service.SaveAsync(projectPath, Project3DState.Empty with { TwoDWorkspaceState = state });

            var restored = await service.LoadAsync(projectPath);
            var restoredMeasurement = Assert.Single(restored.TwoDWorkspaceState!.Measurements!);
            Assert.Equal(measurement.VarName, restoredMeasurement.VarName);
            Assert.Equal(measurement.Expression, restoredMeasurement.Expression);
            Assert.Equal(measurement.Driven, restoredMeasurement.Driven);
            Assert.Equal(measurement.IsParametric, restoredMeasurement.IsParametric);

            var viewModel = CreateViewModel();
            viewModel.TwoDMeasurements = [restoredMeasurement];
            var row = Assert.Single(viewModel.TwoDDimensionParameters);
            Assert.Equal("d1", row.Name);
            Assert.Equal("= 6 * 2", row.ExpressionDisplay);
            Assert.Equal("(12.00)", row.ValueDisplay);
        }
        finally
        {
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }

    [Fact]
    public void ConvertLines_ReentryLoadsGroupAndSupportsLiveStyleWithStagedParameters()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("convert-source", "LINE", [new(0, 0), new(30, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.ConvertLines;
        viewModel.TwoDConvertLineStyle = "dotted";
        viewModel.TwoDConvertLineFirstParameterText = "7";
        viewModel.TwoDConvertLineSecondParameterText = "0.75";

        Assert.True(viewModel.ApplyTwoDConvertLines());
        var convertedGroup = Assert.Single(viewModel.TwoDWorkspace.ConvertLineGroups);
        var generatedId = Assert.Single(convertedGroup.Sources).GeneratedPathIds[0];

        viewModel.TwoDSelectedPathIds = [];
        viewModel.TwoDActiveTool = Editor2DTool.Select;
        viewModel.TwoDConvertLineStyle = "wave";
        Assert.False(viewModel.IsTwoDConvertLinesInspectorVisible);

        viewModel.TwoDSelectedPathIds = [generatedId];

        Assert.True(viewModel.HasSingleTwoDConvertedLineGroupSelection);
        Assert.True(viewModel.IsTwoDConvertLinesInspectorVisible);
        Assert.Equal("Update Lines", viewModel.TwoDConvertLineActionLabel);
        Assert.Equal("dotted", viewModel.TwoDConvertLineStyle);
        Assert.Equal("7", viewModel.TwoDConvertLineFirstParameterText);
        Assert.Equal("0.75", viewModel.TwoDConvertLineSecondParameterText);
        Assert.NotEmpty(viewModel.TwoDConvertLinePreviewPaths);

        viewModel.TwoDConvertLineStyle = "zigzag";

        var liveRestyledGroup = Assert.Single(viewModel.TwoDWorkspace.ConvertLineGroups);
        Assert.Equal(convertedGroup.Id, liveRestyledGroup.Id);
        Assert.Equal("zigzag", liveRestyledGroup.Style);
        Assert.Equal(6, liveRestyledGroup.Settings["wavelength"]);
        var liveRestyledDocument = viewModel.TwoDDocument;

        viewModel.TwoDConvertLineFirstParameterText = "9";

        Assert.Equal(6, Assert.Single(viewModel.TwoDWorkspace.ConvertLineGroups).Settings["wavelength"]);
        Assert.Same(liveRestyledDocument, viewModel.TwoDDocument);

        Assert.True(viewModel.ApplyTwoDConvertLines());
        Assert.Equal(9, Assert.Single(viewModel.TwoDWorkspace.ConvertLineGroups).Settings["wavelength"]);
    }

    [Fact]
    public void ConvertLines_ToolChangeNotifiesInspectorVisibility()
    {
        var viewModel = CreateViewModel();
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        viewModel.TwoDActiveTool = Editor2DTool.ConvertLines;

        Assert.True(viewModel.IsTwoDConvertLinesInspectorVisible);
        Assert.Contains(nameof(EditorPageViewModel.IsTwoDConvertLinesInspectorVisible), changes);
    }

    [Fact]
    public async Task OffsetPreview_RecomputesWithoutMutationAndCommitUsesExactGhost()
    {
        var kernel = new RecordingOffsetGeometryKernelService();
        var viewModel = CreateViewModel(geometryKernelService: kernel);
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(10, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Offset;
        Assert.Equal("12", viewModel.TwoDOffsetDistanceText);
        Assert.Equal("Outward", viewModel.TwoDOffsetSide);
        viewModel.TwoDOffsetDistanceText = "5";

        await viewModel.TwoDOffsetPreviewUpdateTask;

        var firstPreview = Assert.Single(viewModel.TwoDOffsetPreviewPaths);
        Assert.Single(viewModel.TwoDDocument.Paths);
        Assert.All(firstPreview.Points, point => Assert.Equal(5, point.Y, 6));

        viewModel.TwoDOffsetDistanceText = "7";
        await viewModel.TwoDOffsetPreviewUpdateTask;
        Assert.All(Assert.Single(viewModel.TwoDOffsetPreviewPaths).Points, point => Assert.Equal(7, point.Y, 6));

        viewModel.TwoDOffsetConstruction = true;
        await viewModel.TwoDOffsetPreviewUpdateTask;
        Assert.Equal("Construction", viewModel.TwoDOffsetGeometryTypeLabel);
        Assert.True(Assert.Single(viewModel.TwoDOffsetPreviewPaths).IsConstruction);

        viewModel.FlipTwoDOffsetDirection();
        await viewModel.TwoDOffsetPreviewUpdateTask;
        var committedGhost = Assert.Single(viewModel.TwoDOffsetPreviewPaths);
        Assert.Equal("Inward", viewModel.TwoDOffsetSide);
        Assert.All(committedGhost.Points, point => Assert.Equal(-7, point.Y, 6));
        var callsBeforeCommit = kernel.CallCount;

        Assert.True(await viewModel.ConfirmTwoDOffsetAsync());

        Assert.Equal(callsBeforeCommit, kernel.CallCount);
        Assert.Contains(committedGhost, viewModel.TwoDDocument.Paths);
        Assert.True(committedGhost.IsConstruction);
        var constructionLayer = Assert.Single(viewModel.TwoDLayers, layer => layer.Name == "CONSTRUCTION");
        Assert.Equal("#808080", constructionLayer.ColorHex);
        Assert.Contains(committedGhost.Id, constructionLayer.PathIds);
        Assert.Empty(viewModel.TwoDOffsetPreviewPaths);
        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
    }

    [Fact]
    public async Task OffsetPreview_CancelAndToolExitClearGhostWithoutEditingDocument()
    {
        var kernel = new RecordingOffsetGeometryKernelService();
        var viewModel = CreateViewModel(geometryKernelService: kernel);
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(10, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Offset;
        await viewModel.TwoDOffsetPreviewUpdateTask;
        Assert.NotEmpty(viewModel.TwoDOffsetPreviewPaths);

        viewModel.CancelTwoDOffset();

        Assert.Empty(viewModel.TwoDOffsetPreviewPaths);
        Assert.Equal([source], viewModel.TwoDDocument.Paths);

        await viewModel.RefreshTwoDOffsetPreviewAsync();
        Assert.NotEmpty(viewModel.TwoDOffsetPreviewPaths);

        viewModel.TwoDActiveTool = Editor2DTool.Select;

        Assert.Empty(viewModel.TwoDOffsetPreviewPaths);
        Assert.Equal([source], viewModel.TwoDDocument.Paths);
    }

    [Fact]
    public async Task OffsetPreview_LatestRequestWinsWhenKernelCompletesOutOfOrder()
    {
        var kernel = new DelayedOffsetGeometryKernelService();
        var viewModel = CreateViewModel(geometryKernelService: kernel);
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(10, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Offset;
        viewModel.TwoDOffsetDistanceText = "5";
        viewModel.TwoDOffsetDistanceText = "7";

        Assert.Equal(3, kernel.CallCount);
        kernel.Complete(2);
        await viewModel.TwoDOffsetPreviewUpdateTask;
        kernel.Complete(1);
        kernel.Complete(0);
        await Task.Yield();

        Assert.All(Assert.Single(viewModel.TwoDOffsetPreviewPaths).Points, point => Assert.Equal(7, point.Y, 6));
        Assert.Single(viewModel.TwoDDocument.Paths);
    }

    [Fact]
    public void RectangularPattern_ExtentPreviewMatchesCommittedSpacing()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(1, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Patterning;
        viewModel.TwoDPatternMode = "Rectangular";
        viewModel.TwoDPatternDistanceMode = "Extent";
        viewModel.TwoDPatternCopiesXText = "3";
        viewModel.TwoDPatternCopiesYText = "1";
        viewModel.TwoDPatternExtentXText = "40";
        viewModel.TwoDPatternExtentYText = "40";

        var previewX = viewModel.TwoDPatternPreviewPaths
            .Select(path => path.Points[0].X)
            .Order()
            .ToArray();

        Assert.Equal([20, 40], previewX);
        Assert.Equal("Effective spacing: 20 / 0 mm", viewModel.TwoDPatternEffectiveSpacingSummary);
        Assert.True(viewModel.ApplyTwoDPattern());
        Assert.Equal(
            [0, 20, 40],
            viewModel.TwoDDocument!.Paths.Select(path => path.Points[0].X).Order().ToArray());
        Assert.Equal(
            previewX,
            viewModel.TwoDDocument.Paths
                .Where(path => path.Id.Contains(":pattern:", StringComparison.Ordinal))
                .Select(path => path.Points[0].X)
                .Order()
                .ToArray());
    }

    [Fact]
    public void RectangularPattern_DistanceContractUsesOwnedDefaultsAndZeroSpacingForSingleCounts()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(["Spacing", "Extent"], viewModel.TwoDPatternDistanceModeOptionItems);
        Assert.Equal("Spacing", viewModel.TwoDPatternDistanceMode);
        Assert.Equal("40", viewModel.TwoDPatternExtentXText);
        Assert.Equal("40", viewModel.TwoDPatternExtentYText);
        Assert.True(viewModel.IsTwoDPatternSpacingMode);
        Assert.False(viewModel.IsTwoDPatternExtentMode);

        viewModel.TwoDPatternCopiesXText = "1";
        viewModel.TwoDPatternCopiesYText = "1";

        Assert.Equal("Effective spacing: 0 / 0 mm", viewModel.TwoDPatternEffectiveSpacingSummary);
    }

    [Fact]
    public void RectangularPattern_ExtentAllowsDirectionButRejectsNonFiniteValues()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(1, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Patterning;
        viewModel.TwoDPatternDistanceMode = "Extent";
        viewModel.TwoDPatternCopiesXText = "3";
        viewModel.TwoDPatternCopiesYText = "1";
        viewModel.TwoDPatternExtentXText = "-40";

        Assert.Equal([-40, -20], viewModel.TwoDPatternPreviewPaths.Select(path => path.Points[0].X).Order().ToArray());

        viewModel.TwoDPatternExtentXText = "NaN";

        Assert.Empty(viewModel.TwoDPatternPreviewPaths);
        Assert.False(viewModel.ApplyTwoDPattern());
        Assert.Equal("Enter a valid pattern X extent", viewModel.StatusText);
    }

    [Fact]
    public async Task StrokeAndFillConversionAvailability_FollowsTheCurrentSelection()
    {
        var viewModel = CreateViewModel();

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with
        {
            Paths =
            [
                new Editor2DPreviewPath("stroke", "LWPOLYLINE", [new(0, 0), new(5, 0), new(5, 5), new(0, 5)], true),
                new Editor2DPreviewPath("fill", "LWPOLYLINE", [new(10, 0), new(15, 0), new(15, 5), new(10, 5)], true, IsFilled: true),
            ],
        };

        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        viewModel.TwoDSelectedPathIds = ["stroke"];

        Assert.True(viewModel.CanApplyTwoDStrokeToFill);
        Assert.False(viewModel.CanApplyTwoDFillToStroke);
        Assert.Contains(nameof(EditorPageViewModel.CanApplyTwoDStrokeToFill), changes);
        Assert.Contains(nameof(EditorPageViewModel.CanApplyTwoDFillToStroke), changes);

        changes.Clear();
        viewModel.TwoDSelectedPathIds = ["fill"];

        Assert.False(viewModel.CanApplyTwoDStrokeToFill);
        Assert.True(viewModel.CanApplyTwoDFillToStroke);
        Assert.Contains(nameof(EditorPageViewModel.CanApplyTwoDStrokeToFill), changes);
        Assert.Contains(nameof(EditorPageViewModel.CanApplyTwoDFillToStroke), changes);
    }

    [Fact]
    public async Task StrokeAndFillConversions_UseTheWorkspaceOperationAndUpdateDocumentState()
    {
        var viewModel = CreateViewModel();

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with
        {
            Paths =
            [
                new Editor2DPreviewPath("stroke", "LWPOLYLINE", [new(0, 0), new(5, 0), new(5, 5), new(0, 5)], true),
                new Editor2DPreviewPath("fill", "LWPOLYLINE", [new(10, 0), new(15, 0), new(15, 5), new(10, 5)], true, IsFilled: true),
            ],
        };

        viewModel.TwoDSelectedPathIds = ["stroke"];

        Assert.True(viewModel.ApplyTwoDStrokeToFill());
        Assert.True(viewModel.TwoDDocument!.Paths.Single(path => path.Id == "stroke").IsFilled);

        viewModel.TwoDSelectedPathIds = ["fill"];

        Assert.True(viewModel.ApplyTwoDFillToStroke());
        Assert.False(viewModel.TwoDDocument!.Paths.Single(path => path.Id == "fill").IsFilled);
    }

    [Fact]
    public void ScalePivot_IsTransientAcrossToolSessions()
    {
        var viewModel = CreateViewModel();
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScalePivot = new Editor2DPoint(12, 7);

        viewModel.TwoDActiveTool = Editor2DTool.Select;

        Assert.Null(viewModel.TwoDScalePivot);
        Assert.False(viewModel.TwoDScalePivotPicking);
        Assert.Equal("1", viewModel.TwoDScaleFactorText);
    }

    [Fact]
    public void ScaleEntry_ResetsFactorPivotAndCenterMode()
    {
        var viewModel = CreateViewModel();
        viewModel.TwoDScaleFactorText = "4";
        viewModel.TwoDScaleFromCenter = false;
        viewModel.TwoDScalePivot = new Editor2DPoint(3, 2);
        viewModel.TwoDScalePivotPicking = true;

        viewModel.TwoDActiveTool = Editor2DTool.Scale;

        Assert.Equal("1", viewModel.TwoDScaleFactorText);
        Assert.True(viewModel.TwoDScaleFromCenter);
        Assert.Null(viewModel.TwoDScalePivot);
        Assert.False(viewModel.TwoDScalePivotPicking);
    }

    [Fact]
    public void ExactScaleFactor_ValidatesAndAppliesThroughWorkspace()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(2, 4), new(6, 8)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScaleFromCenter = false;
        viewModel.TwoDScaleFactorText = "2";

        Assert.True(viewModel.CanApplyTwoDScale);
        Assert.True(viewModel.ApplyTwoDScale());
        Assert.Equal(new Editor2DPoint(2, 4), viewModel.TwoDDocument!.Paths[0].Points[0]);
        Assert.Equal(new Editor2DPoint(10, 12), viewModel.TwoDDocument.Paths[0].Points[1]);

        viewModel.TwoDScaleFactorText = "0";
        Assert.False(viewModel.CanApplyTwoDScale);
        Assert.False(viewModel.ApplyTwoDScale());
        Assert.Equal("Enter a positive finite scale factor", viewModel.StatusText);
    }

    [Fact]
    public void ConfirmScale_AppliesOnceExitsToolAndClearsTransientPivot()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(2, 4), new(6, 8)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScaleFromCenter = false;
        viewModel.TwoDScalePivot = new Editor2DPoint(2, 4);
        viewModel.TwoDScalePivotPicking = true;
        viewModel.TwoDScaleFactorText = "2";
        viewModel.TwoDWorkspace.ClearHistory();

        Assert.True(viewModel.ConfirmTwoDScaleAndExit());

        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
        Assert.Equal([source.Id], viewModel.TwoDSelectedPathIds);
        Assert.Equal(new Editor2DPoint(2, 4), viewModel.TwoDDocument!.Paths[0].Points[0]);
        Assert.Equal(new Editor2DPoint(10, 12), viewModel.TwoDDocument.Paths[0].Points[1]);
        Assert.Null(viewModel.TwoDScalePivot);
        Assert.False(viewModel.TwoDScalePivotPicking);
        Assert.True(viewModel.TwoDWorkspace.CanUndo);
        Assert.True(viewModel.TwoDWorkspace.Undo());
        Assert.Equal(source.Points, viewModel.TwoDDocument.Paths[0].Points);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    [Fact]
    public void LeavingScale_CommitsStagedFactorOnceAndResetsNextSession()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(0, 0), new(4, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScaleFactorText = "2";
        viewModel.TwoDWorkspace.ClearHistory();

        viewModel.TwoDActiveTool = Editor2DTool.Pan;

        Assert.Equal(Editor2DTool.Pan, viewModel.TwoDActiveTool);
        Assert.Equal(new Editor2DPoint(-2, 0), viewModel.TwoDDocument!.Paths[0].Points[0]);
        Assert.Equal(new Editor2DPoint(6, 0), viewModel.TwoDDocument.Paths[0].Points[1]);
        Assert.Equal("1", viewModel.TwoDScaleFactorText);
        Assert.True(viewModel.TwoDWorkspace.Undo());
        Assert.Equal(source.Points, viewModel.TwoDDocument.Paths[0].Points);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);

        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        Assert.Equal("1", viewModel.TwoDScaleFactorText);
    }

    [Fact]
    public void ConfirmScale_IdentityFactorExitsWithoutGeometryHistory()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(0, 0), new(4, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScaleFactorText = "1.0005";
        viewModel.TwoDWorkspace.ClearHistory();

        Assert.True(viewModel.ConfirmTwoDScaleAndExit());

        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
        Assert.Equal(source.Points, viewModel.TwoDDocument!.Paths[0].Points);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    [Fact]
    public void ConfirmScale_WithoutSelectionExitsAndResetsFactor()
    {
        var viewModel = CreateViewModel();
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScaleFactorText = "2";
        viewModel.TwoDWorkspace.ClearHistory();

        Assert.True(viewModel.ConfirmTwoDScaleAndExit());

        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
        Assert.Equal("1", viewModel.TwoDScaleFactorText);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    [Fact]
    public void CancelScale_DropsStagedFactorAndPivotWithoutGeometryHistory()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(0, 0), new(4, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScaleFactorText = "3";
        viewModel.TwoDScalePivot = new Editor2DPoint(1, 0);
        viewModel.TwoDWorkspace.ClearHistory();

        viewModel.CancelTwoDScaleAndExit();

        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
        Assert.Equal("1", viewModel.TwoDScaleFactorText);
        Assert.Null(viewModel.TwoDScalePivot);
        Assert.Equal(source.Points, viewModel.TwoDDocument!.Paths[0].Points);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("invalid")]
    public void ConfirmScale_InvalidFactorResetsAndExitsWithoutGeometryChange(string factor)
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(2, 4), new(6, 8)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScaleFromCenter = false;
        viewModel.TwoDScalePivot = new Editor2DPoint(2, 4);
        viewModel.TwoDScalePivotPicking = true;
        viewModel.TwoDScaleFactorText = factor;
        viewModel.TwoDWorkspace.ClearHistory();

        Assert.True(viewModel.ConfirmTwoDScaleAndExit());

        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
        Assert.Equal(source.Points, viewModel.TwoDDocument!.Paths[0].Points);
        Assert.Null(viewModel.TwoDScalePivot);
        Assert.False(viewModel.TwoDScalePivotPicking);
        Assert.Equal("1", viewModel.TwoDScaleFactorText);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    [Fact]
    public void MirrorStaging_TracksModeAxisHintsAndToolSessionReset()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(2, 0), new(4, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Mirror;

        Assert.Equal("Switch to Mirror Line, then pick the axis.", viewModel.TwoDMirrorStageHint);
        viewModel.TwoDMirrorLineMode = true;
        Assert.Equal("Click a line (or two points) for the mirror axis.", viewModel.TwoDMirrorStageHint);
        viewModel.TwoDMirrorAxisStart = new Editor2DPoint(0, 0);
        Assert.Equal("Click the second axis point.", viewModel.TwoDMirrorStageHint);
        viewModel.TwoDMirrorAxisEnd = new Editor2DPoint(0, 10);
        Assert.True(viewModel.CanConfirmTwoDMirror);

        viewModel.TwoDActiveTool = Editor2DTool.Select;

        Assert.False(viewModel.TwoDMirrorLineMode);
        Assert.Null(viewModel.TwoDMirrorAxisStart);
        Assert.Null(viewModel.TwoDMirrorAxisEnd);
        Assert.Equal([source.Id], viewModel.TwoDSelectedPathIds);
    }

    [Fact]
    public void ConfirmAndCancelMirror_ApplyCopyAndResetOnlyTransientState()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(2, 0), new(4, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Mirror;
        viewModel.TwoDMirrorLineMode = true;
        viewModel.TwoDMirrorAxisStart = new Editor2DPoint(0, 0);
        viewModel.TwoDMirrorAxisEnd = new Editor2DPoint(0, 10);

        Assert.True(viewModel.TwoDMirrorKeepLink);

        Assert.True(viewModel.ConfirmTwoDMirror());
        Assert.Equal(2, viewModel.TwoDDocument!.Paths.Count);
        var copyId = Assert.Single(viewModel.TwoDSelectedPathIds);
        Assert.StartsWith("shape:mirror:", copyId, StringComparison.Ordinal);
        Assert.Equal(new Editor2DPoint(-2, 0), viewModel.TwoDDocument.Paths.Single(path => path.Id == copyId).Points[0]);
        Assert.Null(viewModel.TwoDMirrorAxisStart);
        Assert.Null(viewModel.TwoDMirrorAxisEnd);
        Assert.False(viewModel.TwoDMirrorLineMode);
        Assert.Equal(Editor2DTool.Mirror, viewModel.TwoDActiveTool);
        Assert.True(viewModel.TwoDMirrorKeepLink);
        Assert.True(viewModel.HasTwoDMirrorLinkSelection);
        Assert.Equal(2, viewModel.MirrorLinks.Count);

        var pathsBeforeBreak = viewModel.TwoDDocument.Paths.ToArray();
        Assert.True(viewModel.BreakTwoDMirrorLinks());
        Assert.Empty(viewModel.MirrorLinks);
        Assert.False(viewModel.HasTwoDMirrorLinkSelection);
        Assert.Equal(pathsBeforeBreak, viewModel.TwoDDocument.Paths);

        viewModel.TwoDMirrorLineMode = true;
        viewModel.TwoDMirrorAxisStart = new Editor2DPoint(1, 1);
        viewModel.TwoDMirrorAxisEnd = new Editor2DPoint(2, 2);
        viewModel.CancelTwoDMirror(exitTool: false);
        Assert.Equal([copyId], viewModel.TwoDSelectedPathIds);
        Assert.Equal(Editor2DTool.Mirror, viewModel.TwoDActiveTool);

        viewModel.CancelTwoDMirror(exitTool: true);
        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
        Assert.True(viewModel.TwoDMirrorKeepLink);
        viewModel.ClearTwoDMirrorObjects();
        Assert.Empty(viewModel.TwoDSelectedPathIds);
    }

    [Fact]
    public void MirrorFlipCopy_DefaultsTrueAndPersistsAcrossConfirmAndCancel()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(2, 0), new(4, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Mirror;

        Assert.True(viewModel.TwoDMirrorFlipCopy);
        viewModel.TwoDMirrorFlipCopy = false;
        viewModel.TwoDMirrorAxisStart = new Editor2DPoint(0, 0);
        viewModel.TwoDMirrorAxisEnd = new Editor2DPoint(0, 10);

        Assert.True(viewModel.ConfirmTwoDMirror());

        var copyId = Assert.Single(viewModel.TwoDSelectedPathIds);
        var copy = viewModel.TwoDDocument!.Paths.Single(path => path.Id == copyId);
        Assert.Equal([new Editor2DPoint(-4, 0), new Editor2DPoint(-2, 0)], copy.Points);
        Assert.False(viewModel.TwoDMirrorFlipCopy);
        Assert.All(viewModel.MirrorLinks.Values, link => Assert.False(link.Mirror));

        viewModel.CancelTwoDMirror(exitTool: true);
        Assert.False(viewModel.TwoDMirrorFlipCopy);
    }

    [Fact]
    public async Task MirrorEscape_CancelsStagingAndReturnsToSelectWithoutClearingObjects()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        var source = new Editor2DPreviewPath("shape", "LINE", [new(2, 0), new(4, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Mirror;
        viewModel.TwoDMirrorLineMode = true;
        viewModel.TwoDMirrorAxisStart = new Editor2DPoint(0, 0);
        viewModel.TwoDMirrorAxisEnd = new Editor2DPoint(0, 10);

        Assert.True(viewModel.TryActivateEditorShortcut("escape"));

        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
        Assert.Equal([source.Id], viewModel.TwoDSelectedPathIds);
        Assert.False(viewModel.TwoDMirrorLineMode);
        Assert.Null(viewModel.TwoDMirrorAxisStart);
        Assert.Null(viewModel.TwoDMirrorAxisEnd);
    }

    [Fact]
    public void MoveCopyMode_ResetsWhenMoveToolActivates()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.TwoDMoveCreateCopy);

        viewModel.TwoDMoveCreateCopy = true;
        viewModel.ActivateTwoDMoveTool();

        Assert.False(viewModel.TwoDMoveCreateCopy);

        viewModel.TwoDMoveCreateCopy = true;
        viewModel.TwoDActiveTool = Editor2DTool.Move;

        Assert.False(viewModel.TwoDMoveCreateCopy);
    }

    [Fact]
    public void MovePointToPointState_IsTransientAcrossToolSessions()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(0, 0), new(5, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDActiveTool = Editor2DTool.Move;
        viewModel.TwoDSelectedPathIds = [source.Id];

        viewModel.ToggleTwoDMovePointToPoint();
        viewModel.TwoDMovePointToPointSource = new Editor2DPoint(12, 7);

        Assert.True(viewModel.TwoDMovePointToPointActive);
        Assert.Equal("Click destination…", viewModel.TwoDMovePointToPointActionLabel);

        viewModel.TwoDActiveTool = Editor2DTool.Select;

        Assert.False(viewModel.TwoDMovePointToPointActive);
        Assert.Null(viewModel.TwoDMovePointToPointSource);
    }

    [Fact]
    public void MovePointToPointState_ResetsWhenSelectionClears()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("shape", "LINE", [new(0, 0), new(5, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDActiveTool = Editor2DTool.Move;
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.ToggleTwoDMovePointToPoint();
        viewModel.TwoDMovePointToPointSource = new Editor2DPoint(12, 7);

        viewModel.TwoDSelectedPathIds = [];

        Assert.False(viewModel.TwoDMovePointToPointActive);
        Assert.Null(viewModel.TwoDMovePointToPointSource);
    }

    [Theory]
    [InlineData(EditorMode.TwoD, true, false, false)]
    [InlineData(EditorMode.ThreeD, false, true, false)]
    [InlineData(EditorMode.Batch, false, false, true)]
    public async Task ActiveEditorMode_RaisesObservableStateAndShowsExactlyOneWorkspace(
        EditorMode mode,
        bool showsTwoD,
        bool showsThreeD,
        bool showsBatch)
    {
        var viewModel = CreateViewModel();
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await viewModel.SetActiveEditorModeAsync(mode);

        Assert.Equal(mode, viewModel.ActiveEditorMode);
        Assert.Equal(showsTwoD, viewModel.IsShowingTwoDWorkspace);
        Assert.Equal(showsThreeD, viewModel.IsShowing3DWorkspace);
        Assert.Equal(showsBatch, viewModel.IsShowingBatchWorkspace);
        Assert.Equal(1, new[]
        {
            viewModel.IsShowingTwoDWorkspace,
            viewModel.IsShowing3DWorkspace,
            viewModel.IsShowingBatchWorkspace,
        }.Count(static isVisible => isVisible));

        if (mode != EditorMode.ThreeD)
        {
            Assert.Contains(nameof(EditorPageViewModel.ActiveEditorMode), changes);
            Assert.Contains(nameof(EditorPageViewModel.IsShowingTwoDWorkspace), changes);
            Assert.Contains(nameof(EditorPageViewModel.IsShowing3DWorkspace), changes);
            Assert.Contains(nameof(EditorPageViewModel.IsShowingBatchWorkspace), changes);
        }
    }

    [Fact]
    public async Task SwitchingBetweenTwoDAndThreeD_PreservesEachModesActiveTool()
    {
        var viewModel = CreateViewModel();

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.ActivateTwoDCircleTool();

        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        viewModel.ActivateMoveTool();
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);
        Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);

        viewModel.ActivateTwoDRectangleTool();
        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);
        Assert.Equal(Editor2DTool.SketchRectangle, viewModel.TwoDActiveTool);
    }

    [Fact]
    public async Task RectangleTool_UsesSessionFilletAndCreatesOneAtomicEditableShape()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.ActivateTwoDRectangleTool();

        viewModel.TwoDRectangleFilletRadius = -2;
        Assert.Equal(0, viewModel.TwoDRectangleFilletRadius);
        viewModel.TwoDRectangleFilletRadius = 100;

        var pathId = viewModel.CreateTwoDRectangle(new(0, 0), new(10, 6));

        Assert.NotNull(pathId);
        Assert.Equal([pathId], viewModel.TwoDSelectedPathIds);
        Assert.Equal(4, viewModel.TwoDCornerParameters.Count);
        Assert.All(viewModel.TwoDCornerParameters, parameter => Assert.Equal(3, parameter.Value));
        Assert.Equal(2, viewModel.TwoDMeasurements.Count(measurement => measurement.IsAutoDimension));
        Assert.Equal(1, viewModel.TwoDSelectedRectangleCount);
        Assert.True(viewModel.CanExpandTwoDRectangles);
        Assert.True(viewModel.UndoTwoDWorkspace());
        Assert.Empty(viewModel.TwoDDocument!.Paths);
        Assert.Empty(viewModel.TwoDCornerParameters);
        Assert.True(viewModel.RedoTwoDWorkspace());
        Assert.Equal(4, viewModel.TwoDCornerParameters.Count);
    }

    [Fact]
    public async Task OpeningBlankTwoDWorkspace_DoesNotCreateATwoDArtifact()
    {
        var viewModel = CreateViewModel();

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.True(viewModel.HasTwoDWorkspaceDocument);
        Assert.Empty(viewModel.TwoDDocument!.Paths);
        Assert.False(viewModel.HasGeneratedOutput);
        Assert.Null(viewModel.LastGeneratedOutputPath);
        Assert.Same(viewModel.TwoDDocument, viewModel.TwoDWorkspace.Document);
    }

    [Fact]
    public async Task TwoDUndoRedoCommands_TrackWorkspaceHistory()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.TwoDWorkspace.ClearHistory();
        var path = new Editor2DPreviewPath("undo-line", "LINE", [new(0, 0), new(5, 0)], false);

        Assert.False(viewModel.CanUndoTwoDWorkspace);
        viewModel.TwoDDocument = viewModel.TwoDDocument! with { Paths = [path] };

        Assert.True(viewModel.CanUndoTwoDWorkspace);
        Assert.True(viewModel.UndoTwoDCommand.CanExecute(null));
        viewModel.UndoTwoDCommand.Execute(null);
        Assert.Empty(viewModel.TwoDDocument!.Paths);
        Assert.True(viewModel.CanRedoTwoDWorkspace);
        viewModel.RedoTwoDCommand.Execute(null);
        Assert.Equal(path.Id, Assert.Single(viewModel.TwoDDocument!.Paths).Id);
    }

    [Fact]
    public async Task UnifiedMenuCommands_RouteHistoryDeleteAndExportByMode()
    {
        var viewModel = CreateViewModelForTests();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.TwoDWorkspace.ClearHistory();
        var path = new Editor2DPreviewPath("menu-line", "LINE", [new(0, 0), new(5, 0)], false);
        viewModel.TwoDDocument = viewModel.TwoDDocument! with { Paths = [path] };
        viewModel.TwoDSelectedPathIds = [path.Id];

        Assert.True(viewModel.UndoCommand.CanExecute(null));
        Assert.True(viewModel.DeleteCommand.CanExecute(null));
        Assert.True(viewModel.ExportDxfCommand.CanExecute(null));
        viewModel.DeleteCommand.Execute(null);
        Assert.Empty(viewModel.TwoDDocument!.Paths);
        viewModel.UndoCommand.Execute(null);
        Assert.Equal(path.Id, Assert.Single(viewModel.TwoDDocument.Paths).Id);

        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        Assert.False(viewModel.DeleteCommand.CanExecute(null));
        Assert.False(viewModel.ExportDxfCommand.CanExecute(null));
        viewModel.ThreeDWorkspace.ReplaceBodies([new Body3D(0, "body", [])], "{}", "body.obj");
        viewModel.ActivateMoveTool();
        viewModel.SelectBodyFromPanel(0);
        viewModel.NudgeSelectedBody(axis: 0, direction: 1);
        Assert.True(viewModel.UndoCommand.CanExecute(null));
        viewModel.UndoCommand.Execute(null);
        Assert.Empty(viewModel.BodyOffsets);

        await viewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        Assert.False(viewModel.UndoCommand.CanExecute(null));
        Assert.False(viewModel.RedoCommand.CanExecute(null));
    }

    [Fact]
    public async Task CatalogActivation_IsModeSafeAndFlipActionsTrackSelection()
    {
        var viewModel = CreateViewModelForTests();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.ActivateSidebarItem("move");
        Assert.Equal(Editor2DTool.Move, viewModel.TwoDActiveTool);
        Assert.Equal(Editor3DTool.Select, viewModel.ActiveTool);

        var flip = Assert.Single(viewModel.SidebarTools, item =>
            item.Action == EditorSidebarAction.FlipSelectionHorizontal);
        Assert.False(flip.IsEnabled);
        var path = new Editor2DPreviewPath("flip", "LINE", [new(1, 0), new(3, 0)], false);
        viewModel.TwoDDocument = viewModel.TwoDDocument! with { Paths = [path] };
        viewModel.TwoDSelectedPathIds = [path.Id];
        Assert.True(flip.IsEnabled);

        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        viewModel.ActivateSidebarItem("move");
        Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);
        Assert.Equal(Editor2DTool.Move, viewModel.TwoDActiveTool);
    }

    [Fact]
    public async Task EditingTwoDDocument_NotifiesLayersPanelWithUpdatedMembership()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        var path = new Editor2DPreviewPath(
            "line-1",
            "LINE",
            [new Editor2DPoint(0, 0), new Editor2DPoint(10, 0)],
            IsClosed: false);

        viewModel.TwoDDocument = viewModel.TwoDDocument! with { Paths = [path] };

        var layer = Assert.Single(viewModel.TwoDLayers);
        Assert.Equal([path.Id], layer.PathIds);
        Assert.Equal("1 entities", layer.ContentSummary);
        Assert.Contains(nameof(EditorPageViewModel.TwoDLayers), changes);
    }

    [Fact]
    public async Task OrdinaryTwoDStateSync_PreservesNestedFoldersAndLayerMembership()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        var layer = Assert.Single(viewModel.TwoDLayers);
        var parent = viewModel.TwoDWorkspace.CreateFolder("Production");
        var child = viewModel.TwoDWorkspace.CreateFolder("Cut", parent.Id);
        Assert.True(viewModel.TwoDWorkspace.MoveLayerToFolder(layer.Id, child.Id));

        viewModel.TwoDPolygonSides = 9;

        Assert.Collection(
            viewModel.TwoDFolders,
            folder => Assert.Equal(parent, folder),
            folder => Assert.Equal(child, folder));
        Assert.Equal(child.Id, Assert.Single(viewModel.TwoDLayers).ParentFolderId);
    }

    [Fact]
    public async Task FillStrokeActions_ExposeAndApplyExistingWorkspaceOperations()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        var path = new Editor2DPreviewPath(
            "fill-toggle", "LWPOLYLINE",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)], true);
        viewModel.TwoDDocument = viewModel.TwoDDocument! with { Paths = [path] };
        viewModel.TwoDSelectedPathIds = [path.Id];

        Assert.True(viewModel.CanApplyTwoDStrokeToFill);
        Assert.True(viewModel.ApplyTwoDStrokeToFill());
        Assert.True(viewModel.TwoDDocument!.Paths.Single().IsFilled);
        Assert.True(viewModel.CanApplyTwoDFillToStroke);
        Assert.True(viewModel.ApplyTwoDFillToStroke());
        Assert.False(viewModel.TwoDDocument!.Paths.Single().IsFilled);
    }

    [Fact]
    public async Task LoadingSavedTwoDProject_RestoresDocumentAndActiveMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-2d-mode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "saved-2d.stch");
        try
        {
            var path = new Editor2DPreviewPath(
                "rectangle-1",
                "LWPOLYLINE",
                [new Editor2DPoint(0, 0), new Editor2DPoint(10, 0), new Editor2DPoint(10, 5), new Editor2DPoint(0, 5)],
                IsClosed: true);
            var twoDState = Editor2DWorkspaceState.Empty with
            {
                IsInitialized = true,
                Document = Editor2DWorkspaceState.Empty.Document with { Paths = [path] },
            };
            var shellState = new EditorWorkspaceState(
                Editor3DTool.Select,
                ThreeDOrthographic: false,
                ShowTwoDWorkspace: true,
                ActiveEditorMode: EditorMode.TwoD);
            await new Project3DStateService().SaveAsync(
                projectPath,
                new Project3DState(null, [], [], WorkspaceState: shellState, TwoDWorkspaceState: twoDState));
            var session = new ProjectSession(
                Guid.NewGuid(),
                "Saved 2D",
                projectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Opened,
                DateTimeOffset.UtcNow);
            var viewModel = CreateViewModel();

            Assert.True(await viewModel.ConfigureParametersAsync(
                new Dictionary<string, object> { [EditorNavigationParameterKeys.ProjectSession] = session },
                CancellationToken.None));
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);

            Assert.Equal(EditorMode.TwoD, viewModel.ActiveEditorMode);
            Assert.True(viewModel.IsShowingTwoDWorkspace);
            Assert.Equal(path.Id, Assert.Single(viewModel.TwoDDocument!.Paths).Id);
            Assert.Equal([path.Id], Assert.Single(viewModel.TwoDLayers).PathIds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadingPendingTwoDDocuments_ImportsAllDrawingsSideBySide()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-2d-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "imported.stch");
        var firstPath = Path.Combine(directory, "first.dxf");
        var secondPath = Path.Combine(directory, "second.svg");
        try
        {
            await new Project3DStateService().SaveAsync(projectPath, new Project3DState(null, [], []));
            File.WriteAllText(firstPath, "first");
            File.WriteAllText(secondPath, "second");
            var session = new ProjectSession(
                Guid.NewGuid(),
                "Imported drawings",
                projectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Imported,
                DateTimeOffset.UtcNow);
            var previewService = new MappingOutputPreviewService(new Dictionary<string, Editor2DPreviewDocument>
            {
                [Path.GetFullPath(firstPath)] = Document("first", 0),
                [Path.GetFullPath(secondPath)] = Document("second", 100),
            });
            var viewModel = CreateViewModel(outputPreviewService: previewService);

            Assert.True(await viewModel.ConfigureParametersAsync(
                new Dictionary<string, object>
                {
                    [EditorNavigationParameterKeys.ProjectSession] = session,
                    [EditorNavigationParameterKeys.PendingTwoDFilePaths] = new[] { firstPath, secondPath },
                },
                CancellationToken.None));
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);

            Assert.Equal(2, viewModel.TwoDDocument!.Paths.Count);
            Assert.Equal(2, viewModel.TwoDWorkspace.ImportGroups.Count);
            Assert.Equal(viewModel.TwoDWorkspace.ImportGroups[0].GeneratedPathIds[0], viewModel.TwoDDocument.Paths[0].Id);
            Assert.Equal(viewModel.TwoDWorkspace.ImportGroups[1].GeneratedPathIds[0], viewModel.TwoDDocument.Paths[1].Id);
            Assert.Equal(Path.GetFullPath(firstPath), viewModel.TwoDWorkspace.ImportGroups[0].SourceFilePath);
            Assert.Equal(Path.GetFullPath(secondPath), viewModel.TwoDWorkspace.ImportGroups[1].SourceFilePath);
            Assert.True(viewModel.TwoDDocument.Paths[1].Points[0].X > viewModel.TwoDDocument.Paths[0].Points[0].X);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static Editor2DPreviewDocument Document(string id, double x)
        {
            var path = new Editor2DPreviewPath(id, "LINE", [new Editor2DPoint(x, 0), new Editor2DPoint(x + 10, 0)], false);
            return new Editor2DPreviewDocument(
                [path],
                new Editor2DBounds(x, 0, x + 10, 0),
                new Dictionary<string, int> { ["LINE"] = 1 },
                []);
        }
    }

    [Fact]
    public async Task LoadingPendingDxf_AppliesConfirmedImportUnitCorrection()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-2d-units-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "imported.stch");
        var dxfPath = Path.Combine(directory, "inch-drawing.dxf");
        try
        {
            await new Project3DStateService().SaveAsync(projectPath, new Project3DState(null, [], []));
            File.WriteAllText(dxfPath, "placeholder");
            var previewPath = new Editor2DPreviewPath("inch-line", "LINE", [new(0, 0), new(10, 0)], false);
            var preview = new Editor2DPreviewDocument(
                [previewPath],
                new Editor2DBounds(0, 0, 10, 0),
                new Dictionary<string, int> { ["LINE"] = 1 },
                []);
            var previewService = new MappingOutputPreviewService(
                new Dictionary<string, Editor2DPreviewDocument> { [Path.GetFullPath(dxfPath)] = preview },
                new Dictionary<string, Editor2DImportUnitsInfo>
                {
                    [Path.GetFullPath(dxfPath)] = new(dxfPath, 1, 25.4, 10, 1),
                });
            var prompt = new RecordingImportUnitsPrompt(25.4);
            var session = new ProjectSession(
                Guid.NewGuid(), "Imported inches", projectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Imported, DateTimeOffset.UtcNow);
            var viewModel = CreateViewModelForTests(
                outputPreviewService: previewService,
                importUnitsPromptService: prompt);

            Assert.True(await viewModel.ConfigureParametersAsync(new Dictionary<string, object>
            {
                [EditorNavigationParameterKeys.ProjectSession] = session,
                [EditorNavigationParameterKeys.PendingTwoDFilePaths] = new[] { dxfPath },
            }, CancellationToken.None));
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);

            var path = Assert.Single(viewModel.TwoDDocument!.Paths);
            Assert.Equal(254, path.Points[1].X, 6);
            Assert.Equal(Path.GetFullPath(dxfPath), prompt.LastInfo?.SourcePath);
            Assert.Equal(25.4, Assert.Single(viewModel.TwoDWorkspace.ImportGroups).AppliedUnitScale);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BatchMode_PreservesBothToolsAndRejectsEditorShortcuts()
    {
        var viewModel = CreateViewModel();
        viewModel.ActivateMoveTool();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.ActivateTwoDCircleTool();

        await viewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        var handled = viewModel.TryActivateEditorShortcut("select");

        Assert.False(handled);
        Assert.Equal(EditorMode.Batch, viewModel.ActiveEditorMode);
        Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);
    }

    [Theory]
    [MemberData(nameof(CatalogShortcutDescriptors))]
    public async Task CatalogShortcut_ActivatesEveryDescriptor(EditorToolDescriptor descriptor)
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(descriptor.Mode);
        var wasOrthographic = viewModel.ThreeDOrthographic;

        var handled = viewModel.TryActivateEditorShortcut(descriptor.ShortcutText!);

        Assert.True(handled);
        if (descriptor.TwoDTool is Editor2DTool twoDTool)
            Assert.Equal(twoDTool, viewModel.TwoDActiveTool);
        if (descriptor.ThreeDTool is Editor3DTool threeDTool)
            Assert.Equal(threeDTool, viewModel.ActiveTool);
        if (descriptor.Action == EditorSidebarAction.ToggleOrthographic)
            Assert.NotEqual(wasOrthographic, viewModel.ThreeDOrthographic);
    }

    [Fact]
    public async Task IdenticalShortcut_ResolvesDifferentlyAcrossModes()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.TryActivateEditorShortcut("3"));
        Assert.Equal(Editor3DTool.Plane, viewModel.ActiveTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        Assert.True(viewModel.TryActivateEditorShortcut("3"));
        Assert.Equal(Editor2DTool.Pan, viewModel.TwoDActiveTool);
    }

    [Fact]
    public async Task SemanticCommandNames_AreNotRemappedToUnrelatedTwoDTools()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.ActivateTwoDCircleTool();

        Assert.False(viewModel.TryActivateEditorShortcut("project"));
        Assert.False(viewModel.TryActivateEditorShortcut("unfold"));
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);
    }

    [Fact]
    public async Task CustomizedCatalog_FeedsRailSearchShortcutsAndPersistedStateFromTheSameSet()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        viewModel.CustomizeTool("2d.circle", order: -10, shortcutText: "G");

        var railIdentifiers = viewModel.SidebarTools.Select(item => item.Identifier).ToArray();
        var searchIdentifiers = viewModel.CommandSearchResults.Select(item => item.Identifier).ToArray();
        var persistedIdentifiers = viewModel.ToolCustomizations
            .Where(customization => customization.Identifier.StartsWith("2d.", StringComparison.Ordinal))
            .Select(customization => customization.Identifier)
            .Order()
            .ToArray();
        Assert.Equal("2d.circle", railIdentifiers[0]);
        Assert.Equal(railIdentifiers, searchIdentifiers);
        Assert.Equal(railIdentifiers.Order(), persistedIdentifiers);
        Assert.Equal(
            EditorToolCatalog.ForMode(EditorMode.TwoD).Select(descriptor => descriptor.Identifier).Order(),
            railIdentifiers.Order());

        viewModel.ActivateTwoDSelectTool();
        Assert.False(viewModel.TryActivateEditorShortcut("C"));
        Assert.True(viewModel.TryActivateEditorShortcut("G"));
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);

        viewModel.CommandSearchQuery = "circle";
        var result = Assert.Single(viewModel.CommandSearchResults);
        Assert.Equal("2d.circle", result.Identifier);
        Assert.Equal("G", result.ShortcutText);
        viewModel.ActivateCommandSearchItem(result.Identifier);
        Assert.Equal(string.Empty, viewModel.CommandSearchQuery);
    }

    [Fact]
    public async Task CommandSearch_ReportsAnEmptyStateForUnmatchedQueries()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.False(viewModel.IsCommandSearchOpen);
        Assert.False(viewModel.IsCommandSearchEmpty);

        viewModel.CommandSearchQuery = "definitely-not-a-tool";

        Assert.True(viewModel.IsCommandSearchOpen);
        Assert.True(viewModel.IsCommandSearchEmpty);
        Assert.Empty(viewModel.CommandSearchResults);
    }

    [Theory]
    [InlineData("grid", EditorCommandPaletteCatalog.ToggleGridIdentifier)]
    [InlineData("snapping", EditorCommandPaletteCatalog.ToggleSnappingIdentifier)]
    [InlineData("chain selection", EditorCommandPaletteCatalog.ToggleChainSelectionIdentifier)]
    [InlineData("zoom in", EditorCommandPaletteCatalog.ZoomInIdentifier)]
    [InlineData("zoom out", EditorCommandPaletteCatalog.ZoomOutIdentifier)]
    [InlineData("zoom to fit", EditorCommandPaletteCatalog.ZoomToFitIdentifier)]
    [InlineData("view.snap", EditorCommandPaletteCatalog.ToggleSnappingIdentifier)]
    [InlineData("view", EditorCommandPaletteCatalog.ToggleGridIdentifier)]
    public async Task CommandSearch_FindsTwoDViewCommandsByLabelIdentifierAndCategory(string query, string identifier)
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        viewModel.CommandSearchQuery = query;

        Assert.Contains(viewModel.CommandSearchResults, item => item.Identifier == identifier);
    }

    [Fact]
    public async Task CommandSearch_TwoDViewCommandsDispatchExistingActionsAndClearQuery()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.TwoDWorkspace.SetDocument(new Editor2DPreviewDocument(
            [],
            new Editor2DBounds(0, 0, 10, 10),
            new Dictionary<string, int>(),
            []));
        var initialGrid = viewModel.TwoDGridVisible;
        var initialSnapping = viewModel.TwoDSnapEnabled;
        var initialChainSelection = viewModel.TwoDChainSelectionEnabled;
        var initialFrameRequest = viewModel.TwoDFrameRequestToken;
        viewModel.TwoDViewportZoom = 2.0;
        viewModel.TwoDViewportOffsetX = 12.0;
        viewModel.TwoDViewportOffsetY = -8.0;

        Activate("grid", EditorCommandPaletteCatalog.ToggleGridIdentifier);
        Assert.NotEqual(initialGrid, viewModel.TwoDGridVisible);
        Activate("snapping", EditorCommandPaletteCatalog.ToggleSnappingIdentifier);
        Assert.NotEqual(initialSnapping, viewModel.TwoDSnapEnabled);
        Activate("chain selection", EditorCommandPaletteCatalog.ToggleChainSelectionIdentifier);
        Assert.NotEqual(initialChainSelection, viewModel.TwoDChainSelectionEnabled);
        Activate("zoom in", EditorCommandPaletteCatalog.ZoomInIdentifier);
        Assert.Equal(2.5, viewModel.TwoDViewportZoom, 8);
        Assert.Equal(15.0, viewModel.TwoDViewportOffsetX, 8);
        Assert.Equal(-10.0, viewModel.TwoDViewportOffsetY, 8);
        Activate("zoom out", EditorCommandPaletteCatalog.ZoomOutIdentifier);
        Assert.Equal(2.0, viewModel.TwoDViewportZoom, 8);
        Assert.Equal(12.0, viewModel.TwoDViewportOffsetX, 8);
        Assert.Equal(-8.0, viewModel.TwoDViewportOffsetY, 8);
        Activate("zoom to fit", EditorCommandPaletteCatalog.ZoomToFitIdentifier);
        Assert.Equal(initialFrameRequest + 1, viewModel.TwoDFrameRequestToken);

        void Activate(string query, string identifier)
        {
            viewModel.CommandSearchQuery = query;
            Assert.Contains(viewModel.CommandSearchResults, item => item.Identifier == identifier);
            viewModel.ActivateCommandSearchItem(identifier);
            Assert.Equal(string.Empty, viewModel.CommandSearchQuery);
        }
    }

    [Fact]
    public async Task FilletContinuity_UpdatesActiveShapeAndPreservesCornerValues()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        var source = new Editor2DPreviewPath(
            "shape",
            "LWPOLYLINE",
            [new(0, 0), new(20, 0), new(20, 20), new(0, 20)],
            true);
        var otherSource = new Editor2DPreviewPath(
            "other",
            "LWPOLYLINE",
            [new(30, 0), new(50, 0), new(50, 20), new(30, 20)],
            true);
        viewModel.TwoDWorkspace.SetDocument(new Editor2DPreviewDocument(
            [source, otherSource],
            new Editor2DBounds(0, 0, 50, 20),
            new Dictionary<string, int> { ["LWPOLYLINE"] = 2 },
            []));
        var first = new Editor2DCornerParameter(
            "shape:0", source.Id, 0, Editor2DCornerKind.Fillet, 2.0, source.Points);
        var second = new Editor2DCornerParameter(
            "shape:1", source.Id, 1, Editor2DCornerKind.Fillet, 4.0, source.Points);
        var other = new Editor2DCornerParameter(
            "other:0", otherSource.Id, 0, Editor2DCornerKind.Fillet, 3.0, otherSource.Points);
        viewModel.TwoDCornerParameters = [first, second, other];

        viewModel.SelectTwoDCornerParameter(first.Id);

        Assert.Equal(Editor2DFilletContinuity.G1, viewModel.TwoDFilletContinuity);
        Assert.Equal("Fillet corner 1", viewModel.TwoDActiveCornerLabel);
        Assert.Contains("2 editable corners", viewModel.TwoDCornerSelectionSummary, StringComparison.Ordinal);
        viewModel.TwoDFilletContinuity = Editor2DFilletContinuity.G2;

        var updated = viewModel.TwoDCornerParameters.ToDictionary(item => item.Id, StringComparer.Ordinal);
        Assert.Equal(Editor2DFilletContinuity.G2, updated[first.Id].Continuity);
        Assert.Equal(Editor2DFilletContinuity.G2, updated[second.Id].Continuity);
        Assert.Equal(2.0, updated[first.Id].Value);
        Assert.Equal(4.0, updated[second.Id].Value);
        Assert.Equal(Editor2DFilletContinuity.G1, updated[other.Id].Continuity);
        Assert.Equal(3.0, updated[other.Id].Value);
    }

    [Theory]
    [InlineData(EditorMode.ThreeD)]
    [InlineData(EditorMode.Batch)]
    public async Task CommandSearch_TwoDViewCommandsAreUnavailableOutsideTwoD(EditorMode mode)
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(mode);

        viewModel.CommandSearchQuery = "toggle";

        Assert.DoesNotContain(
            viewModel.CommandSearchResults,
            item => EditorCommandPaletteCatalog.SearchOnly.Any(command => command.Identifier == item.Identifier));
    }

    [Fact]
    public async Task CommandSearch_ModeChangeRemovesTwoDViewCommandsAndRefreshesEmptyState()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.CommandSearchQuery = "toggle grid";
        Assert.Contains(
            viewModel.CommandSearchResults,
            item => item.Identifier == EditorCommandPaletteCatalog.ToggleGridIdentifier);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);

        Assert.Empty(viewModel.CommandSearchResults);
        Assert.True(viewModel.IsCommandSearchEmpty);
        Assert.Contains(nameof(EditorPageViewModel.CommandSearchResults), changes);
        Assert.Contains(nameof(EditorPageViewModel.IsCommandSearchEmpty), changes);
    }

    [Fact]
    public async Task CommandSearch_ViewCommandsDoNotChangeToolRailContents()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        var railIdentifiers = viewModel.SidebarTools.Select(item => item.Identifier).ToArray();

        Assert.Equal(
            EditorToolCatalog.ForMode(EditorMode.TwoD).Select(item => item.Identifier),
            railIdentifiers);
        Assert.DoesNotContain(
            railIdentifiers,
            identifier => EditorCommandPaletteCatalog.SearchOnly.Any(command => command.Identifier == identifier));
    }

    [Fact]
    public void WorkspaceState_RoundTripsToolCustomizationByStableIdentifier()
    {
        var state = new EditorWorkspaceState(
            Editor3DTool.Select,
            ThreeDOrthographic: false,
            ShowTwoDWorkspace: false,
            ToolCustomizations:
            [
                new EditorToolCustomization("2d.circle", -10, "G"),
                new EditorToolCustomization("3d.project", 2, "P"),
            ]);

        var restored = JsonSerializer.Deserialize<EditorWorkspaceState>(JsonSerializer.Serialize(state));

        Assert.NotNull(restored);
        Assert.Equal(state.ToolCustomizations, restored.ToolCustomizations);
        Assert.Contains(restored.ToolCustomizations!, item => item.Identifier == "2d.circle" && item.ShortcutText == "G");
    }

    [Fact]
    public async Task SidebarTools_AreScopedToTheActiveEditorMode()
    {
        var viewModel = CreateViewModel();

        Assert.NotEmpty(viewModel.SidebarTools);
        Assert.All(viewModel.SidebarTools, item => Assert.Equal(EditorMode.ThreeD, item.Mode));
        Assert.Contains(viewModel.SidebarTools, item => item.Tool == Editor3DTool.Select);
        Assert.Contains(viewModel.SidebarTools, item => item.Tool == Editor3DTool.Move);
        Assert.Contains(viewModel.SidebarTools, item => item.Tool == Editor3DTool.Plane);
        Assert.DoesNotContain(viewModel.SidebarTools, item => item.TwoDTool is not null);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.Equal(EditorToolCatalog.ForMode(EditorMode.TwoD).Count, viewModel.SidebarTools.Count);
        Assert.All(viewModel.SidebarTools, item => Assert.Equal(EditorMode.TwoD, item.Mode));
        Assert.DoesNotContain(viewModel.SidebarTools, item => item.Tool is not null);

        viewModel.ActivateSidebarItem("circle");
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        Assert.Empty(viewModel.SidebarTools);
    }

    [Fact]
    public async Task ChangingMode_NotifiesTheSharedSidebarCollection()
    {
        var viewModel = CreateViewModel();
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.Contains(nameof(EditorPageViewModel.SidebarTools), changes);
    }

    [Theory]
    [InlineData(Editor3DTool.Select)]
    [InlineData(Editor3DTool.Move)]
    [InlineData(Editor3DTool.Plane)]
    [InlineData(Editor3DTool.Measure)]
    [InlineData(Editor3DTool.Unfold)]
    [InlineData(Editor3DTool.Output)]
    public async Task TwoDMode_HidesEveryThreeDInspectorPanel(Editor3DTool threeDTool)
    {
        var viewModel = CreateViewModel();
        viewModel.ActivateTool(threeDTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.False(viewModel.ShowSelectionPanel);
        Assert.False(viewModel.ShowMoveBodiesPanel);
        Assert.False(viewModel.ShowProjectionPanel);
        Assert.False(viewModel.ShowMeasurePanel);
        Assert.False(viewModel.ShowUnfoldPanel);
        Assert.False(viewModel.ShowOutputPanel);
    }

    [Fact]
    public async Task ThreeDMode_HidesTheTwoDInspectorContext()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);

        Assert.True(viewModel.IsShowing3DWorkspace);
        Assert.False(viewModel.IsShowingTwoDWorkspace);
    }

    [Fact]
    public void WorkspaceState_LegacyJsonWithoutModeRemainsCompatible()
    {
        const string legacyJson = """
            {
              "activeTool": 1,
              "threeDOrthographic": true,
              "showGeneratedOutputWorkspace": true,
              "generatedOutputActiveTool": 6
            }
            """;

        var state = JsonSerializer.Deserialize<EditorWorkspaceState>(legacyJson);

        Assert.NotNull(state);
        Assert.Null(state.ActiveEditorMode);
        Assert.True(state.ShowTwoDWorkspace);
        Assert.Equal(Editor3DTool.Move, state.ActiveTool);
        Assert.Equal(Editor2DTool.SketchCircle, state.TwoDActiveTool);
    }

    [Fact]
    public void WorkspaceState_TwoDPropertiesRetainLegacyStchJsonFieldNames()
    {
        var state = new EditorWorkspaceState(
            Editor3DTool.Select,
            ThreeDOrthographic: false,
            ShowTwoDWorkspace: true,
            TwoDActiveTool: Editor2DTool.SketchRectangle,
            TwoDPolygonSides: 9,
            TwoDViewportZoom: 2.5,
            TwoDViewportOffsetX: 4,
            TwoDViewportOffsetY: -3,
            TwoDExpandedRectanglePathIds: ["rectangle-1"]);

        var json = JsonSerializer.Serialize(state);

        Assert.Contains("\"showGeneratedOutputWorkspace\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"generatedOutputActiveTool\":5", json, StringComparison.Ordinal);
        Assert.Contains("\"generatedOutputPolygonSides\":9", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"twoDActiveTool\"", json, StringComparison.Ordinal);
        var restored = JsonSerializer.Deserialize<EditorWorkspaceState>(json);
        Assert.NotNull(restored);
        Assert.Equal(Editor2DTool.SketchRectangle, restored.TwoDActiveTool);
        Assert.Equal(["rectangle-1"], restored.TwoDExpandedRectanglePathIds);
    }

    [Fact]
    public void WorkspaceState_RoundTripsExplicitBatchMode()
    {
        var state = new EditorWorkspaceState(
            Editor3DTool.Move,
            ThreeDOrthographic: true,
            ShowTwoDWorkspace: false,
            Editor2DTool.SketchCircle,
            ActiveEditorMode: EditorMode.Batch);

        var restored = JsonSerializer.Deserialize<EditorWorkspaceState>(JsonSerializer.Serialize(state));

        Assert.NotNull(restored);
        Assert.Equal(EditorMode.Batch, restored.ActiveEditorMode);
        Assert.Equal(Editor3DTool.Move, restored.ActiveTool);
        Assert.Equal(Editor2DTool.SketchCircle, restored.TwoDActiveTool);
    }

    public static EditorPageViewModel CreateViewModelForTests(
        IProjectFileDialogService? projectFileDialogService = null,
        IEditorOutputPreviewService? outputPreviewService = null,
        IUnsavedChangesPromptService? unsavedChangesPromptService = null,
        IEditorImportUnitsPromptService? importUnitsPromptService = null,
        IEditor2DGeometryKernelService? geometryKernelService = null,
        ProjectSessionService? projectSessionService = null)
        => CreateViewModel(projectFileDialogService, outputPreviewService, unsavedChangesPromptService, importUnitsPromptService, geometryKernelService, projectSessionService);

    private static EditorPageViewModel CreateViewModel(
        IProjectFileDialogService? projectFileDialogService = null,
        IEditorOutputPreviewService? outputPreviewService = null,
        IUnsavedChangesPromptService? unsavedChangesPromptService = null,
        IEditorImportUnitsPromptService? importUnitsPromptService = null,
        IEditor2DGeometryKernelService? geometryKernelService = null,
        ProjectSessionService? projectSessionService = null)
        => new(
            NullLogger<EditorPageViewModel>.Instance,
            new StubViewportAssetLocator(),
            new Project3DStateService(),
            projectFileDialogService ?? new StubProjectFileDialogService(),
            new StubOutputLauncherService(),
            outputPreviewService ?? new StubOutputPreviewService(),
            geometryKernelService ?? new Stub2DGeometryKernelService(),
            new Stub3DOperationService(),
            new StubGeometryKernelDescriptorProvider(),
            unsavedChangesPromptService: unsavedChangesPromptService,
            importUnitsPromptService: importUnitsPromptService,
            projectSessionService: projectSessionService);

    private sealed class StubViewportAssetLocator : IEditorViewportAssetLocator
    {
        public string GetViewportHtml() => string.Empty;

        public Uri GetViewportBaseUri() => new("file:///viewport/");
    }

    private sealed class StubProjectFileDialogService : IProjectFileDialogService
    {
        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class StubOutputLauncherService : IEditorOutputLauncherService
    {
        public Task OpenOutputAsync(string outputPath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RevealOutputAsync(string outputPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubOutputPreviewService : IEditorOutputPreviewService
    {
        public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<Editor2DPreviewDocument?>(null);

        public Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<EditorGeneratedOutputSummary?>(null);
    }

    private sealed class MappingOutputPreviewService(
        IReadOnlyDictionary<string, Editor2DPreviewDocument> documents,
        IReadOnlyDictionary<string, Editor2DImportUnitsInfo>? importUnits = null) : IEditorOutputPreviewService
    {
        public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.TryGetValue(Path.GetFullPath(outputPath), out var document);
            return Task.FromResult(document);
        }

        public Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Editor2DImportUnitsInfo?> InspectImportUnitsAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            Editor2DImportUnitsInfo? info = null;
            importUnits?.TryGetValue(Path.GetFullPath(outputPath), out info);
            return Task.FromResult(info);
        }

        public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<EditorGeneratedOutputSummary?>(null);
    }

    private sealed class RecordingImportUnitsPrompt(double factor) : IEditorImportUnitsPromptService
    {
        public Editor2DImportUnitsInfo? LastInfo { get; private set; }

        public Task<double?> PromptAsync(Editor2DImportUnitsInfo info, CancellationToken cancellationToken = default)
        {
            LastInfo = info;
            return Task.FromResult<double?>(factor);
        }
    }

    private sealed class Stub2DGeometryKernelService : IEditor2DGeometryKernelService
    {
        public Task<Editor2DGeometryKernelResult> BuildCurveOffsetPathsAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double offsetDistance,
            bool offsetOutward,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Editor2DGeometryKernelResult.Success([]));

        public Task<Editor2DGeometryKernelResult> BuildThicknessOutlinesAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double thickness,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Editor2DGeometryKernelResult.Success([]));
    }

    private sealed class RecordingOffsetGeometryKernelService : IEditor2DGeometryKernelService
    {
        public int CallCount { get; private set; }

        public Task<Editor2DGeometryKernelResult> BuildCurveOffsetPathsAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double offsetDistance,
            bool offsetOutward,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var signedDistance = offsetOutward ? offsetDistance : -offsetDistance;
            var paths = sourcePaths.Select(path => Editor2DGeometry.TranslatePath(
                path,
                0,
                signedDistance,
                $"{path.Id}:offset-preview:{CallCount}")).ToArray();
            return Task.FromResult(Editor2DGeometryKernelResult.Success(paths));
        }

        public Task<Editor2DGeometryKernelResult> BuildThicknessOutlinesAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double thickness,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Editor2DGeometryKernelResult.Success([]));
    }

    private sealed class DelayedOffsetGeometryKernelService : IEditor2DGeometryKernelService
    {
        private readonly List<(double Distance, bool Outward, IReadOnlyList<Editor2DPreviewPath> Sources, TaskCompletionSource<Editor2DGeometryKernelResult> Completion)> _calls = [];

        public int CallCount => _calls.Count;

        public Task<Editor2DGeometryKernelResult> BuildCurveOffsetPathsAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double offsetDistance,
            bool offsetOutward,
            CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<Editor2DGeometryKernelResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _calls.Add((offsetDistance, offsetOutward, sourcePaths, completion));
            return completion.Task;
        }

        public void Complete(int index)
        {
            var call = _calls[index];
            var signedDistance = call.Outward ? call.Distance : -call.Distance;
            var paths = call.Sources.Select(path => Editor2DGeometry.TranslatePath(
                path,
                0,
                signedDistance,
                $"{path.Id}:delayed-offset:{index}")).ToArray();
            call.Completion.SetResult(Editor2DGeometryKernelResult.Success(paths));
        }

        public Task<Editor2DGeometryKernelResult> BuildThicknessOutlinesAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double thickness,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Editor2DGeometryKernelResult.Success([]));
    }

    private sealed class Stub3DOperationService : IEditor3DOperationService
    {
        public Task<EditorModelLoadResult> LoadModelAsync(string sourceModelPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorModelLoadResult(false, "Not used."));

        public Task<EditorModelLoadResult> LoadModelsAsync(
            IReadOnlyList<string> sourceModelPaths,
            string? existingSourceModelPath = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorModelLoadResult(false, "Not used."));

        public Task<EditorFaceDistortionResult> ComputeFaceDistortionAsync(
            string? sourceModelPath,
            SelectedFace3D selectedFace,
            string distortionMode,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorFaceDistortionResult(false, "Not used."));

        public Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorOperationResult(false, "Not used."));

        public Task<EditorOperationResult> ProjectEdgesAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorOperationResult(false, "Not used."));
    }

    private sealed class StubGeometryKernelDescriptorProvider : IGeometryKernelDescriptorProvider
    {
        public GeometryKernelDescriptor Current { get; } = new(
            "Test kernel",
            "Test implementation",
            "Test runtime",
            "Test capabilities",
            "Test requirements",
            "Test source models");
    }
}
