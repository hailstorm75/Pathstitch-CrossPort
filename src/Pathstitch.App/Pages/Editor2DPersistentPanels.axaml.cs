using System.Linq;
using Avalonia.Interactivity;
using Avalonia.Media;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class Editor2DInspector : EditorInteractionControlBase
{
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
        if (DataContext is EditorPageViewModel viewModel
            && !fontNames.Contains(viewModel.TwoDSelectedTextFontFamily))
        {
            viewModel.TwoDSelectedTextFontFamily = fontNames.FirstOrDefault() ?? "Arial";
        }
    }
}
