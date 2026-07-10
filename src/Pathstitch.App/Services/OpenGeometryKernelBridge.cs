using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Pathstitch.App.Services;

public sealed class OpenGeometryKernelBridge(ILogger<OpenGeometryKernelBridge> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly ILogger<OpenGeometryKernelBridge> _logger = logger;

    internal async Task<OpenGeometryOffsetResult> TryOffsetPolylinesAsync(
        IReadOnlyList<DxfPolyline> polylines,
        double width,
        CancellationToken cancellationToken)
    {
        if (polylines.Count == 0)
            return OpenGeometryOffsetResult.Success([]);

        var runtime = OpenGeometryPackagedRuntime.Resolve();
        if (runtime is null)
        {
            _logger.LogDebug("Packaged OpenGeometry runtime is unavailable.");
            return OpenGeometryOffsetResult.Failure("Packaged OpenGeometry runtime was not found.");
        }

        var requestPath = Path.Combine(Path.GetTempPath(), $"pathstitch_og_request_{Guid.NewGuid():N}.json");
        var responsePath = Path.Combine(Path.GetTempPath(), $"pathstitch_og_response_{Guid.NewGuid():N}.json");

        try
        {
            var request = new OpenGeometryOffsetRequest(
                Command: "offset-polylines",
                Width: width,
                MiterLimit: 4.0,
                Polylines: polylines.Select(static polyline => new OpenGeometryPolyline(
                    polyline.Points.Select(static point => new OpenGeometryPoint(point.X, point.Y)).ToArray(),
                    polyline.IsClosed)).ToArray());

            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions), cancellationToken).ConfigureAwait(false);
            var workerRun = await RunNodeAsync(runtime, requestPath, responsePath, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(responsePath))
            {
                if (workerRun.TimedOut)
                {
                    _logger.LogDebug("OpenGeometry worker timed out without writing a response.");
                    return OpenGeometryOffsetResult.Failure("OpenGeometry worker timed out after 15 seconds.");
                }

                _logger.LogDebug(
                    "OpenGeometry worker exited with {ExitCode} without writing a response. Stderr: {StandardError}",
                    workerRun.ExitCode,
                    workerRun.StandardError);

                var detail = FirstNonWhiteSpace(workerRun.StandardError, workerRun.StandardOutput);
                var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
                return OpenGeometryOffsetResult.Failure($"OpenGeometry worker exited with code {workerRun.ExitCode} without writing a response.{suffix}");
            }

            var response = JsonSerializer.Deserialize<OpenGeometryOffsetResponse>(
                await File.ReadAllTextAsync(responsePath, cancellationToken).ConfigureAwait(false),
                JsonOptions);

            if (response is null || !response.Ok)
            {
                _logger.LogDebug("OpenGeometry worker failed: {Error}", response?.Error ?? "No response payload.");
                return OpenGeometryOffsetResult.Failure(response?.Error ?? "OpenGeometry worker returned an empty response.");
            }

            return OpenGeometryOffsetResult.Success(response.Regions ?? []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OpenGeometry worker invocation failed.");
            return OpenGeometryOffsetResult.Failure($"OpenGeometry worker invocation failed: {ex.Message}");
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(responsePath);
        }
    }

    internal async Task<OpenGeometryCurveOffsetResult> TryOffsetCurvesAsync(
        IReadOnlyList<DxfCurveOffset> curves,
        CancellationToken cancellationToken)
    {
        if (curves.Count == 0)
            return OpenGeometryCurveOffsetResult.Success([]);

        var runtime = OpenGeometryPackagedRuntime.Resolve();
        if (runtime is null)
        {
            _logger.LogDebug("Packaged OpenGeometry runtime is unavailable.");
            return OpenGeometryCurveOffsetResult.Failure("Packaged OpenGeometry runtime was not found.");
        }

        var requestPath = Path.Combine(Path.GetTempPath(), $"pathstitch_og_request_{Guid.NewGuid():N}.json");
        var responsePath = Path.Combine(Path.GetTempPath(), $"pathstitch_og_response_{Guid.NewGuid():N}.json");

        try
        {
            var request = new OpenGeometryCurveOffsetRequest(
                Command: "offset-curves",
                AcuteThresholdDegrees: 30.0,
                Bevel: false,
                Polylines: curves.Select(static curve => new OpenGeometryCurveOffsetPolyline(
                    curve.Kind,
                    curve.Points.Select(static point => new OpenGeometryPoint(point.X, point.Y)).ToArray(),
                    curve.IsClosed,
                    curve.Distance,
                    curve.Center is DxfPoint center ? new OpenGeometryPoint(center.X, center.Y) : null,
                    curve.Radius,
                    curve.StartAngleDegrees,
                    curve.EndAngleDegrees,
                    curve.Segments)).ToArray());

            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions), cancellationToken).ConfigureAwait(false);
            var workerRun = await RunNodeAsync(runtime, requestPath, responsePath, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(responsePath))
            {
                if (workerRun.TimedOut)
                {
                    _logger.LogDebug("OpenGeometry worker timed out without writing a curve-offset response.");
                    return OpenGeometryCurveOffsetResult.Failure("OpenGeometry worker timed out after 15 seconds.");
                }

                _logger.LogDebug(
                    "OpenGeometry worker exited with {ExitCode} without writing a curve-offset response. Stderr: {StandardError}",
                    workerRun.ExitCode,
                    workerRun.StandardError);

                var detail = FirstNonWhiteSpace(workerRun.StandardError, workerRun.StandardOutput);
                var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
                return OpenGeometryCurveOffsetResult.Failure($"OpenGeometry worker exited with code {workerRun.ExitCode} without writing a response.{suffix}");
            }

            var response = JsonSerializer.Deserialize<OpenGeometryCurveOffsetResponse>(
                await File.ReadAllTextAsync(responsePath, cancellationToken).ConfigureAwait(false),
                JsonOptions);

            if (response is null || !response.Ok)
            {
                _logger.LogDebug("OpenGeometry curve offset worker failed: {Error}", response?.Error ?? "No response payload.");
                return OpenGeometryCurveOffsetResult.Failure(response?.Error ?? "OpenGeometry worker returned an empty curve-offset response.");
            }

            return OpenGeometryCurveOffsetResult.Success(response.Paths ?? []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OpenGeometry curve offset worker invocation failed.");
            return OpenGeometryCurveOffsetResult.Failure($"OpenGeometry worker invocation failed: {ex.Message}");
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(responsePath);
        }
    }

    private static async Task<OpenGeometryWorkerRunResult> RunNodeAsync(
        OpenGeometryPackagedRuntime runtime,
        string requestPath,
        string responsePath,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = runtime.NodeExecutablePath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        process.StartInfo.ArgumentList.Add(runtime.WorkerPath);
        process.StartInfo.ArgumentList.Add(requestPath);
        process.StartInfo.ArgumentList.Add(responsePath);

        process.Start();

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await process.WaitForExitAsync(waitCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return new OpenGeometryWorkerRunResult(
                ExitCode: process.HasExited ? process.ExitCode : -1,
                TimedOut: true,
                StandardOutput: await standardOutput.ConfigureAwait(false),
                StandardError: await standardError.ConfigureAwait(false));
        }

        return new OpenGeometryWorkerRunResult(
            ExitCode: process.ExitCode,
            TimedOut: false,
            StandardOutput: await standardOutput.ConfigureAwait(false),
            StandardError: await standardError.ConfigureAwait(false));
    }

    private static string? FirstNonWhiteSpace(params string[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The worker may already have exited between the timeout and kill attempt.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temporary bridge files are best-effort cleanup.
        }
    }

    private sealed record OpenGeometryOffsetRequest(
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("width")] double Width,
        [property: JsonPropertyName("miterLimit")] double MiterLimit,
        [property: JsonPropertyName("polylines")] IReadOnlyList<OpenGeometryPolyline> Polylines);

    private sealed record OpenGeometryPolyline(
        [property: JsonPropertyName("points")] IReadOnlyList<OpenGeometryPoint> Points,
        [property: JsonPropertyName("isClosed")] bool IsClosed);

    private sealed record OpenGeometryOffsetResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("regions")] IReadOnlyList<OpenGeometryOffsetRegion>? Regions,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record OpenGeometryCurveOffsetRequest(
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("acuteThresholdDegrees")] double AcuteThresholdDegrees,
        [property: JsonPropertyName("bevel")] bool Bevel,
        [property: JsonPropertyName("polylines")] IReadOnlyList<OpenGeometryCurveOffsetPolyline> Polylines);

    private sealed record OpenGeometryCurveOffsetPolyline(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("points")] IReadOnlyList<OpenGeometryPoint> Points,
        [property: JsonPropertyName("isClosed")] bool IsClosed,
        [property: JsonPropertyName("distance")] double Distance,
        [property: JsonPropertyName("center")] OpenGeometryPoint? Center,
        [property: JsonPropertyName("radius")] double? Radius,
        [property: JsonPropertyName("startAngleDegrees")] double? StartAngleDegrees,
        [property: JsonPropertyName("endAngleDegrees")] double? EndAngleDegrees,
        [property: JsonPropertyName("segments")] int? Segments);

    private sealed record OpenGeometryCurveOffsetResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("paths")] IReadOnlyList<OpenGeometryCurveOffsetPath>? Paths,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record OpenGeometryWorkerRunResult(
        int ExitCode,
        bool TimedOut,
        string StandardOutput,
        string StandardError);
}

