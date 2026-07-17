using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Pathstitch.App.Converters;

public sealed class LayerColorHexToColorConverter : IValueConverter
{
    internal static readonly Color FallbackColor = Color.Parse("#4D7FFF");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => TryParse(value as string, out var color) ? color : FallbackColor;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;

    internal static bool TryParse(string? value, out Color color)
    {
        var normalized = value?.Trim();
        if (normalized is { Length: 7 }
            && normalized[0] == '#'
            && normalized.Skip(1).All(Uri.IsHexDigit))
        {
            color = Color.Parse(normalized);
            return true;
        }
        color = FallbackColor;
        return false;
    }

    internal static string ToHex(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
