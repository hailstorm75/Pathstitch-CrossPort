using System.Diagnostics;

namespace Pathstitch.App.Services;

public sealed class AppUpdateService(IProcessLauncher processLauncher) : IAppUpdateService
{
    internal const string ReleasePageUrl = "https://github.com/Pathstitch/Pathstitch-CrossPort/releases/latest";

    public AppUpdateStrategy Strategy => AppUpdateStrategy.ManualBrowserDownload;

    public bool SupportsAutomaticChecks => false;

    public bool InstallsUpdates => false;

    public void CheckForUpdates()
        => processLauncher.Start(new ProcessStartInfo
        {
            FileName = ReleasePageUrl,
            UseShellExecute = true,
        });
}
