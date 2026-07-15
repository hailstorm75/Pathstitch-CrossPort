using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Pathstitch.App.Services;

namespace Pathstitch.App.Dialogs;

public sealed partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppVersionInfo.Current}";
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private static void OnSupportClicked(object? sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo
        {
            FileName = "https://buymeacoffee.com/masonchen",
            UseShellExecute = true,
        });
}
