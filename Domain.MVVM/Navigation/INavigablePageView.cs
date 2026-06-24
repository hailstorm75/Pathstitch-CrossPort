namespace Domain.MVVM.Navigation;

public interface INavigablePage : IDisposable
{
    ValueTask<bool> ConfigureParametersAsync(IReadOnlyDictionary<string, object> parameters, CancellationToken cancellationToken);
}

public interface INavigablePageView : INavigablePage
{
    INavigablePageViewModel ViewModel { get; }
}

public interface INavigablePageViewModel : INavigablePage;

public interface INavigationManager;

public sealed class NavigationPageAttribute;