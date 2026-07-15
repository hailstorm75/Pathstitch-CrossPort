using Avalonia;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Pathstitch.App.Controls;
using Pathstitch.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Tests;

public sealed class ReferenceImageWorkflowTests
{
    [Fact]
    public void ImportReferenceImage_RemainsNonGeometryUntilExplicitTrace()
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
        var transformed = workspace.Layers.Single(candidate => candidate.Id == layer.Id).ReferenceImage!;
        Assert.Equal(10, transformed.X);
        Assert.Equal(20, transformed.Y);
        Assert.Equal(300, transformed.Width);
        Assert.Equal(150, transformed.Height);
        Assert.Equal(45, transformed.RotationDegrees);
        Assert.Equal(0.2, transformed.Opacity, 6);
        Assert.Equal(0.8, transformed.TraceThreshold, 6);

        Assert.True(workspace.ToggleLayerLock(layer.Id));
        Assert.False(workspace.UpdateReferenceImageTransform(layer.Id, 0, 0, 1, 1, 0));
        Assert.Null(workspace.TraceReferenceImageBounds(layer.Id));
        Assert.True(workspace.ToggleLayerLock(layer.Id));

        var trace = workspace.TraceReferenceImageBounds(layer.Id);

        Assert.NotNull(trace);
        Assert.Equal(0.8, tracer.LastThreshold, 6);
        Assert.Equal("REFERENCE_TRACE", trace.EntityType);
        Assert.Equal(trace, Assert.Single(workspace.Document.Paths));
        Assert.Equal([trace.Id], workspace.SelectedPathIds);
        var restoredReferenceLayer = workspace.Layers.Single(candidate => candidate.Id == layer.Id);
        Assert.Empty(restoredReferenceLayer.PathIds);
        Assert.NotNull(restoredReferenceLayer.ReferenceImage);
        var geometryLayer = Assert.Single(
            workspace.Layers,
            candidate => candidate.Kind == Editor2DLayerKind.Geometry && candidate.PathIds.Contains(trace.Id));
        Assert.False(workspace.AssignPathsToLayer(restoredReferenceLayer.Id, [trace.Id]));
        Assert.Contains(trace.Id, geometryLayer.PathIds);
        Assert.Equal(3, trace.Points.Count);
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
            threshold: 0.45);

        Assert.NotEmpty(contours);
        Assert.Contains(contours, contour => contour.Count > 4);
        Assert.All(contours.SelectMany(contour => contour), point =>
        {
            Assert.InRange(point.X, 0, 512);
            Assert.InRange(point.Y, 0, 512);
        });
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
        Assert.Contains("DrawReferenceImages(context, size)", canvasSource, StringComparison.Ordinal);
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
        Assert.Contains("OnReferenceFadeClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnToggleLockClicked", panel, StringComparison.Ordinal);
        Assert.Contains("TwoDReferenceCalibrationWidthText", panel, StringComparison.Ordinal);
        Assert.Contains("OnCalibrateReferenceImageClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnReferenceThresholdUpClicked", panel, StringComparison.Ordinal);
        Assert.Contains("OnTraceReferenceImageClicked", panel, StringComparison.Ordinal);
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
        public double LastThreshold { get; private set; }

        public IReadOnlyList<IReadOnlyList<Editor2DPoint>> TraceContours(
            string imageDataBase64,
            double threshold)
        {
            Assert.False(string.IsNullOrWhiteSpace(imageDataBase64));
            LastThreshold = threshold;
            return contours;
        }
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