internal sealed record OpenGeometryOffsetResult(
    bool IsSuccess,
    IReadOnlyList<OpenGeometryOffsetRegion> Regions,
    string? Error)
{
    public static OpenGeometryOffsetResult Success(IReadOnlyList<OpenGeometryOffsetRegion> regions)
        => new(true, regions, null);

    public static OpenGeometryOffsetResult Failure(string error)
        => new(false, [], error);
}

internal sealed record OpenGeometryOffsetRegion(
    [property: JsonPropertyName("outer")] IReadOnlyList<OpenGeometryPoint> Outer,
    [property: JsonPropertyName("holes")] IReadOnlyList<IReadOnlyList<OpenGeometryPoint>> Holes);

internal sealed record OpenGeometryPoint(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y);

internal sealed record DxfCurveOffset(
    string Kind,
    IReadOnlyList<DxfPoint> Points,
    bool IsClosed,
    double Distance,
    DxfPoint? Center = null,
    double? Radius = null,
    double? StartAngleDegrees = null,
    double? EndAngleDegrees = null,
    int? Segments = null);

internal sealed record OpenGeometryCurveOffsetResult(
    bool IsSuccess,
    IReadOnlyList<OpenGeometryCurveOffsetPath> Paths,
    string? Error)
{
    public static OpenGeometryCurveOffsetResult Success(IReadOnlyList<OpenGeometryCurveOffsetPath> paths)
        => new(true, paths, null);

    public static OpenGeometryCurveOffsetResult Failure(string error)
        => new(false, [], error);
}

internal sealed record OpenGeometryCurveOffsetPath(
    [property: JsonPropertyName("points")] IReadOnlyList<OpenGeometryPoint> Points,
    [property: JsonPropertyName("isClosed")] bool IsClosed);
