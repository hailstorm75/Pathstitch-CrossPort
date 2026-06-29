using System;
using System.Linq;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private readonly IReadOnlyList<EditorSidebarToolItemViewModel> _sidebarTools = CreateSidebarTools();

    public IReadOnlyList<EditorSidebarToolItemViewModel> SidebarTools => _sidebarTools;

    public Editor3DTool ActiveTool
    {
        get => _activeTool;
        private set
        {
            if (!SetProperty(ref _activeTool, value))
                return;

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

    public string ActiveToolLabel => SidebarTools.FirstOrDefault(tool => tool.IsActive)?.Label ?? ActiveTool.ToString();

    public bool ShowSelectionPanel => ActiveTool is Editor3DTool.Select or Editor3DTool.Measure or Editor3DTool.Unfold;

    public bool ShowMoveBodiesPanel => IsMoveToolActive;

    public bool ShowProjectionPanel => IsPlaneToolActive;

    public bool ShowMeasurePanel => IsMeasureToolActive;

    public bool ShowUnfoldPanel => IsUnfoldToolActive;

    public bool ShowOutputPanel => IsOutputToolActive;

    public void ActivateSidebarItem(string itemKey)
    {
        var item = SidebarTools.FirstOrDefault(tool => string.Equals(tool.Key, itemKey, StringComparison.Ordinal));
        if (item is null)
            return;

        if (item.Tool is Editor3DTool tool)
        {
            ActivateTool(tool);
            return;
        }

        if (item.Action is EditorSidebarAction action)
            ExecuteSidebarAction(action);
    }

    public bool TryActivateEditorShortcut(string shortcutToken)
    {
        if (IsShowingGeneratedOutputWorkspace)
        {
            return shortcutToken switch
            {
                "select" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputSelectTool),
                "move" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputMoveTool),
                "project" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputPanTool),
                "measure" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputMeasureTool),
                "dimension" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputDimensionTool),
                "scale" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputScaleTool),
                "mirror" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputMirrorTool),
                "offset" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputOffsetTool),
                "add-thickness" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputAddThicknessTool),
                "cleanup" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputCleanupTool),
                "trim" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputTrimTool),
                "fillet" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputFilletTool),
                "chamfer" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputChamferTool),
                "convert-lines" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputConvertLinesTool),
                "unfold" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputLineTool),
                "line" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputLineTool),
                "rectangle" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputRectangleTool),
                "circle" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputCircleTool),
                "polygon" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputPolygonTool),
                "text" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputTextTool),
                "pen" => ActivateGeneratedOutputShortcut(ActivateGeneratedOutputPenTool),
                "output" => ActivateGeneratedOutputShortcut(ShowGeneratedOutputWorkspace),
                "frame-home" => ActivateGeneratedOutputShortcut(FrameGeneratedOutputToContent),
                "delete-selection" => DeleteGeneratedOutputSelection(),
                "escape" => HandleGeneratedOutputEscapeShortcut(),
                _ => false,
            };
        }

        return shortcutToken switch
        {
            "select" => TryActivateSidebarShortcut("select"),
            "move" => TryActivateSidebarShortcut("move"),
            "project" => TryActivateSidebarShortcut("project"),
            "measure" => TryActivateSidebarShortcut("measure"),
            "unfold" => TryActivateSidebarShortcut("unfold"),
            "output" => TryActivateSidebarShortcut("output"),
            "frame-home" => TryActivateSidebarShortcut("frame-home"),
            "camera-mode" => TryActivateSidebarShortcut("camera-mode"),
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
        foreach (var tool in SidebarTools)
        {
            tool.IsActive = tool.Tool == ActiveTool
                || tool.Action is EditorSidebarAction.ToggleOrthographic && ThreeDOrthographic;
            tool.IsEnabled = tool.Action is not EditorSidebarAction.FrameHome || CanFrameHome;
        }
    }

    private static IReadOnlyList<EditorSidebarToolItemViewModel> CreateSidebarTools()
    {
        var tools = EditorSidebarToolCatalog.Default
            .Select(static definition => new EditorSidebarToolItemViewModel(definition))
            .ToArray();

        var activeTool = tools.FirstOrDefault(static tool => tool.Tool == Editor3DTool.Select);
        if (activeTool is not null)
            activeTool.IsActive = true;

        foreach (var tool in tools.Where(static tool => tool.Action is EditorSidebarAction.FrameHome))
            tool.IsEnabled = false;

        return tools;
    }

    private void ExecuteSidebarAction(EditorSidebarAction action)
    {
        switch (action)
        {
            case EditorSidebarAction.FrameHome:
                if (!CanFrameHome)
                    return;

                RequestViewportScript(GetHomeFrameScript());
                StatusText = "Framing visible 3D workspace";
                break;

            case EditorSidebarAction.ToggleOrthographic:
                ToggleOrthographic();
                StatusText = ThreeDOrthographic ? "Orthographic camera active" : "Perspective camera active";
                break;
        }
    }

    private bool TryActivateSidebarShortcut(string itemKey)
    {
        ActivateSidebarItem(itemKey);
        return true;
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

    private bool HandleGeneratedOutputEscapeShortcut()
    {
        if (HasGeneratedOutputSelectedMeasurement)
        {
            ClearGeneratedOutputSelectedMeasurement();
            return true;
        }

        if (HasGeneratedOutputSelection)
        {
            ClearGeneratedOutputSelection();
            return true;
        }

        if (HasGeneratedOutputMeasurements)
        {
            ClearGeneratedOutputMeasurements();
            return true;
        }

        return false;
    }

    private static bool ActivateGeneratedOutputShortcut(Action action)
    {
        action();
        return true;
    }
}
