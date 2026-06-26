using System;
using System.Threading;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UI.Navigation;

namespace Pathstitch.App;

public sealed partial class MainWindowShell : Window
{
  private readonly INavigationManager _navigationManager;
  private readonly ILogger<MainWindowShell> _logger;
  private CancellationTokenSource _navigationCancellationTokenSource = new();


	public MainWindowShell(IServiceProvider serviceProvider)
	{
		_navigationManager = serviceProvider.GetRequiredService<INavigationManager>();
		_logger = serviceProvider.GetService<ILogger<MainWindowShell>>() ?? NullLogger<MainWindowShell>.Instance;

		InitializeComponent();

		WeakReferenceMessenger.Default.Register<NavigationChangeRequestMessage>(this, OnNavigationChanged);
	}

	private async void OnNavigationChanged(object recipient, NavigationChangeRequestMessage message)
	{
		try
		{
			await _navigationCancellationTokenSource.CancelAsync();
			_navigationCancellationTokenSource.Dispose();

			_navigationCancellationTokenSource = new();

			await _navigationManager.TryNavigateAsync(message, PART_PageContainer, _logger, _navigationCancellationTokenSource.Token);
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Navigation failed");
		}
	}
}