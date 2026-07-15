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
  private bool _applicationCloseApproved;
  private bool _applicationClosePreviewRunning;


	public MainWindowShell(IServiceProvider serviceProvider)
	{
		_navigationManager = serviceProvider.GetRequiredService<INavigationManager>();
		_logger = serviceProvider.GetService<ILogger<MainWindowShell>>() ?? NullLogger<MainWindowShell>.Instance;

		InitializeComponent();

		WeakReferenceMessenger.Default.Register<NavigationChangeRequestMessage>(this, OnNavigationChanged);
		Closing += OnClosing;
		Closed += OnClosed;
	}

	private async void OnClosing(object? sender, WindowClosingEventArgs e)
	{
		if (_applicationCloseApproved)
			return;

		e.Cancel = true;
		if (_applicationClosePreviewRunning)
			return;

		_applicationClosePreviewRunning = true;
		try
		{
			var message = new PreviewApplicationClosingMessage();
			var cancelSource = await WeakReferenceMessenger.Default.Send(message);
			if (await cancelSource.Task.ConfigureAwait(true))
				return;

			_applicationCloseApproved = true;
			Close();
		}
		catch (InvalidOperationException)
		{
			// No active document owns the preview message. Nothing needs guarding.
			_applicationCloseApproved = true;
			Close();
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Application close preview failed");
		}
		finally
		{
			_applicationClosePreviewRunning = false;
		}
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		WeakReferenceMessenger.Default.UnregisterAll(this);
		_navigationCancellationTokenSource.Cancel();
		_navigationCancellationTokenSource.Dispose();
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
