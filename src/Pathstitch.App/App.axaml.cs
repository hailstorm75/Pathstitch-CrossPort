using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Pathstitch.App.AppExtensions;
using Pathstitch.App.Controls;
using Pathstitch.App.Services;
using UI.Navigation;

namespace Pathstitch.App;

public partial class App : Application
{
    private IServiceProvider? _services;

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
            .AddSingleton<IProcessLauncher, SystemProcessLauncher>()
            .AddSingleton<IAppUpdateService, AppUpdateService>()
            .AddSingleton<IFileIntegrationService>(services =>
                DesktopFileIntegrationServiceFactory.CreateForCurrentPlatform(
                    services.GetRequiredService<IProcessLauncher>()))
            .AddSingleton<IEditorOutputLauncherService, EditorOutputLauncherService>()
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

            WeakReferenceMessenger.Default.Send(new NavigationChangeRequestMessage(NavigationAddressBook.HomePage));

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
