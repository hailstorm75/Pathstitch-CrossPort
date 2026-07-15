using System.Diagnostics;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public void CheckForUpdates_OpensLatestReleasePage()
    {
        var launcher = new RecordingProcessLauncher();

        new AppUpdateService(launcher).CheckForUpdates();

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
