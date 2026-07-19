using CommunityToolkit.Mvvm.ComponentModel;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed class EditorSidebarToolItemViewModel : ObservableObject
{
    private bool _isActive;
    private string? _shortcutText;
    private string _tooltipText;

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
        _shortcutText = descriptor.ShortcutText;
        _tooltipText = string.IsNullOrWhiteSpace(descriptor.ShortcutText)
            ? $"{descriptor.Label} - {descriptor.Hint}"
            : $"{descriptor.Label} ({descriptor.ShortcutText}) - {descriptor.Hint}";
        ContextualHelpText = descriptor.TwoDTool is { } twoDTool
            ? EditorPageViewModel.GetTwoDToolHint(twoDTool)
            : _tooltipText;
        IconKey = descriptor.IconKey;
        IconPathData = descriptor.IconPathData;
        HasIconPathData = !string.IsNullOrWhiteSpace(descriptor.IconPathData);
        FallbackGlyph = string.IsNullOrWhiteSpace(descriptor.Label)
            ? "?"
            : descriptor.Label[..1].ToUpperInvariant();
        InspectorPanelKey = descriptor.InspectorPanelKey;
        GroupKey = descriptor.GroupKey;
        Order = descriptor.Order;
        Container = descriptor.Container;
        CanPlaceInShapes = descriptor.CanPlaceInShapes;

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

    public string? ShortcutText
    {
        get => _shortcutText;
        private set => SetProperty(ref _shortcutText, value);
    }

    public string TooltipText
    {
        get => _tooltipText;
        private set => SetProperty(ref _tooltipText, value);
    }

    public string ContextualHelpText { get; }

    public string IconKey { get; }

    public string IconPathData { get; }

    public bool HasIconPathData { get; }

    public EditorToolbarContainer Container { get; }

    public bool CanPlaceInShapes { get; }

    public bool IsInMainContainer => Container == EditorToolbarContainer.Main;

    public bool IsInShapesContainer => Container == EditorToolbarContainer.Shapes;

    public bool IsInMoreContainer => Container == EditorToolbarContainer.More;

    public bool CanMoveToShapes => CanPlaceInShapes && !IsInShapesContainer;

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

    public void UpdateShortcut(string? shortcutText)
    {
        ShortcutText = shortcutText;
        TooltipText = string.IsNullOrWhiteSpace(shortcutText)
            ? $"{Label} - {Hint}"
            : $"{Label} ({shortcutText}) - {Hint}";
    }
}
