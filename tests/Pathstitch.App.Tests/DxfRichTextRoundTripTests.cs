using Domain.App.Models;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class DxfRichTextRoundTripTests
{
    [Fact]
    public async Task RichMultilineText_RoundTripsThroughPathstitchXData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-rich-text-{Guid.NewGuid():N}.dxf");
        var longLine = new string('x', 320);
        var expectedText = $"First line\r\nSecond {longLine}";
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath(
                "rich-text",
                "TEXT",
                [],
                false,
                Start: new(12.5, -4.25),
                Text: expectedText,
                TextHeight: 6.5,
                RotationDegrees: 27.0,
                WidthFactor: 1.3,
                FontFamily: "Inter",
                CharacterSpacing: 1.25,
                IsBold: true,
                IsItalic: true,
                IsUnderline: true)],
            new Editor2DBounds(12.5, -4.25, 100, 10),
            new Dictionary<string, int> { ["TEXT"] = 1 },
            []);

        try
        {
            EditorDxfDocument.SavePreviewDocument(path, document);

            var raw = File.ReadAllText(path);
            Assert.Contains("\nAPPID\n", raw, StringComparison.Ordinal);
            Assert.Contains("\nPATHSTITCH\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n1001\nPATHSTITCH\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n1\nFirst line Second ", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("\n1\nFirst line\r\nSecond", raw, StringComparison.Ordinal);

            var internalText = Assert.Single(EditorDxfDocument.LoadPreviewDocument(path).Paths);
            Assert.Equal("First line\nSecond " + longLine, internalText.Text);
            Assert.Equal("Inter", internalText.FontFamily);
            Assert.Equal(1.25, internalText.CharacterSpacing, 8);
            Assert.True(internalText.IsBold);
            Assert.True(internalText.IsItalic);
            Assert.True(internalText.IsUnderline);
            Assert.Equal(27.0, internalText.RotationDegrees);
            Assert.Equal(1.3, internalText.WidthFactor);

            var publicText = Assert.Single((await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path))!.Paths);
            Assert.Equal(internalText.Text, publicText.Text);
            Assert.Equal(internalText.FontFamily, publicText.FontFamily);
            Assert.Equal(internalText.CharacterSpacing, publicText.CharacterSpacing);
            Assert.Equal(internalText.IsBold, publicText.IsBold);
            Assert.Equal(internalText.IsItalic, publicText.IsItalic);
            Assert.Equal(internalText.IsUnderline, publicText.IsUnderline);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MirroredText_RoundTripsUsingStandardGenerationFlagAndSignedBounds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-mirrored-text-{Guid.NewGuid():N}.dxf");
        var start = new Editor2DPoint(12, 4);
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath(
                "mirrored-text",
                "TEXT",
                [],
                false,
                Start: start,
                Text: "Mirror",
                TextHeight: 5,
                RotationDegrees: 27,
                WidthFactor: -1.25)],
            new Editor2DBounds(0, 0, 20, 10),
            new Dictionary<string, int> { ["TEXT"] = 1 },
            []);
        try
        {
            EditorDxfDocument.SavePreviewDocument(path, document);

            var raw = File.ReadAllText(path);
            Assert.Contains("\n41\n1.25\n71\n2\n", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("\n41\n-1.25\n", raw, StringComparison.Ordinal);
            var mirrored = Assert.Single(EditorDxfDocument.LoadPreviewDocument(path).Paths);
            Assert.Equal(27, mirrored.RotationDegrees!.Value, 9);
            Assert.Equal(-1.25, mirrored.WidthFactor!.Value, 9);

            var unrotatedBounds = Editor2DGeometry.BuildTextBoundsPoints(
                start,
                "Mirror",
                5,
                rotationDegrees: 0,
                widthFactor: -1.25);
            Assert.Equal(start.X, unrotatedBounds.Max(point => point.X), 9);
            Assert.True(unrotatedBounds.Min(point => point.X) < start.X);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public void ReflectedText_RoundTripsWithoutIntroducingUpsideDownRotation()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-reflected-text-{Guid.NewGuid():N}.dxf");
        var source = new Editor2DPreviewPath(
            "text", "TEXT", [], false,
            Start: new(2, 0), Text: "Mirror", TextHeight: 2,
            RotationDegrees: 0, WidthFactor: 1);
        var reflected = Editor2DGeometry.ReflectPath(
            source,
            new Editor2DPoint(0, -10),
            new Editor2DPoint(0, 10));
        var document = new Editor2DPreviewDocument(
            [reflected],
            new Editor2DBounds(-10, -10, 20, 20),
            new Dictionary<string, int> { ["TEXT"] = 1 },
            []);

        try
        {
            EditorDxfDocument.SavePreviewDocument(outputPath, document);

            var raw = File.ReadAllText(outputPath);
            Assert.Contains("\n71\n2\n", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("\n50\n180\n", raw, StringComparison.Ordinal);
            var roundTrip = Assert.Single(EditorDxfDocument.LoadPreviewDocument(outputPath).Paths);
            Assert.Equal(-2, roundTrip.Start!.Value.X, 9);
            Assert.Equal(0, roundTrip.Start.Value.Y, 9);
            Assert.Equal(0, roundTrip.RotationDegrees!.Value, 9);
            Assert.Equal(-1, roundTrip.WidthFactor!.Value, 9);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
    [Theory]
    [InlineData("R2018", false)]
    [InlineData("R2000", true)]
    public async Task UnicodeRichTextLayerAndFont_RoundTripAcrossDxfVersions(
        string dxfVersion,
        bool expectsLegacyEscapes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-unicode-{dxfVersion}-{Guid.NewGuid():N}.dxf");
        var expectedText = "Größe 東京 😀\n" + string.Concat(Enumerable.Repeat("縫製", 80));
        var geometry = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath(
                "unicode-text",
                "TEXT",
                [],
                false,
                Start: new(2, 3),
                Text: expectedText,
                TextHeight: 4,
                FontFamily: "Fönt 東京",
                CharacterSpacing: 0.75,
                IsBold: true)],
            new Editor2DBounds(0, 0, 40, 20),
            new Dictionary<string, int> { ["TEXT"] = 1 },
            []);
        var export = new Editor2DExportDocument(
            geometry,
            new Dictionary<string, Editor2DExportPathMetadata>
            {
                ["unicode-text"] = new("Läyer 東京", "#123456"),
            });

        try
        {
            EditorDxfDocument.SaveExportDocument(
                path,
                export,
                new Editor2DExportOptions(DxfVersion: dxfVersion));

            var bytes = await File.ReadAllBytesAsync(path);
            var raw = expectsLegacyEscapes
                ? System.Text.Encoding.ASCII.GetString(bytes)
                : System.Text.Encoding.UTF8.GetString(bytes);
            if (expectsLegacyEscapes)
            {
                Assert.All(bytes, value => Assert.InRange(value, (byte)0, (byte)127));
                Assert.Contains(@"\U+6771\U+4EAC", raw, StringComparison.Ordinal);
                var lines = raw.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
                for (var index = 0; index + 1 < lines.Length; index += 2)
                {
                    if (lines[index].Trim() == "1000")
                        Assert.True(lines[index + 1].Length <= 250);
                }
            }
            else
            {
                Assert.Contains("Größe 東京 😀", raw, StringComparison.Ordinal);
                Assert.Contains("Läyer 東京", raw, StringComparison.Ordinal);
                Assert.DoesNotContain(@"\U+", raw, StringComparison.OrdinalIgnoreCase);
            }

            var roundTrip = Assert.Single(
                (await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path))!.Paths);
            Assert.Equal(expectedText, roundTrip.Text);
            Assert.Equal("Fönt 東京", roundTrip.FontFamily);
            Assert.Equal("Läyer 東京", roundTrip.SourceLayerName);
            Assert.Equal(0.75, roundTrip.CharacterSpacing, 8);
            Assert.True(roundTrip.IsBold);
        }
        finally
        {
            File.Delete(path);
        }
    }}