using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class DxfCurveFlatteningTests
{
    private const double Tolerance = 0.1;

    [Fact]
    public void LargeRadiusCircularEntities_FlattenWithinCadTolerance()
    {
        var path = TempDxf();
        try
        {
            WriteCurves(path);

            var document = EditorDxfDocument.LoadPreviewDocument(path);
            var bulge = Assert.Single(document.Paths, item => item.LayerName == "BULGE");
            var arc = Assert.Single(document.Paths, item => item.LayerName == "ARC");
            var circle = Assert.Single(document.Paths, item => item.LayerName == "CIRCLE");

            Assert.True(bulge.Points.Count > 100);
            Assert.True(arc.Points.Count > 100);
            Assert.True(circle.Points.Count > 200);
            AssertCircularChordTolerance(bulge.Points, 0, 0, 1000, isClosed: false);
            AssertCircularChordTolerance(arc.Points, 0, 0, 1000, isClosed: false);
            AssertCircularChordTolerance(circle.Points, 3000, 0, 1000, isClosed: true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LargeEllipse_FlattensWithinCadTolerance()
    {
        var path = TempDxf();
        try
        {
            WriteCurves(path);

            var ellipse = Assert.Single(
                EditorDxfDocument.LoadPreviewDocument(path).Paths,
                item => item.LayerName == "ELLIPSE");

            Assert.True(ellipse.IsClosed);
            Assert.True(ellipse.Points.Count > 200);
            for (var index = 0; index < ellipse.Points.Count; index++)
            {
                var current = ellipse.Points[index];
                var next = ellipse.Points[(index + 1) % ellipse.Points.Count];
                var start = NormalizeParameter(Math.Atan2(current.Y / 500.0, (current.X - 6000.0) / 1000.0));
                var end = NormalizeParameter(Math.Atan2(next.Y / 500.0, (next.X - 6000.0) / 1000.0));
                while (end <= start + 1e-12)
                    end += Math.PI * 2.0;
                var middle = (start + end) * 0.5;
                var exactMiddle = new DxfPoint(
                    6000.0 + (1000.0 * Math.Cos(middle)),
                    500.0 * Math.Sin(middle));

                Assert.True(
                    DistanceToSegment(exactMiddle, current, next) <= Tolerance + 1e-9,
                    $"Ellipse chord {index} exceeded {Tolerance:R} unit tolerance.");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OcsElevationAndNegativeExtrusion_MatchEzdxfMakePathGolden()
    {
        var path = TempDxf();
        try
        {
            string[] lines =
            [
                "0", "SECTION", "2", "HEADER", "9", "$ACADVER", "1", "AC1024", "0", "ENDSEC",
                "0", "SECTION", "2", "ENTITIES",
                "0", "LWPOLYLINE", "8", "OCS_LW", "90", "2", "70", "0", "38", "5",
                "10", "1", "20", "2", "42", "1", "10", "5", "20", "2",
                "210", "0", "220", "0", "230", "-1",
                "0", "LWPOLYLINE", "8", "OCS_TILTED", "90", "2", "70", "0", "38", "5",
                "10", "1", "20", "2", "10", "3", "20", "2",
                "210", "0", "220", "1", "230", "1",
                "0", "ELLIPSE", "8", "OCS_ELLIPSE", "10", "10", "20", "20", "30", "3",
                "11", "4", "21", "0", "31", "0", "40", "0.5", "41", "0", "42", "1.5707963267948966",
                "210", "0", "220", "0", "230", "-1",
                "0", "ENDSEC", "0", "EOF",
            ];
            File.WriteAllLines(path, lines);

            var document = EditorDxfDocument.LoadPreviewDocument(path);
            var lwPolyline = Assert.Single(document.Paths, item => item.LayerName == "OCS_LW");
            var tilted = Assert.Single(document.Paths, item => item.LayerName == "OCS_TILTED");
            var ellipse = Assert.Single(document.Paths, item => item.LayerName == "OCS_ELLIPSE");
            DxfPoint[] lwGolden =
            [
                new(-1.0, 2.0),
                new(-1.157169914, 1.221509742),
                new(-1.585786438, 0.585786438),
                new(-2.221509742, 0.157169914),
                new(-3.0, 0.0),
                new(-3.778490258, 0.157169914),
                new(-4.414213562, 0.585786438),
                new(-4.842830086, 1.221509742),
                new(-5.0, 2.0),
            ];
            DxfPoint[] ellipseGolden =
            [
                new(14.0, 20.0),
                new(13.685660172, 19.221509742),
                new(12.828427125, 18.585786438),
                new(11.556980515, 18.157169914),
                new(10.0, 18.0),
            ];

            AssertPolylineWithinTolerance(lwPolyline.Points, lwGolden, Tolerance);
            AssertPolylineWithinTolerance(ellipse.Points, ellipseGolden, Tolerance);
            Assert.Equal(-1.0, tilted.Points[0].X, 9);
            Assert.Equal(2.121320343559642, tilted.Points[0].Y, 9);
            Assert.Equal(-3.0, tilted.Points[^1].X, 9);
            Assert.Equal(2.121320343559642, tilted.Points[^1].Y, 9);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OcsArcCircleAndHatch_MatchEzdxfGoldenProjection()
    {
        var path = TempDxf();
        try
        {
            string[] lines =
            [
                "0", "SECTION", "2", "HEADER", "9", "$ACADVER", "1", "AC1024", "0", "ENDSEC",
                "0", "SECTION", "2", "ENTITIES",
                "0", "ARC", "8", "OCS_ARC", "10", "1", "20", "2", "30", "3",
                "40", "4", "50", "0", "51", "90", "210", "0", "220", "0", "230", "-1",
                "0", "CIRCLE", "8", "OCS_CIRCLE", "10", "2", "20", "3", "30", "5",
                "40", "2", "210", "0", "220", "1", "230", "1",
                "0", "TEXT", "8", "OCS_TEXT", "10", "2", "20", "3", "30", "5",
                "40", "2", "50", "0", "1", "Label", "210", "0", "220", "1", "230", "1",
                "0", "HATCH", "8", "OCS_HATCH", "10", "0", "20", "0", "30", "5",
                "210", "0", "220", "1", "230", "1", "2", "SOLID", "70", "1", "71", "0", "91", "1",
                "92", "2", "72", "0", "73", "1", "93", "4",
                "10", "0", "20", "0", "10", "4", "20", "0", "10", "4", "20", "2", "10", "0", "20", "2",
                "97", "0", "75", "0", "76", "1", "98", "0",
                "0", "ENDSEC", "0", "EOF",
            ];
            File.WriteAllLines(path, lines);

            var document = EditorDxfDocument.LoadPreviewDocument(path);
            var arc = Assert.Single(document.Paths, item => item.LayerName == "OCS_ARC");
            var circle = Assert.Single(document.Paths, item => item.LayerName == "OCS_CIRCLE");
            var text = Assert.Single(document.Paths, item => item.LayerName == "OCS_TEXT");
            var hatch = Assert.Single(document.Paths, item => item.LayerName == "OCS_HATCH");

            Assert.Equal("LWPOLYLINE", arc.EntityType);
            Assert.Null(arc.Center);
            AssertPolylineWithinTolerance(
                arc.Points,
                [new(-5.0, 2.0), new(-4.695518130, 3.530733729), new(-3.828427125, 4.828427125), new(-2.530733729, 5.695518130), new(-1.0, 6.0)],
                Tolerance);

            Assert.Equal("LWPOLYLINE", circle.EntityType);
            Assert.True(circle.IsClosed);
            Assert.Null(circle.Center);
            const double circleCenterY = 1.4142135623730951;
            const double projectedRadiusY = 1.4142135623730951;
            Assert.All(circle.Points, point =>
            {
                var ellipseEquation = Math.Pow((point.X + 2.0) / 2.0, 2.0)
                                      + Math.Pow((point.Y - circleCenterY) / projectedRadiusY, 2.0);
                Assert.Equal(1.0, ellipseEquation, 9);
            });

            Assert.Equal(-2.0, text.Start!.Value.X, 9);
            Assert.Equal(1.4142135623730951, text.Start!.Value.Y, 9);
            Assert.Equal(180.0, text.RotationDegrees!.Value, 9);
            Assert.Equal(1.4142135623730951, text.TextHeight!.Value, 9);
            Assert.Equal(1.4142135623730951, text.WidthFactor!.Value, 9);

            var loop = Assert.Single(hatch.FillLoops!);
            DxfPoint[] hatchGolden =
            [
                new(0.0, 3.535533906),
                new(-4.0, 3.535533906),
                new(-4.0, 2.121320344),
                new(0.0, 2.121320344),
            ];
            Assert.Equal(hatchGolden.Length, loop.Count);
            for (var pointIndex = 0; pointIndex < hatchGolden.Length; pointIndex++)
            {
                Assert.Equal(hatchGolden[pointIndex].X, loop[pointIndex].X, 8);
                Assert.Equal(hatchGolden[pointIndex].Y, loop[pointIndex].Y, 8);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public void OcsTextMirrors_PreserveGenerationFlagsNegativeWidthAndExtrusionHandedness()
    {
        var path = TempDxf();
        try
        {
            string[] lines =
            [
                "0", "SECTION", "2", "HEADER", "9", "$ACADVER", "1", "AC1024", "0", "ENDSEC",
                "0", "SECTION", "2", "ENTITIES",
                "0", "TEXT", "8", "NEGATIVE_OCS", "10", "2", "20", "3", "40", "2", "1", "Negative OCS",
                "210", "0", "220", "0", "230", "-1",
                "0", "TEXT", "8", "BACKWARD", "10", "0", "20", "0", "40", "2", "50", "0", "71", "2", "1", "Backward",
                "0", "TEXT", "8", "UPSIDE_DOWN", "10", "0", "20", "0", "40", "2", "50", "0", "71", "4", "1", "Upside down",
                "0", "TEXT", "8", "BOTH", "10", "0", "20", "0", "40", "2", "50", "0", "71", "6", "1", "Both",
                "0", "TEXT", "8", "NEGATIVE_WIDTH", "10", "0", "20", "0", "40", "2", "41", "-1.5", "50", "0", "1", "Legacy negative width",
                "0", "ENDSEC", "0", "EOF",
            ];
            File.WriteAllLines(path, lines);

            var document = EditorDxfDocument.LoadPreviewDocument(path);
            var negativeOcs = Assert.Single(document.Paths, item => item.LayerName == "NEGATIVE_OCS");
            var backward = Assert.Single(document.Paths, item => item.LayerName == "BACKWARD");
            var upsideDown = Assert.Single(document.Paths, item => item.LayerName == "UPSIDE_DOWN");
            var both = Assert.Single(document.Paths, item => item.LayerName == "BOTH");
            var negativeWidth = Assert.Single(document.Paths, item => item.LayerName == "NEGATIVE_WIDTH");

            Assert.Equal(-2.0, negativeOcs.Start!.Value.X, 9);
            Assert.Equal(3.0, negativeOcs.Start.Value.Y, 9);
            Assert.Equal(0.0, negativeOcs.RotationDegrees!.Value, 9);
            Assert.Equal(-1.0, negativeOcs.WidthFactor!.Value, 9);
            Assert.Equal(0.0, backward.RotationDegrees!.Value, 9);
            Assert.Equal(-1.0, backward.WidthFactor!.Value, 9);
            Assert.Equal(180.0, Math.Abs(upsideDown.RotationDegrees!.Value), 9);
            Assert.Equal(-1.0, upsideDown.WidthFactor!.Value, 9);
            Assert.Equal(180.0, Math.Abs(both.RotationDegrees!.Value), 9);
            Assert.Equal(1.0, both.WidthFactor!.Value, 9);
            Assert.Equal(0.0, negativeWidth.RotationDegrees!.Value, 9);
            Assert.Equal(-1.5, negativeWidth.WidthFactor!.Value, 9);
            Assert.True(backward.Points.Min(point => point.X) < backward.Start!.Value.X);
        }
        finally
        {
            File.Delete(path);
        }
    }
    private static void AssertPolylineWithinTolerance(
        IReadOnlyList<DxfPoint> actual,
        IReadOnlyList<DxfPoint> expected,
        double tolerance)
    {
        Assert.True(actual.Count >= 2);
        Assert.True(expected.Count >= 2);
        foreach (var point in expected)
        {
            var distance = Enumerable.Range(0, actual.Count - 1)
                .Min(index => DistanceToSegment(point, actual[index], actual[index + 1]));
            Assert.True(distance <= tolerance + 1e-9, $"Golden point deviated by {distance:R}.");
        }
        foreach (var point in actual)
        {
            var distance = Enumerable.Range(0, expected.Count - 1)
                .Min(index => DistanceToSegment(point, expected[index], expected[index + 1]));
            Assert.True(distance <= tolerance + 1e-9, $"Imported point deviated by {distance:R}.");
        }
    }
    private static void AssertCircularChordTolerance(
        IReadOnlyList<DxfPoint> points,
        double centerX,
        double centerY,
        double radius,
        bool isClosed)
    {
        var segmentCount = isClosed ? points.Count : points.Count - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            var midpointX = ((current.X + next.X) * 0.5) - centerX;
            var midpointY = ((current.Y + next.Y) * 0.5) - centerY;
            var sagitta = radius - Math.Sqrt((midpointX * midpointX) + (midpointY * midpointY));
            Assert.True(
                sagitta <= Tolerance + 1e-9,
                $"Circular chord {index} sagitta {sagitta:R} exceeded {Tolerance:R}.");
        }
    }

    private static double DistanceToSegment(DxfPoint point, DxfPoint start, DxfPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        var position = Math.Clamp(
            (((point.X - start.X) * deltaX) + ((point.Y - start.Y) * deltaY)) / lengthSquared,
            0.0,
            1.0);
        var distanceX = point.X - (start.X + (position * deltaX));
        var distanceY = point.Y - (start.Y + (position * deltaY));
        return Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY));
    }

    private static double NormalizeParameter(double parameter)
        => parameter < 0.0 ? parameter + (Math.PI * 2.0) : parameter;

    private static void WriteCurves(string path)
    {
        string[] lines =
        [
            "0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", "4",
            "9", "$MEASUREMENT", "70", "1", "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
            "0", "LWPOLYLINE", "8", "BULGE", "90", "2", "70", "0",
            "10", "-1000", "20", "0", "42", "1", "10", "1000", "20", "0",
            "0", "ARC", "8", "ARC", "10", "0", "20", "0", "40", "1000", "50", "0", "51", "180",
            "0", "CIRCLE", "8", "CIRCLE", "10", "3000", "20", "0", "40", "1000",
            "0", "ELLIPSE", "8", "ELLIPSE", "10", "6000", "20", "0",
            "11", "1000", "21", "0", "40", "0.5", "41", "0", "42", "6.283185307179586",
            "0", "ENDSEC", "0", "EOF",
        ];
        File.WriteAllLines(path, lines);
    }

    private static string TempDxf()
        => Path.Combine(Path.GetTempPath(), $"pathstitch-curves-{Guid.NewGuid():N}.dxf");
}