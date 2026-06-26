using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Avalonia.Threading;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace UI.Navigation;

public sealed class NavigationManager(IServiceProvider serviceProvider) : INavigationManager
{
	public bool TryGetNavigablePage(string identifier, [NotNullWhen(true)] out INavigablePageView? page)
	{
		page = Dispatcher.UIThread.Invoke(() => serviceProvider.GetKeyedService<INavigablePageView>(identifier));

		return page is not null;
	}
}
