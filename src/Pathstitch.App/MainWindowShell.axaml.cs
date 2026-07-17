using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Services;
using UI.Navigation;

namespace Pathstitch.App;

public sealed partial class MainWindowShell : Window
{
  private readonly INavigationManager _navigationManager;
  private readonly IMessenger _messenger;
  private readonly ILogger<MainWindowShell> _logger;
  private CancellationTokenSource _navigationCancellationTokenSource = new();
  private TaskCompletionSource _navigationIdle = CompletedSource();
  private bool _applicationCloseApproved;
  private bool _applicationClosePreviewRunning;


	public MainWindowShell(IServiceProvider serviceProvider)
	{
		_navigationManager = serviceProvider.GetRequiredService<INavigationManager>();
		_messenger = serviceProvider.GetService<IMessenger>() ?? WeakReferenceMessenger.Default;
		_logger = serviceProvider.GetService<ILogger<MainWindowShell>>() ?? NullLogger<MainWindowShell>.Instance;

		InitializeComponent();
		var windowContext = serviceProvider.GetService<IDocumentWindowContext>();
		if (windowContext is not null)
			windowContext.Owner = this;

		_messenger.Register<NavigationChangeRequestMessage>(this, OnNavigationChanged);
		Closing += OnClosing;
		Closed += OnClosed;
	}

	internal INavigablePageViewModel? CurrentPageViewModel
		=> (PART_PageContainer.Content as INavigablePageView)?.ViewModel;

	internal Task WhenNavigationIdleAsync() => _navigationIdle.Task;

	internal void ApproveApplicationClose() => _applicationCloseApproved = true;

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
			var cancelSource = await _messenger.Send(message);
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
		_messenger.UnregisterAll(this);
		if (PART_PageContainer.Content is INavigablePageView currentPage)
			currentPage.Dispose();
		PART_PageContainer.Content = null;
		_navigationCancellationTokenSource.Cancel();
		_navigationCancellationTokenSource.Dispose();
		_navigationIdle.TrySetResult();
	}

	private async void OnNavigationChanged(object recipient, NavigationChangeRequestMessage message)
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_navigationIdle = completion;
		try
		{
			await _navigationCancellationTokenSource.CancelAsync();
			_navigationCancellationTokenSource.Dispose();

			_navigationCancellationTokenSource = new();

			await _navigationManager.TryNavigateAsync(message, PART_PageContainer, _messenger, _logger, _navigationCancellationTokenSource.Token);
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Navigation failed");
		}
		finally
		{
			completion.TrySetResult();
		}
	}

	private static TaskCompletionSource CompletedSource()
	{
		var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		source.SetResult();
		return source;
	}
}
