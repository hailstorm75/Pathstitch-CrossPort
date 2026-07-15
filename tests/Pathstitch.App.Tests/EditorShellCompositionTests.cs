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
    public void HelpMenu_ExposesAnAboutDialogForAppParity()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var aboutDialog = ReadRepositoryFile("src", "Pathstitch.App", "Dialogs", "AboutDialog.axaml");

        Assert.Contains("AutomationProperties.AutomationId=\"editor.menu.help\"", shell, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"editor.menu.help.about\"", shell, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAboutClicked\"", shell, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"dialog.about\"", aboutDialog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"dialog.about.close\"", aboutDialog, StringComparison.Ordinal);
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
        Assert.Contains("ActivateSelected", codeBehind, StringComparison.Ordinal);
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

        Assert.Contains("DxfPreviewCanvas", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("StackPanel", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsControl", twoD, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBox", twoD, StringComparison.Ordinal);
        Assert.Contains("TwoDOffsetDistanceText", inspector, StringComparison.Ordinal);
        Assert.Contains("TwoDPolygonSides", inspector, StringComparison.Ordinal);
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
