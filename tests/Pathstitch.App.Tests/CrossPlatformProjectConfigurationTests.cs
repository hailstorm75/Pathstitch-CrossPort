using System.Xml.Linq;

namespace Pathstitch.App.Tests;

public sealed class CrossPlatformProjectConfigurationTests
{
    [Fact]
    public void DesktopApp_TargetsPortableDotNetWithWindowsAndAppleSiliconRuntimes()
    {
        var project = LoadProject("src", "Pathstitch.App", "Pathstitch.App.csproj");

        Assert.Equal("net10.0", SingleUnconditionalValue(project, "TargetFramework"));
        Assert.Equal(
            ["win-x64", "osx-arm64"],
            SingleUnconditionalValue(project, "RuntimeIdentifiers").Split(';'));
        Assert.Equal("Exe", SingleUnconditionalValue(project, "OutputType"));
    }

    [Fact]
    public void WindowsOnlyDesktopProperties_AreConditionedAwayFromOsxBuilds()
    {
        var project = LoadProject("src", "Pathstitch.App", "Pathstitch.App.csproj");
        var windowsGroup = project.Root!
            .Elements("PropertyGroup")
            .Single(group => group.Element("ApplicationManifest") is not null);
        var condition = (string?)windowsGroup.Attribute("Condition");

        Assert.Contains("win-x64", condition, StringComparison.Ordinal);
        Assert.Contains("Windows_NT", condition, StringComparison.Ordinal);
        Assert.Equal("WinExe", windowsGroup.Element("OutputType")?.Value);
        Assert.Equal("true", windowsGroup.Element("BuiltInComInteropSupport")?.Value);
        Assert.Equal("app.manifest", windowsGroup.Element("ApplicationManifest")?.Value);
        Assert.Empty(project.Root.Elements("PropertyGroup")
            .Where(group => group.Attribute("Condition") is null)
            .Elements("ApplicationManifest"));
        Assert.Empty(project.Root.Elements("PropertyGroup")
            .Where(group => group.Attribute("Condition") is null)
            .Elements("BuiltInComInteropSupport"));
    }

    [Fact]
    public void Tests_TargetPortableDotNetForBothSupportedDesktopRuntimes()
    {
        var project = LoadProject("tests", "Pathstitch.App.Tests", "Pathstitch.App.Tests.csproj");

        Assert.Equal("net10.0", SingleUnconditionalValue(project, "TargetFramework"));
        Assert.Equal(
            ["win-x64", "osx-arm64"],
            SingleUnconditionalValue(project, "RuntimeIdentifiers").Split(';'));
        Assert.Empty(project.Descendants("EnableWindowsTargeting"));
    }

    private static XDocument LoadProject(params string[] pathParts)
        => XDocument.Load(FindRepositoryFile(pathParts));

    private static string SingleUnconditionalValue(XDocument project, string name)
        => project.Root!
            .Elements("PropertyGroup")
            .Where(group => group.Attribute("Condition") is null)
            .Elements(name)
            .Single()
            .Value;

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(pathParts)}");
    }
}
