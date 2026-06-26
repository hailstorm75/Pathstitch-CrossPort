using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Services;
using Microsoft.Extensions.Logging;

namespace Pathstitch.App.Services;

public sealed class PathstitchCoreOutputPreviewService(ILogger<PathstitchCoreOutputPreviewService> logger) : IEditorOutputPreviewService
{
    private readonly ILogger<PathstitchCoreOutputPreviewService> _logger = logger;

    public async Task<string?> GenerateSvgPreviewAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            return null;

        var repositoryRoot = PathstitchRuntimeResolver.ResolveRepositoryRoot();
        if (repositoryRoot is null)
            return null;

        var pythonCommand = PathstitchRuntimeResolver.ResolvePythonCommand(repositoryRoot, "pathstitch_core.dxf_ops");
        if (pythonCommand is null)
            return null;

        var previewDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
        Directory.CreateDirectory(previewDirectory);
        var svgPath = Path.Combine(previewDirectory, $"preview_{Path.GetFileNameWithoutExtension(outputPath)}_{Guid.NewGuid():N}.svg");

        var payload = JsonSerializer.Serialize(new
        {
            op = "export_svg",
            args = new
            {
                input = outputPath,
                output = svgPath,
            },
        });

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonCommand,
                    Arguments = "-m pathstitch_core.dxf_ops",
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.StartInfo.Environment["PYTHONPATH"] = repositoryRoot;

            process.Start();
            await process.StandardInput.WriteAsync(payload).ConfigureAwait(false);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogInformation("pathstitch_core.dxf_ops stderr (preview): {StdErr}", stderr);

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                using var document = JsonDocument.Parse(stdout);
                var root = document.RootElement;
                var status = root.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()
                    : "error";

                if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            return File.Exists(svgPath)
                ? await File.ReadAllTextAsync(svgPath, cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate SVG preview for {OutputPath}.", outputPath);
            return null;
        }
    }
}
