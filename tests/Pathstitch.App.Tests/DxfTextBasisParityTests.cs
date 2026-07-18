using Domain.App.Models;
using Domain.App.Services;
using Pathstitch.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Tests;

public sealed class DxfTextBasisParityTests
{
    [Fact]
    public void ObliqueMirroredText_ParsesExactPhysicalBasis()
    {
        var path = TemporaryPath(".dxf");
        const double height = 4.0;
        const double width = 1.5;
        const double rotationDegrees = 30.0;
        const double obliqueDegrees = 20.0;
        try
        {
            File.WriteAllText(path, DxfText(
                height,
                width,
                rotationDegrees,
                obliqueDegrees,
                generationFlags: 6));

            var text = Assert.Single(EditorDxfDocument.LoadPreviewDocument(path).Paths);
            var basis = Assert.IsType<Editor2DTextBasis>(text.TextBasis);
            var rotation = rotationDegrees * Math.PI / 180.0;
            var oblique = obliqueDegrees * Math.PI / 180.0;
            Assert.Equal(-height * width * Math.Cos(rotation), basis.Ux, 9);
            Assert.Equal(-height * width * Math.Sin(rotation), basis.Uy, 9);
            Assert.Equal(-height * ((Math.Tan(oblique) * Math.Cos(rotation)) - Math.Sin(rotation)), basis.Vx, 9);
            Assert.Equal(-height * ((Math.Tan(oblique) * Math.Sin(rotation)) + Math.Cos(rotation)), basis.Vy, 9);
            Assert.Equal(
                Editor2DGeometry.BuildTextBoundsPoints(
                    new Editor2DPoint(0, 0),
                    "Shear",
                    text.TextHeight!.Value,
                    text.RotationDegrees!.Value,
                    text.WidthFactor!.Value,
                    textBasis: basis),
                text.Points.Select(point => new Editor2DPoint(point.X, point.Y)).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TiltedOcsObliqueText_PreservesExactProjectedBasis()
    {
        var path = TemporaryPath(".dxf");
        const double height = 4.0;
        const double width = 1.5;
        const double rotationDegrees = 30.0;
        const double obliqueDegrees = 20.0;
        var axisScale = Math.Sqrt(0.5);
        try
        {
            File.WriteAllText(path, DxfTiltedText(
                height,
                width,
                rotationDegrees,
                obliqueDegrees,
                axisScale));

            var text = Assert.Single(EditorDxfDocument.LoadPreviewDocument(path).Paths);
            var basis = Assert.IsType<Editor2DTextBasis>(text.TextBasis);
            var rotation = rotationDegrees * Math.PI / 180.0;
            var oblique = obliqueDegrees * Math.PI / 180.0;
            var localUx = height * width * Math.Cos(rotation);
            var localUy = height * width * Math.Sin(rotation);
            var localVx = height * ((Math.Tan(oblique) * Math.Cos(rotation)) - Math.Sin(rotation));
            var localVy = height * ((Math.Tan(oblique) * Math.Sin(rotation)) + Math.Cos(rotation));
            AssertBasisEqual(
                new Editor2DTextBasis(
                    -localUx,
                    -axisScale * localUy,
                    -localVx,
                    -axisScale * localVy),
                basis);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public void ArbitraryTextBasis_RoundTripsThroughDxfGroup51()
    {
        var outputPath = TemporaryPath(".dxf");
        var basis = new Editor2DTextBasis(8, 0, 2, 4);
        var start = new Editor2DPoint(10, 20);
        var textPath = new Editor2DPreviewPath(
            "sheared",
            "TEXT",
            Editor2DGeometry.BuildTextBoundsPoints(start, "Shear", 4, textBasis: basis),
            false,
            Start: start,
            Text: "Shear",
            TextHeight: 4,
            RotationDegrees: 0,
            WidthFactor: 2,
            TextBasis: basis);
        var document = Document(textPath);
        try
        {
            EditorDxfDocument.SavePreviewDocument(outputPath, document);

            var raw = File.ReadAllText(outputPath);
            Assert.Contains("\n51\n", raw, StringComparison.Ordinal);
            var roundTrip = Assert.Single(EditorDxfDocument.LoadPreviewDocument(outputPath).Paths);
            AssertBasisEqual(basis, Assert.IsType<Editor2DTextBasis>(roundTrip.TextBasis));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void AffineTransform_ComposesTextBasisWithoutScalarizingShear()
    {
        var basis = new Editor2DTextBasis(6, 0, 2, 4);
        var source = TextPath(basis);
        var transform = new Editor2DAffineTransform(2, 0.5, -0.25, 1.5, 3, -2);

        var transformed = Editor2DGeometry.TransformPath(source, transform);
        var actual = Assert.IsType<Editor2DTextBasis>(transformed.TextBasis);

        AssertBasisEqual(new Editor2DTextBasis(12, 3, 3, 7), actual);
        Assert.Equal(transform.TransformPoint(source.Start!), transformed.Start);
        var expectedPoints = Editor2DGeometry.BuildTextBoundsPoints(
            transformed.Start!,
            transformed.Text,
            transformed.TextHeight!.Value,
            transformed.RotationDegrees!.Value,
            transformed.WidthFactor!.Value,
            transformed.CharacterSpacing,
            actual);
        Assert.Equal(expectedPoints.Length, transformed.Points.Count);
        for (var index = 0; index < expectedPoints.Length; index++)
        {
            Assert.Equal(expectedPoints[index].X, transformed.Points[index].X, 9);
            Assert.Equal(expectedPoints[index].Y, transformed.Points[index].Y, 9);
        }
    }

    [Fact]
    public async Task TextBasis_RoundTripsThroughProjectArchive()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-text-basis-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "basis.stch");
        var path = TextPath(new Editor2DTextBasis(8, 1, 2, 4));
        var state = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Document = Document(path),
        };
        try
        {
            var service = new Project3DStateService();
            await service.SaveAsync(
                projectPath,
                new Project3DState(null, [], [], TwoDWorkspaceState: state));

            var restored = await service.LoadAsync(projectPath);
            var restoredPath = Assert.Single(restored.TwoDWorkspaceState!.Document.Paths);
            AssertBasisEqual(path.TextBasis!, Assert.IsType<Editor2DTextBasis>(restoredPath.TextBasis));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public void SvgPdfAndPng_UseFullShearedTextMatrix()
    {
        var svgPath = TemporaryPath(".svg");
        var pdfPath = TemporaryPath(".pdf");
        var basis = new Editor2DTextBasis(8, 0, 2, 4);
        var document = Document(TextPath(basis));
        try
        {
            SvgOutputDocumentWriter.Save(svgPath, document);
            PdfOutputDocumentWriter.Save(pdfPath, document);
            var png = PngOutputDocumentWriter.Render(
                new Editor2DExportDocument(document, new Dictionary<string, Editor2DExportPathMetadata>()),
                new Editor2DExportOptions(PngLongestEdge: 400, PngTransparent: true));

            Assert.Contains(
                "transform=\"matrix(1.789 0 -0.447 0.894 10 -20)\"",
                File.ReadAllText(svgPath),
                StringComparison.Ordinal);
            Assert.Contains(
                "1.789 0 0.447 0.894 ",
                File.ReadAllText(pdfPath),
                StringComparison.Ordinal);
            using var bitmap = SKBitmap.Decode(png);
            Assert.NotNull(bitmap);
            Assert.Contains(
                Enumerable.Range(0, bitmap.Height)
                    .SelectMany(y => Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x, y))),
                color => color.Alpha > 0);
        }
        finally
        {
            File.Delete(svgPath);
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public void SingularTextBasis_IsRejectedInsteadOfApproximated()
    {
        var path = new Editor2DPreviewPath(
            "singular", "TEXT", [], false,
            Start: new Editor2DPoint(0, 0), Text: "Bad", TextHeight: 4,
            TextBasis: new Editor2DTextBasis(4, 0, 8, 0));

        var error = Assert.Throws<InvalidDataException>(() => Editor2DGeometry.ResolveTextBasis(path));

        Assert.Contains("non-singular", error.Message, StringComparison.Ordinal);
    }

    private static Editor2DPreviewPath TextPath(Editor2DTextBasis basis)
    {
        var start = new Editor2DPoint(10, 20);
        Editor2DGeometry.ProjectTextBasis(basis, out var height, out var rotation, out var width);
        return new Editor2DPreviewPath(
            "sheared",
            "TEXT",
            Editor2DGeometry.BuildTextBoundsPoints(
                start,
                "Shear",
                height!.Value,
                rotation!.Value,
                width!.Value,
                textBasis: basis),
            false,
            Start: start,
            Text: "Shear",
            TextHeight: height,
            RotationDegrees: rotation,
            WidthFactor: width,
            TextBasis: basis);
    }

    private static Editor2DPreviewDocument Document(Editor2DPreviewPath path)
    {
        var points = path.Points;
        return new Editor2DPreviewDocument(
            [path],
            new Editor2DBounds(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.X),
                points.Max(point => point.Y)),
            new Dictionary<string, int> { ["TEXT"] = 1 },
            []);
    }

    private static void AssertBasisEqual(Editor2DTextBasis expected, Editor2DTextBasis actual)
    {
        Assert.Equal(expected.Ux, actual.Ux, 8);
        Assert.Equal(expected.Uy, actual.Uy, 8);
        Assert.Equal(expected.Vx, actual.Vx, 8);
        Assert.Equal(expected.Vy, actual.Vy, 8);
    }

    private static string TemporaryPath(string extension)
        => Path.Combine(Path.GetTempPath(), $"pathstitch-text-basis-{Guid.NewGuid():N}{extension}");

    private static string DxfTiltedText(
        double height,
        double width,
        double rotation,
        double oblique,
        double axisScale)
        => string.Join(
            (char)10,
            "0", "SECTION",
            "2", "ENTITIES",
            "0", "TEXT",
            "10", "0",
            "20", "0",
            "40", height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "41", width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "50", rotation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "51", oblique.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "210", "0",
            "220", axisScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "230", axisScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "1", "Tilted",
            "0", "ENDSEC",
            "0", "EOF",
            string.Empty);
    private static string DxfText(
        double height,
        double width,
        double rotation,
        double oblique,
        int generationFlags)
        => string.Join(
            (char)10,
            "0", "SECTION",
            "2", "ENTITIES",
            "0", "TEXT",
            "10", "0",
            "20", "0",
            "40", height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "41", width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "50", rotation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "51", oblique.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "71", generationFlags.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "1", "Shear",
            "0", "ENDSEC",
            "0", "EOF",
            string.Empty);
}
