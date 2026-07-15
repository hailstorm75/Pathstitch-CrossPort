using System.Runtime.InteropServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Domain.App.Models;
using Pathstitch.App.Tests.Fixtures;

namespace Pathstitch.App.Tests;

public sealed class UiAutomationIdentifierTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public void EditorShell_ExposesStableRegionIdentifiers()
    {
        var shell = _ui.LoadXaml("src", "Pathstitch.App", "Pages", "EditorShellView.axaml");
        var expectedIds = new[]
        {
            "editor.shell",
            "editor.header",
            "editor.tool-rail",
            "editor.context-panel",
            "editor.context.3d-bodies",
            "editor.context.2d-layers",
            "editor.context.batch",
            "editor.workspace-host",
            "editor.workspace.3d",
            "editor.workspace.2d",
            "editor.workspace.snap",
            "editor.mode-switcher",
            "editor.inspector-host",
            "editor.inspector.resize",
        };

        foreach (var automationId in expectedIds)
            _ui.FindXamlElementByAutomationId(shell, automationId);
    }

    [Fact]
    public void WorkspacesAndDialogs_ExposeStableCanvasAndActionIdentifiers()
    {
        var twoD = _ui.LoadXaml("src", "Pathstitch.App", "Pages", "Editor2DView.axaml");
        var threeD = _ui.LoadXaml("src", "Pathstitch.App", "Pages", "Editor3DView.axaml");
        var home = _ui.LoadXaml("src", "Pathstitch.App", "Pages", "HomePageView.axaml");

        _ui.FindXamlElementByAutomationId(twoD, "editor.canvas.2d");
        _ui.FindXamlElementByAutomationId(threeD, "editor.canvas.3d");
        _ui.FindXamlElementByAutomationId(threeD, "editor.canvas.3d.webview");
        _ui.FindXamlElementByAutomationId(threeD, "editor.dialog.open-3d-model");
        _ui.FindXamlElementByAutomationId(home, "home.dialog.open-file");
        _ui.FindXamlElementByAutomationId(home, "home.dialog.open-files");
        _ui.FindXamlElementByAutomationId(home, "home.dialog.new-project");
        _ui.FindXamlElementByAutomationId(home, "home.dialog.open-project");
        _ui.FindXamlElementByAutomationId(home, "home.new-project.name");
    }

    [Fact]
    public void InspectorSections_ExposeStableIdentifiers()
    {
        var host = _ui.LoadXaml("src", "Pathstitch.App", "Pages", "EditorInspectorHost.axaml");
        var expectedIds = new[]
        {
            "editor.inspector.2d",
            "editor.inspector.selection",
            "editor.inspector.measure",
            "editor.inspector.move",
            "editor.inspector.projection",
            "editor.inspector.unfold",
            "editor.inspector.output",
            "editor.inspector.errors",
        };

        foreach (var automationId in expectedIds)
            _ui.FindXamlElementByAutomationId(host, automationId);
    }

    [Fact]
    public void CatalogToolButtons_BindAutomationIdToStableDescriptorIdentifier()
    {
        var rail = _ui.LoadXaml("src", "Pathstitch.App", "Pages", "EditorToolRail.axaml");
        _ui.FindXamlElementByAutomationId(rail, "{Binding Identifier}");

        var descriptors = Enum
            .GetValues<EditorMode>()
            .SelectMany(EditorToolCatalog.ForMode)
            .ToArray();

        Assert.NotEmpty(descriptors);
        Assert.All(descriptors, descriptor => Assert.False(string.IsNullOrWhiteSpace(descriptor.Identifier)));
        Assert.Equal(
            descriptors.Length,
            descriptors.Select(descriptor => descriptor.Identifier).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task HeadlessFixture_SelectsByIdWhenSiblingOrderChanges()
    {
        await _ui.RunAsync(() =>
        {
            var expected = new Button();
            AutomationProperties.SetAutomationId(expected, "fixture.target");
            var unrelated = new Button();
            AutomationProperties.SetAutomationId(unrelated, "fixture.other");
            var root = new Grid { Children = { unrelated, expected } };

            Assert.Same(expected, _ui.FindByAutomationId<Button>(root, "fixture.target"));

            root.Children.Move(1, 0);

            Assert.Same(expected, _ui.FindByAutomationId<Button>(root, "fixture.target"));
        });
    }

    [Fact]
    public void PlatformSmokeFixture_UsesTheCurrentPlatformArtifactShape()
    {
        var fixture = new PlatformSmokeTestFixture();
        var launch = fixture.CreateLaunchInfo(Path.GetTempPath(), "--smoke-test");

        Assert.Contains(RuntimeInformation.ProcessArchitecture.ToString(), fixture.CurrentTarget.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("--smoke-test", Assert.Single(launch.ArgumentList));

        if (OperatingSystem.IsWindows())
            Assert.EndsWith("Pathstitch.App.exe", launch.FileName, StringComparison.OrdinalIgnoreCase);
        else if (OperatingSystem.IsMacOS())
            Assert.Contains("Pathstitch.app", launch.FileName, StringComparison.Ordinal);
    }
}
