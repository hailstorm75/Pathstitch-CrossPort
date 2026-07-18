using System.Globalization;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class DxfSplinePreviewTests
{
    [Fact]
    public void CubicSpline_FlattensWithinCadToleranceAndPreservesSourceMetadata()
    {
        var path = TempDxf();
        try
        {
            WriteSpline(
                path,
                degree: 3,
                knots: [0, 0, 0, 0, 1, 1, 1, 1],
                controlPoints: [new(0, 0), new(0, 10), new(10, 10), new(10, 0)],
                weights: []);

            var document = EditorDxfDocument.LoadPreviewDocument(path);

            var spline = Assert.Single(document.Paths);
            Assert.Equal("SPLINE", spline.EntityType);
            Assert.Equal("CURVES", spline.LayerName);
            Assert.Equal("ABC", spline.EntityHandle);
            Assert.False(spline.IsClosed);
            Assert.True(spline.Points.Count > 4);
            Assert.Equal(new DxfPoint(0, 0), spline.Points[0]);
            Assert.Equal(new DxfPoint(10, 0), spline.Points[^1]);
            Assert.Contains(
                spline.Points,
                point => Math.Abs(point.X - 5.0) <= 1e-9
                    && Math.Abs(point.Y - 7.5) <= 1e-9);
            Assert.Equal(1, document.EntityCounts["SPLINE"]);
            Assert.DoesNotContain("SPLINE", document.UnsupportedEntityTypes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RationalQuadraticSpline_PreservesQuarterCircleWeighting()
    {
        var path = TempDxf();
        try
        {
            WriteSpline(
                path,
                degree: 2,
                knots: [0, 0, 0, 1, 1, 1],
                controlPoints: [new(10, 0), new(10, 10), new(0, 10)],
                weights: [1, Math.Sqrt(0.5), 1]);

            var spline = Assert.Single(EditorDxfDocument.LoadPreviewDocument(path).Paths);
            var diagonal = spline.Points.MinBy(
                point => Math.Abs(point.X - point.Y));

            Assert.InRange(diagonal.X, 7.06, 7.08);
            Assert.InRange(diagonal.Y, 7.06, 7.08);
            Assert.InRange(
                Math.Sqrt((diagonal.X * diagonal.X) + (diagonal.Y * diagonal.Y)),
                9.99,
                10.01);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteSpline(
        string path,
        int degree,
        IReadOnlyList<double> knots,
        IReadOnlyList<DxfPoint> controlPoints,
        IReadOnlyList<double> weights)
    {
        var lines = new List<string>
        {
            "0", "SECTION",
            "2", "HEADER",
            "9", "$INSUNITS",
            "70", "4",
            "9", "$MEASUREMENT",
            "70", "1",
            "0", "ENDSEC",
            "0", "SECTION",
            "2", "ENTITIES",
            "0", "SPLINE",
            "5", "ABC",
            "8", "CURVES",
            "100", "AcDbEntity",
            "100", "AcDbSpline",
            "70", "8",
            "71", degree.ToString(CultureInfo.InvariantCulture),
            "72", knots.Count.ToString(CultureInfo.InvariantCulture),
            "73", controlPoints.Count.ToString(CultureInfo.InvariantCulture),
            "74", "0",
        };
        foreach (var knot in knots)
            lines.AddRange(["40", knot.ToString("R", CultureInfo.InvariantCulture)]);
        foreach (var weight in weights)
            lines.AddRange(["41", weight.ToString("R", CultureInfo.InvariantCulture)]);
        foreach (var point in controlPoints)
        {
            lines.AddRange(
            [
                "10", point.X.ToString("R", CultureInfo.InvariantCulture),
                "20", point.Y.ToString("R", CultureInfo.InvariantCulture),
                "30", "0",
            ]);
        }
        lines.AddRange(["0", "ENDSEC", "0", "EOF"]);
        File.WriteAllLines(path, lines);
    }

    private static string TempDxf()
        => Path.Combine(Path.GetTempPath(), $"pathstitch-spline-{Guid.NewGuid():N}.dxf");
}