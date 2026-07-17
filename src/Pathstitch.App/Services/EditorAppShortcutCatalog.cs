using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Domain.App.Models;

namespace Pathstitch.App.Services;

public sealed record EditorAppShortcutDefinition(
    string Identifier,
    string Label,
    string Category,
    string? DefaultGesture);

public static class EditorAppShortcutCatalog
{
    public static IReadOnlyList<EditorAppShortcutDefinition> All { get; } =
    [
        Command(EditorCommandPaletteCatalog.NewIdentifier, "New Project", "File", "Primary+N"),
        Command(EditorCommandPaletteCatalog.OpenIdentifier, "Open Project", "File", "Primary+O"),
        Command(EditorCommandPaletteCatalog.ImportIdentifier, "Import", "File", "Primary+Shift+I"),
        Command(EditorCommandPaletteCatalog.SaveIdentifier, "Save Project", "File", "Primary+S"),
        Command(EditorCommandPaletteCatalog.SaveAsIdentifier, "Save Project As", "File", "Primary+Shift+S"),
        Command(EditorCommandPaletteCatalog.ExportDxfIdentifier, "Export DXF", "File", "Primary+E"),
        Command(EditorCommandPaletteCatalog.ExportSvgIdentifier, "Export SVG", "File", "Primary+Shift+E"),
        Command(EditorCommandPaletteCatalog.ExportPngIdentifier, "Export PNG", "File", null),
        Command(EditorCommandPaletteCatalog.ExportPdfIdentifier, "Export PDF", "File", null),
        Command(EditorCommandPaletteCatalog.StartScreenIdentifier, "Start Screen", "File", null),
        Command(EditorCommandPaletteCatalog.UndoIdentifier, "Undo", "Edit", "Primary+Z"),
        Command(EditorCommandPaletteCatalog.RedoIdentifier, "Redo", "Edit", "Primary+Shift+Z"),
        Command(EditorCommandPaletteCatalog.DeleteIdentifier, "Delete Selection", "Edit", "Delete"),
        Command(EditorCommandPaletteCatalog.ToggleGridIdentifier, "Toggle Grid", "View", "Shift+G"),
        Command(EditorCommandPaletteCatalog.ToggleSnappingIdentifier, "Toggle Snapping", "View", "N"),
        Command(EditorCommandPaletteCatalog.ToggleChainSelectionIdentifier, "Toggle Chain Selection", "View", "A"),
        Command(EditorCommandPaletteCatalog.ZoomInIdentifier, "Zoom In", "View", "Primary+OemPlus"),
        Command(EditorCommandPaletteCatalog.ZoomOutIdentifier, "Zoom Out", "View", "Primary+OemMinus"),
        Command(EditorCommandPaletteCatalog.ZoomToFitIdentifier, "Zoom to Fit", "View", "Primary+D0"),
        Command(EditorCommandPaletteCatalog.SwitchToTwoDIdentifier, "Switch to 2D Mode", "View", null),
        Command(EditorCommandPaletteCatalog.SwitchToThreeDIdentifier, "Switch to 3D Mode", "View", null),
        Command(EditorCommandPaletteCatalog.SwitchToBatchIdentifier, "Switch to Batch Mode", "View", null),
        Command(EditorCommandPaletteCatalog.ClearReferenceImageIdentifier, "Clear Active Reference Image", "View", null),
        Command(EditorCommandPaletteCatalog.SearchIdentifier, "Search Commands", "App", "Primary+K"),
        Command(EditorCommandPaletteCatalog.PreferencesIdentifier, "Preferences", "App", null),
        Command(EditorCommandPaletteCatalog.DocumentationIdentifier, "Documentation", "Help", null),
    ];

    public static IReadOnlyDictionary<string, string?> Resolve(UserPreferences preferences)
    {
        var overrides = preferences.AppCommandShortcuts;
        return All.ToDictionary(
            definition => definition.Identifier,
            definition => overrides is not null
                && overrides.TryGetValue(definition.Identifier, out var value)
                && EditorShortcutGesture.TryNormalize(value, out var normalized)
                    ? normalized
                    : definition.DefaultGesture,
            StringComparer.Ordinal);
    }

    public static EditorAppShortcutDefinition? Find(string identifier)
        => All.FirstOrDefault(definition => string.Equals(definition.Identifier, identifier, StringComparison.Ordinal));

    private static EditorAppShortcutDefinition Command(string id, string label, string category, string? gesture)
        => new(id, label, category, gesture);
}

