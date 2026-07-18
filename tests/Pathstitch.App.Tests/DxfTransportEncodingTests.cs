using System.Text;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class DxfTransportEncodingTests
{
    [Fact]
    public async Task LegacyAnsi1252_TextAndLayer_DecodeWithoutDataLoss()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = TemporaryDxfPath("cp1252");
        var encoding = Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
        try
        {
            await File.WriteAllTextAsync(
                path,
                BuildTextDxf("ANSI_1252", "Prüfung", "Größe €"),
                encoding);

            var internalText = Assert.Single(EditorDxfDocument.LoadPreviewDocument(path).Paths);
            Assert.Equal("Größe €", internalText.Text);
            Assert.Equal("Prüfung", internalText.LayerName);

            var publicText = Assert.Single(
                (await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path))!.Paths);
            Assert.Equal("Größe €", publicText.Text);
            Assert.Equal("Prüfung", publicText.SourceLayerName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("ANSI_WAT", "malformed")]
    [InlineData("ANSI_99999", "unsupported")]
    public void InvalidDeclaredCodePage_FailsWithActionableError(string codePage, string reason)
    {
        var path = TemporaryDxfPath("invalid-codepage");
        try
        {
            File.WriteAllText(path, BuildTextDxf(codePage, "Layer", "Text"), Encoding.ASCII);

            var exception = Assert.Throws<InvalidDataException>(
                () => EditorDxfDocument.LoadPreviewDocument(path));
            Assert.Contains(reason, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$DWGCODEPAGE", exception.Message, StringComparison.Ordinal);
            Assert.Contains(codePage, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void R2018Utf8Bom_DiscoversHeaderAndDecodesUnicodeStrictly()
    {
        var path = TemporaryDxfPath("utf8-bom");
        try
        {
            var dxf = BuildTextDxf("UTF-8", "中文层", "Größe 中文")
                .Replace("AC1015", "AC1032", StringComparison.Ordinal);
            File.WriteAllText(path, dxf, new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true,
                throwOnInvalidBytes: true));

            var text = Assert.Single(EditorDxfDocument.LoadPreviewDocument(path).Paths);

            Assert.Equal("Größe 中文", text.Text);
            Assert.Equal("中文层", text.LayerName);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public void EntityPayloadMimickingHeaderVariables_DoesNotOverrideTransportEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = TemporaryDxfPath("entity-header-lookalike");
        var encoding = Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
        try
        {
            var dxf = BuildTextDxf("ANSI_1252", "Prüfung", "Größe €").Replace(
                "0\r\nENDSEC\r\n0\r\nEOF",
                "0\r\nXRECORD\r\n9\r\n$ACADVER\r\n1\r\nAC1024\r\n9\r\n$DWGCODEPAGE\r\n3\r\nUTF-8\r\n0\r\nENDSEC\r\n0\r\nEOF",
                StringComparison.Ordinal);
            File.WriteAllText(path, dxf, encoding);

            var text = Assert.Single(EditorDxfDocument.LoadPreviewDocument(path).Paths);

            Assert.Equal("Größe €", text.Text);
            Assert.Equal("Prüfung", text.LayerName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("$ACADVER", "1", "AC1015")]
    [InlineData("$DWGCODEPAGE", "3", "ANSI_1251")]
    public void DuplicateTransportDeclarationsInHeader_FailExplicitly(
        string variableName,
        string valueCode,
        string duplicateValue)
    {
        var path = TemporaryDxfPath("duplicate-transport-header");
        try
        {
            var dxf = BuildTextDxf("ANSI_1252", "Layer", "Text").Replace(
                "9\r\n$INSUNITS",
                $"9\r\n{variableName}\r\n{valueCode}\r\n{duplicateValue}\r\n9\r\n$INSUNITS",
                StringComparison.Ordinal);
            File.WriteAllText(path, dxf, Encoding.ASCII);

            var exception = Assert.Throws<InvalidDataException>(
                () => EditorDxfDocument.LoadPreviewDocument(path));

            Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(variableName, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public async Task BinaryDxf_FailsExplicitlyInsteadOfReturningEmptyDocument()
    {
        var path = TemporaryDxfPath("binary");
        try
        {
            await File.WriteAllBytesAsync(
                path,
                Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001A\0opaque"));

            var internalException = Assert.Throws<InvalidDataException>(
                () => EditorDxfDocument.LoadPreviewDocument(path));
            Assert.Contains("Binary DXF", internalException.Message, StringComparison.Ordinal);
            Assert.Contains("ASCII DXF", internalException.Message, StringComparison.Ordinal);

            var publicException = await Assert.ThrowsAsync<InvalidDataException>(
                () => new DxfOutputPreviewService().LoadPreviewDocumentAsync(path));
            Assert.Contains("Binary DXF", publicException.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TemporaryDxfPath(string label)
        => Path.Combine(Path.GetTempPath(), $"pathstitch-{label}-{Guid.NewGuid():N}.dxf");

    private static string BuildTextDxf(string codePage, string layer, string text)
        => string.Join("\r\n",
        [
            "0", "SECTION", "2", "HEADER",
            "9", "$ACADVER", "1", "AC1015",
            "9", "$DWGCODEPAGE", "3", codePage,
            "9", "$INSUNITS", "70", "4",
            "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
            "0", "TEXT", "8", layer,
            "10", "1", "20", "2", "40", "5", "1", text,
            "0", "ENDSEC", "0", "EOF", string.Empty,
        ]);
}