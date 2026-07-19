using Domain.App.Models;

namespace Pathstitch.App.Tests;

public sealed class EditorLearnModeTests
{
    [Fact]
    public async Task ToggleAndCommandPalette_KeepLearnModeStateSynchronized()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();

        Assert.True(editor.LearnModeEnabled);
        Assert.False(editor.IsDirty);

        editor.ToggleLearnMode();

        Assert.False(editor.LearnModeEnabled);
        Assert.True(editor.IsDirty);
        Assert.Equal("Contextual tool guidance is hidden", editor.LearnModeToggleHelpText);

        await editor.ActivateCommandSearchItemAsync(EditorCommandPaletteCatalog.ToggleLearnModeIdentifier);

        Assert.True(editor.LearnModeEnabled);
        Assert.True(editor.CommandSearchResults.Single(item =>
            item.Identifier == EditorCommandPaletteCatalog.ToggleLearnModeIdentifier).IsEnabled);
    }
}
