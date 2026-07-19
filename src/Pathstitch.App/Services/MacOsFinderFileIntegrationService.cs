using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class MacOsFinderFileIntegrationService(IProcessLauncher processLauncher)
    : IFileIntegrationService
{
    private const string OpenCommand = "/usr/bin/open";

    public Task OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return Task.CompletedTask;

        StartOpenCommand(filePath);
        return Task.CompletedTask;
    }

    public Task RevealFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.CompletedTask;

        if (File.Exists(filePath))
        {
            StartOpenCommand("-R", filePath);
            return Task.CompletedTask;
        }

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
            StartOpenCommand(directoryPath);

        return Task.CompletedTask;
    }

    private void StartOpenCommand(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OpenCommand,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        processLauncher.Start(startInfo);
    }
}
