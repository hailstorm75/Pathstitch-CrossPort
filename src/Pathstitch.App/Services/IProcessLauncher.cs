using System.Diagnostics;

namespace Pathstitch.App.Services;

public interface IProcessLauncher
{
    void Start(ProcessStartInfo startInfo);
}

public sealed class SystemProcessLauncher : IProcessLauncher
{
    public void Start(ProcessStartInfo startInfo)
        => Process.Start(startInfo);
}
