using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class MacOSSpotlightProjectDiscoveryProvider : IProjectDiscoveryProvider
{
    private const int MaxDiscoveredProjects = 200;
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(
        CancellationToken cancellationToken = default)
        => await TryDiscoverAsync(cancellationToken).ConfigureAwait(false) ?? [];

    public async IAsyncEnumerable<IReadOnlyList<DiscoveredProject>> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS())
        {
            yield return [];
            yield break;
        }

        IReadOnlyList<DiscoveredProject>? previous = null;
        using var timer = new PeriodicTimer(RefreshInterval);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await TryDiscoverAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null && !SnapshotsEqual(previous, current))
            {
                previous = current.ToArray();
                yield return previous;
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                yield break;
        }
    }

    private static async Task<IReadOnlyList<DiscoveredProject>?> TryDiscoverAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
            return [];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DiscoveryTimeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/mdfind",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-0");
        process.StartInfo.ArgumentList.Add("kMDItemFSName == \"*.stch\"c");

        try
        {
            if (!process.Start())
                return null;

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                return null;

            return output
                .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(path => Path.GetExtension(path).Equals(".stch", StringComparison.OrdinalIgnoreCase))
                .Select(TryCreateDiscoveredProject)
                .Where(project => project is not null)
                .Cast<DiscoveredProject>()
                .GroupBy(project => Path.GetFullPath(project.ProjectFilePath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(project => project.LastModifiedAtUtc).First())
                .OrderByDescending(project => project.LastModifiedAtUtc)
                .ThenBy(project => project.ProjectFilePath, StringComparer.OrdinalIgnoreCase)
                .Take(MaxDiscoveredProjects)
                .ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return null;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            TryKill(process);
            return null;
        }
    }

    private static bool SnapshotsEqual(
        IReadOnlyList<DiscoveredProject>? previous,
        IReadOnlyList<DiscoveredProject> current)
    {
        if (previous is null || previous.Count != current.Count)
            return false;

        for (var index = 0; index < previous.Count; index++)
        {
            if (!string.Equals(
                    previous[index].ProjectFilePath,
                    current[index].ProjectFilePath,
                    StringComparison.OrdinalIgnoreCase)
                || previous[index].LastModifiedAtUtc != current[index].LastModifiedAtUtc)
            {
                return false;
            }
        }

        return true;
    }

    private static DiscoveredProject? TryCreateDiscoveredProject(string path)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(path);
            return File.Exists(normalizedPath)
                ? new DiscoveredProject(normalizedPath, File.GetLastWriteTimeUtc(normalizedPath))
                : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or NotSupportedException)
        {
            // Process already exited or cannot be killed.
        }
    }
}