using Avalonia.Automation;
using Avalonia.Controls;
using System.Diagnostics;
using Pathstitch.App.Tests.Fixtures;

namespace Pathstitch.App.Tests;

public sealed class HeadlessInfrastructureSmokeTests
{
    [Fact]
    public async Task HeadlessHost_MountsAndObservesLiveControlByAutomationId()
    {
        var fixture = new HeadlessUiFixture();
        var button = await fixture.RunAsync(() =>
        {
            var control = new Button { Content = "Live control" };
            AutomationProperties.SetAutomationId(control, "fixture.live-control");
            return control;
        });

        await using var session = await fixture.MountAsync(button, 320, 180);

        await fixture.RunAsync(() =>
        {
            var observed = fixture.FindByAutomationId<Button>(session.Root, "fixture.live-control");
            Assert.Same(button, observed);
            Assert.True(fixture.IsEffectivelyVisible(observed));
            Assert.True(observed.Bounds.Width > 0);
        });
    }

    [Fact]
    public async Task PlatformFixture_ActuallyLaunchesAndObservesAProcessLifetime()
    {
        var fixture = new PlatformSmokeTestFixture();
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sleep",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("ping -n 6 127.0.0.1 > nul");
        }
        else
        {
            startInfo.ArgumentList.Add("5");
        }

        using var process = await fixture.LaunchAndObserveAsync(startInfo, TimeSpan.FromMilliseconds(250));
        Assert.False(process.HasExited);
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
}
