using Domain.App.Models;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class Editor2DDimensionDependencyTests
{
    [Fact]
    public void ExpressionEdit_RecalculatesDependentsAtomicallyAndRejectsCycles()
    {
        var first = new Editor2DMeasurement(
            "first", new(0, 0), new(10, 0), VarName: "d1", Expression: "10", IsParametric: true);
        var second = new Editor2DMeasurement(
            "second", new(0, 10), new(20, 10), VarName: "d2", Expression: "d1 * 2", IsParametric: true);
        var driven = new Editor2DMeasurement(
            "driven", new(0, 20), new(20, 20), VarName: "d3", Expression: "d2 + 5",
            Driven: true, IsParametric: true);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Measurements = [first, second, driven],
            SelectedMeasurementId = first.Id,
        }, recordHistory: false);
        workspace.ClearHistory();

        Assert.True(workspace.TrySetMeasurementExpression(first.Id, "5", out var error), error);

        Assert.Equal(5, workspace.Measurements.Single(item => item.Id == first.Id).Distance, 8);
        Assert.Equal(10, workspace.Measurements.Single(item => item.Id == second.Id).Distance, 8);
        Assert.Equal(20, workspace.Measurements.Single(item => item.Id == driven.Id).Distance, 8);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(10, workspace.Measurements.Single(item => item.Id == first.Id).Distance, 8);
        Assert.Equal(20, workspace.Measurements.Single(item => item.Id == second.Id).Distance, 8);
        Assert.True(workspace.Redo());
        Assert.Equal(5, workspace.Measurements.Single(item => item.Id == first.Id).Distance, 8);

        workspace.ClearHistory();
        var beforeCycle = workspace.Measurements.ToArray();
        Assert.False(workspace.TrySetMeasurementExpression(first.Id, "d2", out var cycleError));
        Assert.NotEmpty(cycleError);
        Assert.Equal(beforeCycle, workspace.Measurements);
        Assert.False(workspace.CanUndo);
    }
}
