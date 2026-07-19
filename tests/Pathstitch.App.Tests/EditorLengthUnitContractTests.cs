using System.Globalization;
using Domain.App.Models;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class EditorLengthUnitContractTests
{
    public static TheoryData<int, double> OfficialInsUnitsFactors => new()
    {
        { 1, 25.4 },
        { 2, 304.8 },
        { 3, 1_609_344.0 },
        { 4, 1.0 },
        { 5, 10.0 },
        { 6, 1_000.0 },
        { 7, 1_000_000.0 },
        { 8, 0.000_025_4 },
        { 9, 0.0254 },
        { 10, 914.4 },
        { 11, 0.000_000_1 },
        { 12, 0.000_001 },
        { 13, 0.001 },
        { 14, 100.0 },
        { 15, 10_000.0 },
        { 16, 100_000.0 },
        { 17, 1_000_000_000_000.0 },
        { 18, 149_597_870_700_000.0 },
        { 19, 9_460_730_472_580_800_000.0 },
        { 20, 30_856_775_812_800_000_000.0 },
        { 21, (1200.0 / 3937.0) * 1000.0 },
        { 22, ((1200.0 / 3937.0) * 1000.0) / 12.0 },
        { 23, ((1200.0 / 3937.0) * 1000.0) * 3.0 },
        { 24, ((1200.0 / 3937.0) * 1000.0) * 5280.0 },
    };

    [Theory]
    [MemberData(nameof(OfficialInsUnitsFactors))]
    public void DxfUnitCatalog_CoversOfficialCodesWithPhysicalMillimeterFactors(
        int code,
        double expectedMillimeters)
    {
        var actual = EditorLengthUnits.MillimetersPerDrawingUnit(code);

        Assert.NotNull(actual);
        Assert.Equal(expectedMillimeters, actual.Value, Math.Max(Math.Abs(expectedMillimeters) * 1e-12, 1e-12));
    }

    [Fact]
    public void DxfUnitCatalog_LeavesUnitlessAndUnknownCodesUnscaled()
    {
        Assert.Null(EditorLengthUnits.MillimetersPerDrawingUnit(0));
        Assert.Null(EditorLengthUnits.MillimetersPerDrawingUnit(25));
        Assert.Equal("unitless", EditorLengthUnits.DxfUnitName(0));
        Assert.Equal("CAD units", EditorLengthUnits.DxfUnitName(25));
    }

    [Fact]
    public void ImportPrompt_UsesEveryExplicitNonMillimeterUnitExceptKnownWeakMeterDefault()
    {
        foreach (var definition in EditorLengthUnits.DxfDefinitions)
        {
            var info = new Editor2DImportUnitsInfo(
                "declared.dxf",
                definition.InsUnitsCode,
                definition.MillimetersPerDrawingUnit,
                10,
                5);
            var shouldBeStrong = definition.InsUnitsCode is not 4 and not 6;

            Assert.Equal(shouldBeStrong, info.HasStrongUnitDeclaration);
            Assert.Equal(shouldBeStrong, info.RequiresPrompt);
            Assert.Equal(
                shouldBeStrong ? definition.MillimetersPerDrawingUnit : 1.0,
                info.RecommendedScaleFactor,
                Math.Max(Math.Abs(definition.MillimetersPerDrawingUnit) * 1e-12, 1e-12));
            Assert.Equal(definition.Name, info.DeclaredUnit);
        }
    }

    [Fact]
    public void DxfWriters_DeclareCanonicalMillimeterAndMetricHeaders()
    {
        var previewPath = TempDxf();
        var polylinePath = TempDxf();
        try
        {
            EditorDxfDocument.SavePreviewDocument(previewPath, PreviewLine(50.8));
            EditorDxfDocument.SaveLwPolylines(
                polylinePath,
                "OUTPUT",
                [new DxfPolyline([new(0, 0), new(50.8, 0)], false)]);

            AssertCanonicalMillimeterHeader(previewPath);
            AssertCanonicalMillimeterHeader(polylinePath);
        }
        finally
        {
            File.Delete(previewPath);
            File.Delete(polylinePath);
        }
    }

    [Fact]
    public void InchImportScaledToMillimeters_ExportsAndReimportsWithoutPhysicalSizeDrift()
    {
        var path = TempDxf();
        try
        {
            EditorDxfDocument.SavePreviewDocument(path, PreviewLine(2.0 * 25.4));

            var metadata = EditorDxfDocument.ReadUnitMetadata(path);
            var reimported = EditorDxfDocument.LoadPreviewDocument(path);
            var exportedLine = Assert.Single(reimported.Paths);
            var length = exportedLine.Points.Max(point => point.X) - exportedLine.Points.Min(point => point.X);
            var promptInfo = new Editor2DImportUnitsInfo(
                path,
                metadata.InsUnitsCode,
                metadata.MillimetersPerDrawingUnit,
                length,
                0);

            Assert.Equal(4, metadata.InsUnitsCode);
            Assert.Equal(1.0, metadata.MillimetersPerDrawingUnit);
            Assert.Equal(50.8, length, 9);
            Assert.False(promptInfo.RequiresPrompt);
            Assert.Equal(1.0, promptInfo.RecommendedScaleFactor);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppendToInchDxf_ConvertsGeneratedMillimetersAndGapToDrawingUnits()
    {
        var source = TempDxf();
        var output = TempDxf();
        try
        {
            WriteSinglePolylineDxf(source, insUnitsCode: 1, endX: 2.0);

            EditorDxfDocument.SaveOrAppendLwPolylines(
                output,
                "GENERATED",
                [new DxfPolyline([new(0, 0), new(25.4, 0)], false)],
                source,
                gap: 25.4);

            var metadata = EditorDxfDocument.ReadUnitMetadata(output);
            var paths = EditorDxfDocument.LoadPreviewDocument(output).Paths;
            Assert.Equal(1, metadata.InsUnitsCode);
            Assert.Equal(2, paths.Count);
            Assert.Contains(paths, path =>
                Math.Abs(path.Points.Min(point => point.X) - 3.0) < 1e-9
                && Math.Abs(path.Points.Max(point => point.X) - 4.0) < 1e-9);
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
        }
    }

    [Fact]
    public void AppendToUnitlessDxf_FailsBeforeMixingCoordinateSystems()
    {
        var source = TempDxf();
        var output = TempDxf();
        try
        {
            WriteSinglePolylineDxf(source, insUnitsCode: 0, endX: 2.0);

            var error = Assert.Throws<InvalidDataException>(() =>
                EditorDxfDocument.SaveOrAppendLwPolylines(
                    output,
                    "GENERATED",
                    [new DxfPolyline([new(0, 0), new(25.4, 0)], false)],
                    source));

            Assert.Contains("missing, unitless, or unsupported", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task ConflictingInsUnits_ForcePromptAndRejectAppendWithoutOutput()
    {
        var source = TempDxf();
        var output = TempDxf();
        try
        {
            WriteSinglePolylineDxf(source, insUnitsCode: 4, endX: 120.0);
            AddDuplicateInsUnits(source, 1);

            var metadata = EditorDxfDocument.ReadUnitMetadata(source);
            var info = await new DxfOutputPreviewService().InspectImportUnitsAsync(source);

            Assert.True(metadata.HasMalformedDeclaration);
            Assert.Null(metadata.InsUnitsCode);
            Assert.Null(metadata.MillimetersPerDrawingUnit);
            Assert.NotNull(info);
            Assert.True(info.HasMalformedUnitDeclaration);
            Assert.True(info.RequiresPrompt);
            Assert.Equal(1.0, info.RecommendedScaleFactor);

            var error = Assert.Throws<InvalidDataException>(() =>
                EditorDxfDocument.SaveOrAppendLwPolylines(
                    output,
                    "GENERATED",
                    [new DxfPolyline([new(0, 0), new(25.4, 0)], false)],
                    source));
            Assert.Contains("malformed or conflicting", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
        }
    }

    private static Domain.App.Models.Editor2DPreviewDocument PreviewLine(double length) => new(
        [new Domain.App.Models.Editor2DPreviewPath("line", "LWPOLYLINE", [new(0, 0), new(length, 0)], false)],
        new Editor2DBounds(0, 0, length, 0),
        new Dictionary<string, int> { ["LWPOLYLINE"] = 1 },
        []);

    private static void AssertCanonicalMillimeterHeader(string path)
    {
        var metadata = EditorDxfDocument.ReadUnitMetadata(path);
        Assert.Equal(EditorLengthUnits.MillimeterInsUnitsCode, metadata.InsUnitsCode);
        Assert.Equal(1.0, metadata.MillimetersPerDrawingUnit);
        Assert.Equal("1", ReadHeaderVariable(path, "$MEASUREMENT"));
    }

    private static string? ReadHeaderVariable(string path, string variable)
    {
        var lines = File.ReadAllLines(path);
        for (var index = 0; index + 3 < lines.Length; index += 2)
        {
            if (lines[index].Trim() == "9"
                && string.Equals(lines[index + 1].Trim(), variable, StringComparison.OrdinalIgnoreCase))
            {
                return lines[index + 3].Trim();
            }
        }

        return null;
    }

    private static void AddDuplicateInsUnits(string path, int insUnitsCode)
    {
        var lines = File.ReadAllLines(path).ToList();
        var measurementValueIndex = lines.FindIndex(
            value => string.Equals(value, "$MEASUREMENT", StringComparison.OrdinalIgnoreCase));
        Assert.True(measurementValueIndex > 0);
        lines.InsertRange(
            measurementValueIndex - 1,
            ["9", "$INSUNITS", "70", insUnitsCode.ToString(CultureInfo.InvariantCulture)]);
        File.WriteAllLines(path, lines);
    }

    private static void WriteSinglePolylineDxf(string path, int insUnitsCode, double endX)
    {
        File.WriteAllLines(path,
        [
            "0", "SECTION",
            "2", "HEADER",
            "9", "$ACADVER",
            "1", "AC1024",
            "9", "$INSUNITS",
            "70", insUnitsCode.ToString(CultureInfo.InvariantCulture),
            "9", "$MEASUREMENT",
            "70", insUnitsCode == 4 ? "1" : "0",
            "0", "ENDSEC",
            "0", "SECTION",
            "2", "ENTITIES",
            "0", "LWPOLYLINE",
            "8", "EXISTING",
            "90", "2",
            "70", "0",
            "10", "0",
            "20", "0",
            "10", endX.ToString("R", CultureInfo.InvariantCulture),
            "20", "0",
            "0", "ENDSEC",
            "0", "EOF",
        ]);
    }

    private static string TempDxf()
        => Path.Combine(Path.GetTempPath(), $"pathstitch-unit-contract-{Guid.NewGuid():N}.dxf");
}