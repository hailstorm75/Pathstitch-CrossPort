using Domain.App.Models;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class PdfRichTextExportTests
{
    [Fact]
    public void RichMultilineText_ExportsFontTransformSpacingAndUnderline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-rich-text-{Guid.NewGuid():N}.pdf");
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath(
                "rich",
                "TEXT",
                [],
                false,
                Start: new(10, 10),
                Text: "First\nSecond",
                TextHeight: 5,
                RotationDegrees: 90,
                WidthFactor: 2,
                FontFamily: "Consolas",
                CharacterSpacing: 1.5,
                IsBold: true,
                IsItalic: true,
                IsUnderline: true)],
            new Editor2DBounds(0, 0, 100, 100),
            new Dictionary<string, int> { ["TEXT"] = 1 },
            []);

        try
        {
            PdfOutputDocumentWriter.Save(path, document);

            var pdf = File.ReadAllText(path);
            Assert.Contains("/F12 27 Tf 4.05 Tc 0 2 -1 0 57.6 90 Tm (First) Tj ET", pdf, StringComparison.Ordinal);
            Assert.Contains("/F12 27 Tf 4.05 Tc 0 2 -1 0 90 90 Tm (Second) Tj ET", pdf, StringComparison.Ordinal);
            Assert.Contains("/BaseFont /Courier-BoldOblique", pdf, StringComparison.Ordinal);
            Assert.Equal(2, pdf.Split(" l S\n", StringSplitOptions.None).Length - 1);
            Assert.Contains("xref\n0 17\n", pdf, StringComparison.Ordinal);
            Assert.EndsWith("%%EOF\n", pdf, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public void MirroredText_ExportsNegativePdfTextMatrix()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-mirrored-text-{Guid.NewGuid():N}.pdf");
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath(
                "mirror",
                "TEXT",
                [],
                false,
                Start: new(10, 10),
                Text: "Mirror",
                TextHeight: 5,
                RotationDegrees: 0,
                WidthFactor: -2)],
            new Editor2DBounds(0, 0, 100, 100),
            new Dictionary<string, int> { ["TEXT"] = 1 },
            []);
        try
        {
            PdfOutputDocumentWriter.Save(path, document);

            var pdf = File.ReadAllText(path);
            Assert.Contains("-2 0 0 1 90 90 Tm (Mirror) Tj ET", pdf, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}