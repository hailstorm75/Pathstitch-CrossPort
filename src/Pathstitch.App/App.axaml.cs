using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Pathstitch.App.AppExtensions;
using Pathstitch.App.Services;
using UI.Navigation;

namespace Pathstitch.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        OcctConfiguration.Configure();

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddPages()
            .AddTelemetry()
            .AddSingleton<RecentProjectsService>()
            .AddSingleton<ProjectSessionService>()
            .AddSingleton<Project3DStateService>()
            .AddSingleton<IProjectFileDialogService, ProjectFileDialogService>()
            .AddSingleton<IEditorOutputLauncherService, EditorOutputLauncherService>()
            .AddSingleton<IEditorOutputPreviewService, DxfOutputPreviewService>()
            .AddSingleton<IGeometryKernelDescriptorProvider, OcctGeometryKernelDescriptorProvider>()
            .AddSingleton<IEditorViewportAssetLocator, EditorViewportAssetLocator>()
            .AddSingleton<IEditor3DOperationService, OcctEditor3DOperationService>()
            .AddSingleton<INavigationManager, NavigationManager>();

        Ioc.Default.ConfigureServices(serviceCollection.BuildServiceProvider());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindowShell(Ioc.Default);

            WeakReferenceMessenger.Default.Send(new NavigationChangeRequestMessage(NavigationAddressBook.HomePage));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
