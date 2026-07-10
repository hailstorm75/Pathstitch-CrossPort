using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Pathstitch.App.Services;

namespace Pathstitch.App;

internal static class MacOSPackagedAcceptance
{
    public static async Task<int> RunAsync(IServiceProvider services, Window window, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var evidencePath = Path.Combine(outputDirectory, "packaged-app-acceptance.json");
        var evidence = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["platform"] = Environment.OSVersion.Platform.ToString(),
        };

        try
        {
            var dialogService = services.GetRequiredService<IProjectFileDialogService>();
            if (dialogService is not ProjectFileDialogService)
                throw new InvalidOperationException("The packaged app did not resolve the native project file-dialog service.");
            var storageProvider = window.StorageProvider
                ?? throw new InvalidOperationException("The packaged window has no platform storage provider.");
            if (!storageProvider.CanOpen || !storageProvider.CanSave)
                throw new InvalidOperationException("The native storage provider cannot open and save files.");
            evidence["fileDialogs"] = new
            {
                service = dialogService.GetType().FullName,
                provider = storageProvider.GetType().FullName,
                storageProvider.CanOpen,
                storageProvider.CanSave,
            };

            var webView = new NativeWebView();
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.Loaded += (_, _) => loaded.TrySetResult();
            webView.NavigationCompleted += (_, args) => navigation.TrySetResult(args.IsSuccess);
            window.Content = webView;
            if (await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(true) != loaded.Task)
                throw new InvalidOperationException("The packaged native WebView was not attached to the window.");
            webView.NavigateToString("<!doctype html><html><body><canvas id='viewport'></canvas><script>document.title='Pathstitch acceptance';</script></body></html>");
            var completed = await Task.WhenAny(navigation.Task, Task.Delay(TimeSpan.FromSeconds(25))).ConfigureAwait(true);
            if (completed != navigation.Task || !await navigation.Task.ConfigureAwait(true))
                throw new InvalidOperationException("The packaged native WebView did not initialize and navigate successfully.");
            evidence["webView"] = new { control = webView.GetType().FullName, navigationCompleted = true };

            var projectPath = Path.Combine(outputDirectory, "packaged-roundtrip.stch");
            var viewportJson = "{\"schemaVersion\":1,\"acceptance\":\"macos-packaged\"}";
            var persistence = services.GetRequiredService<Project3DStateService>();
            await persistence.SaveAsync(projectPath, new Project3DState(viewportJson, [], [])).ConfigureAwait(true);
            var reopened = await persistence.LoadAsync(projectPath).ConfigureAwait(true);
            if (!File.Exists(projectPath) || reopened.ViewportJson != viewportJson)
                throw new InvalidOperationException("The packaged .stch save/reopen round trip did not preserve editor state.");
            evidence["projectRoundTrip"] = new
            {
                path = projectPath,
                bytes = new FileInfo(projectPath).Length,
                sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(projectPath))).ToLowerInvariant(),
                viewportPreserved = true,
            };
            evidence["status"] = "passed";
            await WriteEvidenceAsync(evidencePath, evidence).ConfigureAwait(true);
            return 0;
        }
        catch (Exception exception)
        {
            evidence["status"] = "failed";
            evidence["error"] = exception.ToString();
            await WriteEvidenceAsync(evidencePath, evidence).ConfigureAwait(true);
            return 1;
        }
    }

    private static Task WriteEvidenceAsync(string path, Dictionary<string, object?> evidence)
        => File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
}
