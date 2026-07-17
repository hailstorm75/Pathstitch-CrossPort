using Avalonia.Controls;
using Avalonia.LogicalTree;
using Domain.App.Models;
using Pathstitch.App.Dialogs;
using Pathstitch.App.Tests.Fixtures;

namespace Pathstitch.App.Tests;

public sealed class EditorImportUnitsInfoTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public void WeakMetreDeclaration_UsesPlausibleSizeInsteadOfDeclaredScale()
    {
        var info = new Editor2DImportUnitsInfo("large.dxf", 6, 1000.0, 10_000, 1);

        Assert.True(info.RequiresPrompt);
        Assert.False(info.HasStrongUnitDeclaration);
        Assert.Equal(0.001, info.RecommendedScaleFactor, 9);
    }

    [Fact]
    public void StrongInchDeclaration_DefaultsToDeclaredScale()
    {
        var info = new Editor2DImportUnitsInfo("inches.dxf", 1, 25.4, 10, 5);

        Assert.True(info.HasStrongUnitDeclaration);
        Assert.Equal(25.4, info.RecommendedScaleFactor, 9);
    }

    [Fact]
    public void UnitlessTinyDrawing_DefaultsToPlausibleScale()
    {
        var info = new Editor2DImportUnitsInfo("tiny.dxf", 0, null, 0.1, 0.05);

        Assert.True(info.RequiresPrompt);
        Assert.Equal(1000.0, info.RecommendedScaleFactor, 9);
    }

    [Fact]
    public void PlausibleWeakDeclaration_KeepsCurrentSize()
    {
        var info = new Editor2DImportUnitsInfo("plausible.dxf", 6, 1000.0, 120, 80);

        Assert.False(info.RequiresPrompt);
        Assert.Equal(1.0, info.RecommendedScaleFactor, 9);
    }

    [Fact]
    public void EmptyBounds_DoNotPrompt()
    {
        var info = new Editor2DImportUnitsInfo("empty.dxf", 1, 25.4, 0, 0);

        Assert.False(info.RequiresPrompt);
        Assert.Equal(25.4, info.RecommendedScaleFactor, 9);
    }

    [Theory]
    [InlineData(6, 1000.0, 10_000.0, 1.0, 6, "unusually large")]
    [InlineData(1, 25.4, 10.0, 5.0, 1, "declares its units as inches")]
    [InlineData(0, null, 0.1, 0.05, 3, "unusually small")]
    public async Task Dialog_SelectsRecommendedScaleAndExplainsWhyPrompted(
        int unitCode,
        double? declaredFactor,
        double width,
        double height,
        int expectedIndex,
        string expectedDescription)
    {
        await _ui.RunAsync(() =>
        {
            var dialog = new EditorImportUnitsDialog();
            dialog.SetImportInfo(new Editor2DImportUnitsInfo(
                "drawing.dxf", unitCode, declaredFactor, width, height));

            var choice = _ui.FindByAutomationId<ComboBox>(dialog, "dialog.import-units.choice");
            Assert.Equal(expectedIndex, choice.SelectedIndex);
            Assert.Contains(
                expectedDescription,
                dialog.GetLogicalDescendants().OfType<TextBlock>().Single(text =>
                    text.Text?.Contains("drawing.dxf", StringComparison.Ordinal) == true).Text,
                StringComparison.Ordinal);
        });
    }
}
