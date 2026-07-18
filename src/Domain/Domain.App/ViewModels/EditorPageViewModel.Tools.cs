using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private IReadOnlyList<EditorToolDescriptor> _toolDescriptors = EditorToolCatalog.All;
    private IReadOnlyList<EditorSidebarToolItemViewModel> _sidebarTools = CreateSidebarTools(EditorToolCatalog.All);
    private readonly IReadOnlyList<EditorSidebarToolItemViewModel> _commandPaletteOnlyItems =
        CreateSidebarTools(EditorCommandPaletteCatalog.SearchOnly);
    private string _commandSearchQuery = string.Empty;
    private bool _isCommandSearchOpen;
    private bool _isCommandPaletteActionRunning;
    private bool _isToolbarCustomizationMode;

    public event EventHandler<EditorCommandPaletteHostAction>? CommandPaletteHostActionRequested;

    public IReadOnlyList<EditorSidebarToolItemViewModel> SidebarTools
        => _sidebarTools
            .Where(tool => tool.Mode == ActiveEditorMode)
            .OrderBy(tool => tool.Container)
            .ThenBy(tool => tool.Order)
            .ThenBy(tool => tool.Identifier, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<EditorSidebarToolItemViewModel> MainToolbarTools
        => ToolbarTools(EditorToolbarContainer.Main);

    public IReadOnlyList<EditorSidebarToolItemViewModel> ShapeToolbarTools
        => ToolbarTools(EditorToolbarContainer.Shapes);

    public IReadOnlyList<EditorSidebarToolItemViewModel> MoreToolbarTools
        => ToolbarTools(EditorToolbarContainer.More);

    public bool HasShapeToolbarTools => ShapeToolbarTools.Count > 0;

    public bool HasMoreToolbarTools => MoreToolbarTools.Count > 0;

    private IReadOnlyList<EditorSidebarToolItemViewModel> ToolbarTools(EditorToolbarContainer container)
        => _sidebarTools
            .Where(tool => tool.Mode == ActiveEditorMode && tool.Container == container)
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

    public bool IsCommandSearchOpen
        => _isCommandSearchOpen || !string.IsNullOrWhiteSpace(CommandSearchQuery);

    public bool IsCommandSearchEmpty => IsCommandSearchOpen && CommandSearchResults.Count == 0;

    public IReadOnlyList<EditorSidebarToolItemViewModel> CommandSearchResults
    {
        get
        {
            SyncCommandPaletteOnlyStates();
            var query = CommandSearchQuery.Trim();
            var commands = SidebarTools
                .Concat(_commandPaletteOnlyItems.Where(item => item.Mode == ActiveEditorMode))
                .ToArray();
            if (query.Length == 0)
                return commands;

            return commands
                .Where(item => item.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Hint.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.GroupKey.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.ShortcutText?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(item => item.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToArray();
        }
    }

    public void OpenCommandSearch()
    {
        CommandSearchQuery = string.Empty;
        if (_isCommandSearchOpen)
            return;
        _isCommandSearchOpen = true;
        OnPropertyChanged(nameof(IsCommandSearchOpen));
        OnPropertyChanged(nameof(CommandSearchResults));
        OnPropertyChanged(nameof(IsCommandSearchEmpty));
    }

    public void CloseCommandSearch()
    {
        CommandSearchQuery = string.Empty;
        if (!_isCommandSearchOpen)
            return;
        _isCommandSearchOpen = false;
        OnPropertyChanged(nameof(IsCommandSearchOpen));
        OnPropertyChanged(nameof(IsCommandSearchEmpty));
    }

    public IReadOnlyList<EditorToolCustomization> ToolCustomizations
        => _toolDescriptors
            .Select(descriptor => new EditorToolCustomization(
                descriptor.Identifier,
                descriptor.Order,
                descriptor.ShortcutText,
                descriptor.Container))
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
        => _ = ActivateCommandSearchItemAsync(identifier);

    public async Task ActivateCommandSearchItemAsync(string identifier)
    {
        var item = CommandSearchResults.FirstOrDefault(candidate =>
            string.Equals(candidate.Identifier, identifier, StringComparison.Ordinal));
        if (item is null || !item.IsEnabled || _isCommandPaletteActionRunning)
            return;

        _isCommandPaletteActionRunning = true;
        CloseCommandSearch();
        try
        {
            if (!await ActivateCommandPaletteOnlyItemAsync(item.Identifier).ConfigureAwait(true))
                ActivateSidebarItem(item.Key);
        }
        finally
        {
            _isCommandPaletteActionRunning = false;
            NotifyCommandPaletteStateChanged();
        }
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

    public void CustomizeToolShortcuts(IReadOnlyDictionary<string, string?> shortcutTexts)
    {
        ArgumentNullException.ThrowIfNull(shortcutTexts);
        var customizations = ToolCustomizations
            .Select(customization => shortcutTexts.TryGetValue(customization.Identifier, out var shortcut)
                ? customization with { ShortcutText = shortcut }
                : customization)
            .ToArray();
        ApplyToolCustomizations(customizations, requestPersistence: true);
    }

    public void ApplyCommandShortcutOverrides(IReadOnlyDictionary<string, string?> shortcutTexts)
    {
        ArgumentNullException.ThrowIfNull(shortcutTexts);
        foreach (var item in _commandPaletteOnlyItems)
        {
            var defaultShortcut = EditorCommandPaletteCatalog.SearchOnly.First(descriptor =>
                descriptor.Mode == item.Mode
                && string.Equals(descriptor.Identifier, item.Identifier, StringComparison.Ordinal)).ShortcutText;
            item.UpdateShortcut(shortcutTexts.TryGetValue(item.Identifier, out var shortcut)
                ? shortcut
                : defaultShortcut);
        }
        OnPropertyChanged(nameof(CommandSearchResults));
        OnPropertyChanged(nameof(IsCommandSearchEmpty));
    }

    public bool MoveToolCustomization(string identifier, int direction)
    {
        var descriptor = _toolDescriptors.FirstOrDefault(candidate =>
            candidate.Mode == ActiveEditorMode
            && string.Equals(candidate.Identifier, identifier, StringComparison.Ordinal));
        if (descriptor is null || direction == 0)
            return false;

        var layout = CreateMutableToolbarLayout();
        var items = layout[descriptor.Container];
        var index = items.FindIndex(item => string.Equals(item, identifier, StringComparison.Ordinal));
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= items.Count)
            return false;

        (items[index], items[target]) = (items[target], items[index]);
        ApplyActiveToolbarLayout(layout);
        return true;
    }

    public bool CanPlaceToolInContainer(string identifier, EditorToolbarContainer container)
    {
        var descriptor = _toolDescriptors.FirstOrDefault(candidate =>
            candidate.Mode == ActiveEditorMode
            && string.Equals(candidate.Identifier, identifier, StringComparison.Ordinal));
        return descriptor is not null
            && (container != EditorToolbarContainer.Shapes || descriptor.CanPlaceInShapes);
    }

    public bool MoveToolToContainer(string identifier, EditorToolbarContainer container)
    {
        if (!CanPlaceToolInContainer(identifier, container))
            return false;

        var layout = CreateMutableToolbarLayout();
        foreach (var items in layout.Values)
            items.RemoveAll(item => string.Equals(item, identifier, StringComparison.Ordinal));
        layout[container].Add(identifier);
        ApplyActiveToolbarLayout(layout);
        return true;
    }

    public bool MoveToolBefore(string identifier, string targetIdentifier)
    {
        if (string.Equals(identifier, targetIdentifier, StringComparison.Ordinal))
            return false;

        var target = _toolDescriptors.FirstOrDefault(candidate =>
            candidate.Mode == ActiveEditorMode
            && string.Equals(candidate.Identifier, targetIdentifier, StringComparison.Ordinal));
        if (target is null || !CanPlaceToolInContainer(identifier, target.Container))
            return false;

        var layout = CreateMutableToolbarLayout();
        foreach (var items in layout.Values)
            items.RemoveAll(item => string.Equals(item, identifier, StringComparison.Ordinal));
        var targetItems = layout[target.Container];
        var targetIndex = targetItems.FindIndex(item =>
            string.Equals(item, targetIdentifier, StringComparison.Ordinal));
        if (targetIndex < 0)
            return false;

        targetItems.Insert(targetIndex, identifier);
        ApplyActiveToolbarLayout(layout);
        return true;
    }

    public void ResetToolbarCustomizationForActiveMode()
    {
        var defaults = EditorToolCatalog.ForMode(ActiveEditorMode)
            .ToDictionary(descriptor => descriptor.Identifier, StringComparer.Ordinal);
        var customizations = ToolCustomizations
            .Select(customization => defaults.TryGetValue(customization.Identifier, out var descriptor)
                ? customization with
                {
                    Order = descriptor.Order,
                    ShortcutText = descriptor.ShortcutText,
                    Container = descriptor.DefaultContainer,
                }
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
                ? customization with
                {
                    Order = descriptor.Order,
                    ShortcutText = descriptor.ShortcutText,
                    Container = descriptor.DefaultContainer,
                }
                : customization)
            .ToArray();
        ApplyToolCustomizations(customizations, requestPersistence: true);
    }

    private Dictionary<EditorToolbarContainer, List<string>> CreateMutableToolbarLayout()
        => Enum.GetValues<EditorToolbarContainer>()
            .ToDictionary(
                container => container,
                container => _toolDescriptors
                    .Where(descriptor => descriptor.Mode == ActiveEditorMode
                        && descriptor.Container == container)
                    .OrderBy(descriptor => descriptor.Order)
                    .ThenBy(descriptor => descriptor.Identifier, StringComparer.Ordinal)
                    .Select(descriptor => descriptor.Identifier)
                    .ToList());

    private void ApplyActiveToolbarLayout(
        IReadOnlyDictionary<EditorToolbarContainer, List<string>> layout)
    {
        var placements = layout
            .SelectMany(pair => pair.Value.Select((identifier, order) => new
            {
                Identifier = identifier,
                Container = pair.Key,
                Order = order,
            }))
            .ToDictionary(item => item.Identifier, StringComparer.Ordinal);
        var customizations = ToolCustomizations
            .Select(customization => placements.TryGetValue(customization.Identifier, out var placement)
                ? customization with
                {
                    Container = placement.Container,
                    Order = placement.Order,
                }
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
        NotifyToolbarCollectionsChanged();
        OnPropertyChanged(nameof(CommandSearchResults));
        OnPropertyChanged(nameof(IsCommandSearchEmpty));
        OnPropertyChanged(nameof(ToolCustomizations));
        OnPropertyChanged(nameof(ActiveToolLabel));

        if (requestPersistence)
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
    }
    private void NotifyToolbarCollectionsChanged()
    {
        OnPropertyChanged(nameof(SidebarTools));
        OnPropertyChanged(nameof(MainToolbarTools));
        OnPropertyChanged(nameof(ShapeToolbarTools));
        OnPropertyChanged(nameof(MoreToolbarTools));
        OnPropertyChanged(nameof(HasShapeToolbarTools));
        OnPropertyChanged(nameof(HasMoreToolbarTools));
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

    private async Task<bool> ActivateCommandPaletteOnlyItemAsync(string identifier)
    {
        switch (identifier)
        {
            case EditorCommandPaletteCatalog.ToggleGridIdentifier:
                if (!IsShowingTwoDWorkspace) return true;
                ToggleTwoDGrid();
                return true;
            case EditorCommandPaletteCatalog.ToggleSnappingIdentifier:
                if (!IsShowingTwoDWorkspace) return true;
                ToggleTwoDSnapping();
                return true;
            case EditorCommandPaletteCatalog.ToggleChainSelectionIdentifier:
                if (!IsShowingTwoDWorkspace) return true;
                ToggleTwoDChainSelection();
                return true;
            case EditorCommandPaletteCatalog.ZoomInIdentifier:
                if (!IsShowingTwoDWorkspace) return true;
                ZoomTwoDIn();
                return true;
            case EditorCommandPaletteCatalog.ZoomOutIdentifier:
                if (!IsShowingTwoDWorkspace) return true;
                ZoomTwoDOut();
                return true;
            case EditorCommandPaletteCatalog.ZoomToFitIdentifier:
                if (!IsShowingTwoDWorkspace) return true;
                FrameTwoDToContent();
                return true;
            case EditorCommandPaletteCatalog.ToggleActivityLogIdentifier:
                ToggleActivityLog();
                return true;
            case EditorCommandPaletteCatalog.ToggleLearnModeIdentifier:
                ToggleLearnMode();
                return true;
            case EditorCommandPaletteCatalog.UndoIdentifier:
                UndoCommand.Execute(null);
                return true;
            case EditorCommandPaletteCatalog.RedoIdentifier:
                RedoCommand.Execute(null);
                return true;
            case EditorCommandPaletteCatalog.DeleteIdentifier:
                DeleteCommand.Execute(null);
                return true;
            case EditorCommandPaletteCatalog.ConvertLinesToDashedIdentifier:
                if (!IsShowingTwoDWorkspace || !CanApplyTwoDConvertLines)
                    return true;
                TwoDConvertLineStyle = "dashed";
                ApplyTwoDConvertLines();
                return true;
            case EditorCommandPaletteCatalog.SwitchToTwoDIdentifier:
                await SetActiveEditorModeAsync(EditorMode.TwoD).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.SwitchToThreeDIdentifier:
                await SetActiveEditorModeAsync(EditorMode.ThreeD).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.SwitchToBatchIdentifier:
                await SetActiveEditorModeAsync(EditorMode.Batch).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.NewIdentifier:
                await NewProjectCommand.ExecuteAsync(null).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.OpenIdentifier:
                await OpenProjectCommand.ExecuteAsync(null).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.ImportIdentifier:
                await ImportFilesCommand.ExecuteAsync(null).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.SaveIdentifier:
                await SaveDocumentCommand.ExecuteAsync(null).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.SaveAsIdentifier:
                await SaveDocumentAsCommand.ExecuteAsync(null).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.ExportDxfIdentifier:
                await ExportDxfCommand.ExecuteAsync(null).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.ExportSvgIdentifier:
                await ExportSvgCommand.ExecuteAsync(null).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.ExportPngIdentifier:
                await ExportPngCommand.ExecuteAsync(null).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.ExportPdfIdentifier:
                await ExportPdfCommand.ExecuteAsync(null).ConfigureAwait(true);
                return true;
            case EditorCommandPaletteCatalog.ClearReferenceImageIdentifier:
                if (TwoDActiveReferenceImage is not null && TwoDActiveLayerId is { } layerId)
                    DeleteTwoDLayer(layerId);
                return true;
            case EditorCommandPaletteCatalog.StartScreenIdentifier:
                CommandPaletteHostActionRequested?.Invoke(this, EditorCommandPaletteHostAction.StartScreen);
                return true;
            case EditorCommandPaletteCatalog.SearchIdentifier:
                OpenCommandSearch();
                return true;
            case EditorCommandPaletteCatalog.PreferencesIdentifier:
                CommandPaletteHostActionRequested?.Invoke(this, EditorCommandPaletteHostAction.Preferences);
                return true;
            case EditorCommandPaletteCatalog.DocumentationIdentifier:
                CommandPaletteHostActionRequested?.Invoke(this, EditorCommandPaletteHostAction.Documentation);
                return true;
            default:
                return false;
        }
    }

    private void SyncCommandPaletteOnlyStates()
    {
        foreach (var item in _commandPaletteOnlyItems.Where(item => item.Mode == ActiveEditorMode))
        {
            item.IsEnabled = !_isCommandPaletteActionRunning && item.Identifier switch
            {
                EditorCommandPaletteCatalog.ToggleGridIdentifier
                    or EditorCommandPaletteCatalog.ToggleSnappingIdentifier
                    or EditorCommandPaletteCatalog.ToggleChainSelectionIdentifier
                    or EditorCommandPaletteCatalog.ZoomInIdentifier
                    or EditorCommandPaletteCatalog.ZoomOutIdentifier
                    or EditorCommandPaletteCatalog.ZoomToFitIdentifier => IsShowingTwoDWorkspace,
                EditorCommandPaletteCatalog.ToggleActivityLogIdentifier => true,
                EditorCommandPaletteCatalog.ToggleLearnModeIdentifier => true,
                EditorCommandPaletteCatalog.UndoIdentifier => UndoCommand.CanExecute(null),
                EditorCommandPaletteCatalog.RedoIdentifier => RedoCommand.CanExecute(null),
                EditorCommandPaletteCatalog.DeleteIdentifier => DeleteCommand.CanExecute(null),
                EditorCommandPaletteCatalog.ConvertLinesToDashedIdentifier
                    => IsShowingTwoDWorkspace && CanApplyTwoDConvertLines,
                EditorCommandPaletteCatalog.SwitchToTwoDIdentifier => ActiveEditorMode != EditorMode.TwoD,
                EditorCommandPaletteCatalog.SwitchToThreeDIdentifier => ActiveEditorMode != EditorMode.ThreeD,
                EditorCommandPaletteCatalog.SwitchToBatchIdentifier => ActiveEditorMode != EditorMode.Batch,
                EditorCommandPaletteCatalog.NewIdentifier => NewProjectCommand.CanExecute(null),
                EditorCommandPaletteCatalog.OpenIdentifier => OpenProjectCommand.CanExecute(null),
                EditorCommandPaletteCatalog.ImportIdentifier => ImportFilesCommand.CanExecute(null),
                EditorCommandPaletteCatalog.SaveIdentifier => SaveDocumentCommand.CanExecute(null),
                EditorCommandPaletteCatalog.SaveAsIdentifier => SaveDocumentAsCommand.CanExecute(null),
                EditorCommandPaletteCatalog.ExportDxfIdentifier => ExportDxfCommand.CanExecute(null),
                EditorCommandPaletteCatalog.ExportSvgIdentifier => ExportSvgCommand.CanExecute(null),
                EditorCommandPaletteCatalog.ExportPngIdentifier => ExportPngCommand.CanExecute(null),
                EditorCommandPaletteCatalog.ExportPdfIdentifier => ExportPdfCommand.CanExecute(null),
                EditorCommandPaletteCatalog.ClearReferenceImageIdentifier
                    => IsShowingTwoDWorkspace && TwoDActiveReferenceImage is not null,
                _ => true,
            };
        }
    }

    private void NotifyCommandPaletteStateChanged()
    {
        SyncCommandPaletteOnlyStates();
        OnPropertyChanged(nameof(CommandSearchResults));
        OnPropertyChanged(nameof(IsCommandSearchEmpty));
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
