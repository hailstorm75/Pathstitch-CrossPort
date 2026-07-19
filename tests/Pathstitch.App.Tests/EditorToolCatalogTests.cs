using System.Text.Json;
using Avalonia.Media;
using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Tests.Fixtures;

namespace Pathstitch.App.Tests;

public sealed class EditorToolCatalogTests
{
    [Fact]
    public void Catalog_CoversEveryTwoDAndThreeDToolExactlyOnce()
    {
        var twoDTools = EditorToolCatalog.ForMode(EditorMode.TwoD)
            .Where(descriptor => descriptor.TwoDTool is not null)
            .Select(descriptor => descriptor.TwoDTool!.Value)
            .Order()
            .ToArray();
        var threeDTools = EditorToolCatalog.ForMode(EditorMode.ThreeD)
            .Where(descriptor => descriptor.ThreeDTool is not null)
            .Select(descriptor => descriptor.ThreeDTool!.Value)
            .Order()
            .ToArray();
        var actions = EditorToolCatalog.All
            .Where(descriptor => descriptor.Action is not null)
            .Select(descriptor => descriptor.Action!.Value)
            .Order()
            .ToArray();

        Assert.Equal(Enum.GetValues<Editor2DTool>().Order().ToArray(), twoDTools);
        Assert.Equal(Enum.GetValues<Editor3DTool>().Order().ToArray(), threeDTools);
        Assert.Equal(Enum.GetValues<EditorSidebarAction>().Order().ToArray(), actions);
        Assert.Empty(EditorToolCatalog.ForMode(EditorMode.Batch));
    }

