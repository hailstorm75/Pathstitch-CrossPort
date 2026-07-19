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
            store.Save(new UserPreferences("Dark", true, true, true));

            var loaded = store.Load();

            Assert.Equal("Dark", loaded.Appearance);
            Assert.True(loaded.ReversePanDirection);
            Assert.True(loaded.GettingStartedDismissed);
            Assert.True(loaded.SupportCardDismissed);
            Assert.True(loaded.ConsolidateSvgStrokes);
            Assert.Equal("strokes", loaded.SvgFillMode);
            Assert.Equal(3.0, loaded.SvgImportThickness);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsMacIconAndIndependentFinderPreviewToggles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-macos-preferences-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            store.Save(new UserPreferences(
                AppIcon: "Dark",
                FinderPreviewDxf: false,
                FinderPreviewStep: true,
                FinderPreviewStch: false));

            var loaded = store.Load();

            Assert.Equal("Dark", loaded.AppIcon);
            Assert.False(loaded.FinderPreviewDxf);
            Assert.True(loaded.FinderPreviewStep);
            Assert.False(loaded.FinderPreviewStch);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MacPreferences_DefaultToAutomaticIconAndEnabledPreviews()
    {
        var defaults = new UserPreferences();

        Assert.Equal("Automatic", defaults.AppIcon);
        Assert.True(defaults.FinderPreviewDxf);
        Assert.True(defaults.FinderPreviewStep);
        Assert.True(defaults.FinderPreviewStch);
        Assert.Equal(MacOSAppIconVariant.Light, MacOSAppIconResolver.Resolve("Light", true));
        Assert.Equal(MacOSAppIconVariant.Dark, MacOSAppIconResolver.Resolve("Dark", false));
        Assert.Equal(MacOSAppIconVariant.Dark, MacOSAppIconResolver.Resolve("Automatic", true));
    }

    [Fact]
    public void MacIntegrationBridgePath_UsesPackagedFrameworksDirectory()
    {
        var candidates = MacOSIntegrationService.GetCandidateLibraryPaths(
            Path.Combine("bundle", "Contents", "MacOS"));

        Assert.Contains(candidates, path =>
            path.EndsWith(
                Path.Combine("Contents", "Frameworks", "libPathstitchMacBridge.dylib"),
                StringComparison.Ordinal));
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
