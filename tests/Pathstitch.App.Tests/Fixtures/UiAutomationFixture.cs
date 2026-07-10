using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Pathstitch.App.Tests.Fixtures;

/// <summary>
/// Reusable, display-server-free selectors for UI coordination tests.
/// Selectors use AutomationId so layout order and localized labels may change safely.
/// </summary>
public sealed class HeadlessUiFixture
{
    public TControl FindByAutomationId<TControl>(Control root, string automationId)
        where TControl : Control
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);

        var matches = root
            .GetLogicalDescendants()
            .Prepend(root)
            .OfType<TControl>()
            .Where(control => AutomationProperties.GetAutomationId(control) == automationId)
            .ToArray();

        return Assert.Single(matches);
    }

    public XDocument LoadXaml(params string[] relativePath)
        => XDocument.Load(RepositoryFile(relativePath));

    public XElement FindXamlElementByAutomationId(XDocument document, string automationId)
    {
        var matches = document
            .Descendants()
            .Where(element => string.Equals(
                AutomationId(element),
                automationId,
                StringComparison.Ordinal))
            .ToArray();

        return Assert.Single(matches);
    }

    public static string? AutomationId(XElement element)
        => element
            .Attributes()
            .SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId")
            ?.Value;

    public static string RepositoryFile(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file: {Path.Combine(relativePath)}");
    }
}

/// <summary>
/// Shared target and process setup for packaged, platform-level launch smoke tests.
/// The fixture intentionally does not assume a Windows executable name on macOS.
/// </summary>
public sealed class PlatformSmokeTestFixture
{
    public PlatformSmokeTarget CurrentTarget { get; } = PlatformSmokeTarget.DetectCurrent();

    public ProcessStartInfo CreateLaunchInfo(string artifactRoot, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        var executable = Path.Combine(artifactRoot, CurrentTarget.RelativeExecutablePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = artifactRoot,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }
}

public sealed record PlatformSmokeTarget(
    string OperatingSystem,
    string RuntimeIdentifier,
    string RelativeExecutablePath)
{
    public static PlatformSmokeTarget DetectCurrent()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            var unsupported => throw new PlatformNotSupportedException(
                $"UI smoke tests do not support {unsupported} processes."),
        };

        if (System.OperatingSystem.IsWindows())
            return new("windows", $"win-{architecture}", "Pathstitch.App.exe");

        if (System.OperatingSystem.IsMacOS())
            return new("macos", $"osx-{architecture}", Path.Combine("Pathstitch.app", "Contents", "MacOS", "Pathstitch.App"));

        throw new PlatformNotSupportedException(
            "Packaged UI smoke tests currently target Windows and macOS.");
    }
}
