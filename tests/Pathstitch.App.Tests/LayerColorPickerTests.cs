using Avalonia.Media;
using Pathstitch.App.Converters;

namespace Pathstitch.App.Tests;

public sealed class LayerColorPickerTests
{
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
