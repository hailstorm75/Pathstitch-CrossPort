using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pathstitch.App.AppExtensions;
using Pathstitch.App.Controls;
using Pathstitch.App.Services;
using UI.Navigation;

namespace Pathstitch.App;

public partial class App : Application
{
    private IServiceProvider? _services;
    private DesktopDocumentWindowManager? _documentWindowManager;
    private DesktopDocumentWindowCoordinator? _documentWindowCoordinator;
    private DesktopFileOpenRouter? _fileOpenRouter;
    private IActivatableLifetime? _activatableLifetime;
    private string? _fileActivationAcceptanceOutput;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddPages()
            .AddTelemetry()
            .AddSingleton<RecentProjectsService>()
            .AddScoped<ProjectSessionService>()
            .AddSingleton<Project3DStateService>()
            .AddScoped<IDocumentWindowContext, DocumentWindowContext>()
            .AddScoped<IMessenger>(_ => new StrongReferenceMessenger())
            .AddSingleton<DesktopDocumentWindowCoordinator>()
            .AddScoped<DesktopDocumentWindowService>()
            .AddScoped<IDocumentWindowService>(services =>
                services.GetRequiredService<DesktopDocumentWindowService>())
            .AddScoped<IProjectFileDialogService, ProjectFileDialogService>()
            .AddScoped<IUnsavedChangesPromptService, AvaloniaUnsavedChangesPromptService>()
            .AddScoped<IProjectOpenDispositionPromptService, AvaloniaProjectOpenDispositionPromptService>()
            .AddScoped<IEditorImportUnitsPromptService, AvaloniaEditorImportUnitsPromptService>()
            .AddSingleton<IPsdImportService, PackagedPsdImportService>()
            .AddScoped<IPsdImportModePromptService, AvaloniaPsdImportModePromptService>()
            .AddSingleton<IProcessLauncher, SystemProcessLauncher>()
            .AddSingleton<IAppUpdateService, AppUpdateService>()
            .AddSingleton<IFileIntegrationService>(services =>
                DesktopFileIntegrationServiceFactory.CreateForCurrentPlatform(
                    services.GetRequiredService<IProcessLauncher>()))
            .AddSingleton<IEditorOutputLauncherService, EditorOutputLauncherService>()
            .AddSingleton<IPdfVectorImportService, PackagedPdfVectorImportService>()
            .AddSingleton<IEditorOutputPreviewService, DxfOutputPreviewService>()
            .AddSingleton<IGeometryKernelDescriptorProvider, OpenGeometryKernelDescriptorProvider>()
            .AddSingleton<IEditorViewportAssetLocator, EditorViewportAssetLocator>()
            .AddSingleton<OpenGeometryKernelBridge>()
            .AddSingleton<IGeometryWorkerRuntimeResolver, AppOwnedGeometryWorkerRuntimeResolver>()
            .AddSingleton<IStepGeometryKernelService, PackagedStepGeometryKernelService>()
            .AddSingleton<IReferenceImageTraceService, AvaloniaReferenceImageTraceService>()
            .AddSingleton<IReferenceImageBackgroundRemovalService, AvaloniaReferenceImageBackgroundRemovalService>()
            .AddSingleton<IEditor2DGeometryKernelService, OpenGeometryEditor2DGeometryKernelService>()
            .AddSingleton<IEditor3DOperationService, OpenGeometryEditor3DOperationService>()
            .AddScoped<INavigationManager, NavigationManager>();

        _services = serviceCollection.BuildServiceProvider();
        Ioc.Default.ConfigureServices(_services);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyUserPreferences();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _documentWindowCoordinator = _services!.GetRequiredService<DesktopDocumentWindowCoordinator>();
            _documentWindowManager = new DesktopDocumentWindowManager(_services!, desktop);
            _documentWindowCoordinator.Attach(
                _documentWindowManager.OpenDocumentAsync,
                _documentWindowManager.ShowStartScreen);
            desktop.MainWindow = _documentWindowManager.CreateWelcomeWindow();
            var windowServices = _documentWindowManager.WelcomeServices!;
            desktop.Exit += (_, _) => DisposeServices();

