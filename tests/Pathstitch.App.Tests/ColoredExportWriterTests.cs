using Domain.App.Models;
using Pathstitch.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Tests;

public sealed class ColoredExportWriterTests
{
    [Fact]
    public void PngWriter_UsesPerPathMetadataColorsAndBlackFallback()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-colored-{Guid.NewGuid():N}.png");
        try
        {
            var geometry = ColorTestDocument();
            var export = new Editor2DExportDocument(
                geometry,
                new Dictionary<string, Editor2DExportPathMetadata>
                {
                    ["red"] = new("Cut", "#FF0000"),
                    ["blue"] = new("Score", "#0000FF"),
                    ["construction"] = new("Wrong", "#00FF00"),
                });

            PngOutputDocumentWriter.Save(
                outputPath,
                export,
                new Editor2DExportOptions(SvgStrokeWidth: 0.1, PngLongestEdge: 256, PngTransparent: true));

            using var bitmap = SKBitmap.Decode(outputPath);
            AssertColor(bitmap.GetPixel(128, 205), 255, 0, 0);
            AssertColor(bitmap.GetPixel(128, 51), 0, 0, 255);
            AssertColor(bitmap.GetPixel(128, 128), 128, 128, 128);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void PdfWriter_EmitsPerPathStrokeAndFillColorsWithBlackFallback()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-colored-{Guid.NewGuid():N}.pdf");
        try
        {
            var export = new Editor2DExportDocument(
                ColorTestDocument(),
                new Dictionary<string, Editor2DExportPathMetadata>
                {
                    ["red"] = new("Cut", "#FF0000"),
                    ["blue"] = new("Score", "#0000FF"),
                    ["construction"] = new("Wrong", "#00FF00"),
                });

            PdfOutputDocumentWriter.Save(outputPath, export);

            var pdf = File.ReadAllText(outputPath);
            Assert.Contains("1 0 0 RG 1 0 0 rg", pdf, StringComparison.Ordinal);
            Assert.Contains("0 0 1 RG 0 0 1 rg", pdf, StringComparison.Ordinal);
            Assert.Contains("0.502 0.502 0.502 RG 0.502 0.502 0.502 rg", pdf, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void LegacyWriterOverloadsRemainBlackWithoutMetadata()
    {
        var pngPath = Path.Combine(Path.GetTempPath(), $"pathstitch-legacy-{Guid.NewGuid():N}.png");
        var pdfPath = Path.Combine(Path.GetTempPath(), $"pathstitch-legacy-{Guid.NewGuid():N}.pdf");
        try
        {
            var geometry = ColorTestDocument();
            PngOutputDocumentWriter.Save(
                pngPath,
                geometry,
                new Editor2DExportOptions(SvgStrokeWidth: 0.1, PngLongestEdge: 256, PngTransparent: true));
            PdfOutputDocumentWriter.Save(pdfPath, geometry);

            using var bitmap = SKBitmap.Decode(pngPath);
            AssertColor(bitmap.GetPixel(128, 51), 0, 0, 0);
            Assert.DoesNotContain("1 0 0 RG", File.ReadAllText(pdfPath), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(pngPath);
            File.Delete(pdfPath);
        }
    }

    private static Editor2DPreviewDocument ColorTestDocument()
        => new(
            [
                new Editor2DPreviewPath("red", "LINE", [new(0, 2), new(10, 2)], false),
                new Editor2DPreviewPath("construction", "LINE", [new(0, 5), new(10, 5)], false, IsConstruction: true),
                new Editor2DPreviewPath("blue", "LINE", [new(0, 8), new(10, 8)], false),
            ],
            new Editor2DBounds(0, 0, 10, 10),
            new Dictionary<string, int> { ["LINE"] = 3 },
            []);

    private static void AssertColor(SKColor actual, byte red, byte green, byte blue)
    {
        Assert.True(actual.Alpha > 0);
        Assert.InRange(actual.Red, (byte)Math.Max(0, red - 2), (byte)Math.Min(255, red + 2));
        Assert.InRange(actual.Green, (byte)Math.Max(0, green - 2), (byte)Math.Min(255, green + 2));
        Assert.InRange(actual.Blue, (byte)Math.Max(0, blue - 2), (byte)Math.Min(255, blue + 2));
    }
}
