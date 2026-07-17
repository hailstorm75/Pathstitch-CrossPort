using Avalonia;
using Avalonia.Controls;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Pathstitch.App.Controls;
using Pathstitch.App.Pages;
using Pathstitch.App.Services;
using Pathstitch.App.Tests.Fixtures;
using SkiaSharp;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pathstitch.App.Tests;

public sealed class ReferenceImageWorkflowTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public void ImportReferenceImage_UsesOptionalInsertionPointAndKeepsLegacyOriginDefault()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        var centered = workspace.ImportReferenceImage(
            "centered.png",
            Convert.ToBase64String([1]),
            20,
            10,
            new Editor2DPoint(50, -25));
        Assert.Equal(50, centered.ReferenceImage!.X);
        Assert.Equal(-25, centered.ReferenceImage.Y);

        Assert.True(workspace.Undo());
        Assert.DoesNotContain(workspace.Layers, layer => layer.IsReferenceImage);
        Assert.True(workspace.Redo());
        var restored = Assert.Single(workspace.Layers, layer => layer.IsReferenceImage).ReferenceImage!;
        Assert.Equal(50, restored.X);
        Assert.Equal(-25, restored.Y);

        var legacyWorkspace = new Editor2DWorkspaceViewModel();
        var legacy = legacyWorkspace.ImportReferenceImage(
            "legacy.png",
            Convert.ToBase64String([2]),
            20,
            10);

        Assert.Equal(0, legacy.ReferenceImage!.X);
        Assert.Equal(0, legacy.ReferenceImage.Y);
    }

    [Fact]
    public void ReferenceImageSelection_BeginsTransformWithoutChangingUnderlyingTool()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var geometryLayer = workspace.Layers.Single(layer => layer.Kind == Editor2DLayerKind.Geometry);
        var referenceLayer = workspace.ImportReferenceImage("pattern.png", Convert.ToBase64String([1]), 100, 50);

        Assert.True(workspace.IsReferenceImageTransformEditActive);
        Assert.True(workspace.SelectLayer(geometryLayer.Id));
        workspace.SetActiveTool(Editor2DTool.SketchLine);
        Assert.True(workspace.SelectLayer(referenceLayer.Id));

        Assert.Equal(Editor2DTool.SketchLine, workspace.ActiveTool);
        Assert.Equal(referenceLayer.Id, workspace.ActiveLayerId);
        Assert.True(workspace.IsReferenceImageTransformEditActive);
        Assert.Empty(workspace.SelectedPathIds);
    }

    [Fact]
    public void ReferenceImageLayerSwitch_CommitsTransformAsOneUndoStep()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var geometryLayer = workspace.Layers.Single(layer => layer.Kind == Editor2DLayerKind.Geometry);
        var referenceLayer = workspace.ImportReferenceImage("pattern.png", Convert.ToBase64String([1]), 100, 50);
        Assert.True(workspace.SelectLayer(geometryLayer.Id));
        workspace.ClearHistory();
        workspace.SetActiveTool(Editor2DTool.SketchLine);
        Assert.True(workspace.SelectLayer(referenceLayer.Id));
        var original = workspace.Layers.Single(layer => layer.Id == referenceLayer.Id).ReferenceImage!;

        Assert.True(workspace.UpdateReferenceImageTransform(referenceLayer.Id, 10, 5, 120, 60, 10));
        Assert.True(workspace.UpdateReferenceImageTransform(referenceLayer.Id, 20, 15, 140, 70, 25));
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.SelectLayer(geometryLayer.Id));

        Assert.False(workspace.IsReferenceImageTransformEditActive);
        Assert.True(workspace.CanUndo);
        Assert.Equal(20, workspace.Layers.Single(layer => layer.Id == referenceLayer.Id).ReferenceImage!.X);
        Assert.True(workspace.Undo());
        Assert.Equal(original, workspace.Layers.Single(layer => layer.Id == referenceLayer.Id).ReferenceImage);
        Assert.Equal(referenceLayer.Id, workspace.ActiveLayerId);
        Assert.Equal(Editor2DTool.SketchLine, workspace.ActiveTool);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void ImportingAnotherReferenceImage_CommitsActiveTransformBeforeStartingNext()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var first = workspace.ImportReferenceImage("first.png", Convert.ToBase64String([1]), 100, 50);
        Assert.True(workspace.UpdateReferenceImageTransform(first.Id, 12, 8, 110, 55, 15));

        var second = workspace.ImportReferenceImage("second.png", Convert.ToBase64String([2]), 80, 40);

        Assert.Equal(12, workspace.Layers.Single(layer => layer.Id == first.Id).ReferenceImage!.X);
        Assert.Equal(second.Id, workspace.ActiveLayerId);
        Assert.True(workspace.IsReferenceImageTransformEditActive);
        Assert.Equal(2, workspace.Layers.Count(layer => layer.IsReferenceImage));
    }

    [Fact]
    public void LockedOrHiddenReferenceImage_DoesNotBeginTransformOnSelection()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var geometryLayer = workspace.Layers.Single(layer => layer.Kind == Editor2DLayerKind.Geometry);
        var referenceLayer = workspace.ImportReferenceImage("pattern.png", Convert.ToBase64String([1]), 100, 50);

        Assert.True(workspace.ToggleLayerLock(referenceLayer.Id));
        Assert.True(workspace.SelectLayer(geometryLayer.Id));
        Assert.True(workspace.SelectLayer(referenceLayer.Id));
        Assert.False(workspace.IsReferenceImageTransformEditActive);

        Assert.True(workspace.ToggleLayerLock(referenceLayer.Id));
        Assert.True(workspace.ToggleLayerVisibility(referenceLayer.Id));
        Assert.True(workspace.SelectLayer(geometryLayer.Id));
        Assert.True(workspace.SelectLayer(referenceLayer.Id));
        Assert.False(workspace.IsReferenceImageTransformEditActive);
    }

    [Fact]
    public void ReferenceImageTransformEdit_CommitsOneUndoForManyPreviewUpdates()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var layer = workspace.ImportReferenceImage("pattern.png", Convert.ToBase64String([1]), 100, 50);
        var original = layer.ReferenceImage!;
        workspace.ClearHistory();

        Assert.True(workspace.BeginReferenceImageTransformEdit(layer.Id));
        Assert.True(workspace.UpdateReferenceImageTransform(layer.Id, 10, 5, 120, 60, 10));
        Assert.True(workspace.UpdateReferenceImageTransform(layer.Id, 20, 15, 140, 70, 25));
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.CommitReferenceImageTransformEdit());

        var final = workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!;
        Assert.Equal(20, final.X);
        Assert.Equal(15, final.Y);
        Assert.Equal(140, final.Width);
        Assert.Equal(70, final.Height);
        Assert.Equal(25, final.RotationDegrees);
        Assert.True(workspace.Undo());
        Assert.Equal(original, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.Redo());
        Assert.Equal(final, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage);
    }

    [Fact]
    public void ReferenceImageTransformEdit_CancelRestoresExactStartWithoutHistory()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var layer = workspace.ImportReferenceImage("pattern.png", Convert.ToBase64String([1]), 100, 50);
        var original = layer.ReferenceImage!;
        workspace.ClearHistory();

        Assert.True(workspace.BeginReferenceImageTransformEdit(layer.Id));
        Assert.True(workspace.UpdateReferenceImageTransform(layer.Id, -8, 12, 80, 40, -35));
        Assert.True(workspace.CancelReferenceImageTransformEdit());

        Assert.Equal(original, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.ToggleLayerLock(layer.Id));
        workspace.ClearHistory();
        Assert.False(workspace.BeginReferenceImageTransformEdit(layer.Id));
        Assert.False(workspace.UpdateReferenceImageTransform(layer.Id, 1, 1, 1, 1, 1));
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void ReferenceImageOpacityEdit_UsesContinuousValuesAndOneUndoPerGesture()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var layer = workspace.ImportReferenceImage("pattern.png", Convert.ToBase64String([1]), 100, 50);
        var originalOpacity = layer.ReferenceImage!.Opacity;
        workspace.ClearHistory();

        Assert.True(workspace.BeginReferenceImageTransformEdit(layer.Id));
        Assert.True(workspace.SetReferenceImageOpacity(layer.Id, 0.63));
        Assert.True(workspace.SetReferenceImageOpacity(layer.Id, 0.37));
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.CommitReferenceImageTransformEdit());
        Assert.Equal(0.37, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Opacity);

        Assert.True(workspace.Undo());
        Assert.Equal(originalOpacity, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Opacity);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.Redo());
        Assert.Equal(0.37, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Opacity);

        workspace.ClearHistory();
        Assert.True(workspace.BeginReferenceImageTransformEdit(layer.Id));
        Assert.True(workspace.SetReferenceImageOpacity(layer.Id, 0.12));
        Assert.True(workspace.CancelReferenceImageTransformEdit());
        Assert.Equal(0.37, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Opacity);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void ReferenceImageOpacity_ClampsAndRejectsLockedNonFiniteOrUnchangedValues()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var layer = workspace.ImportReferenceImage("pattern.png", Convert.ToBase64String([1]), 100, 50);
        workspace.ClearHistory();

        Assert.True(workspace.SetReferenceImageOpacity(layer.Id, -1));
        Assert.Equal(0, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Opacity);
        workspace.ClearHistory();
        Assert.False(workspace.SetReferenceImageOpacity(layer.Id, 0));
        Assert.False(workspace.SetReferenceImageOpacity(layer.Id, double.NaN));
        Assert.False(workspace.SetReferenceImageOpacity(layer.Id, double.PositiveInfinity));
        Assert.False(workspace.CanUndo);

        Assert.True(workspace.SetReferenceImageOpacity(layer.Id, 2));
        Assert.Equal(1, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Opacity);
        Assert.True(workspace.ToggleLayerLock(layer.Id));
        workspace.ClearHistory();
        Assert.False(workspace.BeginReferenceImageTransformEdit(layer.Id));
        Assert.False(workspace.SetReferenceImageOpacity(layer.Id, 0.5));
        Assert.Equal(1, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Opacity);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public async Task LayersPanel_OpacitySliderUpdatesLiveAndCommitsOneUndoOnFocusExit()
    {
        var stateBuilder = new Editor2DWorkspaceViewModel();
        stateBuilder.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var layer = stateBuilder.ImportReferenceImage("pattern.png", Convert.ToBase64String([1]), 100, 50);
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        editor.ApplyPersistedTwoDWorkspaceState(stateBuilder.State);
        var panel = await _ui.RunAsync(() => new Editor2DLayersPanel { DataContext = editor });
        await using var session = await _ui.MountAsync(panel, width: 520, height: 900);

        await _ui.RunAsync(() =>
        {
            var slider = _ui.FindByAutomationId<Slider>(panel, $"editor.reference.opacity.{layer.Id}");
            Assert.True(slider.IsEnabled);
            slider.Focus();
            slider.Value = 0.63;
            slider.Value = 0.37;

            Assert.Equal(0.37, editor.TwoDWorkspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Opacity);
            Assert.False(editor.CanUndoTwoDWorkspace);

            _ui.FindByAutomationId<Button>(panel, "editor.layers.import-reference").Focus();
        });
        await _ui.RunAsync(() => { });

        await _ui.RunAsync(() =>
        {
            Assert.True(editor.CanUndoTwoDWorkspace);
            Assert.True(editor.UndoTwoDWorkspace());
            Assert.Equal(0.65, editor.TwoDWorkspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Opacity);
            Assert.False(editor.CanUndoTwoDWorkspace);
        });
    }

    [Fact]
    public void ReferenceImageDepth_JsonRoundTripsFrontAndLegacyDefaultsBack()
    {
        var image = new Editor2DReferenceImage(
            "image",
            "pattern.png",
            Convert.ToBase64String([1]),
            10,
            5,
            1,
            2,
            10,
            5,
            TraceThreshold: 0.7,
            TraceTolerance: 20,
            TraceCornerSmoothness: 30,
            TracePathOptimization: 40,
            TraceSilhouetteOnly: true,
            Depth: Editor2DReferenceImageDepth.Front);

        var json = JsonSerializer.Serialize(image);
        Assert.Contains("\"depth\":\"front\"", json, StringComparison.Ordinal);
        Assert.Equal(image, JsonSerializer.Deserialize<Editor2DReferenceImage>(json));

        var legacy = JsonNode.Parse(json)!.AsObject();
        Assert.True(legacy.Remove("depth"));
        Assert.True(legacy.Remove("traceTolerance"));
        Assert.True(legacy.Remove("traceCornerSmoothness"));
        Assert.True(legacy.Remove("tracePathOptimization"));
        Assert.True(legacy.Remove("traceSilhouetteOnly"));
        var legacyImage = JsonSerializer.Deserialize<Editor2DReferenceImage>(legacy.ToJsonString())!;
        Assert.Equal(
            Editor2DReferenceImageDepth.Back,
            legacyImage.Depth);
        Assert.Equal(50, legacyImage.TraceTolerance);
        Assert.Equal(50, legacyImage.TraceCornerSmoothness);
        Assert.Equal(50, legacyImage.TracePathOptimization);
        Assert.False(legacyImage.TraceSilhouetteOnly);
    }

    [Fact]
    public void SetReferenceImageDepth_IsAtomicUndoableAndPreservesImageSettings()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var layer = workspace.ImportReferenceImage("pattern.png", Convert.ToBase64String([1]), 100, 50);
        Assert.True(workspace.UpdateReferenceImageTransform(layer.Id, 7, 9, 120, 60, 25));
        Assert.True(workspace.CalibrateReferenceImage(layer.Id, 240));
        Assert.True(workspace.SetReferenceImageOpacity(layer.Id, 0.35));
        var before = workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!;
        workspace.ClearHistory();

        Assert.True(workspace.SetReferenceImageDepth(layer.Id, Editor2DReferenceImageDepth.Front));
        Assert.Equal(before with { Depth = Editor2DReferenceImageDepth.Front },
            workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(before, workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.Redo());
        Assert.Equal(Editor2DReferenceImageDepth.Front,
            workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!.Depth);

        workspace.ClearHistory();
        Assert.False(workspace.SetReferenceImageDepth(layer.Id, Editor2DReferenceImageDepth.Front));
        Assert.False(workspace.SetReferenceImageDepth(layer.Id, (Editor2DReferenceImageDepth)42));
        Assert.False(workspace.SetReferenceImageDepth("missing", Editor2DReferenceImageDepth.Back));
        Assert.False(workspace.CanUndo);

        Assert.True(workspace.ToggleLayerLock(layer.Id));
        workspace.ClearHistory();
        Assert.False(workspace.SetReferenceImageDepth(layer.Id, Editor2DReferenceImageDepth.Back));
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void ReferenceImageDepth_InvalidNumericStateNormalizesBackWithoutHistory()
    {
        var invalidImage = new Editor2DReferenceImage(
            "image",
            "pattern.png",
            Convert.ToBase64String([1]),
            10,
            5,
            0,
            0,
            10,
            5,
            Depth: (Editor2DReferenceImageDepth)42);
        var workspace = new Editor2DWorkspaceViewModel();

        workspace.Apply(Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Layers =
            [
                new Editor2DLayer("geometry", "Geometry", []),
                new Editor2DLayer(
                    invalidImage.Id,
                    "Reference",
                    [],
                    Kind: Editor2DLayerKind.ReferenceImage,
                    ReferenceImage: invalidImage),
            ],
            ActiveLayerId = invalidImage.Id,
        }, recordHistory: false);

        Assert.Equal(Editor2DReferenceImageDepth.Back,
            workspace.Layers.Single(item => item.Id == invalidImage.Id).ReferenceImage!.Depth);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public async Task ReferenceImageTrace_PreviewsCancelsThenCommitsAtomically()
    {
        var tracer = new RecordingReferenceImageTraceService(
            [[new(40, 20), new(360, 30), new(200, 180)]]);
        var workspace = new Editor2DWorkspaceViewModel(tracer);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);

        var layer = workspace.ImportReferenceImage(
            "pattern.png",
            Convert.ToBase64String([1, 2, 3, 4]),
            pixelWidth: 400,
            pixelHeight: 200);

        Assert.Equal(Editor2DLayerKind.ReferenceImage, layer.Kind);
        Assert.Empty(layer.PathIds);
        Assert.Empty(workspace.Document.Paths);
        Assert.Equal(240, layer.ReferenceImage!.Width);
        Assert.Equal(120, layer.ReferenceImage.Height);
        Assert.Equal(0.6, layer.ReferenceImage.CalibrationUnitsPerPixel, 6);

        Assert.True(workspace.UpdateReferenceImageTransform(layer.Id, 10, 20, 120, 60, 45));
        Assert.True(workspace.CalibrateReferenceImage(layer.Id, 300));
        Assert.True(workspace.SetReferenceImageOpacity(layer.Id, 0.2));
        Assert.True(workspace.SetReferenceImageTraceThreshold(layer.Id, 0.8));
        Assert.True(workspace.SetReferenceImageTraceOptions(layer.Id, image => image with
        {
            TraceTolerance = 25,
            TraceCornerSmoothness = 35,
            TracePathOptimization = 45,
            TraceSilhouetteOnly = true,
        }));
        var transformed = workspace.Layers.Single(candidate => candidate.Id == layer.Id).ReferenceImage!;
        Assert.Equal(10, transformed.X);
        Assert.Equal(20, transformed.Y);
        Assert.Equal(300, transformed.Width);
        Assert.Equal(150, transformed.Height);
        Assert.Equal(45, transformed.RotationDegrees);
        Assert.Equal(0.2, transformed.Opacity, 6);
        Assert.Equal(0.8, transformed.TraceThreshold, 6);
        Assert.Equal(25, transformed.TraceTolerance);
        Assert.Equal(35, transformed.TraceCornerSmoothness);
        Assert.Equal(45, transformed.TracePathOptimization);
        Assert.True(transformed.TraceSilhouetteOnly);

        Assert.True(workspace.ToggleLayerLock(layer.Id));
        Assert.False(workspace.UpdateReferenceImageTransform(layer.Id, 0, 0, 1, 1, 0));
        Assert.False(workspace.BeginReferenceImageTrace(layer.Id));
        Assert.True(workspace.ToggleLayerLock(layer.Id));

        workspace.ClearHistory();
        Assert.True(workspace.BeginReferenceImageTrace(layer.Id));
        Assert.True(workspace.IsReferenceImageTracePreviewPending);
        await workspace.WaitForReferenceImageTracePreviewAsync();

        Assert.Equal(0.8, tracer.LastOptions!.Threshold, 6);
        Assert.Equal(25, tracer.LastOptions.Tolerance);
        Assert.Equal(35, tracer.LastOptions.CornerSmoothness);
        Assert.Equal(45, tracer.LastOptions.PathOptimization);
        Assert.True(tracer.LastOptions.SilhouetteOnly);
        Assert.Empty(workspace.Document.Paths);
        var preview = Assert.Single(workspace.ReferenceImageTracePreviewPaths);
        Assert.Equal("REFERENCE_TRACE", preview.EntityType);
        workspace.CancelReferenceImageTrace();
        Assert.Empty(workspace.ReferenceImageTracePreviewPaths);
        Assert.Empty(workspace.Document.Paths);
        Assert.True(workspace.Layers.Single(candidate => candidate.Id == layer.Id).IsVisible);
        Assert.False(workspace.CanUndo);

        Assert.True(workspace.BeginReferenceImageTrace(layer.Id));
        await workspace.WaitForReferenceImageTracePreviewAsync();
        var trace = workspace.CommitReferenceImageTrace();

        Assert.NotNull(trace);
        Assert.Equal("REFERENCE_TRACE", trace.EntityType);
        Assert.Equal(trace, Assert.Single(workspace.Document.Paths));
        Assert.Equal([trace.Id], workspace.SelectedPathIds);
        var restoredReferenceLayer = workspace.Layers.Single(candidate => candidate.Id == layer.Id);
        Assert.False(restoredReferenceLayer.IsVisible);
        Assert.Empty(restoredReferenceLayer.PathIds);
        Assert.NotNull(restoredReferenceLayer.ReferenceImage);
        var geometryLayer = Assert.Single(
            workspace.Layers, candidate => candidate.Name == "pattern_traced");
        Assert.False(workspace.AssignPathsToLayer(restoredReferenceLayer.Id, [trace.Id]));
        Assert.Contains(trace.Id, geometryLayer.PathIds);
        Assert.Equal(3, trace.Points.Count);
        Assert.True(workspace.Undo());
        Assert.Empty(workspace.Document.Paths);
        Assert.True(workspace.Layers.Single(candidate => candidate.Id == layer.Id).IsVisible);
        Assert.True(workspace.Redo());
        Assert.False(workspace.Layers.Single(candidate => candidate.Id == layer.Id).IsVisible);

        Assert.True(workspace.ToggleLayerVisibility(layer.Id));
        Assert.True(workspace.BeginReferenceImageTrace(layer.Id));
        await workspace.WaitForReferenceImageTracePreviewAsync();
        Assert.NotNull(workspace.CommitReferenceImageTrace());
        var reusedTarget = Assert.Single(workspace.Layers, candidate => candidate.Name == "pattern_traced");
        Assert.Equal(2, reusedTarget.PathIds.Count);
    }

    [Fact]
    public async Task ReferenceImageTrace_AppliesOnlyLatestRequestAndCancelRejectsLateResult()
    {
        var tracer = new ControllableReferenceImageTraceService();
        var workspace = new Editor2DWorkspaceViewModel(tracer);
        var layer = workspace.ImportReferenceImage("pattern.png", "image", 100, 100);
        workspace.ClearHistory();

        Assert.True(workspace.BeginReferenceImageTrace(layer.Id));
        var firstTask = workspace.WaitForReferenceImageTracePreviewAsync();
        var first = await tracer.NextRequestAsync();
        Assert.True(workspace.IsReferenceImageTracePreviewPending);
        Assert.Null(workspace.CommitReferenceImageTrace());

        Assert.True(workspace.SetReferenceImageTraceThreshold(layer.Id, 0.8));
        var secondTask = workspace.WaitForReferenceImageTracePreviewAsync();
        var second = await tracer.NextRequestAsync();
        first.Complete([[new(0, 0), new(10, 0), new(0, 10)]]);

        Assert.False(await firstTask);
        Assert.Empty(workspace.ReferenceImageTracePreviewPaths);
        Assert.True(workspace.IsReferenceImageTracePreviewPending);

        second.Complete([[new(20, 20), new(80, 20), new(20, 80)]]);
        Assert.True(await secondTask);
        var latest = Assert.Single(workspace.ReferenceImageTracePreviewPaths);
        Assert.Equal(new Editor2DPoint(-30, -30), latest.Points[0]);
        Assert.False(workspace.IsReferenceImageTracePreviewPending);

        Assert.True(workspace.RefreshReferenceImageTracePreview());
        var cancelledTask = workspace.WaitForReferenceImageTracePreviewAsync();
        var cancelled = await tracer.NextRequestAsync();
        workspace.CancelReferenceImageTrace();
        cancelled.Complete([[new(0, 0), new(99, 0), new(0, 99)]]);

        Assert.False(await cancelledTask);
        Assert.False(workspace.HasReferenceImageTraceSession);
        Assert.False(workspace.IsReferenceImageTracePreviewPending);
        Assert.Empty(workspace.ReferenceImageTracePreviewPaths);
        Assert.Empty(workspace.Document.Paths);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void ImageMetadata_ReadsPngDimensionsForImportSizing()
    {
        var header = new byte[24];
        header[0] = 0x89;
        header[1] = 0x50;
        header[2] = 0x4E;
        header[3] = 0x47;
        header[16] = 0x00;
        header[17] = 0x00;
        header[18] = 0x02;
        header[19] = 0x80;
        header[20] = 0x00;
        header[21] = 0x00;
        header[22] = 0x01;
        header[23] = 0xE0;

        Assert.True(Editor2DReferenceImageMetadata.TryReadPixelSize(header, out var width, out var height));
        Assert.Equal(640, width);
        Assert.Equal(480, height);
    }

    [Fact]
    public void ImageMetadata_ReadsWebpExtendedHeaderDimensions()
    {
        var header = new byte[30];
        "RIFF"u8.CopyTo(header);
        "WEBP"u8.CopyTo(header.AsSpan(8));
        "VP8X"u8.CopyTo(header.AsSpan(12));
        header[24] = 127;
        header[25] = 2;
        header[26] = 0;
        header[27] = 239;
        header[28] = 1;
        header[29] = 0;

        Assert.True(Editor2DReferenceImageMetadata.TryReadPixelSize(header, out var width, out var height));
        Assert.Equal(640, width);
        Assert.Equal(496, height);
    }

    [Fact]
    public void ImageMetadata_ReadsLittleEndianTiffDimensions()
    {
        var header = new byte[38];
        header[0] = (byte)'I';
        header[1] = (byte)'I';
        header[2] = 42;
        header[4] = 8;
        header[8] = 2;

        WriteTiffLongEntry(header, 10, 256, 640);
        WriteTiffLongEntry(header, 22, 257, 480);

        Assert.True(Editor2DReferenceImageMetadata.TryReadPixelSize(header, out var width, out var height));
        Assert.Equal(640, width);
        Assert.Equal(480, height);
    }

    [Fact]
    public void ImageMetadata_ReadsIsoBmffIspeDimensions()
    {
        var header = new byte[20];
        header[4] = (byte)'i';
        header[5] = (byte)'s';
        header[6] = (byte)'p';
        header[7] = (byte)'e';
        header[12] = 0x00;
        header[13] = 0x00;
        header[14] = 0x02;
        header[15] = 0x80;
        header[16] = 0x00;
        header[17] = 0x00;
        header[18] = 0x01;
        header[19] = 0xE0;

        Assert.True(Editor2DReferenceImageMetadata.TryReadPixelSize(header, out var width, out var height));
        Assert.Equal(640, width);
        Assert.Equal(480, height);
    }

    private static void WriteTiffLongEntry(byte[] data, int offset, ushort tag, uint value)
    {
        data[offset] = (byte)tag;
        data[offset + 1] = (byte)(tag >> 8);
        data[offset + 2] = 4;
        data[offset + 4] = 1;
        data[offset + 8] = (byte)value;
        data[offset + 9] = (byte)(value >> 8);
        data[offset + 10] = (byte)(value >> 16);
        data[offset + 11] = (byte)(value >> 24);
    }

    [Fact]
    public void ReferenceImageBackgroundRemoval_IsReversibleAndPersistsOriginal()
    {
        var remover = new RecordingReferenceImageBackgroundRemovalService("removed");
        var workspace = new Editor2DWorkspaceViewModel(null, remover);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var layer = workspace.ImportReferenceImage("pattern.png", "original", 10, 10);

        Assert.True(workspace.RemoveReferenceImageBackground(layer.Id));
        var removed = workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!;
        Assert.Equal("removed", removed.DataBase64);
        Assert.Equal("original", removed.OriginalDataBase64);
        Assert.True(removed.BackgroundRemoved);

        Assert.True(workspace.RestoreReferenceImageBackground(layer.Id));
        var restored = workspace.Layers.Single(item => item.Id == layer.Id).ReferenceImage!;
        Assert.Equal("original", restored.DataBase64);
        Assert.Null(restored.OriginalDataBase64);
        Assert.False(restored.BackgroundRemoved);
    }

    [Fact]
    public void AvaloniaBackgroundRemoval_ClearsConnectedBorderColor()
    {
        using var bitmap = new SKBitmap(5, 5);
        bitmap.Erase(SKColors.White);
        bitmap.SetPixel(2, 2, SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var output = new AvaloniaReferenceImageBackgroundRemovalService()
            .RemoveBackground(Convert.ToBase64String(data.ToArray()));

        Assert.NotNull(output);
        var outputBytes = Convert.FromBase64String(output!);
        using var decoded = SKBitmap.Decode(outputBytes);
        Assert.Equal(0, decoded.GetPixel(0, 0).Alpha);
        Assert.Equal(255, decoded.GetPixel(2, 2).Alpha);
    }

    [Fact]
    public void AvaloniaTracer_UsesThresholdedPixelsInsteadOfTheImageBounds()
    {
        var fixture = RepositoryFile(
            "Pathstitch", "Pathstitch", "Assets.xcassets", "StchDocument.imageset", "stchdoc_512.png");
        var tracer = new AvaloniaReferenceImageTraceService();
        var contours = tracer.TraceContours(
            Convert.ToBase64String(File.ReadAllBytes(fixture)),
            new Editor2DReferenceImageTraceOptions(Threshold: 0.45));

        Assert.NotEmpty(contours);
        Assert.Contains(contours, contour => contour.Count > 4);
        Assert.All(contours.SelectMany(contour => contour), point =>
        {
            Assert.InRange(point.X, 0, 512);
            Assert.InRange(point.Y, 0, 512);
        });
    }

    [Fact]
    public void AvaloniaTracer_SilhouetteUsesAlphaAndReturnsLargestContour()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(8, 5, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(new SKRect(0, 0, 4, 4), paint);
        canvas.DrawRect(new SKRect(6, 0, 8, 2), paint);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var tracer = new AvaloniaReferenceImageTraceService();

        var thresholded = tracer.TraceContours(
            Convert.ToBase64String(data.ToArray()),
            new Editor2DReferenceImageTraceOptions(Threshold: 0.1, CornerSmoothness: 0));
        var silhouette = tracer.TraceContours(
            Convert.ToBase64String(data.ToArray()),
            new Editor2DReferenceImageTraceOptions(
                Threshold: 0.1,
                CornerSmoothness: 0,
                SilhouetteOnly: true));

        Assert.Empty(thresholded);
        Assert.Single(silhouette);
        Assert.True(silhouette[0].Max(point => point.X) <= 4);
    }

    [Fact]
    public void CanvasReferenceImageRenderContract_UsesTransformSizeAndDedicatedBinding()
    {
        var image = new Editor2DReferenceImage(
            "image",
            "pattern.png",
            Convert.ToBase64String([1]),
            100,
            50,
            X: 12,
            Y: -4,
            Width: 100,
            Height: 50,
            RotationDegrees: 30,
            Opacity: 0.4);

        var rect = DxfPreviewCanvas.GetReferenceImageLocalRect(image, zoom: 3);
        var bounds = DxfPreviewCanvas.MeasureReferenceImageBounds([image]);
        var canvas = new DxfPreviewCanvas { ReferenceImages = [image] };

        Assert.Equal(new Rect(-150, -75, 300, 150), rect);
        Assert.Equal(image.X, bounds.CenterX, 6);
        Assert.Equal(image.Y, bounds.CenterY, 6);
        Assert.True(bounds.Width > image.Width);
        Assert.True(bounds.Height > image.Height);
        Assert.Equal(image, Assert.Single(canvas.ReferenceImages));
        var view = ReadPage("Editor2DView.axaml");
        var canvasSource = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");
        Assert.Contains("ReferenceImages=\"{Binding TwoDReferenceImages}\"", view, StringComparison.Ordinal);
        var paperDraw = canvasSource.IndexOf("DrawPaperBounds(context, size, Document.Bounds)", StringComparison.Ordinal);
        var backDraw = canvasSource.IndexOf(
            "DrawReferenceImages(context, size, Editor2DReferenceImageDepth.Back)",
            StringComparison.Ordinal);
        var geometryDraw = canvasSource.IndexOf("DrawPaths(context, size, visiblePaths)", StringComparison.Ordinal);
        var frontDraw = canvasSource.IndexOf(
            "DrawReferenceImages(context, size, Editor2DReferenceImageDepth.Front)",
            StringComparison.Ordinal);
        var referenceGizmoDraw = canvasSource.IndexOf("DrawReferenceImageGizmo(context, size)", StringComparison.Ordinal);
        var firstOverlayDraw = canvasSource.IndexOf("DrawTranslationGizmo(context, size)", StringComparison.Ordinal);
        Assert.True(
            paperDraw >= 0
            && paperDraw < backDraw
            && backDraw < geometryDraw
            && geometryDraw < frontDraw
            && frontDraw < referenceGizmoDraw
            && referenceGizmoDraw < firstOverlayDraw);
        var images = new[]
        {
            image with { Id = "back-1", Depth = Editor2DReferenceImageDepth.Back },
            image with { Id = "front", Depth = Editor2DReferenceImageDepth.Front },
            image with { Id = "back-2", Depth = Editor2DReferenceImageDepth.Back },
        };
        Assert.Equal(["back-1", "back-2"],
            DxfPreviewCanvas.ReferenceImagesAtDepth(images, Editor2DReferenceImageDepth.Back).Select(item => item.Id));
        Assert.Equal(["front"],
            DxfPreviewCanvas.ReferenceImagesAtDepth(images, Editor2DReferenceImageDepth.Front).Select(item => item.Id));
        Assert.Contains("context.DrawImage(", canvasSource, StringComparison.Ordinal);
        Assert.Contains("context.PushOpacity", canvasSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LayersPanel_ExposesImportTransformCalibrationOpacityLockAndTraceControls()
    {
        var panel = ReadPage("Editor2DLayersPanel.axaml");

        Assert.Contains("editor.layers.import-reference", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceMoveLeftClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceScaleUpClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceRotateClicked", panel, StringComparison.Ordinal);
        Assert.Contains("editor.reference.depth.back.{0}", panel, StringComparison.Ordinal);
        Assert.Contains("editor.reference.depth.front.{0}", panel, StringComparison.Ordinal);
        Assert.Contains("GroupName=\"{Binding Id}\"", panel, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding !IsLocked}\"", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceDepthBackClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceDepthFrontClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceFadeClicked", panel, StringComparison.Ordinal);
        Assert.Contains("editor.reference.opacity.{0}", panel, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding ReferenceImage.Opacity, Mode=OneWay}\"", panel, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocusWithin || slider.IsPointerOver", ReadPage("Editor2DLayersPanel.axaml.cs"), StringComparison.Ordinal);
        Assert.Contains("OnReferenceOpacitySliderPointerReleased", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceOpacitySliderValueChanged", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceOpacity10Clicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceOpacity25Clicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceOpacity50Clicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceOpacity75Clicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceOpacity100Clicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnToggleLockClicked", panel, StringComparison.Ordinal);
        Assert.Contains("TwoDReferenceCalibrationWidthText", panel, StringComparison.Ordinal);
        Assert.Contains("OnCalibrateReferenceImageClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceThresholdUpClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnTraceReferenceImageClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnCommitReferenceTraceClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnCancelReferenceTraceClicked", panel, StringComparison.Ordinal);
        Assert.Contains("TwoDReferenceTraceCornerSmoothness", panel, StringComparison.Ordinal);
        Assert.Contains("TwoDReferenceTracePathOptimization", panel, StringComparison.Ordinal);
        Assert.Contains("editor.reference.trace.progress", panel, StringComparison.Ordinal);
        Assert.Contains("IsTwoDReferenceTraceRunning", panel, StringComparison.Ordinal);
        Assert.Contains("TracePreviewPaths=\"{Binding TwoDWorkspace.ReferenceImageTracePreviewPaths}\"", ReadPage("Editor2DView.axaml"), StringComparison.Ordinal);
        var canvasSource = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");
        var viewSource = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DView.axaml.cs");
        Assert.Contains("ReferenceImageTransformStarted?.Invoke", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ReferenceImageTransformCompleted?.Invoke", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ReferenceImageTransformCanceled?.Invoke", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ReferenceImageTransformEditActive=\"{Binding TwoDReferenceImageTransformEditActive}\"", ReadPage("Editor2DView.axaml"), StringComparison.Ordinal);
        Assert.Contains("ReferenceImageTransformEditActive && e.Key is Key.Enter or Key.Escape", canvasSource, StringComparison.Ordinal);
        Assert.Contains("if (!ReferenceImageTransformEditActive || ActiveReferenceImage", canvasSource, StringComparison.Ordinal);
        Assert.Contains("BeginTwoDReferenceImageTransform", viewSource, StringComparison.Ordinal);
        Assert.Contains("CommitTwoDReferenceImageTransform", viewSource, StringComparison.Ordinal);
        Assert.Contains("CancelTwoDReferenceImageTransform", viewSource, StringComparison.Ordinal);
        var panelCode = ReadPage("Editor2DLayersPanel.axaml.cs");
        Assert.Contains("BeginTwoDReferenceImageTransform", panelCode, StringComparison.Ordinal);
        Assert.Contains("CommitTwoDReferenceImageTransform", panelCode, StringComparison.Ordinal);
        Assert.Contains("SetTwoDReferenceImageOpacity", panelCode, StringComparison.Ordinal);
        var layerViewModel = ReadRepositoryFile("src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.Layers.cs");
        Assert.Contains("RefreshTwoDReferenceImagePreviewFacade", layerViewModel, StringComparison.Ordinal);
        Assert.Contains("if (_twoDWorkspace.IsReferenceImageTransformEditActive)", layerViewModel, StringComparison.Ordinal);
        Assert.Contains("OnRemoveReferenceBackgroundClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnRestoreReferenceBackgroundClicked", panel, StringComparison.Ordinal);
    }

    private static string ReadPage(string fileName)
        => ReadRepositoryFile("src", "Pathstitch.App", "Pages", fileName);

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

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(pathParts)}");
    }

    private sealed class RecordingReferenceImageTraceService(
        IReadOnlyList<IReadOnlyList<Editor2DPoint>> contours) : IReferenceImageTraceService
    {
        public Editor2DReferenceImageTraceOptions? LastOptions { get; private set; }

        public IReadOnlyList<IReadOnlyList<Editor2DPoint>> TraceContours(
            string imageDataBase64,
            Editor2DReferenceImageTraceOptions options)
        {
            Assert.False(string.IsNullOrWhiteSpace(imageDataBase64));
            LastOptions = options;
            return contours;
        }
    }

    private sealed class ControllableReferenceImageTraceService : IReferenceImageTraceService
    {
        private readonly SemaphoreSlim _available = new(0);
        private readonly Queue<TraceRequest> _requests = new();
        private readonly object _gate = new();

        public IReadOnlyList<IReadOnlyList<Editor2DPoint>> TraceContours(
            string imageDataBase64,
            Editor2DReferenceImageTraceOptions options)
        {
            var request = new TraceRequest(options);
            lock (_gate)
                _requests.Enqueue(request);
            _available.Release();
            return request.Result.Task.GetAwaiter().GetResult();
        }

        public async Task<TraceRequest> NextRequestAsync()
        {
            if (!await _available.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Trace request did not start.");
            lock (_gate)
                return _requests.Dequeue();
        }
    }

    private sealed record TraceRequest(Editor2DReferenceImageTraceOptions Options)
    {
        public TaskCompletionSource<IReadOnlyList<IReadOnlyList<Editor2DPoint>>> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(IReadOnlyList<IReadOnlyList<Editor2DPoint>> contours)
            => Result.TrySetResult(contours);
    }

    private sealed class RecordingReferenceImageBackgroundRemovalService(string result)
        : IReferenceImageBackgroundRemovalService
    {
        public string? RemoveBackground(string imageDataBase64)
        {
            Assert.Equal("original", imageDataBase64);
            return result;
        }
    }

    private static string RepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(Path.Combine(pathParts));
    }
}
