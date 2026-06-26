using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class EditorOutputLauncherService : IEditorOutputLauncherService
{
    public Task OpenOutputAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            return Task.CompletedTask;

        Process.Start(new ProcessStartInfo
        {
            FileName = outputPath,
            UseShellExecute = true,
        });

        return Task.CompletedTask;
    }

    public Task RevealOutputAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(outputPath))
            return Task.CompletedTask;

        if (File.Exists(outputPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{outputPath}\"",
                UseShellExecute = true,
            });
            return Task.CompletedTask;
        }

        var directoryPath = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return Task.CompletedTask;

        Process.Start(new ProcessStartInfo
        {
            FileName = directoryPath,
            UseShellExecute = true,
        });

        return Task.CompletedTask;
    }
}
