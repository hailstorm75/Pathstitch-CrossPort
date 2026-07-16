using System.Globalization;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Tests;

public sealed class Editor2DDimensionExpressionParityTests
{
    [Theory]
    [InlineData("sqrt(9)", 3.0)]
    [InlineData("-2^2", 4.0)]
    [InlineData("2^3^2", 512.0)]
    [InlineData("2^-2", 0.25)]
    [InlineData("1e3 + .5E+2 + 1.e2", 1150.0)]
    [InlineData("1 inch + 2.54cm", 50.8)]
    [InlineData("1in + 1inches + 1\"", 76.2)]
    [InlineData("1m - 10cm + 2mm", 902.0)]
    public void Evaluate_MatchesDocumentedParityGrammarAndMillimeterUnits(string expression, double expected)
    {
        Assert.True(Editor2DDimensionExpression.TryEvaluate(
            expression, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase), out var value, out var error), error);
        Assert.Equal(expected, value, 8);
    }

    [Fact]
    public void Variables_AreCaseInsensitiveAndDependencyScanExcludesFunctionsUnitsAndExponentMarker()
    {
        var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["D1"] = 3, ["d_2"] = 4 };
        const string expression = "sqrt(d1^2 + D_2^2) + 1e-3mm + 2inch";

        Assert.True(Editor2DDimensionExpression.TryEvaluate(expression, variables, out var value, out var error), error);
        Assert.Equal(55.801, value, 8);
        Assert.True(Editor2DDimensionExpression.TryGetReferencedVariables(expression, out var references, out error), error);
        Assert.Equal(2, references.Count);
        Assert.Contains("d1", references);
        Assert.Contains("d_2", references);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("1e", "exponent")]
    [InlineData("sqrt", "parentheses")]
    [InlineData("sqrt(-1)", "non-negative")]
    [InlineData("1/1e-13", "zero")]
    [InlineData("2 yards", "Unsupported unit")]
    [InlineData("sin(1)", "Unknown function")]
    [InlineData("missing + 1", "Unknown variable")]
    [InlineData("1e308^2", "finite")]
    public void InvalidExpressions_ReturnPreciseErrors(string expression, string expectedError)
    {
        Assert.False(Editor2DDimensionExpression.TryEvaluate(
            expression, new Dictionary<string, double>(), out _, out var error));
        Assert.Contains(expectedError, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsupportedUnit_IsNotReportedAsDependency()
    {
        Assert.False(Editor2DDimensionExpression.TryGetReferencedVariables(
            "2 yards + d1", out var references, out var error));
        Assert.Empty(references);
        Assert.Contains("Unsupported unit", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workspace_ReevaluatesAndCachesAllParametersWhileDrivenEndpointStaysFixed()
    {
        var first = new Editor2DMeasurement(
            "first", new(0, 0), new(10, 0), VarName: "d1", Expression: "10", IsParametric: true);
        var second = new Editor2DMeasurement(
            "second", new(0, 10), new(20, 10), VarName: "d2", Expression: "d1 * 2", IsParametric: true);
        var driven = new Editor2DMeasurement(
            "driven", new(0, 20), new(99, 20), VarName: "d3", Expression: "sqrt(d2^2)",
            Driven: true, IsParametric: true);
        var workspace = Workspace(first, second, driven);

        Assert.True(workspace.TrySetMeasurementExpression(first.Id, "1 inch", out var error), error);

        AssertMeasurement(workspace, first.Id, distance: 25.4, evaluated: 25.4);
        AssertMeasurement(workspace, second.Id, distance: 50.8, evaluated: 50.8);
        AssertMeasurement(workspace, driven.Id, distance: 99.0, evaluated: 50.8);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(10, workspace.Measurements.Single(item => item.Id == first.Id).EvaluatedValue);
        Assert.True(workspace.Redo());
        Assert.Equal(50.8, workspace.Measurements.Single(item => item.Id == driven.Id).EvaluatedValue!.Value, 8);
    }

    [Fact]
    public void Apply_RebuildsLegacyNullAndStaleCachesWithoutMovingEndpoints()
    {
        var first = new Editor2DMeasurement(
            "first", new(0, 0), new(77, 0), VarName: "d1", Expression: "10",
            IsParametric: true, EvaluatedValue: null);
        var second = new Editor2DMeasurement(
            "second", new(0, 10), new(88, 10), VarName: "d2", Expression: "d1 * 2",
            Driven: true, IsParametric: true, EvaluatedValue: 999);
        var workspace = Workspace(first, second);

        AssertMeasurement(workspace, first.Id, distance: 77, evaluated: 10);
        AssertMeasurement(workspace, second.Id, distance: 88, evaluated: 20);
        Assert.False(workspace.CanUndo);

        workspace.Apply(workspace.State with
        {
            Measurements =
            [
                first with { Expression = "missing", EvaluatedValue = 10 },
                second with { EvaluatedValue = 20 },
            ],
        }, recordHistory: false);
        Assert.All(workspace.Measurements, measurement => Assert.Null(measurement.EvaluatedValue));
    }

    [Theory]
    [InlineData("d2", "Circular")]
    [InlineData("missing + 1", "Unknown variable")]
    [InlineData("-1", "positive")]
    public void Workspace_InvalidEditIsAtomicAndDoesNotCreateHistory(string expression, string expectedError)
    {
        var first = new Editor2DMeasurement(
            "first", new(0, 0), new(10, 0), VarName: "d1", Expression: "10", IsParametric: true);
        var second = new Editor2DMeasurement(
            "second", new(0, 10), new(20, 10), VarName: "d2", Expression: "d1 * 2", IsParametric: true);
        var workspace = Workspace(first, second);
        var before = workspace.Measurements.ToArray();

        Assert.False(workspace.TrySetMeasurementExpression(first.Id, expression, out var error));

        Assert.Contains(expectedError, error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, workspace.Measurements);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public async Task EvaluatedValues_PersistAndInvalidCachedValuesNormalizeAway()
    {
        var measurement = new Editor2DMeasurement(
            "m", new(0, 0), new(25.4, 0), VarName: "d1", Expression: "1 inch",
            IsParametric: true, EvaluatedValue: 25.4);
        var state = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Measurements = [measurement],
        };
        var path = Path.Combine(Path.GetTempPath(), $"dimension-evaluated-{Guid.NewGuid():N}.stch");
        try
        {
            var service = new Project3DStateService();
            await service.SaveAsync(path, new Project3DState(null, [], [], TwoDWorkspaceState: state));
            var loaded = await service.LoadAsync(path);
            Assert.Equal(25.4, Assert.Single(loaded.TwoDWorkspaceState!.Measurements!).EvaluatedValue!.Value, 8);

            var workspace = new Editor2DWorkspaceViewModel();
            workspace.Apply(state with { Measurements = [measurement with { EvaluatedValue = double.NaN }] }, recordHistory: false);
            Assert.Equal(25.4, Assert.Single(workspace.Measurements).EvaluatedValue!.Value, 8);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LegacyChain_RoundTripRebuildsNullAndStaleCachesWhenApplied()
    {
        var first = new Editor2DMeasurement(
            "first", new(0, 0), new(10, 0), VarName: "d1", Expression: "1 inch",
            IsParametric: true, EvaluatedValue: null);
        var second = new Editor2DMeasurement(
            "second", new(0, 10), new(20, 10), VarName: "d2", Expression: "d1 * 2",
            IsParametric: true, EvaluatedValue: 1);
        var state = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Measurements = [first, second],
        };
        var path = Path.Combine(Path.GetTempPath(), $"dimension-legacy-{Guid.NewGuid():N}.stch");
        try
        {
            var service = new Project3DStateService();
            await service.SaveAsync(path, new Project3DState(null, [], [], TwoDWorkspaceState: state));
            var loaded = await service.LoadAsync(path);
            var workspace = new Editor2DWorkspaceViewModel();
            workspace.Apply(loaded.TwoDWorkspaceState!, recordHistory: false);

            AssertMeasurement(workspace, first.Id, distance: 10, evaluated: 25.4);
            AssertMeasurement(workspace, second.Id, distance: 20, evaluated: 50.8);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SetDriven_AssignsOrphanParameterAndToggleOffAppliesResolvedLengthAtomically()
    {
        var orphan = new Editor2DMeasurement("orphan", new(0, 0), new(12, 0));
        var workspace = Workspace(orphan);

        Assert.True(workspace.SetMeasurementDriven(orphan.Id, driven: true));
        var driven = Assert.Single(workspace.Measurements);
        Assert.Equal("d1", driven.VarName);
        Assert.Equal("12", driven.Expression);
        Assert.Equal(12, driven.EvaluatedValue);
        Assert.Equal(12, driven.Distance);
        Assert.True(driven.Driven);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(orphan, Assert.Single(workspace.Measurements));

        var legacy = new Editor2DMeasurement(
            "legacy", new(0, 0), new(20, 0), VarName: "d1", Expression: "5",
            Driven: true, IsParametric: true, EvaluatedValue: null);
        workspace = Workspace(legacy);
        workspace.ClearHistory();
        Assert.True(workspace.SetMeasurementDriven(legacy.Id, driven: false));
        var driving = Assert.Single(workspace.Measurements);
        Assert.False(driving.Driven);
        Assert.Equal(5, driving.EvaluatedValue);
        Assert.Equal(5, driving.Distance);
        Assert.True(workspace.CanUndo);
    }

    [Fact]
    public void TableAndCanvas_UseCachedValueAndDoNotDoubleUnitSuffixes()
    {
        var formula = new Editor2DMeasurement(
            "formula", new(0, 0), new(99, 0), VarName: "d1", Expression: "1 inch",
            IsParametric: true, EvaluatedValue: 25.4);
        var driven = formula with { Id = "driven", Driven = true, EvaluatedValue = 50.8 };
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        editor.TwoDMeasurements = [formula, driven];

        var row = editor.TwoDDimensionParameters.First(item => item.Id == formula.Id);
        Assert.Equal("= 1 inch", row.ExpressionDisplay);
        Assert.Equal("25.40", row.ValueDisplay);
        Assert.Equal("fx: 25.40", DxfCanvasMeasurementEditing.FormatLabel(formula));
        Assert.Equal("(50.80 mm)", DxfCanvasMeasurementEditing.FormatLabel(driven));
        Assert.Equal("30.00 mm", DxfCanvasMeasurementEditing.FormatLabel(
            formula with { Expression = "30", EvaluatedValue = 30 }));
        Assert.DoesNotContain("inch mm", DxfCanvasMeasurementEditing.FormatLabel(formula), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("LINE", "length", 10.0)]
    [InlineData("CIRCLE", "radius", 5.0)]
    [InlineData("ARC", "radius", 5.0)]
    public void AttachedExpression_ResizesEntityAndDependentsInOneUndoableTransaction(
        string entityType,
        string dimensionType,
        double initialValue)
    {
        var path = entityType switch
        {
            "LINE" => new Editor2DPreviewPath(
                "source", "LINE", [new(0, 0), new(initialValue, 0)], false, Start: new(0, 0)),
            "CIRCLE" => new Editor2DPreviewPath(
                "source", "CIRCLE", Editor2DGeometry.BuildCirclePoints(new(0, 0), initialValue), true,
                Center: new(0, 0), Radius: initialValue),
            _ => new Editor2DPreviewPath(
                "source", "ARC", Editor2DGeometry.BuildArcPoints(new(0, 0), initialValue, 10, 120), false,
                Center: new(0, 0), Radius: initialValue, StartAngleDegrees: 10, EndAngleDegrees: 120),
        };
        Assert.True(Editor2DGeometry.TryBuildAttachedMeasurement(
            path, dimensionType, 2, 30, out var start, out var end));
        var attached = new Editor2DMeasurement(
            "attached", start, end, EntityPathId: path.Id, DimensionType: dimensionType,
            OffsetDistance: 2, PlacementAngleDegrees: 30, VarName: "d1",
            Expression: initialValue.ToString(CultureInfo.InvariantCulture), IsParametric: true,
            EvaluatedValue: initialValue);
        var dependent = new Editor2DMeasurement(
            "dependent", new(0, 20), new(initialValue * 2, 20), VarName: "d2",
            Expression: "d1 * 2", IsParametric: true, EvaluatedValue: initialValue * 2);
        var document = Editor2DWorkspaceState.Empty.Document with
        {
            Paths = [path],
            Bounds = new Editor2DBounds(-initialValue, -initialValue, initialValue, initialValue),
            EntityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [entityType] = 1 },
        };
        var layer = new Editor2DLayer("layer", "Layer", [path.Id]);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(Editor2DWorkspaceState.Empty with
        {
            Document = document,
            Measurements = [attached, dependent],
            SelectedMeasurementId = attached.Id,
            Layers = [layer],
            ActiveLayerId = layer.Id,
        }, recordHistory: false);
        workspace.ClearHistory();

        Assert.True(workspace.TrySetMeasurementExpression(attached.Id, "20", out var error), error);

        var resized = Assert.Single(workspace.Document.Paths);
        if (entityType == "LINE")
            Assert.Equal(20, Math.Sqrt(
                Math.Pow(resized.Points[^1].X - resized.Points[0].X, 2)
                + Math.Pow(resized.Points[^1].Y - resized.Points[0].Y, 2)), 8);
        else
            Assert.Equal(20, resized.Radius!.Value, 8);
        Assert.Equal(path.Id, resized.Id);
        Assert.Equal([path.Id], Assert.Single(workspace.Layers).PathIds);
        AssertMeasurement(workspace, attached.Id, distance: 20, evaluated: 20);
        AssertMeasurement(workspace, dependent.Id, distance: 40, evaluated: 40);
        Assert.True(workspace.CanUndo);

        Assert.True(workspace.Undo());
        Assert.Equal(document, workspace.Document);
        Assert.Equal([attached, dependent], workspace.Measurements);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.Redo());
        Assert.Equal(20, workspace.Measurements.Single(item => item.Id == attached.Id).Distance, 8);
        Assert.Equal(40, workspace.Measurements.Single(item => item.Id == dependent.Id).Distance, 8);
    }

    [Theory]
    [InlineData("width", Editor2DCornerKind.Fillet, 25.0)]
    [InlineData("height", Editor2DCornerKind.Chamfer, 35.0)]
    public void AutoRectangleDimensionValue_ResizesParametricCornersAndSiblingsAtomically(
        string dimensionType,
        Editor2DCornerKind cornerKind,
        double targetValue)
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var pathId = workspace.CreateRectangle(
            new Editor2DPoint(30, 40),
            new Editor2DPoint(10, 20),
            initialFilletRadius: 2)!;
        var originalPath = workspace.Document.Paths.Single(path => path.Id == pathId);
        var originalCorners = workspace.CornerParameters
            .Where(parameter => parameter.PathId == pathId)
            .Select(parameter => parameter with { Kind = cornerKind })
            .ToArray();
        var sourcePoints = originalCorners[0].SourcePoints;
        var renderedPath = Editor2DCornerGeometry.Apply(
            originalPath with { Points = sourcePoints, IsAxisAlignedRectangle = true },
            originalCorners);
        var unrelatedPath = new Editor2DPreviewPath(
            "unrelated", "LINE", [new(50, 50), new(60, 50)], false);
        var unrelatedCorner = new Editor2DCornerParameter(
            "unrelated:corner", unrelatedPath.Id, 0, Editor2DCornerKind.Chamfer, 1,
            [new(50, 50), new(60, 50), new(60, 60)]);
        var layer = workspace.Layers.Single(candidate => candidate.PathIds.Contains(pathId));
        var target = workspace.Measurements.Single(measurement =>
            measurement.Id == $"{pathId}:{dimensionType}") with
        {
            VarName = "d1",
            Expression = "20",
            IsParametric = true,
            EvaluatedValue = 20,
        };
        var oppositeType = dimensionType == "width" ? "height" : "width";
        Assert.True(Editor2DGeometry.TryBuildAttachedMeasurement(
            renderedPath,
            oppositeType,
            5,
            null,
            originalCorners,
            out var siblingStart,
            out var siblingEnd));
        var manualSibling = new Editor2DMeasurement(
            "manual-sibling",
            siblingStart,
            siblingEnd,
            EntityPathId: pathId,
            DimensionType: oppositeType,
            RectP1: sourcePoints[0],
            RectP2: sourcePoints[2],
            OffsetDistance: 5);
        var dependent = new Editor2DMeasurement(
            "dependent",
            new Editor2DPoint(0, 60),
            new Editor2DPoint(40, 60),
            VarName: "d2",
            Expression: "d1 * 2",
            IsParametric: true,
            EvaluatedValue: 40);
        Editor2DConvertLineGroup Convert(string id, Editor2DPreviewPath path) => new(
            id,
            "dashed",
            new Dictionary<string, double>(),
            [new Editor2DConvertLineSource(path, layer.Id, 0, 0, [path.Id])]);
        var relatedImport = new Editor2DImportGroup(
            "import-related", "rectangle.dxf", 1, [pathId], layer.Id, 0, 0);
        var unrelatedImport = new Editor2DImportGroup(
            "import-unrelated", "unrelated.dxf", 1, [unrelatedPath.Id], layer.Id, 1, 1);
        var relatedSewing = new Editor2DSewingHoleOperation(
            "sewing-related", [pathId], [pathId], Editor2DSewingHoleParameters.Default);
        var unrelatedSewing = new Editor2DSewingHoleOperation(
            "sewing-unrelated", [unrelatedPath.Id], [unrelatedPath.Id], Editor2DSewingHoleParameters.Default);
        var document = workspace.Document with
        {
            Paths = [renderedPath, unrelatedPath],
            Bounds = new Editor2DBounds(10, 20, 60, 60),
            EntityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["LWPOLYLINE"] = 1,
                ["LINE"] = 1,
            },
        };
        workspace.Apply(workspace.State with
        {
            Document = document,
            Measurements = workspace.Measurements
                .Select(measurement => measurement.Id == target.Id ? target : measurement)
                .Append(manualSibling)
                .Append(dependent)
                .ToArray(),
            SelectedMeasurementId = target.Id,
            Layers = workspace.Layers.Select(candidate => candidate.Id == layer.Id
                ? candidate with { PathIds = candidate.PathIds.Append(unrelatedPath.Id).ToArray() }
                : candidate).ToArray(),
            CornerParameters = originalCorners.Append(unrelatedCorner).ToArray(),
            ImportGroups = [relatedImport, unrelatedImport],
            SewingHoleOperations = [relatedSewing, unrelatedSewing],
            ConvertLineGroups = [Convert("convert-related", renderedPath), Convert("convert-unrelated", unrelatedPath)],
            ExpandedRectanglePathIds = [pathId, unrelatedPath.Id],
        }, recordHistory: false);
        workspace.ClearHistory();
        var before = workspace.State;

        Assert.True(workspace.TrySetMeasurementValue(target.Id, targetValue, out var error), error);

        var resized = workspace.Document.Paths.Single(path => path.Id == pathId);
        var resizedCorners = workspace.CornerParameters
            .Where(parameter => parameter.PathId == pathId)
            .OrderBy(parameter => parameter.Id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(originalCorners.Select(parameter => parameter.Id).Order(), resizedCorners.Select(parameter => parameter.Id).Order());
        Assert.All(resizedCorners, parameter =>
        {
            Assert.Equal(cornerKind, parameter.Kind);
            Assert.Equal(2, parameter.Value);
            Assert.Equal(sourcePoints[0], parameter.SourcePoints[0]);
            Assert.Equal(dimensionType == "width" ? 5 : 10, parameter.SourcePoints[2].X, 8);
            Assert.Equal(dimensionType == "height" ? 5 : 20, parameter.SourcePoints[2].Y, 8);
        });
        Assert.Equal(pathId, resized.Id);
        Assert.Equal(originalPath.EntityType, resized.EntityType);
        Assert.Equal(layer.Id, workspace.Layers.Single(candidate => candidate.PathIds.Contains(pathId)).Id);
        Assert.Contains(unrelatedPath.Id, workspace.Layers.Single(candidate => candidate.Id == layer.Id).PathIds);
        Assert.Equal(targetValue, workspace.Measurements.Single(item => item.Id == target.Id).Distance, 8);
        Assert.Equal(20, workspace.Measurements.Single(item => item.Id == manualSibling.Id).Distance, 8);
        Assert.Equal(targetValue * 2, workspace.Measurements.Single(item => item.Id == dependent.Id).Distance, 8);
        Assert.Equal(targetValue * 2, workspace.Measurements.Single(item => item.Id == dependent.Id).EvaluatedValue!.Value, 8);
        Assert.All(
            workspace.Measurements.Where(item => item.EntityPathId == pathId && item.DimensionType is "width" or "height"),
            item =>
            {
                Assert.Equal(sourcePoints[0], item.RectP1);
                Assert.Equal(resizedCorners[0].SourcePoints[2], item.RectP2);
            });
        Assert.Equal([unrelatedImport.Id], workspace.ImportGroups.Select(group => group.Id));
        Assert.Equal([unrelatedSewing.Id], workspace.SewingHoleOperations.Select(operation => operation.Id));
        Assert.Equal(["convert-unrelated"], workspace.ConvertLineGroups.Select(group => group.Id));
        Assert.Contains(workspace.CornerParameters, parameter => parameter.Id == unrelatedCorner.Id);
        Assert.Equal([unrelatedPath.Id], workspace.State.ExpandedRectanglePathIds);
        Assert.True(workspace.CanUndo);

        Assert.True(workspace.Undo());
        Assert.Equal(before, workspace.State);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.Redo());
        Assert.Equal(targetValue, workspace.Measurements.Single(item => item.Id == target.Id).Distance, 8);
    }

    [Fact]
    public void AutoRectangleDimensionValue_InvalidUnsupportedAndNoOpRemainAtomic()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var pathId = workspace.CreateRectangle(new(0, 0), new(20, 10))!;
        var width = workspace.Measurements.Single(item => item.Id == $"{pathId}:width");
        var free = new Editor2DMeasurement("free", new(0, 0), new(5, 0));
        workspace.SetMeasurements(workspace.Measurements.Append(free).ToArray());
        workspace.ClearHistory();
        var before = workspace.State;

        Assert.True(workspace.TrySetMeasurementValue(width.Id, width.Distance, out var noOpError), noOpError);
        Assert.False(workspace.TrySetMeasurementValue(width.Id, 0, out _));
        Assert.False(workspace.TrySetMeasurementValue(width.Id, double.NaN, out _));
        Assert.False(workspace.TrySetMeasurementValue(free.Id, 8, out _));
        Assert.Equal(before, workspace.State);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void AttachedRectangleMeasurement_LegacyFlaggedBoundsRemainSupportedWithoutCornerMetadata()
    {
        var path = new Editor2DPreviewPath(
            "legacy",
            "LWPOLYLINE",
            [new(1, 0), new(9, 0), new(10, 1), new(10, 4), new(9, 5), new(1, 5), new(0, 4), new(0, 1)],
            true,
            IsAxisAlignedRectangle: true,
            RotationDegrees: 12,
            IsConstruction: true);

        Assert.True(Editor2DGeometry.TryBuildAttachedMeasurement(
            path, "width", -2, null, out var start, out var end));
        Assert.Equal(new Editor2DPoint(0, -2), start);
        Assert.Equal(new Editor2DPoint(10, -2), end);

        Assert.True(Editor2DGeometry.TryResizeForAttachedDimension(
            path,
            " width ",
            20,
            [],
            out var resized,
            out var resizedCorners));
        Assert.Empty(resizedCorners);
        Assert.Equal(
            [new(2, 0), new(18, 0), new(20, 1), new(20, 4), new(18, 5), new(2, 5), new(0, 4), new(0, 1)],
            resized.Points);
        Assert.Equal(path.Id, resized.Id);
        Assert.Equal(path.EntityType, resized.EntityType);
        Assert.Equal(path.RotationDegrees, resized.RotationDegrees);
        Assert.True(resized.IsConstruction);
        Assert.True(resized.IsAxisAlignedRectangle);
        Assert.Equal(20, resized.Points.Max(point => point.X) - resized.Points.Min(point => point.X), 8);
        Assert.Equal(5, resized.Points.Max(point => point.Y) - resized.Points.Min(point => point.Y), 8);

        Assert.True(Editor2DGeometry.TryResizeForAttachedDimension(
            path,
            "HEIGHT",
            10,
            [],
            out var taller,
            out var tallerCorners));
        Assert.Empty(tallerCorners);
        Assert.Equal(
            new Editor2DPoint[]
            {
                new(1, 0), new(9, 0), new(10, 2), new(10, 8),
                new(9, 10), new(1, 10), new(0, 8), new(0, 2),
            },
            taller.Points);
        Assert.Equal(10, taller.Points.Max(point => point.Y) - taller.Points.Min(point => point.Y), 8);
    }

    [Fact]
    public void LegacyRoundedRectangleDimensionValue_ResizesGeometryAndMixedCaseSiblings()
    {
        var path = new Editor2DPreviewPath(
            "legacy",
            "LWPOLYLINE",
            [new(1, 0), new(9, 0), new(10, 1), new(10, 4), new(9, 5), new(1, 5), new(0, 4), new(0, 1)],
            true,
            IsAxisAlignedRectangle: true);
        var width = new Editor2DMeasurement(
            "legacy:width", new(0, -2), new(10, -2), true, path.Id, " WIDTH ",
            new(0, 0), new(10, 5), OffsetDistance: -2);
        var height = new Editor2DMeasurement(
            "legacy:height", new(-2, 0), new(-2, 5), true, path.Id, " Height ",
            new(0, 0), new(10, 5), OffsetDistance: -2);
        var layer = new Editor2DLayer("layer", "Layer", [path.Id]);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Document = Editor2DWorkspaceState.Empty.Document with
            {
                Paths = [path],
                Bounds = new(0, 0, 10, 5),
                EntityCounts = new Dictionary<string, int> { ["LWPOLYLINE"] = 1 },
            },
            Measurements = [width, height],
            Layers = [layer],
            ActiveLayerId = layer.Id,
        }, recordHistory: false);
        workspace.ClearHistory();

        Assert.True(workspace.TrySetMeasurementValue(width.Id, 20, out var error), error);

        Assert.Equal(20, workspace.Measurements.Single(item => item.Id == width.Id).Distance, 8);
        Assert.Equal(5, workspace.Measurements.Single(item => item.Id == height.Id).Distance, 8);
        Assert.All(workspace.Measurements, measurement =>
        {
            Assert.Equal(new Editor2DPoint(0, 0), measurement.RectP1);
            Assert.Equal(new Editor2DPoint(20, 5), measurement.RectP2);
        });
        Assert.Equal(path.Id, Assert.Single(workspace.Document.Paths).Id);
        Assert.Equal([path.Id], Assert.Single(workspace.Layers).PathIds);
        Assert.Empty(workspace.CornerParameters);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(path, Assert.Single(workspace.Document.Paths));
    }

    [Fact]
    public void AttachedExpression_PrunesOnlyEditedPathOwnershipAndHistoryRestoresIt()
    {
        var edited = new Editor2DPreviewPath("edited", "LINE", [new(0, 0), new(10, 0)], false);
        var unrelated = new Editor2DPreviewPath("unrelated", "LINE", [new(0, 20), new(10, 20)], false);
        Assert.True(Editor2DGeometry.TryBuildAttachedMeasurement(
            edited, "length", 2, null, out var start, out var end));
        var measurement = new Editor2DMeasurement(
            "dimension", start, end, EntityPathId: edited.Id, DimensionType: "length",
            OffsetDistance: 2, VarName: "d1", Expression: "10", IsParametric: true, EvaluatedValue: 10);
        var layer = new Editor2DLayer("layer", "Layer", [edited.Id, unrelated.Id]);
        Editor2DConvertLineGroup Convert(string id, Editor2DPreviewPath path) => new(
            id, "dashed", new Dictionary<string, double>(),
            [new Editor2DConvertLineSource(path, layer.Id, 0, 0, [path.Id])]);
        var relatedImport = new Editor2DImportGroup(
            "import-related", "related.dxf", 1, [edited.Id], layer.Id, 0, 0);
        var unrelatedImport = new Editor2DImportGroup(
            "import-unrelated", "unrelated.dxf", 1, [unrelated.Id], layer.Id, 1, 1);
        var relatedSewing = new Editor2DSewingHoleOperation(
            "sewing-related", [edited.Id], [unrelated.Id], Editor2DSewingHoleParameters.Default);
        var unrelatedSewing = new Editor2DSewingHoleOperation(
            "sewing-unrelated", [unrelated.Id], [unrelated.Id], Editor2DSewingHoleParameters.Default);
        var relatedCorner = new Editor2DCornerParameter(
            "corner-related", edited.Id, 0, Editor2DCornerKind.Chamfer, 1, edited.Points);
        var unrelatedCorner = new Editor2DCornerParameter(
            "corner-unrelated", unrelated.Id, 0, Editor2DCornerKind.Chamfer, 1, unrelated.Points);
        var document = Editor2DWorkspaceState.Empty.Document with
        {
            Paths = [edited, unrelated],
            Bounds = new Editor2DBounds(0, 0, 10, 20),
            EntityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["LINE"] = 2 },
        };
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(Editor2DWorkspaceState.Empty with
        {
            Document = document,
            Measurements = [measurement],
            SelectedMeasurementId = measurement.Id,
            Layers = [layer],
            ActiveLayerId = layer.Id,
            ImportGroups = [relatedImport, unrelatedImport],
            SewingHoleOperations = [relatedSewing, unrelatedSewing],
            ConvertLineGroups = [Convert("convert-related", edited), Convert("convert-unrelated", unrelated)],
            CornerParameters = [relatedCorner, unrelatedCorner],
            ExpandedRectanglePathIds = [edited.Id, unrelated.Id],
        }, recordHistory: false);
        workspace.ClearHistory();

        Assert.True(workspace.TrySetMeasurementExpression(measurement.Id, "20", out var error), error);

        Assert.Equal([unrelatedImport.Id], workspace.ImportGroups.Select(group => group.Id));
        Assert.Equal([unrelatedSewing.Id], workspace.SewingHoleOperations.Select(operation => operation.Id));
        Assert.Equal(["convert-unrelated"], workspace.ConvertLineGroups.Select(group => group.Id));
        Assert.Equal([unrelatedCorner.Id], workspace.CornerParameters.Select(parameter => parameter.Id));
        Assert.Equal([unrelated.Id], workspace.State.ExpandedRectanglePathIds);
        Assert.Equal(layer.PathIds, Assert.Single(workspace.Layers).PathIds);

        Assert.True(workspace.Undo());
        Assert.Equal(2, workspace.ImportGroups.Count);
        Assert.Equal(2, workspace.SewingHoleOperations.Count);
        Assert.Equal(2, workspace.ConvertLineGroups.Count);
        Assert.Equal(2, workspace.CornerParameters.Count);
        Assert.Equal([edited.Id, unrelated.Id], workspace.State.ExpandedRectanglePathIds);
        Assert.Equal(layer.PathIds, Assert.Single(workspace.Layers).PathIds);

        Assert.True(workspace.Redo());
        Assert.Equal([unrelatedImport.Id], workspace.ImportGroups.Select(group => group.Id));
        Assert.Equal([unrelatedSewing.Id], workspace.SewingHoleOperations.Select(operation => operation.Id));
        Assert.Equal(["convert-unrelated"], workspace.ConvertLineGroups.Select(group => group.Id));
        Assert.Equal([unrelated.Id], workspace.State.ExpandedRectanglePathIds);
        Assert.Equal(layer.PathIds, Assert.Single(workspace.Layers).PathIds);
    }

    [Fact]
    public void InvalidatedCache_SurvivesUnrelatedWorkspaceStateChanges()
    {
        var measurement = new Editor2DMeasurement(
            "m", new(0, 0), new(10, 0), VarName: "d1", Expression: "10",
            IsParametric: true, EvaluatedValue: 10);
        var workspace = Workspace(measurement);
        var moved = DxfCanvasMeasurementEditing.MoveEndpoint(measurement, new(12, 0), start: false);

        workspace.SetMeasurements([moved]);
        Assert.Null(Assert.Single(workspace.Measurements).EvaluatedValue);
        workspace.SetActiveTool(Editor2DTool.Pan);
        Assert.Null(Assert.Single(workspace.Measurements).EvaluatedValue);
        workspace.SetSelectedMeasurement(measurement.Id);
        Assert.Null(Assert.Single(workspace.Measurements).EvaluatedValue);
    }

    private static Editor2DWorkspaceViewModel Workspace(params Editor2DMeasurement[] measurements)
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Measurements = measurements,
            SelectedMeasurementId = measurements[0].Id,
        }, recordHistory: false);
        workspace.ClearHistory();
        return workspace;
    }

    private static void AssertMeasurement(
        Editor2DWorkspaceViewModel workspace, string id, double distance, double evaluated)
    {
        var measurement = workspace.Measurements.Single(item => item.Id == id);
        Assert.Equal(distance, measurement.Distance, 8);
        Assert.Equal(evaluated, measurement.EvaluatedValue!.Value, 8);
    }
}
