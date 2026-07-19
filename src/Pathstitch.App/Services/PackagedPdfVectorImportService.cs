using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class PackagedPdfVectorImportService(PathstitchDxfWorkerClient workerClient) : IPdfVectorImportService
{
    public async Task<string> ConvertToDxfAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("PDF drawing was not found.", sourcePath);

        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Imported");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"pdf-{Guid.NewGuid():N}.dxf");
        try
        {
            var response = await workerClient.SendAsync(
                "import_pdf",
                new { input = sourcePath, output = outputPath },
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(response.GetProperty("status").GetString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                var message = response.TryGetProperty("message", out var error)
                    ? error.GetString()
                    : "PDF worker returned an unknown error.";
                throw new InvalidDataException(message);
            }
            if (!File.Exists(outputPath))
                throw new InvalidDataException("PDF worker did not create a DXF drawing.");
            return outputPath;
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}