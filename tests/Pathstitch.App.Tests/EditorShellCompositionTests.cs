using Domain.App.Models;

namespace Pathstitch.App.Tests;

public sealed class EditorShellCompositionTests
{
    [Fact]
    public void EditorPage_IsAThinNavigationHostForTheShell()
    {
        var page = ReadPage("EditorPageView.axaml");

        Assert.Contains("<pages:EditorShellView", page, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.shell\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeWebView", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DxfPreviewCanvas", page, StringComparison.Ordinal);
        Assert.True(page.Split('\n').Length < 20);
    }

    [Fact]
    public void Shell_OwnsAllEditorRegionsAndModeSwitching()
    {
        var shell = ReadPage("EditorShellView.axaml");

        Assert.Contains("<pages:EditorToolRail", shell, StringComparison.Ordinal);
        Assert.Contains("<pages:Editor2DLayersPanel", shell, StringComparison.Ordinal);
        Assert.Contains("<pages:Editor3DBodiesPanel", shell, StringComparison.Ordinal);
        Assert.Contains("<pages:Editor2DView", shell, StringComparison.Ordinal);
        Assert.Contains("<pages:Editor3DView", shell, StringComparison.Ordinal);
        Assert.Contains("<pages:EditorInspectorHost", shell, StringComparison.Ordinal);
        Assert.Contains("<pages:EditorBatchContextPanel", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Editor2DToolBar", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnShow3DWorkspaceClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnShowTwoDWorkspaceClicked\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeDViewportDrop_UsesGenericWorkspaceImporter()
    {
        var code = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor3DView.axaml.cs");

        Assert.Contains("OpenActivatedFilesAsync(filePaths)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSourceModelsAsync(filePaths)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void FileMenu_OffersDocumentLifecycleCommandsAndShortcuts()
    {
        var shell = ReadPage("EditorShellView.axaml");

        Assert.Contains("Command=\"{Binding SaveDocumentCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SaveDocumentAsCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SaveAndCloseDocumentCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CloseDocumentCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+S\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+Shift+S\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+Shift+W\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+W\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ExportDxfCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ExportSvgCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ExportPngCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ExportPdfCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("SetHotKey(ExportDxfMenuItem, EditorCommandPaletteCatalog.ExportDxfIdentifier)", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs"), StringComparison.Ordinal);
        Assert.Contains("SetHotKey(SaveAsMenuItem, EditorCommandPaletteCatalog.SaveAsIdentifier)", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs"), StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding NewProjectCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenProjectCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ImportFilesCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+N\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+O\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+Shift+I\"", shell, StringComparison.Ordinal);
        var code = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs");
        Assert.Contains("EditorAppShortcutCatalog.Resolve", code, StringComparison.Ordinal);
        Assert.Contains("SetHotKey(NewProjectMenuItem", code, StringComparison.Ordinal);
        Assert.Contains("SetHotKey(OpenProjectMenuItem", code, StringComparison.Ordinal);
        Assert.Contains("SetHotKey(ImportFilesMenuItem", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EditMenu_UsesOneModeAwareHistoryPairAndGuardedDelete()
    {
        var shell = ReadPage("EditorShellView.axaml");

        Assert.Equal(1, shell.Split("editor.menu.edit.undo\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, shell.Split("editor.menu.edit.redo\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("Command=\"{Binding UndoCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RedoCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DeleteCommand}\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("editor.menu.edit.undo-3d", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("HotKey=\"Delete\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolsAndModifyMenus_ProjectTheSharedCatalog()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var code = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs");

        Assert.Contains("editor.menu.tools", shell, StringComparison.Ordinal);
        Assert.Contains("editor.menu.tools.search", shell, StringComparison.Ordinal);
        Assert.Contains("editor.menu.modify", shell, StringComparison.Ordinal);
        Assert.Contains("viewModel.SidebarTools.Where", code, StringComparison.Ordinal);
        Assert.Contains("tool.Tool is not null || tool.TwoDTool is not null", code, StringComparison.Ordinal);
        Assert.Contains("EditorSidebarAction.FlipSelectionHorizontal", code, StringComparison.Ordinal);
        Assert.Contains("viewModel.ActivateSidebarItem(toolKey)", code, StringComparison.Ordinal);
        Assert.Contains("SetHotKey(SearchCommandsMenuItem, EditorCommandPaletteCatalog.SearchIdentifier)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewMenu_OffersLegacyTwoDZoomControls()
    {
        var shell = ReadPage("EditorShellView.axaml");

        Assert.Contains("editor.menu.view.zoom-in", shell, StringComparison.Ordinal);
        Assert.Contains("editor.menu.view.zoom-out", shell, StringComparison.Ordinal);
        Assert.Contains("editor.menu.view.zoom-to-fit", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnZoomInClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnZoomOutClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnZoomToFitClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+OemPlus\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+OemMinus\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+D0\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpMenu_UsesThePaletteStartScreenCoordinatorPath()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var code = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs");

        Assert.Contains("editor.menu.help.start-screen", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnShowStartScreenClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("private static void ShowStartScreen()", code, StringComparison.Ordinal);
        Assert.Contains("DesktopDocumentWindowCoordinator", code, StringComparison.Ordinal);
        Assert.Contains("ShowStartScreen();", code, StringComparison.Ordinal);
        Assert.Contains("SetHotKey(StartScreenMenuItem, EditorCommandPaletteCatalog.StartScreenIdentifier)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_OffersAccessibleActivityLogTrayFromViewMenuAndWorkspace()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var tray = ReadPage("EditorActivityLogTray.axaml");

        Assert.Contains("editor.menu.view.log-tray", shell, StringComparison.Ordinal);
        Assert.Contains("editor.workspace.log-tray", shell, StringComparison.Ordinal);
        Assert.Contains("<pages:EditorActivityLogTray Grid.Row=\"3\"", shell, StringComparison.Ordinal);
        Assert.Contains("editor.log-tray", tray, StringComparison.Ordinal);
        Assert.Contains("editor.log-tray.empty", tray, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibilitySummary}\"", tray, StringComparison.Ordinal);
        Assert.Contains("ActivityScrollViewer.ScrollToEnd", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorActivityLogTray.axaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_OffersProjectScopedLearnModeAndContextualTwoDHint()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var inspector = ReadPage("Editor2DInspector.axaml");

        Assert.Contains("editor.menu.view.learn-mode", shell, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding LearnModeEnabled, Mode=OneWay}\"", shell, StringComparison.Ordinal);
        Assert.Contains("OnToggleLearnModeClicked", shell, StringComparison.Ordinal);
        Assert.Contains("editor.2d.learn-hint", inspector, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding LearnModeEnabled}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TwoDToolHint}\"", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void LayersPanel_OffersReferenceImageAutoCropToggle()
    {
        var layers = ReadPage("Editor2DLayersPanel.axaml");

        Assert.Contains("editor.layers.auto-crop-reference-images", layers, StringComparison.Ordinal);
        Assert.Contains("Crop transparent margins on import", layers, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding AutoCropTransparentReferenceImages, Mode=TwoWay}\"", layers, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDWorkspace_AcceptsFileDropsAtCanvasWorldPosition()
    {
        var view = ReadPage("Editor2DView.axaml");
        var code = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DView.axaml.cs");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");

        Assert.Contains("editor.canvas.2d.drop-overlay", view, StringComparison.Ordinal);
        Assert.Contains("DragDrop.SetAllowDrop(TwoDPreviewCanvas, true)", code, StringComparison.Ordinal);
        Assert.Contains("ScreenPointToWorld(e.GetPosition(TwoDPreviewCanvas))", code, StringComparison.Ordinal);
        Assert.Contains("OpenDroppedFilesAsync(paths, insertionPoint)", code, StringComparison.Ordinal);
        Assert.Contains("public Editor2DPoint ScreenPointToWorld", canvas, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopManager_CoordinatesApplicationQuitAcrossDocumentScopes()
    {
        var manager = ReadRepositoryFile("src", "Pathstitch.App", "Services", "DesktopDocumentWindowManager.cs");
        var shell = ReadRepositoryFile("src", "Pathstitch.App", "MainWindowShell.axaml.cs");

        Assert.Contains("_desktop.ShutdownRequested += OnShutdownRequested", manager, StringComparison.Ordinal);
        Assert.Contains("_applicationCloseCoordinator.TryApproveAsync", manager, StringComparison.Ordinal);
        Assert.Contains("Prepend(_activeDocument)", manager, StringComparison.Ordinal);
        Assert.Contains("document.Window.ApproveApplicationClose", manager, StringComparison.Ordinal);
        Assert.Contains("_welcome?.Window.ApproveApplicationClose()", manager, StringComparison.Ordinal);
        Assert.Contains("internal void ApproveApplicationClose", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpMenu_ExposesAnAboutDialogForAppParity()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var aboutDialog = ReadRepositoryFile("src", "Pathstitch.App", "Dialogs", "AboutDialog.axaml");

        Assert.Contains("AutomationProperties.AutomationId=\"editor.menu.help\"", shell, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.menu.help.about\"", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAboutClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("editor.menu.help.check-updates", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnCheckForUpdatesClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"dialog.about\"", aboutDialog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"dialog.about.close\"", aboutDialog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"dialog.about.support\"", aboutDialog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"dialog.about.check-updates\"", aboutDialog, StringComparison.Ordinal);
        Assert.Contains("VersionText", ReadRepositoryFile("src", "Pathstitch.App", "Dialogs", "AboutDialog.axaml.cs"), StringComparison.Ordinal);
        var home = ReadPage("HomePageView.axaml");
        Assert.DoesNotContain("vNext Home", home, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"home.version\"", home, StringComparison.Ordinal);
        Assert.Contains("AppVersionInfo.HomeLabel", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "HomePageView.axaml.cs"), StringComparison.Ordinal);
        Assert.Contains("About Pathstitch", aboutDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageDrop_AcceptsFilesAcrossTheWholeWelcomeSurface()
    {
        var home = ReadPage("HomePageView.axaml");
        var code = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "HomePageView.axaml.cs");

        Assert.Contains("DragDrop.AllowDrop=\"True\"", home, StringComparison.Ordinal);
        Assert.Contains("AddDropHandler(this, OnRootFileDrop)", code, StringComparison.Ordinal);
        Assert.Contains("AddDropHandler(FileDropSurface, OnFileDropSurfaceDrop)", code, StringComparison.Ordinal);
        Assert.Contains("HandleDroppedFilesAsync(e)", code, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", code, StringComparison.Ordinal);
        Assert.Contains("viewModel.OpenFilesAsync(filePaths)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpMenu_ExposesPreferencesShortcutEditor()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var preferences = ReadRepositoryFile("src", "Pathstitch.App", "Dialogs", "PreferencesDialog.axaml");

        Assert.Contains("editor.menu.help.preferences", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPreferencesClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("dialog.preferences", preferences, StringComparison.Ordinal);
        Assert.Contains("dialog.preferences.apply", preferences, StringComparison.Ordinal);
        Assert.Contains("dialog.preferences.reset", preferences, StringComparison.Ordinal);
        Assert.Contains("dialog.preferences.appearance", preferences, StringComparison.Ordinal);
        Assert.Contains("ThemeVariant.Light", ReadRepositoryFile("src", "Pathstitch.App", "Dialogs", "PreferencesDialog.axaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void HelpMenu_ExposesDocumentationSurface()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var documentation = ReadRepositoryFile("src", "Pathstitch.App", "Dialogs", "DocumentationDialog.axaml");

        Assert.Contains("editor.menu.help.documentation", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnDocumentationClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("Pathstitch Documentation", documentation, StringComparison.Ordinal);
        Assert.Contains("dialog.documentation.close", documentation, StringComparison.Ordinal);
        Assert.Contains("Typical workflow", documentation, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandPalette_HandlesKeyboardSelectionAndActivation()
    {
        var codeBehind = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorCommandPalette.axaml.cs");

        Assert.Contains("case Key.Down", codeBehind, StringComparison.Ordinal);
        Assert.Contains("case Key.Up", codeBehind, StringComparison.Ordinal);
        Assert.Contains("case Key.Enter", codeBehind, StringComparison.Ordinal);
        Assert.Contains("MoveSelection", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ScrollIntoView", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ActivateSelected", codeBehind, StringComparison.Ordinal);
        Assert.Contains("IsCommandSearchSelected", codeBehind + ReadPage("EditorCommandPalette.axaml"), StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsEnabled}\"", ReadPage("EditorCommandPalette.axaml"), StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding GroupKey}\"", ReadPage("EditorCommandPalette.axaml"), StringComparison.Ordinal);
        Assert.Contains("OpenCommandSearch", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ActivateCommandSearchItemAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandPaletteHostActionRequested", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs"), StringComparison.Ordinal);
        Assert.Contains("Button.command-result.selected", ReadPage("EditorCommandPalette.axaml"), StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommandSearchResults\"", ReadPage("EditorCommandPalette.axaml"), StringComparison.Ordinal);
        Assert.Contains("FocusSearch", codeBehind, StringComparison.Ordinal);
        Assert.Contains("EditorShortcutGesture.TryCapture", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs"), StringComparison.Ordinal);
        Assert.Contains("EditorAppShortcutCatalog.Resolve", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceViews_ContainOnlyTheirOwnViewportTechnology()
    {
        var twoD = ReadPage("Editor2DView.axaml");
        var threeD = ReadPage("Editor3DView.axaml");

        Assert.Contains("DxfPreviewCanvas", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeWebView", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("SOLID BODIES", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("OnTwoDSelectToolClicked", twoD, StringComparison.Ordinal);

        Assert.Contains("EditorViewportWebView", threeD, StringComparison.Ordinal);
        Assert.DoesNotContain("DxfPreviewCanvas", threeD, StringComparison.Ordinal);
        Assert.DoesNotContain("TwoD", threeD, StringComparison.Ordinal);
        Assert.DoesNotContain("2D Selection", threeD, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeDWorkspace_HintsIncludeStepSources()
    {
        var workspaceState = ReadRepositoryFile("src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.WorkspaceState.cs");
        var sourceModels = ReadRepositoryFile("src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.SourceModels.cs");

        Assert.Contains(".step", workspaceState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".stp", workspaceState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STEP/STP", sourceModels, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BatchWorkspace_OffersDxfInputs()
    {
        var panel = ReadPage("EditorBatchContextPanel.axaml");
        var view = ReadPage("EditorBatchView.axaml");
        var codeBehind = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorBatchContextPanel.axaml.cs");

        Assert.Contains(".dxf", panel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content=\"Add file\"", panel, StringComparison.Ordinal);
        Assert.Contains("Text=\"Batch inputs\"", view, StringComparison.Ordinal);
        Assert.Contains("AddInputFile", codeBehind, StringComparison.Ordinal);
        Assert.Contains("editor.batch.export-dxf", panel, StringComparison.Ordinal);
        Assert.Contains("OutputDirectory", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.apply-offset", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.apply-sewing-holes", panel, StringComparison.Ordinal);
        Assert.Contains("Select all", panel, StringComparison.Ordinal);
        Assert.Contains("SelectedItemCount", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.export-selected-only", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.export-format", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.export-naming", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.export-name", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.pick-inputs", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.pick-output-folder", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.reveal-output", panel, StringComparison.Ordinal);
        Assert.Contains("editor.batch.drop-target", view, StringComparison.Ordinal);
        Assert.Contains("DragDrop.AllowDrop=\"True\"", view, StringComparison.Ordinal);
        Assert.Contains("OnRemoveBatchItemClicked", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorBatchView.axaml.cs"), StringComparison.Ordinal);
        var picker = ReadRepositoryFile("src", "Pathstitch.App", "Services", "ProjectFileDialogService.cs");
        Assert.Contains("BatchInputFileType", picker, StringComparison.Ordinal);
        Assert.Contains("OpenFolderPickerAsync", picker, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDView_BindsScalePivotStateToCanvas()
    {
        var twoD = ReadPage("Editor2DView.axaml");

        Assert.Contains("ScalePivot=\"{Binding TwoDScalePivot, Mode=TwoWay}\"", twoD, StringComparison.Ordinal);
        Assert.Contains("ScalePivotPicking=\"{Binding TwoDScalePivotPicking, Mode=TwoWay}\"", twoD, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDScaleInspector_ExposesExactFactorAndPivotMode()
    {
        var inspector = ReadPage("Editor2DInspector.axaml");
        var handlers = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorInteractionControlBase.cs");
        var workspace = ReadPage("Editor2DView.axaml");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");
        var tools = ReadRepositoryFile("src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.Tools.cs");

        Assert.Contains("TwoDScaleFromCenter, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDScaleFactorText, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanApplyTwoDScale}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"OnTwoDScaleFactorKeyDown\"", inspector, StringComparison.Ordinal);
        Assert.Contains("OnApplyTwoDScaleClicked", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.scale.inspector", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.scale.pick-pivot", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.scale.apply", inspector, StringComparison.Ordinal);
        Assert.Contains("OnTwoDScaleFactorKeyDown", handlers, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(handlers, "viewModel.ConfirmTwoDScaleAndExit();"));
        Assert.Contains("ScaleFromCenter=\"{Binding TwoDScaleFromCenter}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("ScaleFactor=\"{Binding TwoDScalePreviewFactor}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("ScaleFactorText=\"{Binding TwoDScaleFactorText, Mode=TwoWay}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("SetCurrentValue(ScaleFactorTextProperty", canvas, StringComparison.Ordinal);
        Assert.Contains("viewModel.CancelTwoDEscape()", canvas, StringComparison.Ordinal);
        Assert.Contains("CancelTwoDScaleAndExit()", tools, StringComparison.Ordinal);
        Assert.Contains("scaleViewModel.ConfirmTwoDScaleAndExit()", canvas, StringComparison.Ordinal);
        Assert.DoesNotContain("var scaledDocument = ScalePaths(_scaleDocumentSnapshot", canvas, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDMirrorStaging_IsBoundToCanvasAndInspector()
    {
        var view = ReadPage("Editor2DView.axaml");
        var inspector = ReadPage("Editor2DInspector.axaml");
        var handlers = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorInteractionControlBase.cs");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");
        var tools = ReadRepositoryFile("src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.Tools.cs");

        Assert.Contains("TwoDMirrorLineMode=\"{Binding TwoDMirrorLineMode, Mode=TwoWay}\"", view, StringComparison.Ordinal);
        Assert.Contains("TwoDMirrorFlipCopy=\"{Binding TwoDMirrorFlipCopy}\"", view, StringComparison.Ordinal);
        Assert.Contains("TwoDMirrorAxisStart=\"{Binding TwoDMirrorAxisStart, Mode=TwoWay}\"", view, StringComparison.Ordinal);
        Assert.Contains("TwoDMirrorAxisEnd=\"{Binding TwoDMirrorAxisEnd, Mode=TwoWay}\"", view, StringComparison.Ordinal);
        Assert.Contains("editor.2d.mirror.line-mode", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDMirrorFlipCopy, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.mirror.flip-copy", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDMirrorKeepLink, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.mirror.keep-live-link", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.mirror.confirm", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.mirror.cancel", inspector, StringComparison.Ordinal);
        Assert.Contains("OnConfirmTwoDMirrorClicked", handlers, StringComparison.Ordinal);
        Assert.Contains("OnCancelTwoDMirrorClicked", handlers, StringComparison.Ordinal);
        Assert.Contains("if (!TwoDMirrorLineMode)", canvas, StringComparison.Ordinal);
        Assert.Contains("Editor2DGeometry.CreateMirrorCopy", canvas, StringComparison.Ordinal);
        Assert.Contains("mirrorViewModel.ConfirmTwoDMirror()", canvas, StringComparison.Ordinal);
        Assert.Contains("viewModel.CancelTwoDEscape()", canvas, StringComparison.Ordinal);
        Assert.Contains("CancelTwoDMirror(exitTool: true)", tools, StringComparison.Ordinal);
        Assert.Contains("Header = \"Break Mirror Link\"", canvas, StringComparison.Ordinal);
        Assert.Contains("editor.canvas.2d.break-mirror-link", canvas, StringComparison.Ordinal);
        Assert.Contains("viewModel.BreakTwoDMirrorLinks()", canvas, StringComparison.Ordinal);
        Assert.Contains("HasTwoDMirrorLinkSelection: true", canvas, StringComparison.Ordinal);
        Assert.DoesNotContain("MirrorPaths(", canvas, StringComparison.Ordinal);
    }

    [Fact]
    public void RectangularPatternInspector_ExposesSpacingAndExtentModes()
    {
        var inspector = ReadPage("Editor2DInspector.axaml");

        Assert.Contains("TwoDPatternDistanceModeOptionItems", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDPatternDistanceMode, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.pattern.distance-mode", inspector, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsTwoDPatternSpacingMode}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDPatternExtentXText, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDPatternExtentYText, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsTwoDPatternExtentMode}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDPatternEffectiveSpacingSummary", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDMovePointToPointState_IsBoundToCanvasAndInspector()
    {
        var twoD = ReadPage("Editor2DView.axaml");
        var inspector = ReadPage("Editor2DInspector.axaml");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");

        Assert.Contains("TwoDMovePointToPointActive=\"{Binding TwoDMovePointToPointActive, Mode=TwoWay}\"", twoD, StringComparison.Ordinal);
        Assert.Contains("TwoDMovePointToPointSource=\"{Binding TwoDMovePointToPointSource, Mode=TwoWay}\"", twoD, StringComparison.Ordinal);
        Assert.Contains("editor.2d.move.point-to-point", inspector, StringComparison.Ordinal);
        Assert.Contains("OnToggleTwoDMovePointToPointClicked", inspector, StringComparison.Ordinal);
        Assert.Contains("SetCurrentValue(TwoDMovePointToPointActiveProperty, false)", canvas, StringComparison.Ordinal);
        Assert.Contains("SetCurrentValue(TwoDMovePointToPointSourceProperty, null)", canvas, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentTwoDControls_LiveOutsideTheViewportView()
    {
        var toolbar = ReadPage("EditorToolRail.axaml");
        var inspector = ReadPage("Editor2DInspector.axaml");
        var host = ReadPage("EditorInspectorHost.axaml");

        Assert.Contains("ItemsSource=\"{Binding SidebarTools}\"", toolbar, StringComparison.Ordinal);
        Assert.Contains("IsShowingBatchWorkspace", toolbar, StringComparison.Ordinal);
        Assert.Contains("2D Selection", inspector, StringComparison.Ordinal);
        Assert.Contains("<pages:Editor2DInspector", host, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolRail_IsCatalogDrivenAndScrollableAtConstrainedHeights()
    {
        var rail = ReadPage("EditorToolRail.axaml");

        Assert.Contains("ItemsSource=\"{Binding SidebarTools}\"", rail, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", rail, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", rail, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(rail, "Click=\"OnSidebarToolClicked\""));
        Assert.Contains("Tag=\"{Binding Key}\"", rail, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSidebarToolClicked\"", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("TwoD", rail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EditorMode.TwoD)]
    [InlineData(EditorMode.ThreeD)]
    public void CatalogGroups_DriveOrderedRailSectionBoundaries(EditorMode mode)
    {
        var descriptors = EditorToolCatalog.ForMode(mode).OrderBy(item => item.Order).ToArray();

        Assert.NotEmpty(descriptors);
        Assert.False(descriptors[0].StartsSection);
        for (var index = 1; index < descriptors.Length; index++)
        {
            var groupChanged = !string.Equals(
                descriptors[index - 1].GroupKey,
                descriptors[index].GroupKey,
                StringComparison.Ordinal);
            Assert.Equal(groupChanged, descriptors[index].StartsSection);
        }
    }

    [Fact]
    public void TwoDTools_HaveNoPerToolXamlButtonsOrActivationHandlers()
    {
        var twoD = ReadPage("Editor2DView.axaml");
        var interactionBase = ReadRepositoryFile(
            "src", "Pathstitch.App", "Pages", "EditorInteractionControlBase.cs");

        Assert.DoesNotContain("<Button", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("OnTwoDSelectToolClicked", interactionBase, StringComparison.Ordinal);
        Assert.DoesNotContain("OnTwoDPenToolClicked", interactionBase, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolClicked", interactionBase.Replace("OnSidebarToolClicked", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDViewport_ContainsOnlyCanvasInteractionSurface()
    {
        var twoD = ReadPage("Editor2DView.axaml");
        var inspector = ReadPage("Editor2DInspector.axaml");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");
        var tools = ReadRepositoryFile("src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.Tools.cs");

        Assert.Contains("DxfPreviewCanvas", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("StackPanel", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsControl", twoD, StringComparison.Ordinal);
        Assert.Contains("editor.canvas.2d.gizmo.dimension-input", twoD, StringComparison.Ordinal);
        Assert.Contains("TwoDOffsetDistanceText", inspector, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.2d.offset.flip\"", inspector, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.2d.offset.ok\"", inspector, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.2d.offset.cancel\"", inspector, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.2d.offset.construction\"", inspector, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding TwoDOffsetConstruction, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("OnTwoDOffsetDistanceKeyDown", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDPolygonSides", inspector, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsTwoDRectangleToolActive}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.2d.rectangle.fillet-radius\"", inspector, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding TwoDRectangleFilletRadius, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("RectangleFilletRadius=\"{Binding TwoDRectangleFilletRadius}\"", twoD, StringComparison.Ordinal);
        Assert.Contains("SewingHoleMargin=\"{Binding TwoDSewingHoleMargin, Mode=TwoWay}\"", twoD, StringComparison.Ordinal);
        Assert.Contains("Explode Compound", canvas, StringComparison.Ordinal);
        Assert.Contains("Stroke to Fill", canvas, StringComparison.Ordinal);
        Assert.Contains("Fill to Stroke", canvas, StringComparison.Ordinal);
        Assert.Contains("OffsetDistanceText=\"{Binding TwoDOffsetDistanceText, Mode=TwoWay}\"", twoD, StringComparison.Ordinal);
        Assert.Contains("OffsetPreviewPaths=\"{Binding TwoDOffsetPreviewPaths}\"", twoD, StringComparison.Ordinal);
        Assert.Contains("TwoDSelectedTextFitModeOptions", inspector, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.2d.corner.ok\"", inspector, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.2d.corner.cancel\"", inspector, StringComparison.Ordinal);
        Assert.Contains("ConfirmTwoDCornerToolSession", canvas, StringComparison.Ordinal);
        Assert.Contains("viewModel.CancelTwoDEscape()", canvas, StringComparison.Ordinal);
        Assert.Contains("CancelTwoDCornerToolSession(exitTool: true)", tools, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.2d.export-selected-only\"", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDExportSelectedOnly, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDExportMeasurementLines, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDSvgPrecisionText, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDSvgStrokeWidthText, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDDxfVersionOptions", inspector, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.2d.export-png\"", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDPngLongestEdgeText, Mode=TwoWay", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDTextTool_UsesCanvasEntryAndExistingTextApplyPipeline()
    {
        var twoD = ReadPage("Editor2DView.axaml");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");

        Assert.Contains("TextEntry=\"{Binding TwoDSelectedTextDraft, Mode=TwoWay}\"", twoD, StringComparison.Ordinal);
        Assert.Contains("OnTextInput", canvas, StringComparison.Ordinal);
        Assert.Contains("Shift", canvas, StringComparison.Ordinal);
        Assert.Contains("ApplyTwoDSelectedText", canvas, StringComparison.Ordinal);
        Assert.Contains("CreateTwoDText", canvas, StringComparison.Ordinal);
        Assert.Contains("IsTwoDTextInspectorVisible", ReadPage("Editor2DInspector.axaml"), StringComparison.Ordinal);
        Assert.Contains("_isTextEntryActive", canvas, StringComparison.Ordinal);
        Assert.Contains("TryBeginTextEditing", canvas, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDLayersPanel_ExposesMergeWithBelow()
    {
        var layers = ReadPage("Editor2DLayersPanel.axaml");
        var codeBehind = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DLayersPanel.axaml.cs");

        Assert.Contains("Content=\"Merge ↓\"", layers, StringComparison.Ordinal);
        Assert.Contains("OnMergeLayerWithBelowClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Merge selected", layers, StringComparison.Ordinal);
        Assert.Contains("OnMergeSelectedLayersClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Content=\"Rename\"", layers, StringComparison.Ordinal);
        Assert.Contains("Content=\"Delete\"", layers, StringComparison.Ordinal);
        Assert.Contains("OnRenameLayerClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnDeleteLayerClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Content=\"Set\"", layers, StringComparison.Ordinal);
        Assert.Contains("OnSetLayerColorClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Avalonia.Controls.ColorPicker", layers, StringComparison.Ordinal);
        Assert.Contains("ColorChanged=\"OnLayerColorChanged\"", layers, StringComparison.Ordinal);
        Assert.Contains("editor.layer.color.{0}", layers, StringComparison.Ordinal);
        Assert.Contains("IsAlphaEnabled=\"False\"", layers, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding !IsReferenceImage}\"", layers, StringComparison.Ordinal);
        Assert.Contains("OnLayerColorTextKeyDown", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TryNormalizeLayerColorHex", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml",
            ReadRepositoryFile("src", "Pathstitch.App", "App.axaml"), StringComparison.Ordinal);
        Assert.Contains("Content=\"+ Folder\"", layers, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TwoDLayerHierarchyItems}\"", layers, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"viewModels:Editor2DLayerHierarchyItem\"", layers, StringComparison.Ordinal);
        Assert.Contains("Content=\"+ Subfolder\"", layers, StringComparison.Ordinal);
        Assert.Contains("OnToggleFolderExpandedClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnRenameFolderClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnCreateSubfolderClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DragDrop.AllowDrop=\"True\"", layers, StringComparison.Ordinal);
        Assert.Contains("OnHierarchyDragStarted", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnHierarchyDrop", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnMoveFolderUpClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnMoveFolderDownClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnMoveFolderToRootClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnFolderParentChanged", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnMoveLayerToRootClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnCreateFolderClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnDeleteFolderClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Content=\"Stroke to Fill\"", layers + ReadPage("Editor2DInspector.axaml"), StringComparison.Ordinal);
        Assert.Contains("Content=\"Fill to Stroke\"", ReadPage("Editor2DInspector.axaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandPalette_HoverSelectsAndScrollsCommandResult()
    {
        var palette = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorCommandPalette.axaml");
        var codeBehind = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorCommandPalette.axaml.cs");

        Assert.Contains("PointerEntered=\"OnCommandPointerEntered\"", palette, StringComparison.Ordinal);
        Assert.Contains("CommandSearchResults.ScrollIntoView(index)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FilletInspector_ExposesActiveCornerAndContinuityBindings()
    {
        var inspector = ReadPage("Editor2DInspector.axaml");
        var view = ReadPage("Editor2DView.axaml");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");

        Assert.Contains("IsVisible=\"{Binding IsTwoDCornerToolActive}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TwoDFilletContinuityOptions}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding TwoDFilletContinuity, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.corner.continuity", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.corner.value", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDActiveCornerLabel", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDCornerSelectionSummary", inspector, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TwoDActiveCornerParameters}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("SelectedCornerParameterId=\"{Binding TwoDSelectedCornerParameterId, Mode=TwoWay}\"", view, StringComparison.Ordinal);
        Assert.Contains("SetCurrentValue(SelectedCornerParameterIdProperty, parameter.Id)", canvas, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDTransformGizmo_ExposesEditablePrecisionOverlayLifecycle()
    {
        var view = ReadPage("Editor2DView.axaml");
        var codeBehind = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DView.axaml.cs");

        Assert.Contains("GizmoDimensionPill", view, StringComparison.Ordinal);
        Assert.Contains("OnGizmoDimensionInputKeyDown", view, StringComparison.Ordinal);
        Assert.Contains("TransformPrecisionRequested", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SelectionTransformRequested", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyTwoDSelectionTransform", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TryApplyTransformPrecisionInput", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DismissTransformPrecisionInput", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Input", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertLinesInspector_ExposesReentryPreviewAndEditableBindings()
    {
        var inspector = ReadPage("Editor2DInspector.axaml");

        Assert.Contains("IsVisible=\"{Binding IsTwoDConvertLinesInspectorVisible}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TwoDConvertLineStyleOptions}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding TwoDConvertLineStyle, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("<controls:Editor2DConvertLinePreview", inspector, StringComparison.Ordinal);
        Assert.Contains("Paths=\"{Binding TwoDConvertLinePreviewPaths}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TwoDConvertLineFirstParameterText, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TwoDConvertLineSecondParameterText, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TwoDConvertLineThirdParameterText, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding TwoDConvertLineActionLabel}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanApplyTwoDConvertLines}\"", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void SewingInspector_ExposesSelectModeOperationReentry()
    {
        var inspector = ReadPage("Editor2DInspector.axaml");

        Assert.Contains("IsVisible=\"{Binding IsSewingHoleInspectorVisible}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SewingHoleOperations}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedSewingHoleOperation, Mode=TwoWay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.sewing.inspector", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.sewing.operation", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.sewing.preview", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.sewing.commit", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void DimensionInspector_ExposesHintsAndLiveParameterTable()
    {
        var inspector = ReadPage("Editor2DInspector.axaml");

        Assert.Contains("IsVisible=\"{Binding IsTwoDDimensionToolActive}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TwoDDimensionParameters}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ExpressionDisplay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ValueDisplay}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("StringFormat=editor.2d.dimension.parameter.{0}", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.dimension.inspector", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.dimension.hint", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.dimension.formula-hint", inspector, StringComparison.Ordinal);
        Assert.Contains("sqrt(d2^2+d3^2)", inspector, StringComparison.Ordinal);
        Assert.Contains("2.54cm", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.dimension.parameters", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void DimensionExpressionEditors_ExposeCommitValidationAndPersistentToolLifecycle()
    {
        var view = ReadPage("Editor2DView.axaml");
        var inspector = ReadPage("Editor2DInspector.axaml");
        var codeBehind = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DView.axaml.cs");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");

        Assert.Contains("editor.canvas.2d.dimension-expression-input", view, StringComparison.Ordinal);
        Assert.Contains("OnDimensionExpressionInputKeyDown", view, StringComparison.Ordinal);
        Assert.Contains("HasTwoDMeasurementExpressionError", view, StringComparison.Ordinal);
        Assert.Contains("editor.2d.dimension.selected-expression", inspector, StringComparison.Ordinal);
        Assert.Contains("OnTwoDMeasurementExpressionKeyDown", inspector, StringComparison.Ordinal);
        Assert.Contains("Mode=OneWay", inspector, StringComparison.Ordinal);
        Assert.Contains("TryCommitTwoDMeasurementExpression", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TwoDSelectedMeasurementId = null", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RequestDimensionExpressionInput(referenceMeasurement.Id)", canvas, StringComparison.Ordinal);
        Assert.Contains("RequestDimensionExpressionInput(attachedMeasurement.Id)", canvas, StringComparison.Ordinal);
        Assert.Contains("RequestDimensionExpressionInput($\"{newPathId}:length\")", canvas, StringComparison.Ordinal);
        Assert.Contains("RequestDimensionExpressionInput($\"{newPathId}:radius\")", canvas, StringComparison.Ordinal);
        Assert.Contains("RequestDimensionExpressionInput($\"{newPathId}:width\")", canvas, StringComparison.Ordinal);
        Assert.Contains("e.Key == Key.Tab", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TryGetCreationPrecisionMeasurement", codeBehind, StringComparison.Ordinal);
        Assert.Contains("L: {Math.Sqrt", canvas, StringComparison.Ordinal);
        Assert.Contains("R: {radius.ToString(\"0.00\", CultureInfo.InvariantCulture)} mm", canvas, StringComparison.Ordinal);
        Assert.Contains("SeedParametricDimension", canvas, StringComparison.Ordinal);
        Assert.Contains("Driven or reference dimension", inspector, StringComparison.Ordinal);
        Assert.Contains("TopLevel.GetTopLevel(this)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("e.Handled = shouldConsume", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorHost_ScopesWorkspaceSpecificPanels()
    {
        var host = ReadPage("EditorInspectorHost.axaml");
        Assert.Contains("<pages:Editor2DInspector", host, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.inspector.2d\"", host, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsShowingTwoDWorkspace}\"", host, StringComparison.Ordinal);

        var threeDSections = new Dictionary<string, string>
        {
            ["EditorSelectionInspector.axaml"] = "ShowSelectionPanel",
            ["EditorMeasureInspector.axaml"] = "ShowMeasurePanel",
            ["EditorMoveInspector.axaml"] = "ShowMoveBodiesPanel",
            ["EditorProjectionInspector.axaml"] = "ShowProjectionPanel",
            ["EditorUnfoldInspector.axaml"] = "ShowUnfoldPanel",
            ["EditorOutputInspector.axaml"] = "ShowOutputPanel",
        };

        foreach (var (fileName, visibilityProperty) in threeDSections)
        {
            Assert.Contains(
                $"IsVisible=\"{{Binding {visibilityProperty}}}\"",
                ReadPage(fileName),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnfoldInspector_ExposesMacSeamControls()
    {
        var unfold = ReadPage("EditorUnfoldInspector.axaml");

        Assert.Contains("SeamControlModeIndex", unfold, StringComparison.Ordinal);
        Assert.Contains("Manual (Cuts)", unfold, StringComparison.Ordinal);
        Assert.Contains("Hybrid (Folds)", unfold, StringComparison.Ordinal);
        Assert.Contains("SeamOverrideSummary", unfold, StringComparison.Ordinal);
        Assert.Contains("OnClearActiveSeamOverridesClicked", unfold, StringComparison.Ordinal);
        Assert.Contains("GlobalSeamDecorationIndex", unfold, StringComparison.Ordinal);
        Assert.Contains("AnchorFaceSummary", unfold, StringComparison.Ordinal);
        Assert.Contains("SelectedSeamDecorationIndex", unfold, StringComparison.Ordinal);
        Assert.Contains("Default (Use Global)", unfold, StringComparison.Ordinal);
        Assert.Contains("Plain (Cut)", unfold, StringComparison.Ordinal);
        Assert.Contains("Glue Tab", unfold, StringComparison.Ordinal);
        Assert.Contains("NetLayoutIndex", unfold, StringComparison.Ordinal);
        Assert.Contains("Connected Net", unfold, StringComparison.Ordinal);
        Assert.Contains("UnrollModeIndex", unfold, StringComparison.Ordinal);
        Assert.Contains("Spanning Tree", unfold, StringComparison.Ordinal);
        Assert.True(
            unfold.Split("IsVisible=\"{Binding IsConnectedNetLayout}\"").Length - 1 >= 4,
            "Unroll, seam control, and seam decoration controls must be connected-net only.");
        Assert.Contains("IsVisible=\"{Binding IsSeparatePiecesLayout}\"", unfold, StringComparison.Ordinal);
        Assert.Contains("NATIVE SEPARATE PIECES", unfold, StringComparison.Ordinal);
    }

    [Fact]
    public void DxfCanvasContextMenu_ExposesMacSelectionTransforms()
    {
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");

        Assert.Contains("Header = \"Duplicate\"", canvas, StringComparison.Ordinal);
        Assert.Contains("Header = \"Flip Horizontal\"", canvas, StringComparison.Ordinal);
        Assert.Contains("Header = \"Flip Vertical\"", canvas, StringComparison.Ordinal);
        Assert.Contains("DuplicateTwoDSelection", canvas, StringComparison.Ordinal);
        Assert.Contains("FlipTwoDSelection(horizontal)", canvas, StringComparison.Ordinal);
        Assert.Contains("CreateBooleanMenuItem(\"Union\", \"Union\")", canvas, StringComparison.Ordinal);
        Assert.Contains("CreateBooleanMenuItem(\"Subtract\", \"Subtract\")", canvas, StringComparison.Ordinal);
        Assert.Contains("CreateBooleanMenuItem(\"Intersect\", \"Intersect\")", canvas, StringComparison.Ordinal);
        Assert.Contains("ApplyTwoDBooleanAsync(operation)", canvas, StringComparison.Ordinal);
        Assert.Contains("Header = \"Convert to Dashed\"", canvas, StringComparison.Ordinal);
        Assert.Contains("TwoDConvertLineStyle = \"dashed\"", canvas, StringComparison.Ordinal);
        Assert.Contains("ApplyTwoDConvertLines", canvas, StringComparison.Ordinal);
        Assert.Contains("Header = \"Reload from Disk\"", canvas, StringComparison.Ordinal);
        Assert.Contains("ReloadSelectedTwoDImportsFromDiskAsync", canvas, StringComparison.Ordinal);
        Assert.Contains("HasSelectedTwoDImportGroup: true", canvas, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshGeneratedOutputAsync", canvas, StringComparison.Ordinal);
        var outputInspector = ReadPage("EditorOutputInspector.axaml");
        Assert.Contains("OnRefreshTwoDClicked", outputInspector, StringComparison.Ordinal);
        Assert.Contains("editor.output.refresh-generated", outputInspector, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandPalette_IsCatalogBackedAndHostedByTheEditorShell()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var palette = ReadPage("EditorCommandPalette.axaml");

        Assert.Contains("<pages:EditorCommandPalette", shell, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText=\"Search tools and commands…\"", palette, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CommandSearchQuery, Mode=TwoWay}\"", palette, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding CommandSearchResults}\"", palette, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{Binding Identifier}\"", palette, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsCommandSearchEmpty}\"", palette, StringComparison.Ordinal);
        Assert.Contains("No matching commands", palette, StringComparison.Ordinal);
    }

    private static string ReadPage(string fileName)
        => File.ReadAllText(FindRepositoryFile("src", "Pathstitch.App", "Pages", fileName));

    private static string ReadRepositoryFile(params string[] pathParts)
        => File.ReadAllText(FindRepositoryFile(pathParts));

    private static int CountOccurrences(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(pathParts)}");
    }
}
