using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pathstitch.App.Dialogs;

public sealed partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? "1.0";
        VersionText.Text = $"Version {version}";
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private static void OnSupportClicked(object? sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo
        {
            FileName = "https://buymeacoffee.com/masonchen",
            UseShellExecute = true,
        });
}
