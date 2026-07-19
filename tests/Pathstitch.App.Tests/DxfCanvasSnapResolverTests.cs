using Avalonia;
using Domain.App.Models;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Tests;

public sealed class DxfCanvasSnapResolverTests
{
    private static readonly Func<Editor2DPoint, Point> IdentityTransform = point => new Point(point.X, point.Y);

    [Fact]
    public void Resolve_UsesMacOsPriorityAndScreenTolerance()
    {
        var document = Document(
            Line("horizontal", 0, 5, 10, 5),
            Line("vertical", 5, 0, 5, 10),
            Line("midpoint", 0, 0, 10, 10));
        var resolver = new DxfCanvasSnapResolver();

        var intersection = resolver.Resolve(
            document, [], new Editor2DPoint(5.4, 5.2), new Point(5.4, 5.2), IdentityTransform, 1);

        Assert.NotNull(intersection);
        Assert.Equal(DxfCanvasSnapKind.Intersection, intersection.Kind);
        Assert.Equal(new Editor2DPoint(5, 5), intersection.ModelPoint);

        var outsideTolerance = resolver.Resolve(
            document, [], new Editor2DPoint(30, 30), new Point(30, 30), IdentityTransform, 1);
        Assert.Null(outsideTolerance);
    }

    [Fact]
    public void Resolve_ProvidesEndpointsMidpointsAndCoincidentPoints()
    {
        var resolver = new DxfCanvasSnapResolver();
        var document = Document(Line("line", 0, 0, 10, 0));

        Assert.Equal(
            DxfCanvasSnapKind.Endpoint,
            resolver.Resolve(document, [], new Editor2DPoint(.2, .1), new Point(.2, .1), IdentityTransform, 1)!.Kind);
        Assert.Equal(
            DxfCanvasSnapKind.Midpoint,
            resolver.Resolve(document, [], new Editor2DPoint(5.1, .1), new Point(5.1, .1), IdentityTransform, 1)!.Kind);

        var coincident = resolver.Resolve(
            document, [], new Editor2DPoint(7, .2), new Point(7, .2), IdentityTransform, 1);
        Assert.Equal(DxfCanvasSnapKind.Coincident, coincident!.Kind);
        Assert.Equal(new Editor2DPoint(7, 0), coincident.ModelPoint);
    }

    [Fact]
    public void Resolve_HandlesCircleAndArcSemanticPoints()
    {
        var resolver = new DxfCanvasSnapResolver();
        var circle = new Editor2DPreviewPath(
            "circle", "CIRCLE", [], true, Center: new Editor2DPoint(0, 0), Radius: 10);
        var arc = new Editor2DPreviewPath(
            "arc", "ARC", [], false, Center: new Editor2DPoint(30, 0), Radius: 10,
            StartAngleDegrees: 0, EndAngleDegrees: 180);
        var document = Document(circle, arc);

        Assert.Equal(
            DxfCanvasSnapKind.Center,
            resolver.Resolve(document, [], new Editor2DPoint(.1, .1), new Point(.1, .1), IdentityTransform, 1)!.Kind);
        Assert.Equal(
            DxfCanvasSnapKind.Coincident,
            resolver.Resolve(document, [], new Editor2DPoint(9.8, .2), new Point(9.8, .2), IdentityTransform, 1)!.Kind);
        Assert.Equal(
            DxfCanvasSnapKind.Endpoint,
            resolver.Resolve(document, [], new Editor2DPoint(40.2, 0), new Point(40.2, 0), IdentityTransform, 1)!.Kind);
        Assert.Equal(
            DxfCanvasSnapKind.Midpoint,
            resolver.Resolve(document, [], new Editor2DPoint(30, 10.2), new Point(30, 10.2), IdentityTransform, 1)!.Kind);
    }

    [Fact]
    public void Resolve_ExcludesHiddenPaths()
    {
        var resolver = new DxfCanvasSnapResolver();
        var document = Document(Line("hidden", 0, 0, 10, 0));

        var result = resolver.Resolve(
            document, ["hidden"], new Editor2DPoint(0, 0), new Point(0, 0), IdentityTransform);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_ParametricCornersExposeOnlyTangentEndsCenterAndCoincidentOutline()
    {
        var source = new[]
        {
            new Editor2DPoint(0, 0),
            new Editor2DPoint(10, 0),
            new Editor2DPoint(10, 10),
        };
        var basePath = new Editor2DPreviewPath("corner", "LWPOLYLINE", source, false);
        var parameter = new Editor2DCornerParameter(
            "fillet", basePath.Id, 1, Editor2DCornerKind.Fillet, 2, source);
        var renderedPath = Editor2DCornerGeometry.Apply(basePath, [parameter]);
        var resolver = new DxfCanvasSnapResolver();
        var document = Document(renderedPath);

        var tangent = resolver.Resolve(
            document, [], new Editor2DPoint(8, 0), new Point(8, 0), IdentityTransform, .05, [parameter]);
        var center = resolver.Resolve(
            document, [], new Editor2DPoint(8, 2), new Point(8, 2), IdentityTransform, .05, [parameter]);
        var flattenedArcVertex = renderedPath.Points[5];
        var outline = resolver.Resolve(
            document, [], flattenedArcVertex, new Point(flattenedArcVertex.X, flattenedArcVertex.Y), IdentityTransform, .05, [parameter]);

        Assert.Equal(DxfCanvasSnapKind.Endpoint, tangent!.Kind);
        Assert.Equal(DxfCanvasSnapKind.Midpoint, center!.Kind);
        Assert.Equal(DxfCanvasSnapKind.Coincident, outline!.Kind);
    }

    [Fact]
    public void OrthogonalConstraint_ConstrainsOnlyInsideSevenDegreeWindow()
    {
        var reference = new Editor2DPoint(0, 0);
        var constrained = DxfCanvasSnapResolver.ApplyOrthogonalConstraint(reference, new Editor2DPoint(10, 1));
        var unchanged = DxfCanvasSnapResolver.ApplyOrthogonalConstraint(reference, new Editor2DPoint(10, 2));

        Assert.InRange(Math.Abs(constrained.Y), 0, 1e-9);
        Assert.Equal(new Editor2DPoint(10, 2), unchanged);
    }

    private static Editor2DPreviewPath Line(string id, double x1, double y1, double x2, double y2)
        => new(id, "LINE", [new Editor2DPoint(x1, y1), new Editor2DPoint(x2, y2)], false);

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
        => new(paths, new Editor2DBounds(0, 0, 40, 20), new Dictionary<string, int>(), []);
}
