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
    public void FileMenu_OffersDocumentLifecycleCommandsAndShortcuts()
    {
        var shell = ReadPage("EditorShellView.axaml");

        Assert.Contains("Command=\"{Binding SaveDocumentCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SaveAndCloseDocumentCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CloseDocumentCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+S\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+Shift+W\"", shell, StringComparison.Ordinal);
        Assert.Contains("HotKey=\"Ctrl+W\"", shell, StringComparison.Ordinal);
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
        Assert.Contains("Button.command-result.selected", ReadPage("EditorCommandPalette.axaml"), StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommandSearchResults\"", ReadPage("EditorCommandPalette.axaml"), StringComparison.Ordinal);
        Assert.Contains("FocusSearch", codeBehind, StringComparison.Ordinal);
        Assert.Contains("KeyModifiers.Control", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs"), StringComparison.Ordinal);
        Assert.Contains("KeyModifiers.Meta", ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorShellView.axaml.cs"), StringComparison.Ordinal);
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

        Assert.Contains("TwoDScaleFromCenter, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDScaleFactorText, Mode=TwoWay", inspector, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanApplyTwoDScale}\"", inspector, StringComparison.Ordinal);
        Assert.Contains("OnApplyTwoDScaleClicked", inspector, StringComparison.Ordinal);
        Assert.Contains("editor.2d.scale.apply", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDMirrorStaging_IsBoundToCanvasAndInspector()
    {
        var view = ReadPage("Editor2DView.axaml");
        var inspector = ReadPage("Editor2DInspector.axaml");
        var handlers = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "EditorInteractionControlBase.cs");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");

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
        Assert.Contains("mirrorViewModel.CancelTwoDMirror(exitTool: true)", canvas, StringComparison.Ordinal);
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

        Assert.Contains("DxfPreviewCanvas", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("StackPanel", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsControl", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBox", twoD, StringComparison.Ordinal);
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
        Assert.Contains("CancelTwoDCornerToolSession", canvas, StringComparison.Ordinal);
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
        Assert.Contains("Content=\"+ Folder\"", layers, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TwoDFolders}\"", layers, StringComparison.Ordinal);
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
        Assert.Contains("SelectedSeamDecoration", unfold, StringComparison.Ordinal);
        Assert.Contains("NetLayoutIndex", unfold, StringComparison.Ordinal);
        Assert.Contains("Connected Net", unfold, StringComparison.Ordinal);
        Assert.Contains("UnrollModeIndex", unfold, StringComparison.Ordinal);
        Assert.Contains("Spanning Tree", unfold, StringComparison.Ordinal);
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
        Assert.Contains("RefreshGeneratedOutputAsync", canvas, StringComparison.Ordinal);
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
