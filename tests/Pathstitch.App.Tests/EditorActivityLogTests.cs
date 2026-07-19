using Domain.App.Models;

namespace Pathstitch.App.Tests;

public sealed class EditorActivityLogTests
{
    [Fact]
    public async Task ToggleAndCommandPalette_KeepTrayStateSynchronized()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();

        Assert.False(editor.IsActivityLogExpanded);
        Assert.Equal("Show Log Tray", editor.ActivityLogToggleLabel);

        editor.ToggleActivityLog();

        Assert.True(editor.IsActivityLogExpanded);
        Assert.Equal("Hide Log Tray", editor.ActivityLogToggleLabel);

        await editor.ActivateCommandSearchItemAsync(EditorCommandPaletteCatalog.ToggleActivityLogIdentifier);

        Assert.False(editor.IsActivityLogExpanded);
        Assert.True(editor.CommandSearchResults.Single(item =>
            item.Identifier == EditorCommandPaletteCatalog.ToggleActivityLogIdentifier).IsEnabled);
    }

    [Fact]
    public void CompletedLayerMutation_LogsOnceButRejectedMutationStaysSilent()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        editor.TwoDDocument = Editor2DWorkspaceState.Empty.Document with
        {
            Paths =
            [
                new Editor2DPreviewPath(
                    "line",
                    "LINE",
                    [new Editor2DPoint(0, 0), new Editor2DPoint(5, 0)],
                    false),
            ],
        };
        var layer = Assert.Single(editor.TwoDLayers);

        editor.RenameTwoDLayer(layer.Id, "Cut");
        editor.RenameTwoDLayer(layer.Id, "   ");

        var activity = Assert.Single(editor.ActivityLog);
        Assert.Equal("Rename Layer", activity.Action);
        Assert.Equal("Cut", activity.Details);
        Assert.Equal(layer.Id, activity.LayerId);
    }
}
