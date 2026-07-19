using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class PackagedDxfTransportConversionService(PathstitchDxfWorkerClient workerClient)
    : IDxfTransportConversionService
{
    private static readonly byte[] BinaryDxfSignature = Encoding.ASCII.GetBytes("AutoCAD Binary DXF");

    public async Task<string> ConvertBinaryToAsciiAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Binary DXF drawing was not found.", sourcePath);
        if (!HasBinaryDxfSignature(sourcePath))
            throw new InvalidDataException("DXF transport conversion requires an AutoCAD Binary DXF input.");

        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Imported");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"binary-dxf-{Guid.NewGuid():N}.dxf");
        try
        {
            var response = await workerClient.SendAsync(
                "convert_binary_dxf",
                new { input = sourcePath, output = outputPath },
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(response.GetProperty("status").GetString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                var message = response.TryGetProperty("message", out var error)
                    ? error.GetString()
                    : "Binary DXF conversion returned an unknown error.";
                throw new InvalidDataException(message);
            }
            if (!File.Exists(outputPath) || HasBinaryDxfSignature(outputPath))
                throw new InvalidDataException("Binary DXF conversion did not create a valid ASCII DXF drawing.");
            return outputPath;
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
    }

    private static bool HasBinaryDxfSignature(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> prefix = stackalloc byte[BinaryDxfSignature.Length];
        return stream.Read(prefix) == prefix.Length && prefix.SequenceEqual(BinaryDxfSignature);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}