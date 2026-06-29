using CommunityToolkit.Mvvm.ComponentModel;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed class EditorSidebarToolItemViewModel : ObservableObject
{
    private bool _isActive;

    public EditorSidebarToolItemViewModel(EditorSidebarToolDefinition definition)
    {
        Key = definition.Key;
        Tool = definition.Tool;
        Action = definition.Action;
        Label = definition.Label;
        Hint = definition.Hint;
        ShortcutText = definition.ShortcutText;
        TooltipText = string.IsNullOrWhiteSpace(definition.ShortcutText)
            ? $"{definition.Label} - {definition.Hint}"
            : $"{definition.Label} ({definition.ShortcutText}) - {definition.Hint}";
        IconPathData = definition.IconPathData;
        StartsSection = definition.StartsSection;
        IsEnabled = true;
    }

    public string Key { get; }

    public Editor3DTool? Tool { get; }

    public EditorSidebarAction? Action { get; }

    public string Label { get; }

    public string Hint { get; }

    public string? ShortcutText { get; }

    public string TooltipText { get; }

    public string IconPathData { get; }

    public bool StartsSection { get; }

    private bool _isEnabled;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}
