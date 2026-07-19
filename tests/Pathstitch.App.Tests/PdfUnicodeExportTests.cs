using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Domain.App.Models;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class PdfUnicodeExportTests
{
    [Fact]
    public void UnicodeText_UsesBundledFontGlyphsAndToUnicodeMaps()
    {
        var requestedOutput = Environment.GetEnvironmentVariable("PATHSTITCH_PDF_QA_OUTPUT");
        var outputPath = string.IsNullOrWhiteSpace(requestedOutput)
            ? Path.Combine(Path.GetTempPath(), $"pathstitch-unicode-{Guid.NewGuid():N}.pdf")
            : Path.GetFullPath(requestedOutput);
        var document = CreateDocument("Größe 中文 😀\n第二行");

        try
        {
            PdfOutputDocumentWriter.Save(outputPath, document);

            var bytes = File.ReadAllBytes(outputPath);
            var structure = Encoding.Latin1.GetString(bytes);
            Assert.StartsWith("%PDF-", structure, StringComparison.Ordinal);
            Assert.Contains("/ToUnicode", structure, StringComparison.Ordinal);
            Assert.Contains("/Subtype /Type3", structure, StringComparison.Ordinal);
            Assert.Contains("/CharProcs", structure, StringComparison.Ordinal);
            Assert.Contains("NotoSansCJKsc", structure, StringComparison.Ordinal);
            Assert.DoesNotContain("/Subtype /Type1", structure, StringComparison.Ordinal);
            Assert.Contains("%%EOF", structure, StringComparison.Ordinal);
            Assert.True(bytes.Length > 10_000);
            var decodedStreams = InflatePdfStreams(bytes);
            Assert.Contains(decodedStreams, stream => stream.Contains("<4E2D>", StringComparison.Ordinal));
            Assert.Contains(decodedStreams, stream => stream.Contains("<6587>", StringComparison.Ordinal));
            Assert.Contains(decodedStreams, stream => stream.Contains("<4E8C>", StringComparison.Ordinal));
            Assert.Contains(decodedStreams, stream => stream.Contains("<7B2C>", StringComparison.Ordinal));
            Assert.Contains(decodedStreams, stream => stream.Contains("<884C>", StringComparison.Ordinal));
            Assert.Contains(decodedStreams, stream => stream.Contains("<D83DDE00>", StringComparison.Ordinal));
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(requestedOutput) && File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public void Latin1Text_UsesUnicodePipelineInsteadOfAsciiReplacement()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-latin1-{Guid.NewGuid():N}.pdf");
        try
        {
            PdfOutputDocumentWriter.Save(outputPath, CreateDocument("Größe"));

            var structure = Encoding.Latin1.GetString(File.ReadAllBytes(outputPath));
            Assert.Contains("/ToUnicode", structure, StringComparison.Ordinal);
            Assert.Contains("NotoSansCJKsc", structure, StringComparison.Ordinal);
            Assert.DoesNotContain("/Subtype /Type1", structure, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public void InvalidUnicode_DoesNotReplaceExistingTarget()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-pdf-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, "drawing.pdf");
        var sentinel = Encoding.UTF8.GetBytes("existing-pdf-sentinel");
        File.WriteAllBytes(outputPath, sentinel);
        try
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                PdfOutputDocumentWriter.Save(outputPath, CreateDocument("broken \ud800")));

            Assert.Contains("invalid UTF-16", exception.Message, StringComparison.Ordinal);
            Assert.Equal(sentinel, File.ReadAllBytes(outputPath));
            Assert.Empty(Directory.EnumerateFiles(directory, ".drawing.pdf.*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IReadOnlyList<string> InflatePdfStreams(byte[] pdfBytes)
    {
        var structure = Encoding.Latin1.GetString(pdfBytes);
        var decoded = new List<string>();
        var searchIndex = 0;
        while (searchIndex < structure.Length)
        {
            var markerIndex = structure.IndexOf("stream\n", searchIndex, StringComparison.Ordinal);
            if (markerIndex < 0)
                break;
            var objectIndex = structure.LastIndexOf("obj\n", markerIndex, StringComparison.Ordinal);
            if (objectIndex < 0)
                break;
            var header = structure[objectIndex..markerIndex];
            var lengthMatch = Regex.Match(header, @"/Length\s+(\d+)");
            var streamStart = markerIndex + "stream\n".Length;
            if (header.Contains("/FlateDecode", StringComparison.Ordinal)
                && lengthMatch.Success
                && int.TryParse(lengthMatch.Groups[1].Value, out var length)
                && streamStart + length <= pdfBytes.Length)
            {
                try
                {
                    using var compressed = new MemoryStream(pdfBytes, streamStart, length, writable: false);
                    using var decompressor = new ZLibStream(compressed, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    decompressor.CopyTo(output);
                    decoded.Add(Encoding.Latin1.GetString(output.ToArray()));
                }
                catch (InvalidDataException)
                {
                    // A binary stream may coincidentally contain the ASCII stream marker.
                }
            }
            searchIndex = streamStart + 1;
        }
        return decoded;
    }
    private static Editor2DPreviewDocument CreateDocument(string text)
        => new(
            [
                new Editor2DPreviewPath(
                    "unicode",
                    "TEXT",
                    [],
                    false,
                    Start: new(10, 20),
                    Text: text,
                    TextHeight: 6,
                    RotationDegrees: 12,
                    WidthFactor: 1.1,
                    FontFamily: "Arial",
                    CharacterSpacing: 0.25,
                    IsBold: true,
                    IsUnderline: true),
                new Editor2DPreviewPath(
                    "frame",
                    "LWPOLYLINE",
                    [new(0, 0), new(100, 0), new(100, 60), new(0, 60)],
                    true),
            ],
            new Editor2DBounds(0, 0, 100, 60),
            new Dictionary<string, int> { ["TEXT"] = 1, ["LWPOLYLINE"] = 1 },
            []);
}