using Domain.App.Models;

namespace Domain.App.Services;

public static class Editor2DTextCreationService
{
    private const double MinimumSize = 1e-6;

    public static Editor2DPreviewPath? Create(
        string pathId,
        Editor2DPoint boxStart,
        Editor2DPoint boxEnd,
        string text,
        double height,
        string fontFamily,
        double characterSpacing,
        bool bold,
        bool italic,
        bool underline,
        string fitMode = "None")
    {
        if (string.IsNullOrWhiteSpace(pathId)
            || !IsFinite(boxStart)
            || !IsFinite(boxEnd)
            || !double.IsFinite(height)
            || !double.IsFinite(characterSpacing))
        {
            return null;
        }

        var boxWidth = Math.Abs(boxEnd.X - boxStart.X);
        var boxHeight = Math.Abs(boxEnd.Y - boxStart.Y);
        if (boxWidth <= MinimumSize || boxHeight <= MinimumSize)
            return null;

        var start = new Editor2DPoint(Math.Min(boxStart.X, boxEnd.X), Math.Min(boxStart.Y, boxEnd.Y));
        var normalizedText = string.IsNullOrWhiteSpace(text) ? "Label" : text.Replace("\r\n", "\n");
        var normalizedHeight = Math.Max(height, 0.1);
        var normalizedFont = string.IsNullOrWhiteSpace(fontFamily) ? null : fontFamily.Trim();
        var normalizedFitMode = NormalizeFitMode(fitMode);
        var lines = normalizedText.Split('\n');
        var lineCount = Math.Max(lines.Length, 1);
        var longestLineLength = Math.Max(lines.Max(static line => line.Length), 1);
        if (normalizedFitMode is "Height" or "Both")
        {
            normalizedHeight = Math.Max(0.1, boxHeight / lineCount);
        }
        else if (normalizedFitMode == "Width")
        {
            normalizedHeight = Math.Max(0.1, boxWidth / (longestLineLength * 0.6));
        }

        var widthFactor = 1.0;
        if (normalizedFitMode == "Both")
        {
            var naturalWidth = longestLineLength * 0.6 * normalizedHeight;
            if (naturalWidth > MinimumSize)
                widthFactor = boxWidth / naturalWidth;
        }

        return new Editor2DPreviewPath(
            pathId.Trim(),
            "TEXT",
            Editor2DGeometry.BuildTextBoundsPoints(
                start, normalizedText, normalizedHeight, widthFactor: widthFactor, characterSpacing: characterSpacing),
            IsClosed: false,
            Start: start,
            Text: normalizedText,
            TextHeight: normalizedHeight,
            RotationDegrees: 0.0,
            WidthFactor: widthFactor,
            FontFamily: normalizedFont,
            CharacterSpacing: characterSpacing,
            IsBold: bold,
            IsItalic: italic,
            IsUnderline: underline);
    }

    private static string NormalizeFitMode(string? fitMode)
        => fitMode?.Trim() is "Height" or "Width" or "Both" ? fitMode.Trim() : "None";

    private static bool IsFinite(Editor2DPoint point)
        => double.IsFinite(point.X) && double.IsFinite(point.Y);
}
