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
            .AddSingleton<ProjectSessionService>()
            .AddSingleton<Project3DStateService>()
            .AddSingleton<IProjectFileDialogService, ProjectFileDialogService>()
            .AddSingleton<IUnsavedChangesPromptService, AvaloniaUnsavedChangesPromptService>()
            .AddSingleton<IEditorImportUnitsPromptService, AvaloniaEditorImportUnitsPromptService>()
            .AddSingleton<IPsdImportService, PackagedPsdImportService>()
            .AddSingleton<IPsdImportModePromptService, AvaloniaPsdImportModePromptService>()
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
            .AddSingleton<INavigationManager, NavigationManager>();

        _services = serviceCollection.BuildServiceProvider();
        Ioc.Default.ConfigureServices(_services);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyUserPreferences();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindowShell(Ioc.Default);
            desktop.Exit += (_, _) => DisposeServices();

            var logger = _services?.GetService<ILogger<App>>();
            _fileOpenRouter = new(exception => logger?.LogError(exception, "Failed to route activated files"));
            _fileActivationAcceptanceOutput = Environment.GetEnvironmentVariable(
                "PATHSTITCH_MACOS_FILE_ACTIVATION_OUTPUT");
            _activatableLifetime = TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;
            if (_activatableLifetime is not null)
                _activatableLifetime.Activated += OnApplicationActivated;

            WeakReferenceMessenger.Default.Send(new NavigationChangeRequestMessage(NavigationAddressBook.HomePage));

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
                        _services,
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

        desktop.MainWindow?.Show();
        desktop.MainWindow?.Activate();
        var mainWindow = desktop.MainWindow as MainWindowShell;
        if (mainWindow is
            {
                CurrentPageViewModel: Domain.App.ViewModels.EditorPageViewModel editorPageViewModel,
            })
        {
            await editorPageViewModel.OpenActivatedFilesAsync(filePaths).ConfigureAwait(true);
        }
        else
        {
            var currentHomePage = mainWindow?.CurrentPageViewModel
                as Domain.App.ViewModels.HomePageViewModel;
            var homePageViewModel = currentHomePage
                ?? _services.GetRequiredKeyedService<INavigablePageViewModel>(NavigationAddressBook.HomePage)
                    as Domain.App.ViewModels.HomePageViewModel;
            if (homePageViewModel is not null)
                await homePageViewModel.OpenFilesAsync(filePaths).ConfigureAwait(true);
        }

        if (mainWindow is not null)
            await mainWindow.WhenNavigationIdleAsync().ConfigureAwait(true);

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
