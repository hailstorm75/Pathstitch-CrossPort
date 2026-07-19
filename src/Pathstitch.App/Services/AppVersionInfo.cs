using System.Reflection;

namespace Pathstitch.App.Services;

public static class AppVersionInfo
{
    public static string Current
        => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? "1.0";

    public static string HomeLabel => $"v{Current}";
}
