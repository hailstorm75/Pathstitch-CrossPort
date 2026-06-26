using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Pathstitch.App.Services;

internal static class PathstitchRuntimeResolver
{
    private static readonly string[] LocalPythonRelativePaths =
    [
        Path.Combine(".venv", "Scripts", "python.exe"),
        Path.Combine("venv", "Scripts", "python.exe"),
        Path.Combine("env", "Scripts", "python.exe"),
        Path.Combine("pyenv", "Scripts", "python.exe"),
    ];

    public static string? ResolveRepositoryRoot()
    {
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "pathstitch_core")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        return null;
    }

    public static string? ResolvePythonCommand(string repositoryRoot, string requiredModuleImport)
    {
        foreach (var candidate in EnumeratePythonCandidates(repositoryRoot))
        {
            if (CanImportRequiredModule(candidate, repositoryRoot, requiredModuleImport))
                return candidate;
        }

        return null;
    }

    public static string BuildMissingRuntimeMessage(string requiredCapability)
        => $"Unable to locate a Python runtime for {requiredCapability}. Set PATHSTITCH_PYTHON to a Python interpreter with the Pathstitch dependencies installed, or create a local .venv/venv under the repository.";

    private static IEnumerable<string> EnumeratePythonCandidates(string repositoryRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        void AddCandidate(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            var normalized = candidate.Trim();
            if (seen.Add(normalized))
                candidates.Add(normalized);
        }

        AddCandidate(Environment.GetEnvironmentVariable("PATHSTITCH_PYTHON"));

        foreach (var relativePath in LocalPythonRelativePaths)
        {
            AddCandidate(Path.Combine(repositoryRoot, relativePath));
        }

        AddCandidate("python");
        AddCandidate("python3");

        return candidates;
    }

    private static bool CanImportRequiredModule(string pythonCommand, string repositoryRoot, string requiredModuleImport)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonCommand,
                    Arguments = $"-c \"import {requiredModuleImport}\"",
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.StartInfo.Environment["PYTHONPATH"] = repositoryRoot;

            process.Start();
            process.WaitForExit(5000);
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore cleanup failures for probe processes.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
