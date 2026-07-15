using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class UserPreferencesStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsAppearanceAndNavigation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-preferences-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            store.Save(new UserPreferences("Dark", true, true));

            var loaded = store.Load();

            Assert.Equal("Dark", loaded.Appearance);
            Assert.True(loaded.ReversePanDirection);
            Assert.True(loaded.GettingStartedDismissed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MalformedFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-preferences-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not-json");
            var loaded = new UserPreferencesStore(path).Load();

            Assert.Equal(new UserPreferences(), loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
