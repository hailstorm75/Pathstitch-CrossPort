using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class WindowsExplorerFileIntegrationService(IProcessLauncher processLauncher)
    : IFileIntegrationService
{
    public Task OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return Task.CompletedTask;

        processLauncher.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true,
        });

        return Task.CompletedTask;
    }

    public Task RevealFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.CompletedTask;

        var target = File.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(target)
            || (!File.Exists(filePath) && !Directory.Exists(target)))
        {
            return Task.CompletedTask;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(File.Exists(filePath) ? $"/select,{filePath}" : target);
        processLauncher.Start(startInfo);

        return Task.CompletedTask;
    }
}
