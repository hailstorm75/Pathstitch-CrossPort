using Avalonia.Input;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class DesktopPrimaryShortcutTests
{
    [Theory]
    [InlineData(false, false, KeyModifiers.Control)]
    [InlineData(false, true, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(true, false, KeyModifiers.Meta)]
    [InlineData(true, true, KeyModifiers.Meta | KeyModifiers.Shift)]
    public void Create_UsesPlatformPrimaryModifier(bool isMacOS, bool shift, KeyModifiers expected)
    {
        var gesture = DesktopPrimaryShortcut.Create(Key.E, shift, isMacOS);

        Assert.Equal(Key.E, gesture.Key);
        Assert.Equal(expected, gesture.KeyModifiers);
        Assert.True(DesktopPrimaryShortcut.Matches(expected, shift, isMacOS));
        Assert.False(DesktopPrimaryShortcut.Matches(expected | KeyModifiers.Alt, shift, isMacOS));
    }
}
