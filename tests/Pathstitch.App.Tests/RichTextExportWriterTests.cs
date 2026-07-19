using Domain.App.Models;
using Pathstitch.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Tests;

public sealed class RichTextExportWriterTests
{
    [Fact]
    public void SvgWriter_PreservesRichMultilineUnicodeText()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-rich-text-{Guid.NewGuid():N}.svg");
        var document = RichTextDocument();

        try
        {
            SvgOutputDocumentWriter.Save(outputPath, document);

            var svg = File.ReadAllText(outputPath);
            Assert.Contains("font-size=\"5\"", svg, StringComparison.Ordinal);
            Assert.Contains("font-family=\"Noto Sans\"", svg, StringComparison.Ordinal);
            Assert.Contains("font-weight=\"bold\"", svg, StringComparison.Ordinal);
            Assert.Contains("font-style=\"italic\"", svg, StringComparison.Ordinal);
            Assert.Contains("text-decoration=\"underline\"", svg, StringComparison.Ordinal);
            Assert.Contains("letter-spacing=\"0.75\"", svg, StringComparison.Ordinal);
            Assert.Contains("transform=\"translate(10 -20) rotate(-90) scale(2 1) translate(-10 20)\"", svg, StringComparison.Ordinal);
            Assert.Contains("<tspan x=\"10\" y=\"-26\">Größe 東京</tspan>", svg, StringComparison.Ordinal);
            Assert.Contains("<tspan x=\"10\" y=\"-20\">Second &amp; &lt;safe&gt;</tspan>", svg, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void PngWriter_RendersUnicodeTextWithoutGeometryPoints()
    {
        var document = RichTextDocument();
        var bytes = PngOutputDocumentWriter.Render(
            new Editor2DExportDocument(document, new Dictionary<string, Editor2DExportPathMetadata>()),
            new Editor2DExportOptions(PngLongestEdge: 400, PngTransparent: true));

        using var bitmap = SKBitmap.Decode(bytes);
        var inkPixels = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                    inkPixels++;
            }
        }

        Assert.True(inkPixels > 100, $"Expected rendered glyph ink, found {inkPixels} non-transparent pixels.");
    }

    [Fact]
    public void SvgAndPngWriters_PreserveMirroredTextTransform()
    {
        var svgPath = Path.Combine(Path.GetTempPath(), $"pathstitch-mirrored-text-{Guid.NewGuid():N}.svg");
        var positive = MirroredTextDocument(widthFactor: 2);
        var mirrored = MirroredTextDocument(widthFactor: -2);
        try
        {
            SvgOutputDocumentWriter.Save(svgPath, mirrored);
            var svg = File.ReadAllText(svgPath);
            Assert.Contains("scale(-2 1)", svg, StringComparison.Ordinal);

            using var positiveBitmap = SKBitmap.Decode(PngOutputDocumentWriter.Render(
                new Editor2DExportDocument(positive, new Dictionary<string, Editor2DExportPathMetadata>()),
                new Editor2DExportOptions(PngLongestEdge: 400, PngTransparent: true)));
            using var mirroredBitmap = SKBitmap.Decode(PngOutputDocumentWriter.Render(
                new Editor2DExportDocument(mirrored, new Dictionary<string, Editor2DExportPathMetadata>()),
                new Editor2DExportOptions(PngLongestEdge: 400, PngTransparent: true)));

            var positiveCentroid = AlphaCentroidX(positiveBitmap);
            var mirroredCentroid = AlphaCentroidX(mirroredBitmap);
            Assert.True(
                mirroredCentroid < positiveCentroid - 10.0,
                $"Expected mirrored ink left of positive ink, got {mirroredCentroid:R} and {positiveCentroid:R}.");
        }
        finally
        {
            File.Delete(svgPath);
        }
    }

    private static double AlphaCentroidX(SKBitmap bitmap)
    {
        double weightedX = 0.0;
        double totalAlpha = 0.0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var alpha = bitmap.GetPixel(x, y).Alpha;
                weightedX += x * alpha;
                totalAlpha += alpha;
            }
        }
        Assert.True(totalAlpha > 0.0, "Expected rendered text ink.");
        return weightedX / totalAlpha;
    }

    private static Editor2DPreviewDocument MirroredTextDocument(double widthFactor)
        => new(
            [new Editor2DPreviewPath(
                "mirror",
                "TEXT",
                [],
                false,
                Start: new(25, 20),
                Text: "F",
                TextHeight: 10,
                RotationDegrees: 0,
                WidthFactor: widthFactor)],
            new Editor2DBounds(0, 0, 50, 40),
            new Dictionary<string, int> { ["TEXT"] = 1 },
            []);
    private static Editor2DPreviewDocument RichTextDocument()
        => new(
            [new Editor2DPreviewPath(
                "rich",
                "TEXT",
                [],
                false,
                Start: new(10, 20),
                Text: "Größe 東京\nSecond & <safe>",
                TextHeight: 5,
                RotationDegrees: 90,
                WidthFactor: 2,
                FontFamily: "Noto Sans",
                CharacterSpacing: 1.5,
                IsBold: true,
                IsItalic: true,
                IsUnderline: true)],
            new Editor2DBounds(0, 0, 50, 40),
            new Dictionary<string, int> { ["TEXT"] = 1 },
            []);
}