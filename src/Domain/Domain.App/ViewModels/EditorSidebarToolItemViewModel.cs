using CommunityToolkit.Mvvm.ComponentModel;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed class EditorSidebarToolItemViewModel : ObservableObject
{
    private bool _isActive;

    public EditorSidebarToolItemViewModel(EditorToolDescriptor descriptor)
    {
        Identifier = descriptor.Identifier;
        Mode = descriptor.Mode;
        Key = descriptor.CommandKey;
        Tool = descriptor.ThreeDTool;
        TwoDTool = descriptor.TwoDTool;
        Action = descriptor.Action;
        Label = descriptor.Label;
        Hint = descriptor.Hint;
        ShortcutText = descriptor.ShortcutText;
        TooltipText = string.IsNullOrWhiteSpace(descriptor.ShortcutText)
            ? $"{descriptor.Label} - {descriptor.Hint}"
            : $"{descriptor.Label} ({descriptor.ShortcutText}) - {descriptor.Hint}";
        IconKey = descriptor.IconKey;
        IconPathData = descriptor.IconPathData;
        HasIconPathData = !string.IsNullOrWhiteSpace(descriptor.IconPathData);
        FallbackGlyph = string.IsNullOrWhiteSpace(descriptor.Label)
            ? "?"
            : descriptor.Label[..1].ToUpperInvariant();
        InspectorPanelKey = descriptor.InspectorPanelKey;
        GroupKey = descriptor.GroupKey;
        Order = descriptor.Order;
        StartsSection = descriptor.StartsSection;
        IsEnabled = descriptor.IsEnabled;
        IsActive = descriptor.IsSelected;
    }

    public string Identifier { get; }

    public EditorMode Mode { get; }

    public string Key { get; }

    public Editor3DTool? Tool { get; }

    public Editor2DTool? TwoDTool { get; }

    public EditorSidebarAction? Action { get; }

    public string Label { get; }

    public string Hint { get; }

    public string? ShortcutText { get; }

    public string TooltipText { get; }

    public string IconKey { get; }

    public string IconPathData { get; }

    public bool HasIconPathData { get; }

    public string FallbackGlyph { get; }

    public string? InspectorPanelKey { get; }

    public string GroupKey { get; }

    public int Order { get; }

    public bool StartsSection { get; }

    private bool _isEnabled;
    private bool _isCustomizationMode;
    private bool _isCommandSearchSelected;

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

    public bool IsCustomizationMode
    {
        get => _isCustomizationMode;
        set => SetProperty(ref _isCustomizationMode, value);
    }

    public bool IsCommandSearchSelected
    {
        get => _isCommandSearchSelected;
        set => SetProperty(ref _isCommandSearchSelected, value);
    }
}