            var logger = _services?.GetService<ILogger<App>>();
            _fileOpenRouter = new(exception => logger?.LogError(exception, "Failed to route activated files"));
            _fileActivationAcceptanceOutput = Environment.GetEnvironmentVariable(
                "PATHSTITCH_MACOS_FILE_ACTIVATION_OUTPUT");
            _activatableLifetime = TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;
            if (_activatableLifetime is not null)
                _activatableLifetime.Activated += OnApplicationActivated;

            var startupFiles = NormalizeStartupFileArguments(desktop.Args);
            _fileOpenRouter.Enqueue(startupFiles);
            desktop.MainWindow.Opened += (_, _) =>
            {
                _fileOpenRouter?.SetReady(paths => RouteActivatedFilesAsync(desktop, paths));
                if (!string.IsNullOrWhiteSpace(_fileActivationAcceptanceOutput))
                {
                    Directory.CreateDirectory(_fileActivationAcceptanceOutput);
                    File.WriteAllText(
                        Path.Combine(_fileActivationAcceptanceOutput, "file-activation-ready.txt"),
                        DateTimeOffset.UtcNow.ToString("O"));
                }
            };

            var acceptanceOutput = Environment.GetEnvironmentVariable("PATHSTITCH_MACOS_ACCEPTANCE_OUTPUT");
            if (!string.IsNullOrWhiteSpace(acceptanceOutput) && _services is not null)
            {
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    var exitCode = await MacOSPackagedAcceptance.RunAsync(
                        windowServices,
                        desktop.MainWindow,
                        acceptanceOutput).ConfigureAwait(true);
                    desktop.Shutdown(exitCode);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal static string[] NormalizeStartupFileArguments(IReadOnlyList<string>? arguments)
        => (arguments ?? [])
            .Where(static argument => !string.IsNullOrWhiteSpace(argument))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void OnApplicationActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is not FileActivatedEventArgs fileActivation)
            return;

        var paths = fileActivation.Files
            .Select(file => file.Path)
            .Where(path => path.IsFile)
            .Select(path => path.LocalPath)
            .ToArray();
        _fileOpenRouter?.Enqueue(paths);
    }

    private async Task RouteActivatedFilesAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        IReadOnlyList<string> filePaths)
    {
        if (_services is null)
            return;

        if (_documentWindowManager is null)
            return;
        await _documentWindowManager.OpenFilesAsync(filePaths).ConfigureAwait(true);
        var mainWindow = _documentWindowManager.ActiveWindow;

        if (!string.IsNullOrWhiteSpace(_fileActivationAcceptanceOutput))
        {
            var evidencePath = Path.Combine(
                _fileActivationAcceptanceOutput,
                "file-activation-acceptance.json");
            var evidence = new
            {
                schemaVersion = 1,
                status = "passed",
                files = filePaths.Select(Path.GetFullPath).ToArray(),
                activePage = mainWindow?.CurrentPageViewModel?.GetType().FullName,
            };
            await File.WriteAllTextAsync(
                evidencePath,
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }))
                .ConfigureAwait(true);
        }
    }

    private void DisposeServices()
    {
        if (_activatableLifetime is not null)
            _activatableLifetime.Activated -= OnApplicationActivated;
        _activatableLifetime = null;
        _fileOpenRouter?.Dispose();
        _fileOpenRouter = null;
        _fileActivationAcceptanceOutput = null;
        _documentWindowCoordinator?.Detach();
        _documentWindowCoordinator = null;
        _documentWindowManager?.Dispose();
        _documentWindowManager = null;
        (_services as IDisposable)?.Dispose();
        _services = null;
    }

    private static void ApplyUserPreferences()
    {
        var preferences = new UserPreferencesStore().Load();
        DxfPreviewCanvas.ReversePanDirection = preferences.ReversePanDirection;
        SvgPreviewDocumentParser.ConsolidateStrokes = preferences.ConsolidateSvgStrokes;
        SvgPreviewDocumentParser.FillMode = preferences.SvgFillMode;
        SvgPreviewDocumentParser.ImportThickness = preferences.SvgImportThickness;
        if (Current is not null)
        {
            Current.RequestedThemeVariant = preferences.Appearance.ToLowerInvariant() switch
            {
                "light" => Avalonia.Styling.ThemeVariant.Light,
                "dark" => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };
        }
    }
}
