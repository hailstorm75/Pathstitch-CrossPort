using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Pathstitch.App.Services;

namespace Pathstitch.App.Dialogs;

public sealed partial class AboutDialog : Window
{
    private readonly IAppUpdateService _updateService;

    public AboutDialog()
        : this(new AppUpdateService(new SystemProcessLauncher()))
    {
    }

    public AboutDialog(IAppUpdateService updateService)
    {
        InitializeComponent();
        _updateService = updateService;
        VersionText.Text = $"Version {AppVersionInfo.Current}";
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnCheckForUpdatesClicked(object? sender, RoutedEventArgs e)
        => _updateService.CheckForUpdates();

    private static void OnSupportClicked(object? sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo
        {
            FileName = "https://buymeacoffee.com/masonchen",
            UseShellExecute = true,
        });
}
