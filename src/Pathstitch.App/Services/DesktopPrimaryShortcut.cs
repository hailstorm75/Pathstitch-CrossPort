using System;
using Avalonia.Input;

namespace Pathstitch.App.Services;

internal static class DesktopPrimaryShortcut
{
    public static KeyGesture Create(Key key, bool shift = false, bool? isMacOS = null)
        => new(key, Modifiers(shift, isMacOS));

    public static bool Matches(
        KeyModifiers actual,
        bool shift = false,
        bool? isMacOS = null)
        => actual == Modifiers(shift, isMacOS);

    private static KeyModifiers Modifiers(bool shift, bool? isMacOS)
    {
        var modifiers = (isMacOS ?? OperatingSystem.IsMacOS())
            ? KeyModifiers.Meta
            : KeyModifiers.Control;
        return shift ? modifiers | KeyModifiers.Shift : modifiers;
    }
}
