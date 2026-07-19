using System;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public enum DesktopPlatform
{
    Windows,
    MacOS,
}

public static class DesktopFileIntegrationServiceFactory
{
    public static IFileIntegrationService CreateForCurrentPlatform(IProcessLauncher processLauncher)
    {
        if (OperatingSystem.IsWindows())
            return Create(DesktopPlatform.Windows, processLauncher);

        if (OperatingSystem.IsMacOS())
            return Create(DesktopPlatform.MacOS, processLauncher);

        throw new PlatformNotSupportedException(
            "File integration is currently supported on Windows and macOS.");
    }

    public static IFileIntegrationService Create(
        DesktopPlatform platform,
        IProcessLauncher processLauncher)
        => platform switch
        {
            DesktopPlatform.Windows => new WindowsExplorerFileIntegrationService(processLauncher),
            DesktopPlatform.MacOS => new MacOsFinderFileIntegrationService(processLauncher),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null),
        };
}
