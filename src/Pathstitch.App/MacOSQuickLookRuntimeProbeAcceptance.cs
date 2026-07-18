using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Pathstitch.App.Services;

namespace Pathstitch.App;

internal static class MacOSQuickLookRuntimeProbeAcceptance
{
    private static readonly TimeSpan CollectionTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(
        string action,
        string nonce,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var normalizedAction = action.Trim().ToLowerInvariant();
        var evidenceName = normalizedAction == "prepare"
            ? "app-group-writer-probe.json"
            : "app-group-collector-probe.json";
        var evidencePath = Path.Combine(outputDirectory, evidenceName);
        var evidence = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["action"] = normalizedAction,
            ["nonce"] = nonce,
            ["processIdentifier"] = Environment.ProcessId,
            ["expectedPreferences"] = new
            {
                dxf = false,
                step = true,
                stch = false,
            },
        };

        var cancelOnFailure = false;
        try
        {
            if (!Guid.TryParseExact(nonce, "N", out _))
                throw new InvalidOperationException("Runtime-probe nonce must be 32 lowercase hexadecimal characters.");

            switch (normalizedAction)
            {
                case "prepare":
                    cancelOnFailure = true;
                    if (!MacOSIntegrationService.TryPrepareQuickLookRuntimeProbe(nonce))
                        throw new InvalidOperationException("Packaged app could not prepare app-group runtime probe.");
                    var dxfEnabled = false;
                    var stepEnabled = false;
                    var stchEnabled = false;
                    var patternReadBack =
                        MacOSIntegrationService.TryGetQuickLookPreference(
                            MacOSFinderPreviewFormat.Dxf, out dxfEnabled) && !dxfEnabled
                        && MacOSIntegrationService.TryGetQuickLookPreference(
                            MacOSFinderPreviewFormat.Step, out stepEnabled) && stepEnabled
                        && MacOSIntegrationService.TryGetQuickLookPreference(
                            MacOSFinderPreviewFormat.Stch, out stchEnabled) && !stchEnabled;
                    if (!patternReadBack)
                        throw new InvalidOperationException("Packaged app could not read back runtime-probe preferences.");
                    evidence["observedPreferences"] = new
                    {
                        dxf = dxfEnabled,
                        step = stepEnabled,
                        stch = stchEnabled,
                    };
                    evidence["status"] = "passed";
                    break;
                case "collect":
                    cancelOnFailure = true;
                    var deadline = DateTimeOffset.UtcNow + CollectionTimeout;
                    while (true)
                    {
                        var collectionResult =
                            MacOSIntegrationService.CollectQuickLookRuntimeProbe(
                                nonce,
                                outputDirectory);
                        if (collectionResult ==
                            MacOSQuickLookRuntimeProbeCollectionResult.Collected)
                        {
                            break;
                        }
                        if (collectionResult ==
                            MacOSQuickLookRuntimeProbeCollectionResult.Failed)
                        {
                            throw new InvalidOperationException(
                                "Packaged app could not collect Quick Look runtime attestations.");
                        }
                        if (DateTimeOffset.UtcNow >= deadline)
                            throw new TimeoutException("Quick Look extensions did not produce both runtime attestations.");
                        await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);
                    }

                    var previewPath = Path.Combine(
                        outputDirectory,
                        "quicklook-preview-runtime-probe.json");
                    var thumbnailPath = Path.Combine(
                        outputDirectory,
                        "quicklook-thumbnail-runtime-probe.json");
                    evidence["collectedFiles"] = new[]
                    {
                        await DescribeFileAsync(previewPath).ConfigureAwait(true),
                        await DescribeFileAsync(thumbnailPath).ConfigureAwait(true),
                    };
                    evidence["status"] = "passed";
                    break;
                default:
                    throw new InvalidOperationException(
                        "PATHSTITCH_MACOS_RUNTIME_PROBE_ACTION must be prepare or collect.");
            }

            await WriteEvidenceAsync(evidencePath, evidence).ConfigureAwait(true);
            return 0;
        }
        catch (Exception exception)
        {
            if (cancelOnFailure)
                _ = MacOSIntegrationService.TryCancelQuickLookRuntimeProbe(nonce);
            evidence["status"] = "failed";
            evidence["error"] = exception.ToString();
            await WriteEvidenceAsync(evidencePath, evidence).ConfigureAwait(true);
            return 1;
        }
    }

    private static async Task<object> DescribeFileAsync(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            throw new InvalidOperationException($"Runtime attestation is missing or empty: {Path.GetFileName(path)}");
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
        return new
        {
            path,
            bytes = bytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        };
    }

    private static Task WriteEvidenceAsync(
        string path,
        Dictionary<string, object?> evidence)
        => File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                evidence,
                new JsonSerializerOptions { WriteIndented = true }));
}

