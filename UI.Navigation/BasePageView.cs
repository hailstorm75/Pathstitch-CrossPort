using Avalonia.Controls;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace UI.Navigation;

public abstract class BasePageView : UserControl, INavigablePageView
{
    public INavigablePageViewModel ViewModel
    {
        get => (DataContext as INavigablePageViewModel)!;
        private init => DataContext = value;
    }
    
    protected BasePageView(IServiceProvider serviceProvider, string identifier)
    {
        ViewModel = serviceProvider.GetRequiredKeyedService<INavigablePageViewModel>(identifier);
    }

    public ValueTask<bool> ConfigureParametersAsync(IReadOnlyDictionary<string, object> parameters, CancellationToken cancellationToken)
        => ViewModel.ConfigureParametersAsync(parameters, cancellationToken);

    public void Dispose() => ViewModel.Dispose();
}