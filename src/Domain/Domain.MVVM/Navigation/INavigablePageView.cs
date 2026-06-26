namespace Domain.MVVM.Navigation;

public interface INavigablePageView : INavigablePage
{
    INavigablePageViewModel ViewModel { get; }
}