using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class EditorDxfDocumentUnitMetadataTests
{
    [Theory]
    [InlineData(1, 25.4)]
    [InlineData(4, 1.0)]
    public void ReadUnitMetadata_MapsCommonDrawingUnitsToMillimeters(int insUnitsCode, double expectedMillimetersPerDrawingUnit)
    {
        var path = CreateTempDxf(
            "0", "SECTION",
            "2", "HEADER",
            "9", "$INSUNITS",
            "70", insUnitsCode.ToString(),
            "0", "ENDSEC",
            "0", "EOF");

        try
        {
            var metadata = EditorDxfDocument.ReadUnitMetadata(path);

            Assert.Equal(insUnitsCode, metadata.InsUnitsCode);
            Assert.NotNull(metadata.MillimetersPerDrawingUnit);
            Assert.Equal(expectedMillimetersPerDrawingUnit, metadata.MillimetersPerDrawingUnit.Value, 6);
            Assert.True(metadata.HasUnitScale);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadUnitMetadata_TreatsUnitlessDrawingAsNoScale()
    {
        var path = CreateTempDxf(
            "0", "SECTION",
            "2", "HEADER",
            "9", "$INSUNITS",
            "70", "0",
            "0", "ENDSEC",
            "0", "EOF");

        try
        {
            var metadata = EditorDxfDocument.ReadUnitMetadata(path);

            Assert.Equal(0, metadata.InsUnitsCode);
            Assert.Null(metadata.MillimetersPerDrawingUnit);
            Assert.False(metadata.HasUnitScale);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempDxf(params string[] tokens)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-units-{Guid.NewGuid():N}.dxf");
        File.WriteAllLines(path, tokens);
        return path;
    }
}
