namespace Pathstitch.App.Tests;

using Domain.App.Models;
using Pathstitch.App.Controls;

public sealed class DxfPreviewCanvasInteractionTests
{
    [Theory]
    [InlineData(Editor2DTool.Select)]
    [InlineData(Editor2DTool.Scale)]
    public void SelectionHandles_AreVisibleForSelectionEditingTools(Editor2DTool activeTool)
    {
        Assert.True(DxfPreviewCanvas.ShouldDrawSelectionHandles(activeTool));
    }

    public static IEnumerable<object[]> NonSelectionEditingTools =>
        Enum.GetValues<Editor2DTool>()
            .Where(tool => tool is not Editor2DTool.Select and not Editor2DTool.Scale)
            .Select(tool => new object[] { tool });

    [Theory]
    [MemberData(nameof(NonSelectionEditingTools))]
    public void SelectionHandles_AreHiddenForOtherTools(Editor2DTool activeTool)
    {
        Assert.False(DxfPreviewCanvas.ShouldDrawSelectionHandles(activeTool));
    }
}
