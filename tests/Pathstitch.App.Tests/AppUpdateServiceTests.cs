using System.Diagnostics;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public void CheckForUpdates_OpensLatestReleasePage()
    {
        var launcher = new RecordingProcessLauncher();

        var service = new AppUpdateService(launcher);

        service.CheckForUpdates();

        Assert.Equal(AppUpdateStrategy.ManualBrowserDownload, service.Strategy);
        Assert.False(service.SupportsAutomaticChecks);
        Assert.NotNull(launcher.StartInfo);
        Assert.Equal("https://github.com/Pathstitch/Pathstitch-CrossPort/releases/latest", launcher.StartInfo!.FileName);
        Assert.True(launcher.StartInfo.UseShellExecute);
    }

    private sealed class RecordingProcessLauncher : IProcessLauncher
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo) => StartInfo = startInfo;
    }
}