    [Fact]
    public void Catalog_HasUniqueCompleteAndOrderedDescriptors()
    {
        Assert.NotEmpty(EditorToolCatalog.All);
        Assert.Equal(
            EditorToolCatalog.All.Count,
            EditorToolCatalog.All.Select(descriptor => descriptor.Identifier).Distinct(StringComparer.Ordinal).Count());

        foreach (var descriptor in EditorToolCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Identifier));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.IconKey));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.IconPathData));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Label));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Hint));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.CommandKey));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.GroupKey));
            Assert.True(descriptor.IsEnabled);
            Assert.False(descriptor.IsSelected);

            var targetCount = (descriptor.TwoDTool is null ? 0 : 1)
                + (descriptor.ThreeDTool is null ? 0 : 1)
                + (descriptor.Action is null ? 0 : 1);
            Assert.Equal(1, targetCount);

            if (descriptor.TwoDTool is not null)
                Assert.Equal(EditorMode.TwoD, descriptor.Mode);
            if (descriptor.ThreeDTool is not null)
                Assert.Equal(EditorMode.ThreeD, descriptor.Mode);
            if (descriptor.Action is not null)
                Assert.NotEqual(EditorMode.Batch, descriptor.Mode);
            if (descriptor.IsTool)
                Assert.False(string.IsNullOrWhiteSpace(descriptor.InspectorPanelKey));
        }

        foreach (var modeGroup in EditorToolCatalog.All.GroupBy(descriptor => (descriptor.Mode, descriptor.Container)))
        {
            Assert.Equal(
                Enumerable.Range(0, modeGroup.Count()),
                modeGroup.Select(descriptor => descriptor.Order));
            Assert.Equal(
                modeGroup.Count(),
                modeGroup.Select(descriptor => descriptor.CommandKey).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Catalog_ProvidesParsableVectorIconsForEveryToolAndAction()
    {
        AvaloniaHeadlessTestHost.EnsureInitialized();

        foreach (var descriptor in EditorToolCatalog.All)
        {
            var geometry = StreamGeometry.Parse(descriptor.IconPathData);

            Assert.NotNull(geometry);
            Assert.True(geometry.Bounds.Width > 0, descriptor.Identifier);
            Assert.True(geometry.Bounds.Height > 0, descriptor.Identifier);
        }
    }

    [Fact]
    public void Find_ResolvesModeSpecificCommands()
    {
        var twoDSelect = EditorToolCatalog.Find(EditorMode.TwoD, "select");
        var threeDSelect = EditorToolCatalog.Find(EditorMode.ThreeD, "select");

        Assert.NotNull(twoDSelect);
        Assert.NotNull(threeDSelect);
        Assert.Equal(Editor2DTool.Select, twoDSelect.TwoDTool);
        Assert.Equal(Editor3DTool.Select, threeDSelect.ThreeDTool);
        Assert.NotEqual(twoDSelect.Identifier, threeDSelect.Identifier);
        Assert.Null(EditorToolCatalog.Find(EditorMode.Batch, "select"));
    }

    [Fact]
    public void FindByShortcut_ResolvesEveryShortcutDescriptorOnlyWithinItsMode()
    {
        foreach (var descriptor in EditorToolCatalog.All)
        {
            if (descriptor.ShortcutText is null)
            {
                Assert.NotEqual(
                    descriptor,
                    EditorToolCatalog.FindByShortcut(descriptor.Mode, descriptor.CommandKey));
                continue;
            }

            Assert.Equal(
                descriptor,
                EditorToolCatalog.FindByShortcut(descriptor.Mode, descriptor.ShortcutText));

            foreach (var otherMode in Enum.GetValues<EditorMode>().Where(mode => mode != descriptor.Mode))
            {
                var otherModeMatch = EditorToolCatalog.FindByShortcut(otherMode, descriptor.ShortcutText);
                Assert.True(
                    otherModeMatch is null || otherModeMatch.Mode != descriptor.Mode,
                    $"{descriptor.Identifier} leaked into {otherMode} shortcut routing.");
            }
        }
    }

    [Fact]
    public void FindByShortcut_NormalizesCaseAndWhitespace()
    {
        var descriptor = Assert.Single(
            EditorToolCatalog.ForMode(EditorMode.TwoD),
            candidate => candidate.ShortcutText == "L");

        Assert.Equal(descriptor, EditorToolCatalog.FindByShortcut(EditorMode.TwoD, " l "));
        Assert.Null(EditorToolCatalog.FindByShortcut(EditorMode.TwoD, "  "));
    }

    [Fact]
    public void ShortcutValidation_RejectsDuplicatesWithinAModeButAllowsThemAcrossModes()
    {
        var twoDSelect = EditorToolCatalog.Find(EditorMode.TwoD, "select")!;
        var twoDMove = EditorToolCatalog.Find(EditorMode.TwoD, "move")!;
        var threeDSelect = EditorToolCatalog.Find(EditorMode.ThreeD, "select")!;
        var duplicateInTwoD = twoDMove with { ShortcutText = twoDSelect.ShortcutText };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EditorToolCatalog.ValidateShortcutUniqueness([twoDSelect, duplicateInTwoD]));

        Assert.Contains(twoDSelect.Identifier, exception.Message, StringComparison.Ordinal);
        Assert.Contains(duplicateInTwoD.Identifier, exception.Message, StringComparison.Ordinal);
        EditorToolCatalog.ValidateShortcutUniqueness([twoDSelect, threeDSelect]);
    }

    [Fact]
    public void ApplyCustomizations_UsesStableIdentifiersAndPreservesTheCatalogSet()
    {
        var customizations = EditorToolCatalog.All
            .Select(descriptor => new EditorToolCustomization(
                descriptor.Identifier,
                descriptor.Order,
                descriptor.ShortcutText,
                descriptor.Container))
            .ToArray();
        var circleIndex = Array.FindIndex(customizations, customization => customization.Identifier == "2d.circle");
        customizations[circleIndex] = customizations[circleIndex] with { Order = -10, ShortcutText = " g " };

        var customized = EditorToolCatalog.ApplyCustomizations(customizations);

        Assert.Equal(
            EditorToolCatalog.All.Select(descriptor => descriptor.Identifier).Order(),
            customized.Select(descriptor => descriptor.Identifier).Order());
        var circle = Assert.Single(customized, descriptor => descriptor.Identifier == "2d.circle");
        Assert.Equal(0, circle.Order);
        Assert.Equal("G", circle.ShortcutText);
        Assert.Equal(circle, EditorToolCatalog.FindByShortcut(customized, EditorMode.TwoD, "g"));
    }

    [Fact]
    public void ApplyCustomizations_RejectsDuplicateShortcutsWithoutChangingTheDefaultCatalog()
    {
        var defaultCircle = Assert.Single(EditorToolCatalog.All, descriptor => descriptor.Identifier == "2d.circle");

        Assert.Throws<InvalidOperationException>(() => EditorToolCatalog.ApplyCustomizations(
        [
            new EditorToolCustomization("2d.circle", defaultCircle.Order, "R"),
        ]));

        Assert.Equal("C", EditorToolCatalog.Find(EditorMode.TwoD, "circle")!.ShortcutText);
    }

    [Fact]
    public void DefaultLayout_UsesMainShapesAndMoreWithEveryToolPlacedExactlyOnce()
    {
        var twoD = EditorToolCatalog.ForMode(EditorMode.TwoD);

        Assert.Equal(twoD.Count, twoD.Select(descriptor => descriptor.Identifier).Distinct().Count());
        Assert.Equal(
        [
            "2d.select", "2d.move", "2d.pan", "2d.scale", "2d.offset",
            "2d.add-thickness", "2d.add-sewing-holes", "2d.cleanup", "2d.trim",
            "2d.measure", "2d.dimension", "2d.fillet", "2d.chamfer",
            "2d.pattern", "2d.paper-folding",
        ],
            twoD.Where(descriptor => descriptor.Container == EditorToolbarContainer.Main)
                .OrderBy(descriptor => descriptor.Order)
                .Select(descriptor => descriptor.Identifier));
        Assert.Equal(
        [
            "2d.line", "2d.circle", "2d.rectangle", "2d.polygon", "2d.text", "2d.pen",
        ],
            twoD.Where(descriptor => descriptor.Container == EditorToolbarContainer.Shapes)
                .OrderBy(descriptor => descriptor.Order)
                .Select(descriptor => descriptor.Identifier));
        Assert.Equal(
        [
            "2d.mirror", "2d.convert-lines", "2d.flip-horizontal", "2d.flip-vertical", "2d.duplicate",
        ],
            twoD.Where(descriptor => descriptor.Container == EditorToolbarContainer.More)
                .OrderBy(descriptor => descriptor.Order)
                .Select(descriptor => descriptor.Identifier));
        Assert.All(
            twoD.Where(descriptor => descriptor.Container == EditorToolbarContainer.Shapes),
            descriptor => Assert.True(descriptor.CanPlaceInShapes));
        Assert.All(
            EditorToolCatalog.ForMode(EditorMode.ThreeD),
            descriptor => Assert.Equal(EditorToolbarContainer.Main, descriptor.Container));
    }

    [Fact]
    public void ApplyCustomizations_MigratesLegacyOneRailOrderToMainWithoutLosingTools()
    {
        var legacy = EditorToolCatalog.ForMode(EditorMode.TwoD)
            .OrderByDescending(descriptor => descriptor.Identifier, StringComparer.Ordinal)
            .Select((descriptor, order) => new EditorToolCustomization(
                descriptor.Identifier,
                order,
                descriptor.ShortcutText))
            .ToArray();

        var customized = EditorToolCatalog.ApplyCustomizations(legacy)
            .Where(descriptor => descriptor.Mode == EditorMode.TwoD)
            .OrderBy(descriptor => descriptor.Order)
            .ToArray();

        Assert.Equal(legacy.Select(item => item.Identifier), customized.Select(item => item.Identifier));
        Assert.All(customized, descriptor => Assert.Equal(EditorToolbarContainer.Main, descriptor.Container));
    }

    [Fact]
    public void ApplyCustomizations_RepairsDuplicatesUnknownsInvalidShapesAndMissingTools()
    {
        var customized = EditorToolCatalog.ApplyCustomizations(
        [
            new EditorToolCustomization("2d.line", 4, "L", EditorToolbarContainer.More),
            new EditorToolCustomization("2d.line", 0, "L", EditorToolbarContainer.Shapes),
            new EditorToolCustomization("2d.select", 0, "1", EditorToolbarContainer.Shapes),
            new EditorToolCustomization("removed.tool", 0, null, EditorToolbarContainer.Main),
        ]);

        Assert.Equal(
            EditorToolCatalog.All.Select(descriptor => descriptor.Identifier).Order(),
            customized.Select(descriptor => descriptor.Identifier).Order());
        Assert.Equal(
            EditorToolbarContainer.More,
            Assert.Single(customized, descriptor => descriptor.Identifier == "2d.line").Container);
        Assert.Equal(
            EditorToolbarContainer.Main,
            Assert.Single(customized, descriptor => descriptor.Identifier == "2d.select").Container);
        Assert.Equal(
            EditorToolbarContainer.Shapes,
            Assert.Single(customized, descriptor => descriptor.Identifier == "2d.circle").Container);
        Assert.All(
            customized.Where(descriptor => descriptor.Container == EditorToolbarContainer.Shapes),
            descriptor => Assert.True(descriptor.CanPlaceInShapes));
    }

    [Fact]
    public void LegacyCustomizationJson_DeserializesWithNoContainer()
    {
        var customization = JsonSerializer.Deserialize<EditorToolCustomization>(
            """{"identifier":"2d.circle","order":3,"shortcutText":"C"}""");

        Assert.NotNull(customization);
        Assert.Null(customization.Container);
    }

    [Fact]
    public void SidebarItems_AreProjectedFromTheSharedThreeDCatalog()
    {
        var descriptors = EditorToolCatalog.ForMode(EditorMode.ThreeD);
        var items = descriptors.Select(descriptor => new EditorSidebarToolItemViewModel(descriptor)).ToArray();

        Assert.Equal(descriptors.Select(descriptor => descriptor.Identifier), items.Select(item => item.Identifier));
        Assert.Equal(descriptors.Select(descriptor => descriptor.CommandKey), items.Select(item => item.Key));
        Assert.Equal(descriptors.Select(descriptor => descriptor.GroupKey), items.Select(item => item.GroupKey));
        Assert.Equal(descriptors.Select(descriptor => descriptor.Order), items.Select(item => item.Order));
    }

    [Fact]
    public void SidebarItems_ExposeSpecificTwoDToolHintsForContextualHoverHelp()
    {
        var descriptors = EditorToolCatalog.ForMode(EditorMode.TwoD);
        var select = new EditorSidebarToolItemViewModel(
            Assert.Single(descriptors, descriptor => descriptor.TwoDTool == Editor2DTool.Select));
        var rectangle = new EditorSidebarToolItemViewModel(
            Assert.Single(descriptors, descriptor => descriptor.TwoDTool == Editor2DTool.SketchRectangle));

        Assert.Equal(EditorPageViewModel.GetTwoDToolHint(Editor2DTool.Select), select.ContextualHelpText);
        Assert.Equal(EditorPageViewModel.GetTwoDToolHint(Editor2DTool.SketchRectangle), rectangle.ContextualHelpText);
        Assert.NotEqual(select.ContextualHelpText, rectangle.ContextualHelpText);
        Assert.Contains("first corner", rectangle.ContextualHelpText, StringComparison.Ordinal);
    }
}
