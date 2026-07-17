using Avalonia.Media;
using Pathstitch.App.Converters;
using Pathstitch.App.Pages;

namespace Pathstitch.App.Tests;

public sealed class LayerColorPickerTests
{
    [Theory]
    [InlineData("#ff8800", "#FF8800")]
    [InlineData("  #123abc  ", "#123ABC")]
    public void ManualHex_NormalizesOpaqueRgb(string input, string expected)
    {
        Assert.True(Editor2DLayersPanel.TryNormalizeLayerColorHex(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    [InlineData("#11223380")]
    public void ManualHex_RejectsInvalidOrAlphaValues(string input)
        => Assert.False(Editor2DLayersPanel.TryNormalizeLayerColorHex(input, out _));

    [Fact]
    public void Converter_ParsesRgbUsesFallbackAndDropsPickerAlpha()
    {
        Assert.True(LayerColorHexToColorConverter.TryParse("#FF8800", out var parsed));
        Assert.Equal(Color.FromRgb(255, 136, 0), parsed);
        Assert.False(LayerColorHexToColorConverter.TryParse("bad", out var fallback));
        Assert.Equal(LayerColorHexToColorConverter.FallbackColor, fallback);
        Assert.Equal("#123456", LayerColorHexToColorConverter.ToHex(Color.FromArgb(17, 18, 52, 86)));
    }
}
