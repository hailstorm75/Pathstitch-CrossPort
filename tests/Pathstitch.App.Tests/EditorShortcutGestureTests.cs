using Avalonia.Input;
using Domain.App.Models;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class EditorShortcutGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+n", "Primary+Shift+N")]
    [InlineData("Cmd+Option+F5", "Primary+Alt+F5")]
    [InlineData("1", "D1")]
    [InlineData("Delete", "Delete")]
    public void Normalize_UsesPortableCanonicalGestures(string input, string expected)
    {
        Assert.True(EditorShortcutGesture.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void CaptureAndParse_PreserveModifiersAndSpecialKeys()
    {
        Assert.True(EditorShortcutGesture.TryCapture(
            Key.F5,
            KeyModifiers.Control | KeyModifiers.Shift,
            out var canonical));
        Assert.Equal("Primary+Shift+F5", canonical);
        Assert.True(EditorShortcutGesture.TryCreateKeyGesture(canonical, out var gesture, isMacOS: false));
        Assert.Equal(Key.F5, gesture!.Key);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, gesture.KeyModifiers);
        Assert.Equal("Cmd+Shift+F5", EditorShortcutGesture.ToDisplayText(canonical, isMacOS: true));
        Assert.Equal("Ctrl+Shift+F5", EditorShortcutGesture.ToToolShortcutText(canonical));
        Assert.Equal("1", EditorShortcutGesture.ToToolShortcutText("D1"));
    }

    [Fact]
    public void ConflictDetection_IsGlobalForAppCommandsButModeScopedForTools()
    {
        var app = EditorAppShortcutCatalog.All.ToDictionary(
            definition => definition.Identifier,
            definition => definition.DefaultGesture,
            StringComparer.Ordinal);
        app[EditorCommandPaletteCatalog.NewIdentifier] = "C";
        EditorToolCustomization[] tools =
        [
            new("2d.circle", 0, "C"),
            new("3d.move", 0, "C"),
        ];

        var conflict = EditorShortcutGesture.FindConflict(
            app,
            tools,
            identifier => identifier.StartsWith("2d.", StringComparison.Ordinal)
                ? EditorMode.TwoD
                : EditorMode.ThreeD);

        Assert.Contains("New Project", conflict ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreferencesStore_RoundTripsAppShortcutOverrides()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-shortcuts-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            store.Save(new UserPreferences(AppCommandShortcuts: new Dictionary<string, string>
            {
                [EditorCommandPaletteCatalog.NewIdentifier] = "Primary+Shift+N",
                [EditorCommandPaletteCatalog.SaveIdentifier] = string.Empty,
            }));

            var resolved = EditorAppShortcutCatalog.Resolve(store.Load());
            Assert.Equal("Primary+Shift+N", resolved[EditorCommandPaletteCatalog.NewIdentifier]);
            Assert.Null(resolved[EditorCommandPaletteCatalog.SaveIdentifier]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
