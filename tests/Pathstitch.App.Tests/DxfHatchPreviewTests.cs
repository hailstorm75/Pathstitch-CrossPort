using Domain.App.Models;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class DxfHatchPreviewTests
{
    [Fact]
    public async Task PolylineBoundaries_PreserveOuterAndHoleLoopsWithMetadata()
    {
        var path = TempDxf();
        try
        {
            WriteHatch(path,
            [
                "91", "2",
                "92", "3", "72", "0", "73", "1", "93", "4",
                "10", "0", "20", "0", "10", "20", "20", "0",
                "10", "20", "20", "20", "10", "0", "20", "20", "97", "0",
                "92", "2", "72", "0", "73", "1", "93", "4",
                "10", "6", "20", "6", "10", "14", "20", "6",
                "10", "14", "20", "14", "10", "6", "20", "14", "97", "0",
                "75", "0", "76", "1", "98", "0",
            ]);

            var document = EditorDxfDocument.LoadPreviewDocument(path);

            var hatch = Assert.Single(document.Paths);
            Assert.True(hatch.IsFilled);
            var loops = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyList<DxfPoint>>>(hatch.FillLoops);
            Assert.Equal(2, loops.Count);
            Assert.Equal(400.0, Area(loops[0]), 8);
            Assert.Equal(64.0, Area(loops[1]), 8);
            Assert.Equal(hatch.Points, loops[0]);
            Assert.Equal("HATCH", hatch.EntityType);
            Assert.Equal("FILLS", hatch.LayerName);
            Assert.Equal("BEEF", hatch.EntityHandle);
            Assert.True(hatch.IsClosed);
            Assert.Equal(1, document.EntityCounts["HATCH"]);
            Assert.DoesNotContain("HATCH", document.UnsupportedEntityTypes);

            var editorDocument = await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path);
            var editorHatch = Assert.Single(editorDocument!.Paths);
            Assert.Equal(2, editorHatch.FillLoops!.Count);
            var translated = Editor2DGeometry.TranslatePath(editorHatch, 5, -2);
            var translatedLoops = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyList<Editor2DPoint>>>(translated.FillLoops);
            Assert.Equal(new Editor2DPoint(11, 4), translatedLoops[1][0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EdgeBoundary_FlattensLineAndArcEdges()
    {
        var path = TempDxf();
        try
        {
            WriteHatch(path,
            [
                "91", "1", "92", "1", "93", "4",
                "72", "1", "10", "0", "20", "0", "11", "10", "21", "0",
                "72", "2", "10", "10", "20", "5", "40", "5", "50", "270", "51", "90", "73", "1",
                "72", "1", "10", "10", "20", "10", "11", "0", "21", "10",
                "72", "1", "10", "0", "20", "10", "11", "0", "21", "0",
                "97", "0", "75", "0", "76", "1", "98", "0",
            ]);

            var hatch = Assert.Single(EditorDxfDocument.LoadPreviewDocument(path).Paths);

            Assert.True(hatch.IsFilled);
            Assert.True(hatch.Points.Count >= 11);
            Assert.Contains(hatch.Points, point => Math.Abs(point.X - 15.0) < 1e-8 && Math.Abs(point.Y - 5.0) < 1e-8);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FilledPreviewPath_ExportsAndReimportsAsHatch()
    {
        var path = TempDxf();
        try
        {
            var points = new Editor2DPoint[] { new(1, 2), new(8, 2), new(8, 6), new(1, 6) };
            var hole = new Editor2DPoint[] { new(3, 3), new(6, 3), new(6, 5), new(3, 5) };
            var document = new Editor2DPreviewDocument(
                [new Editor2DPreviewPath(
                    "fill", "LWPOLYLINE", points, true, IsFilled: true,
                    FillLoops: [points, hole])],
                new Editor2DBounds(1, 2, 8, 6),
                new Dictionary<string, int> { ["LWPOLYLINE"] = 1 },
                []);

            EditorDxfDocument.SavePreviewDocument(path, document);
            var raw = File.ReadAllText(path);
            var reimported = EditorDxfDocument.LoadPreviewDocument(path);

            Assert.Contains("\nHATCH\n", raw.Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.DoesNotContain("\nLWPOLYLINE\n", raw.Replace("\r\n", "\n", StringComparison.Ordinal));
            var hatch = Assert.Single(reimported.Paths);
            Assert.Equal("HATCH", hatch.EntityType);
            Assert.True(hatch.IsFilled);
            Assert.Equal(28.0, Area(hatch.Points), 8);
            Assert.Equal(2, hatch.FillLoops!.Count);
            Assert.Equal(6.0, Area(hatch.FillLoops[1]), 8);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteHatch(string path, IReadOnlyList<string> boundaryPairs)
    {
        var lines = new List<string>
        {
            "0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", "4",
            "9", "$MEASUREMENT", "70", "1", "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES", "0", "HATCH",
            "5", "BEEF", "8", "FILLS", "100", "AcDbEntity", "100", "AcDbHatch",
            "10", "0", "20", "0", "30", "0", "210", "0", "220", "0", "230", "1",
            "2", "SOLID", "70", "1", "71", "0",
        };
        lines.AddRange(boundaryPairs);
        lines.AddRange(["0", "ENDSEC", "0", "EOF"]);
        File.WriteAllLines(path, lines);
    }

    private static double Area(IReadOnlyList<DxfPoint> points)
    {
        var twiceArea = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            twiceArea += (points[index].X * next.Y) - (next.X * points[index].Y);
        }
        return Math.Abs(twiceArea) * 0.5;
    }

    private static string TempDxf()
        => Path.Combine(Path.GetTempPath(), $"pathstitch-hatch-{Guid.NewGuid():N}.dxf");
}