namespace Pathstitch.App.Services;
public enum AppUpdateStrategy
{
    ManualBrowserDownload = 0,
}


public interface IAppUpdateService
{
    AppUpdateStrategy Strategy { get; }

    bool SupportsAutomaticChecks { get; }

    bool InstallsUpdates { get; }

    void CheckForUpdates();
}
