using System;
using System.Linq;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private IReadOnlyList<EditorToolDescriptor> _toolDescriptors = EditorToolCatalog.All;
    private IReadOnlyList<EditorSidebarToolItemViewModel> _sidebarTools = CreateSidebarTools(EditorToolCatalog.All);
    private readonly IReadOnlyList<EditorSidebarToolItemViewModel> _commandPaletteOnlyItems =
        CreateSidebarTools(EditorCommandPaletteCatalog.SearchOnly);
    private string _commandSearchQuery = string.Empty;
    private bool _isToolbarCustomizationMode;

    public IReadOnlyList<EditorSidebarToolItemViewModel> SidebarTools
        => _sidebarTools
            .Where(tool => tool.Mode == ActiveEditorMode)
            .OrderBy(tool => tool.Order)
            .ThenBy(tool => tool.Identifier, StringComparer.Ordinal)
            .ToArray();

    public string CommandSearchQuery
    {
        get => _commandSearchQuery;
        set
        {
            if (!SetProperty(ref _commandSearchQuery, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(CommandSearchResults));
            OnPropertyChanged(nameof(IsCommandSearchOpen));
            OnPropertyChanged(nameof(IsCommandSearchEmpty));
        }
    }

    public bool IsCommandSearchOpen => !string.IsNullOrWhiteSpace(CommandSearchQuery);

    public bool IsCommandSearchEmpty => IsCommandSearchOpen && CommandSearchResults.Count == 0;

    public IReadOnlyList<EditorSidebarToolItemViewModel> CommandSearchResults
    {
        get
        {
            var query = CommandSearchQuery.Trim();
            if (query.Length == 0)
                return SidebarTools;

            return SidebarTools
                .Concat(_commandPaletteOnlyItems.Where(item => item.Mode == ActiveEditorMode))
                .Where(item => item.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Hint.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.ShortcutText?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();
        }
    }

    public IReadOnlyList<EditorToolCustomization> ToolCustomizations
        => _toolDescriptors
            .Select(descriptor => new EditorToolCustomization(
                descriptor.Identifier,
                descriptor.Order,
                descriptor.ShortcutText))
            .ToArray();

    public bool IsToolbarCustomizationMode
    {
        get => _isToolbarCustomizationMode;
        set
        {
            if (!SetProperty(ref _isToolbarCustomizationMode, value))
                return;
            foreach (var tool in _sidebarTools)
                tool.IsCustomizationMode = value;
        }
    }

    public Editor3DTool ActiveTool
    {
        get => _threeDWorkspace.ActiveTool;
        private set
        {
            if (!_threeDWorkspace.ActivateTool(value))
                return;

            OnPropertyChanged();

            SyncSidebarToolStates();
            OnPropertyChanged(nameof(IsSelectToolActive));
            OnPropertyChanged(nameof(IsMoveToolActive));
            OnPropertyChanged(nameof(IsPlaneToolActive));
            OnPropertyChanged(nameof(IsMeasureToolActive));
            OnPropertyChanged(nameof(IsUnfoldToolActive));
            OnPropertyChanged(nameof(IsOutputToolActive));
            OnPropertyChanged(nameof(ActiveToolLabel));
            OnPropertyChanged(nameof(CanEditSelectedBodyOffset));
            OnPropertyChanged(nameof(MoveBodiesHint));
            OnPropertyChanged(nameof(ShowSelectionPanel));
            OnPropertyChanged(nameof(ShowMoveBodiesPanel));
            OnPropertyChanged(nameof(ShowProjectionPanel));
            OnPropertyChanged(nameof(ShowMeasurePanel));
            OnPropertyChanged(nameof(ShowUnfoldPanel));
            OnPropertyChanged(nameof(ShowOutputPanel));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public bool IsSelectToolActive => ActiveTool == Editor3DTool.Select;

    public bool IsMoveToolActive => ActiveTool == Editor3DTool.Move;

    public bool IsPlaneToolActive => ActiveTool == Editor3DTool.Plane;

    public bool IsMeasureToolActive => ActiveTool == Editor3DTool.Measure;

    public bool IsUnfoldToolActive => ActiveTool == Editor3DTool.Unfold;

    public bool IsOutputToolActive => ActiveTool == Editor3DTool.Output;

    public string ActiveToolLabel => SidebarTools.FirstOrDefault(tool => tool.IsActive)?.Label
        ?? (ActiveEditorMode switch
        {
            EditorMode.TwoD => TwoDActiveTool.ToString(),
            EditorMode.ThreeD => ActiveTool.ToString(),
            EditorMode.Batch => "Batch",
            _ => "Editor",
        });

    public bool ShowSelectionPanel
        => IsShowing3DWorkspace
            && ActiveTool is Editor3DTool.Select or Editor3DTool.Measure or Editor3DTool.Unfold;

    public bool ShowMoveBodiesPanel => IsShowing3DWorkspace && IsMoveToolActive;

    public bool ShowProjectionPanel => IsShowing3DWorkspace && IsPlaneToolActive;

    public bool ShowMeasurePanel => IsShowing3DWorkspace && IsMeasureToolActive;

    public bool ShowUnfoldPanel => IsShowing3DWorkspace && IsUnfoldToolActive;

    public bool ShowOutputPanel => IsShowing3DWorkspace && IsOutputToolActive;

    public void ActivateSidebarItem(string itemKey)
    {
        var item = SidebarTools.FirstOrDefault(tool =>
            tool.Mode == ActiveEditorMode
            && string.Equals(tool.Key, itemKey, StringComparison.Ordinal));
        if (item is null)
            return;

        if (item.Tool is Editor3DTool tool)
        {
            ActivateTool(tool);
            return;
        }

        if (item.TwoDTool is Editor2DTool twoDTool)
        {
            TwoDActiveTool = twoDTool;
            return;
        }

        if (item.Action is EditorSidebarAction action)
            ExecuteSidebarAction(action);
    }

    public void ActivateCommandSearchItem(string identifier)
    {
        var item = CommandSearchResults.FirstOrDefault(candidate =>
            string.Equals(candidate.Identifier, identifier, StringComparison.Ordinal));
        if (item is null)
            return;

        if (!ActivateCommandPaletteOnlyItem(item.Identifier))
            ActivateSidebarItem(item.Key);
        CommandSearchQuery = string.Empty;
    }

    public void CustomizeTool(string identifier, int order, string? shortcutText)
    {
        if (!_toolDescriptors.Any(descriptor => string.Equals(
                descriptor.Identifier,
                identifier,
                StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Unknown editor tool identifier '{identifier}'.", nameof(identifier));
        }

        var customizations = ToolCustomizations
            .Select(customization => string.Equals(customization.Identifier, identifier, StringComparison.Ordinal)
                ? customization with { Order = order, ShortcutText = shortcutText }
                : customization)
            .ToArray();
        ApplyToolCustomizations(customizations, requestPersistence: true);
    }

    public bool MoveToolCustomization(string identifier, int direction)
    {
        var ordered = _toolDescriptors
            .Where(descriptor => descriptor.Mode == ActiveEditorMode)
            .OrderBy(descriptor => descriptor.Order)
            .ThenBy(descriptor => descriptor.Identifier, StringComparer.Ordinal)
            .ToArray();
        var index = Array.FindIndex(ordered, descriptor => string.Equals(
            descriptor.Identifier,
            identifier,
            StringComparison.Ordinal));
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= ordered.Length)
            return false;

        var first = ordered[index];
        var second = ordered[target];
        var customizations = ToolCustomizations
            .Select(customization => customization.Identifier switch
            {
                var value when string.Equals(value, first.Identifier, StringComparison.Ordinal)
                    => customization with { Order = second.Order },
                var value when string.Equals(value, second.Identifier, StringComparison.Ordinal)
                    => customization with { Order = first.Order },
                _ => customization,
            })
            .ToArray();
        ApplyToolCustomizations(customizations, requestPersistence: true);
        return true;
    }

    public void ResetToolbarCustomizationForActiveMode()
    {
        var defaults = EditorToolCatalog.ForMode(ActiveEditorMode)
            .ToDictionary(descriptor => descriptor.Identifier, StringComparer.Ordinal);
        var customizations = ToolCustomizations
            .Select(customization => defaults.TryGetValue(customization.Identifier, out var descriptor)
                ? customization with { Order = descriptor.Order, ShortcutText = descriptor.ShortcutText }
                : customization)
            .ToArray();
        ApplyToolCustomizations(customizations, requestPersistence: true);
    }

    public void ResetToolbarCustomizationForAllModes()
    {
        var defaults = EditorToolCatalog.All
            .ToDictionary(descriptor => descriptor.Identifier, StringComparer.Ordinal);
        var customizations = ToolCustomizations
            .Select(customization => defaults.TryGetValue(customization.Identifier, out var descriptor)
                ? customization with { Order = descriptor.Order, ShortcutText = descriptor.ShortcutText }
                : customization)
            .ToArray();
        ApplyToolCustomizations(customizations, requestPersistence: true);
    }

    public bool TryActivateEditorShortcut(string shortcutText)
    {
        if (ActiveEditorMode == EditorMode.Batch || string.IsNullOrWhiteSpace(shortcutText))
            return false;

        var descriptor = EditorToolCatalog.FindByShortcut(_toolDescriptors, ActiveEditorMode, shortcutText);
        if (descriptor is not null)
        {
            ActivateSidebarItem(descriptor.CommandKey);
            return true;
        }

        return shortcutText.Trim().ToLowerInvariant() switch
        {
            "delete-selection" when IsShowingTwoDWorkspace => DeleteTwoDSelection(),
            "escape" when IsShowingTwoDWorkspace => CancelTwoDEscape(),
            "escape" => HandleEscapeShortcut(),
            _ => false,
        };
    }

    public void ActivateTool(Editor3DTool tool)
    {
        switch (tool)
        {
            case Editor3DTool.Select:
                ActivateSelectTool();
                break;

            case Editor3DTool.Move:
                ActivateMoveTool();
                break;

            case Editor3DTool.Plane:
                ActivatePlaneTool();
                break;

            case Editor3DTool.Measure:
                ActivateMeasureTool();
                break;

            case Editor3DTool.Unfold:
                ActivateUnfoldTool();
                break;

            case Editor3DTool.Output:
                ActivateOutputTool();
                break;
        }
    }

    public void ActivateSelectTool()
    {
        var wasMoveToolActive = IsMoveToolActive;
        ActiveTool = Editor3DTool.Select;
        IsPlaneSelectionActive = false;
        if (wasMoveToolActive)
            SelectedBodyIndex = null;

        RequestPlaneSelectionStateSync();
        RequestBodyMoveStateSync();
    }

    public void ActivateMoveTool()
    {
        ActiveTool = Editor3DTool.Move;
        IsPlaneSelectionActive = false;
        RequestPlaneSelectionStateSync();
        RequestBodyMoveStateSync();
    }

    public void ActivatePlaneTool()
    {
        var wasMoveToolActive = IsMoveToolActive;
        ActiveTool = Editor3DTool.Plane;

        if (SelectedFaces.Count != 1 && SelectedFaces.Count > 0)
        {
            SelectedFaces = [];
            RefreshSelectionState();
            RequestViewportScript(BuildSetSelectedFacesScript(SelectedFaces));
        }

        IsPlaneSelectionActive = true;
        if (wasMoveToolActive)
            SelectedBodyIndex = null;

        EnsureProjectionWorkspaceInitializedForPlaneTool();
        RequestPlaneSelectionStateSync();
        RequestBodyMoveStateSync();
    }

    public void ActivateMeasureTool()
    {
        var wasMoveToolActive = IsMoveToolActive;
        ActiveTool = Editor3DTool.Measure;
        IsPlaneSelectionActive = false;
        if (wasMoveToolActive)
            SelectedBodyIndex = null;

        if (NormalizeSelectionForActiveTool(syncViewport: true))
            StatusText = "Measure keeps one active face; updated to the latest selection";

        RequestPlaneSelectionStateSync();
        RequestBodyMoveStateSync();
        RequestDistortionRefresh(TimeSpan.FromMilliseconds(50));
    }

    public void ActivateUnfoldTool()
    {
        var wasMoveToolActive = IsMoveToolActive;
        ActiveTool = Editor3DTool.Unfold;
        IsPlaneSelectionActive = false;
        if (wasMoveToolActive)
            SelectedBodyIndex = null;

        RequestPlaneSelectionStateSync();
        RequestBodyMoveStateSync();
    }

    public void ActivateOutputTool()
    {
        var wasMoveToolActive = IsMoveToolActive;
        ActiveTool = Editor3DTool.Output;
        IsPlaneSelectionActive = false;
        if (wasMoveToolActive)
            SelectedBodyIndex = null;

        RequestPlaneSelectionStateSync();
        RequestBodyMoveStateSync();
    }

    private void SyncSidebarToolStates()
    {
        foreach (var tool in _sidebarTools)
        {
            tool.IsActive = tool.Mode == ActiveEditorMode
                && (tool.Tool == ActiveTool
                    || tool.TwoDTool == TwoDActiveTool
                    || tool.Action is EditorSidebarAction.ToggleOrthographic && ThreeDOrthographic);
            tool.IsEnabled = tool.Mode == ActiveEditorMode
                && (tool.Action switch
                {
                    EditorSidebarAction.FrameHome => CanFrameHome,
                    EditorSidebarAction.FlipSelectionHorizontal or EditorSidebarAction.FlipSelectionVertical => HasTwoDSelection,
                    _ => true,
                });
        }
    }

    private static IReadOnlyList<EditorSidebarToolItemViewModel> CreateSidebarTools(
        IReadOnlyList<EditorToolDescriptor> descriptors)
    {
        var tools = descriptors
            .Select(static descriptor => new EditorSidebarToolItemViewModel(descriptor))
            .ToArray();

        var activeTool = tools.FirstOrDefault(static tool => tool.Tool == Editor3DTool.Select);
        if (activeTool is not null)
            activeTool.IsActive = true;

        foreach (var tool in tools.Where(static tool => tool.Action is EditorSidebarAction.FrameHome))
            tool.IsEnabled = false;

        return tools;
    }

    private void ApplyToolCustomizations(
        IReadOnlyList<EditorToolCustomization>? customizations,
        bool requestPersistence)
    {
        _toolDescriptors = EditorToolCatalog.ApplyCustomizations(customizations);
        _sidebarTools = CreateSidebarTools(_toolDescriptors);
        foreach (var tool in _sidebarTools)
            tool.IsCustomizationMode = IsToolbarCustomizationMode;
        SyncSidebarToolStates();
        OnPropertyChanged(nameof(SidebarTools));
        OnPropertyChanged(nameof(CommandSearchResults));
        OnPropertyChanged(nameof(IsCommandSearchEmpty));
        OnPropertyChanged(nameof(ToolCustomizations));
        OnPropertyChanged(nameof(ActiveToolLabel));

        if (requestPersistence)
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
    }

    private void ExecuteSidebarAction(EditorSidebarAction action)
    {
        switch (action)
        {
            case EditorSidebarAction.FrameHome:
                if (!CanFrameHome)
                    return;

                if (IsShowingTwoDWorkspace)
                {
                    FrameTwoDToContent();
                    StatusText = "Framing visible 2D workspace";
                }
                else
                {
                    RequestViewportScript(GetHomeFrameScript());
                    StatusText = "Framing visible 3D workspace";
                }
                break;

            case EditorSidebarAction.ToggleOrthographic:
                ToggleOrthographic();
                StatusText = ThreeDOrthographic ? "Orthographic camera active" : "Perspective camera active";
                break;

            case EditorSidebarAction.DuplicateSelection:
                DuplicateTwoDSelection();
                break;

            case EditorSidebarAction.FlipSelectionHorizontal:
                FlipTwoDSelection(horizontal: true);
                break;

            case EditorSidebarAction.FlipSelectionVertical:
                FlipTwoDSelection(horizontal: false);
                break;
        }
    }

    private bool ActivateCommandPaletteOnlyItem(string identifier)
    {
        if (!IsShowingTwoDWorkspace)
            return false;

        switch (identifier)
        {
            case EditorCommandPaletteCatalog.ToggleGridIdentifier:
                ToggleTwoDGrid();
                return true;
            case EditorCommandPaletteCatalog.ToggleSnappingIdentifier:
                ToggleTwoDSnapping();
                return true;
            case EditorCommandPaletteCatalog.ToggleChainSelectionIdentifier:
                ToggleTwoDChainSelection();
                return true;
            case EditorCommandPaletteCatalog.ZoomInIdentifier:
                ZoomTwoDIn();
                return true;
            case EditorCommandPaletteCatalog.ZoomOutIdentifier:
                ZoomTwoDOut();
                return true;
            case EditorCommandPaletteCatalog.ZoomToFitIdentifier:
                FrameTwoDToContent();
                return true;
            default:
                return false;
        }
    }

    private bool HandleEscapeShortcut()
    {
        if (IsPlaneSelectionActive)
        {
            CancelPlaneSelection();
            StatusText = "Projection plane selection cancelled";
            return true;
        }

        if (HasFaceSelection)
        {
            ClearSelectedFaces();
            return true;
        }

        if (HasSelectedBody)
        {
            ClearSelectedBody();
            return true;
        }

        return false;
    }

    public bool CancelTwoDEscape()
    {
        if (IsTwoDCornerToolActive)
        {
            CancelTwoDCornerToolSession(exitTool: true);
            return true;
        }

        if (IsTwoDOffsetToolActive)
        {
            CancelTwoDOffset(exitTool: true);
            return true;
        }

        if (IsTwoDScaleToolActive)
        {
            CancelTwoDScaleAndExit();
            return true;
        }

        if (IsTwoDMirrorToolActive)
        {
            CancelTwoDMirror(exitTool: true);
            return true;
        }

        var handled = false;
        if (HasTwoDSelectedMeasurement)
        {
            ClearTwoDSelectedMeasurement();
            handled = true;
        }

        if (HasTwoDSelection)
        {
            ClearTwoDSelection();
            handled = true;
        }

        if (TwoDActiveTool != Editor2DTool.Select)
        {
            TwoDActiveTool = Editor2DTool.Select;
            handled = true;
        }

        return handled;
    }

}
