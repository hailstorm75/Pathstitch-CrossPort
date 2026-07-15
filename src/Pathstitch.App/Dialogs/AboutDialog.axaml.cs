using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pathstitch.App.Dialogs;

public sealed partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
