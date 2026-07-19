using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Domain.App.ViewModels;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Pages;

public partial class Editor2DInspector : EditorInteractionControlBase
{
    private string? _rejectedDimensionExpressionText;

    public Editor2DInspector()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var fontNames = FontManager.Current.SystemFonts
            .Select(font => font.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();
        InstalledFontSelector.ItemsSource = fontNames;
        if (DataContext is EditorPageViewModel loadedViewModel)
            loadedViewModel.TwoDTextFontPreview = null;
        if (DataContext is EditorPageViewModel viewModel
            && !fontNames.Contains(viewModel.TwoDSelectedTextFontFamily))
        {
            viewModel.TwoDSelectedTextFontFamily = fontNames.FirstOrDefault() ?? "Arial";
        }
    }

    private void OnFontPreviewEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel
            && sender is Control { DataContext: string fontFamily })
            viewModel.TwoDTextFontPreview = fontFamily;
    }

    private void OnFontPreviewExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.TwoDTextFontPreview = null;
    }

    private void OnTwoDMeasurementExpressionTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel
            && viewModel.HasTwoDMeasurementExpressionError)
        {
            var currentText = (sender as TextBox)?.Text ?? string.Empty;
            if (!string.Equals(currentText, _rejectedDimensionExpressionText, StringComparison.Ordinal))
            {
                _rejectedDimensionExpressionText = null;
                viewModel.ClearTwoDMeasurementExpressionError();
            }
        }
    }

    private void OnTwoDMeasurementExpressionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || sender is not TextBox textBox
            || DataContext is not EditorPageViewModel viewModel
            || string.IsNullOrWhiteSpace(viewModel.TwoDSelectedMeasurementId))
        {
            return;
        }

        e.Handled = true;
        if (!viewModel.TryCommitTwoDMeasurementExpression(
                viewModel.TwoDSelectedMeasurementId,
                textBox.Text ?? string.Empty,
                out _))
        {
            _rejectedDimensionExpressionText = textBox.Text ?? string.Empty;
            textBox.Focus();
            textBox.SelectAll();
            return;
        }

        _rejectedDimensionExpressionText = null;
        TopLevel.GetTopLevel(this)?
            .GetVisualDescendants()
            .OfType<DxfPreviewCanvas>()
            .FirstOrDefault()
            ?.Focus();
    }
}
