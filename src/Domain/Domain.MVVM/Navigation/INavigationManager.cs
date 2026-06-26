using System.Diagnostics.CodeAnalysis;

namespace Domain.MVVM.Navigation;

public interface INavigationManager
{
	bool TryGetNavigablePage(string identifier, [NotNullWhen(true)] out INavigablePageView? page);
}