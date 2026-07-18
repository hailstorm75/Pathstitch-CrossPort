using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Help;
using Pathstitch.App.Services;
using UI.Navigation;

namespace Pathstitch.App;

public sealed partial class MainWindowShell : Window, IContextualHelpHost
{
  private const string DefaultContextualHelpText = "Hover over a control for help.";

  private readonly INavigationManager _navigationManager;
  private readonly IMessenger _messenger;
  private readonly ILogger<MainWindowShell> _logger;
  private readonly IProcessLauncher _processLauncher;
  private readonly List<Control> _activeContextualHelpSources = [];
  private CancellationTokenSource _navigationCancellationTokenSource = new();
  private TaskCompletionSource _navigationIdle = CompletedSource();
  private bool _applicationCloseApproved;
  private bool _applicationClosePreviewRunning;


	public MainWindowShell(IServiceProvider serviceProvider)
	{
		_navigationManager = serviceProvider.GetRequiredService<INavigationManager>();
		_messenger = serviceProvider.GetService<IMessenger>() ?? WeakReferenceMessenger.Default;
		_logger = serviceProvider.GetService<ILogger<MainWindowShell>>() ?? NullLogger<MainWindowShell>.Instance;
		_processLauncher = serviceProvider.GetService<IProcessLauncher>() ?? new SystemProcessLauncher();

		InitializeComponent();
		var windowContext = serviceProvider.GetService<IDocumentWindowContext>();
		if (windowContext is not null)
			windowContext.Owner = this;

		_messenger.Register<NavigationChangeRequestMessage>(this, OnNavigationChanged);
		Closing += OnClosing;
		Closed += OnClosed;
		KeyDown += OnWindowKeyDown;
	}

	internal INavigablePageViewModel? CurrentPageViewModel
		=> (PART_PageContainer.Content as INavigablePageView)?.ViewModel;

	internal Task WhenNavigationIdleAsync() => _navigationIdle.Task;

	internal void ApproveApplicationClose() => _applicationCloseApproved = true;

	internal string DisplayedContextualHelpText => PART_ContextualHelpText.Text ?? string.Empty;

	internal bool IsContextualHelpDocumentationAvailable => PART_ContextualHelpShortcut.IsVisible;

	void IContextualHelpHost.ActivateContextualHelp(Control source)
	{
		_activeContextualHelpSources.Remove(source);
		_activeContextualHelpSources.Add(source);
		DisplayContextualHelp(source);
	}

	void IContextualHelpHost.DeactivateContextualHelp(Control source)
	{
		if (!_activeContextualHelpSources.Remove(source))
			return;

		for (var index = _activeContextualHelpSources.Count - 1; index >= 0; index--)
		{
			var fallback = _activeContextualHelpSources[index];
			if (!fallback.IsPointerOver || string.IsNullOrWhiteSpace(ContextualHelp.GetText(fallback)))
			{
				_activeContextualHelpSources.RemoveAt(index);
				continue;
			}

			DisplayContextualHelp(fallback);
			return;
		}

		DisplayContextualHelp(null);
	}

	private void DisplayContextualHelp(Control? source)
	{
		var text = source is null ? null : ContextualHelp.GetText(source);
		var documentationTarget = source is null ? null : ContextualHelp.GetDocumentationTarget(source);
		PART_ContextualHelpText.Text = string.IsNullOrWhiteSpace(text)
			? DefaultContextualHelpText
			: text.Trim();
		PART_ContextualHelpShortcut.IsVisible = !string.IsNullOrWhiteSpace(documentationTarget);
	}

	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key != Key.F1 || !TryOpenActiveContextualDocumentation())
			return;

		e.Handled = true;
	}

	internal bool TryOpenActiveContextualDocumentation()
	{
		if (_activeContextualHelpSources.Count == 0)
			return false;

		var source = _activeContextualHelpSources[^1];
		var target = ContextualHelp.GetDocumentationTarget(source);
		if (string.IsNullOrWhiteSpace(target)
			|| !Uri.TryCreate(target, UriKind.Absolute, out var uri)
			|| uri.Scheme is not ("http" or "https" or "file"))
			return false;

		try
		{
			_processLauncher.Start(new ProcessStartInfo
			{
				FileName = target,
				UseShellExecute = true,
			});
			return true;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to open contextual documentation target");
			return false;
		}
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
		KeyDown -= OnWindowKeyDown;
		_activeContextualHelpSources.Clear();
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
