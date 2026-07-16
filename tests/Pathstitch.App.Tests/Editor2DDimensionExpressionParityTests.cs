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
