using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Pathstitch.App.Pages;

namespace Pathstitch.App.AppExtensions;

public static class PagingExtensions
{
    public static IServiceCollection AddPages(this IServiceCollection services)
    {
        // Add `KeyedTransient` Views and corresponding ViewModels here under the same navigation identifier
        services.AddKeyedTransient<INavigablePageView, HomePageView>(NavigationAddressBook.HomePage);
        services.AddKeyedTransient<INavigablePageViewModel, HomePageViewModel>(NavigationAddressBook.HomePage);

        return services;
    }
}