using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Pathstitch.App.AppExtensions;
using UI.Navigation;

namespace Pathstitch.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddPages()
            .AddTelemetry()
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