public static class EditorShortcutGesture
{
    private static readonly string[] ModifierOrder = ["Primary", "Alt", "Shift"];

    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var tokens = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return true;

        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            var modifier = tokens[index] switch
            {
                var token when token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("Control", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("Cmd", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("Command", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("Meta", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("Primary", StringComparison.OrdinalIgnoreCase) => "Primary",
                var token when token.Equals("Alt", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("Option", StringComparison.OrdinalIgnoreCase) => "Alt",
                var token when token.Equals("Shift", StringComparison.OrdinalIgnoreCase) => "Shift",
                _ => string.Empty,
            };
            if (modifier.Length == 0 || !modifiers.Add(modifier))
                return false;
        }

        if (!TryNormalizeKey(tokens[^1], out var keyToken))
            return false;
        normalized = string.Join('+', ModifierOrder.Where(modifiers.Contains).Append(keyToken));
        return true;
    }

    public static bool TryCapture(Key key, KeyModifiers modifiers, out string? normalized)
    {
        normalized = null;
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return false;

        if (!TryNormalizeKey(key.ToString(), out var keyToken))
            return false;

        var parts = new List<string>();
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            parts.Add("Primary");
        if ((modifiers & KeyModifiers.Alt) != 0)
            parts.Add("Alt");
        if ((modifiers & KeyModifiers.Shift) != 0)
            parts.Add("Shift");
        parts.Add(keyToken);
        normalized = string.Join('+', parts);
        return true;
    }

    public static string ToDisplayText(string? canonical, bool? isMacOS = null)
    {
        if (string.IsNullOrWhiteSpace(canonical))
            return string.Empty;
        return canonical.Replace(
            "Primary",
            isMacOS ?? OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl",
            StringComparison.Ordinal);
    }

    public static string? ToToolShortcutText(string? canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical))
            return null;
        if (canonical.Length == 2 && canonical[0] == 'D' && char.IsDigit(canonical[1]))
            return canonical[1].ToString();
        return ToDisplayText(canonical, isMacOS: false);
    }

    public static bool TryCreateKeyGesture(string? canonical, out KeyGesture? gesture, bool? isMacOS = null)
    {
        gesture = null;
        if (!TryNormalize(canonical, out var normalized) || normalized is null)
            return false;

        var tokens = normalized.Split('+');
        if (!Enum.TryParse<Key>(tokens[^1], ignoreCase: true, out var key))
            return false;

        var modifiers = KeyModifiers.None;
        foreach (var token in tokens[..^1])
        {
            modifiers |= token switch
            {
                "Primary" => isMacOS ?? OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control,
                "Alt" => KeyModifiers.Alt,
                "Shift" => KeyModifiers.Shift,
                _ => KeyModifiers.None,
            };
        }
        gesture = new KeyGesture(key, modifiers);
        return true;
    }

    public static string? FindConflict(
        IReadOnlyDictionary<string, string?> appShortcuts,
        IReadOnlyList<EditorToolCustomization> toolShortcuts,
        Func<string, EditorMode?> toolModeResolver)
    {
        var assignedApps = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in EditorAppShortcutCatalog.All)
        {
            if (!appShortcuts.TryGetValue(definition.Identifier, out var value)
                || !TryNormalize(value, out var normalized)
                || normalized is null)
                continue;
            if (assignedApps.TryGetValue(normalized, out var other))
                return $"Shortcut '{ToDisplayText(normalized)}' conflicts between {other} and {definition.Label}.";
            assignedApps[normalized] = definition.Label;
        }

        var assignedTools = new Dictionary<(EditorMode Mode, string Gesture), string>();
        foreach (var tool in toolShortcuts)
        {
            var mode = toolModeResolver(tool.Identifier);
            if (mode is null || !TryNormalize(tool.ShortcutText, out var normalized) || normalized is null)
                continue;
            if (assignedApps.TryGetValue(normalized, out var appLabel))
                return $"Shortcut '{ToDisplayText(normalized)}' conflicts between {appLabel} and {tool.Identifier}.";
            var key = (mode.Value, normalized);
            if (assignedTools.TryGetValue(key, out var otherTool))
                return $"Shortcut '{ToDisplayText(normalized)}' conflicts between {otherTool} and {tool.Identifier}.";
            assignedTools[key] = tool.Identifier;
        }
        return null;
    }

    private static bool TryNormalizeKey(string value, out string normalized)
    {
        normalized = value.Trim();
        if (normalized.Length == 1 && char.IsLetterOrDigit(normalized[0]))
        {
            normalized = normalized.ToUpperInvariant();
            if (char.IsDigit(normalized[0]))
                normalized = $"D{normalized}";
            return true;
        }

        if (normalized.Equals("Backspace", StringComparison.OrdinalIgnoreCase))
            normalized = nameof(Key.Back);
        else if (normalized.Equals("Plus", StringComparison.OrdinalIgnoreCase)
                 || normalized.Equals("=", StringComparison.OrdinalIgnoreCase))
            normalized = nameof(Key.OemPlus);
        else if (normalized.Equals("Minus", StringComparison.OrdinalIgnoreCase)
                 || normalized.Equals("-", StringComparison.OrdinalIgnoreCase))
            normalized = nameof(Key.OemMinus);

        if (!Enum.TryParse<Key>(normalized, ignoreCase: true, out var key)
            || key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return false;
        normalized = key.ToString();
        return true;
    }
}
