using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pathstitch.App.Dialogs;

public sealed partial class DocumentationDialog : Window
{
    public DocumentationDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
