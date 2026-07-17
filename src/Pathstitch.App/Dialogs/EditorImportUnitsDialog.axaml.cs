using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.Models;

namespace Pathstitch.App.Dialogs;

public sealed partial class EditorImportUnitsDialog : Window
{
    private sealed record Choice(string Label, double Factor);

    public EditorImportUnitsDialog()
    {
        InitializeComponent();
    }

    public void SetImportInfo(Editor2DImportUnitsInfo info)
    {
        DescriptionText.Text = info.HasStrongUnitDeclaration
            ? $"\u201c{Path.GetFileName(info.SourcePath)}\u201d imported at {info.Width:0.###} \u00d7 {info.Height:0.###} mm and declares its units as {info.DeclaredUnit}. Confirm the real-world size."
            : $"\u201c{Path.GetFileName(info.SourcePath)}\u201d imported at {info.Width:0.###} \u00d7 {info.Height:0.###} mm, which looks unusually {(info.MaxDimension > 2000.0 ? "large" : "small")}. Pick the real-world size.";
        var choices = new List<Choice> { new($"Keep current size ({info.Width:0.###} \u00d7 {info.Height:0.###} mm)", 1.0) };
        if (info.HasStrongUnitDeclaration
            && info.MillimetersPerDrawingUnit is { } fileFactor
            && Math.Abs(fileFactor - 1.0) > 1e-9)
        {
            choices.Add(new($"Apply file units ({info.DeclaredUnit} → mm)", fileFactor));
        }

        foreach (var (label, factor) in new[]
        {
            ("Centimetres → mm", 10.0),
            ("Inches → mm", 25.4),
            ("Metres → mm", 1000.0),
            ("Shrink ÷10", 0.1),
            ("Shrink ÷25.4", 1.0 / 25.4),
            ("Shrink ÷1000", 0.001),
        })
        {
            if (choices.All(choice => Math.Abs(choice.Factor - factor) > 1e-9))
                choices.Add(new($"{label} → {(info.Width * factor).ToString("0.###", CultureInfo.InvariantCulture)} × {(info.Height * factor).ToString("0.###", CultureInfo.InvariantCulture)} mm", factor));
        }

        ScaleChoice.ItemsSource = choices;
        ScaleChoice.SelectedIndex = Math.Max(
            0,
            choices.FindIndex(choice => Math.Abs(choice.Factor - info.RecommendedScaleFactor) < 1e-9));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
        => Close((ScaleChoice.SelectedItem as Choice)?.Factor ?? 1.0);
}
