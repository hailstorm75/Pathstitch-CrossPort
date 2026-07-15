using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class SvgPreviewDocumentTests
{
    [Fact]
    public async Task LoadPreviewDocumentAsync_CanConsolidateNarrowClosedStroke()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-{Guid.NewGuid():N}.svg");
        try
        {
            await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"20\" height=\"2\" /></svg>");
            SvgPreviewDocumentParser.ConsolidateStrokes = true;
            SvgPreviewDocumentParser.ImportThickness = 0;
            var document = await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path);
            var stroke = Assert.Single(document!.Paths);
            Assert.Equal("POLYLINE", stroke.EntityType);
            Assert.False(stroke.IsClosed);
            Assert.Equal(2, stroke.Points.Count);
        }
        finally
        {
            SvgPreviewDocumentParser.ConsolidateStrokes = false;
            SvgPreviewDocumentParser.ImportThickness = 0;
            File.Delete(path);
        }
    }
    [Fact]
    public async Task LoadPreviewDocumentAsync_ParsesCommonSvgPrimitives()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-{Guid.NewGuid():N}.svg");
        await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"><line x1=\"0\" y1=\"1\" x2=\"4\" y2=\"5\" /><rect x=\"1\" y=\"2\" width=\"10\" height=\"5\" /><circle cx=\"4\" cy=\"6\" r=\"2\" /></svg>");

        try
        {
            SvgPreviewDocumentParser.ConsolidateStrokes = false;
            SvgPreviewDocumentParser.ImportThickness = 0;
            var document = await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path);

            Assert.NotNull(document);
            Assert.Equal(3, document.Paths.Count);
            Assert.Contains(document.Paths, path => path.EntityType == "LINE");
            Assert.Contains(document.Paths, path => path.EntityType == "RECTANGLE");
            Assert.Contains(document.Paths, path => path.EntityType == "CIRCLE");
        }
        finally
        {
            SvgPreviewDocumentParser.ConsolidateStrokes = false;
            SvgPreviewDocumentParser.ImportThickness = 0;
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPreviewDocumentAsync_CanPreserveExplicitSvgFills()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-{Guid.NewGuid():N}.svg");
        try
        {
            await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"10\" height=\"5\" fill=\"#123456\" /><circle cx=\"4\" cy=\"4\" r=\"2\" style=\"fill:none\" /></svg>");
            SvgPreviewDocumentParser.FillMode = "preserve";
            SvgPreviewDocumentParser.ImportThickness = 0;
            var document = await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path);
            Assert.True(document!.Paths.Single(path => path.EntityType == "RECTANGLE").IsFilled);
            Assert.False(document.Paths.Single(path => path.EntityType == "CIRCLE").IsFilled);
        }
        finally
        {
            SvgPreviewDocumentParser.FillMode = "strokes";
            SvgPreviewDocumentParser.ImportThickness = 0;
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPreviewDocumentAsync_ThickensOpenSvgStrokes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-{Guid.NewGuid():N}.svg");
        try
        {
            await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"><line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"0\" /></svg>");
            SvgPreviewDocumentParser.ImportThickness = 4.0;
            var document = await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path);
            var outline = Assert.Single(document!.Paths);
            Assert.True(outline.IsClosed);
            Assert.Equal("LWPOLYLINE", outline.EntityType);
            Assert.Equal(4.0, outline.Points.Max(point => point.Y) - outline.Points.Min(point => point.Y), 6);
        }
        finally
        {
            SvgPreviewDocumentParser.ImportThickness = 0;
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPreviewDocumentAsync_PreservesPhysicalSizeFromViewBox()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-{Guid.NewGuid():N}.svg");
        await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1in\" height=\"0.5in\" viewBox=\"10 20 100 50\"><rect x=\"10\" y=\"20\" width=\"100\" height=\"50\" /></svg>");

        try
        {
            var document = await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path);
            var rectangle = Assert.Single(document!.Paths);
            Assert.Equal(25.4, rectangle.Points.Max(point => point.X) - rectangle.Points.Min(point => point.X), 6);
            Assert.Equal(12.7, rectangle.Points.Max(point => point.Y) - rectangle.Points.Min(point => point.Y), 6);
            Assert.Equal(0, rectangle.Points.Min(point => point.X), 6);
            Assert.Equal(0, rectangle.Points.Min(point => point.Y), 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPreviewDocumentAsync_ParsesRelativeLinePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-{Guid.NewGuid():N}.svg");
        try
        {
            await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M 1 2 l 3 4 h 2 v -1\" /></svg>");
            SvgPreviewDocumentParser.ImportThickness = 0;
            var document = await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path);

            var previewPath = Assert.Single(document!.Paths);
            Assert.Equal("PATH", previewPath.EntityType);
            Assert.False(previewPath.IsClosed);
            Assert.Equal(4, previewPath.Points.Count);
            Assert.Equal(1, previewPath.Points[0].X, 6);
            Assert.Equal(2, previewPath.Points[0].Y, 6);
            Assert.Equal(4, previewPath.Points[1].X, 6);
            Assert.Equal(6, previewPath.Points[1].Y, 6);
            Assert.Equal(6, previewPath.Points[2].X, 6);
            Assert.Equal(6, previewPath.Points[2].Y, 6);
            Assert.Equal(6, previewPath.Points[3].X, 6);
            Assert.Equal(5, previewPath.Points[3].Y, 6);
        }
        finally
        {
            SvgPreviewDocumentParser.ImportThickness = 0;
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPreviewDocumentAsync_ParsesClosedFilledPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-{Guid.NewGuid():N}.svg");
        try
        {
            await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M 0 0 L 10 0 L 10 10 Z\" fill=\"#ff0000\" /></svg>");
            SvgPreviewDocumentParser.FillMode = "preserve";
            SvgPreviewDocumentParser.ImportThickness = 0;
            var document = await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path);

            var previewPath = Assert.Single(document!.Paths);
            Assert.True(previewPath.IsClosed);
            Assert.True(previewPath.IsFilled);
            Assert.Equal(4, previewPath.Points.Count);
            Assert.Equal(0, previewPath.Points[0].X, 6);
            Assert.Equal(0, previewPath.Points[0].Y, 6);
            Assert.Equal(10, previewPath.Points[1].X, 6);
            Assert.Equal(0, previewPath.Points[1].Y, 6);
            Assert.Equal(10, previewPath.Points[2].X, 6);
            Assert.Equal(10, previewPath.Points[2].Y, 6);
            Assert.Equal(0, previewPath.Points[3].X, 6);
            Assert.Equal(0, previewPath.Points[3].Y, 6);
        }
        finally
        {
            SvgPreviewDocumentParser.FillMode = "strokes";
            SvgPreviewDocumentParser.ImportThickness = 0;
            File.Delete(path);
        }
    }
}
