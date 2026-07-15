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

        foreach (var modeGroup in EditorToolCatalog.All.GroupBy(descriptor => descriptor.Mode))
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
                descriptor.ShortcutText))
            .ToArray();
        var circleIndex = Array.FindIndex(customizations, customization => customization.Identifier == "2d.circle");
        customizations[circleIndex] = customizations[circleIndex] with { Order = -10, ShortcutText = " g " };

        var customized = EditorToolCatalog.ApplyCustomizations(customizations);

        Assert.Equal(
            EditorToolCatalog.All.Select(descriptor => descriptor.Identifier).Order(),
            customized.Select(descriptor => descriptor.Identifier).Order());
        var circle = Assert.Single(customized, descriptor => descriptor.Identifier == "2d.circle");
        Assert.Equal(-10, circle.Order);
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
    public void SidebarItems_AreProjectedFromTheSharedThreeDCatalog()
    {
        var descriptors = EditorToolCatalog.ForMode(EditorMode.ThreeD);
        var items = descriptors.Select(descriptor => new EditorSidebarToolItemViewModel(descriptor)).ToArray();

        Assert.Equal(descriptors.Select(descriptor => descriptor.Identifier), items.Select(item => item.Identifier));
        Assert.Equal(descriptors.Select(descriptor => descriptor.CommandKey), items.Select(item => item.Key));
        Assert.Equal(descriptors.Select(descriptor => descriptor.GroupKey), items.Select(item => item.GroupKey));
        Assert.Equal(descriptors.Select(descriptor => descriptor.Order), items.Select(item => item.Order));
    }
}
