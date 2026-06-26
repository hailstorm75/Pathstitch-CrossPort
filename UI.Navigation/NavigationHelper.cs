using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging;

namespace UI.Navigation;

public static class NavigationHelper
{
	public static async Task<bool> TryNavigateAsync(
		this INavigationManager manager,
		NavigationChangeRequestMessage request,
		ContentControl container,
		ILogger logger,
		CancellationToken cancellationToken = default)
	{
		using var scope = logger.BeginScope("Navigating to {Page}", request.Value);

		try
		{
			var message = new BeforeNavigationChangeMessage(request);
			var cancelAwaiter = await WeakReferenceMessenger.Default.Send(message);

			cancellationToken.Register(() => cancelAwaiter.TrySetResult(true));

			var cancel = await cancelAwaiter.Task.ConfigureAwait(false);

			if (cancel)
			{
				logger.LogWarning("Navigation cancelled");
				return false;
			}
		}
		catch (OperationCanceledException)
		{
			logger.LogWarning("Navigation cancelled");
			return false;
		}
		catch (InvalidOperationException)
		{
			// Did not cancel
		}

		return await Dispatcher.UIThread.InvokeAsync(() => PerformNavigateAsync(manager, request, container, logger, cancellationToken));
	}

	private static async Task<bool> PerformNavigateAsync(
		INavigationManager manager,
		NavigationChangeRequestMessage request,
		ContentControl container,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		try
		{
			logger.LogDebug("Got navigation request for identifier '{Identifier}' with {ParameterCount} parameter(s)", request.Value, request.Parameters.Count);

			if (!manager.TryGetNavigablePage(request.Value, out var page))
			{
				logger.LogWarning("Navigation request for identifier '{Identifier}' was not found", request.Value);
				return false;
			}

			if (!await page.ConfigureParametersAsync(request.Parameters, cancellationToken).ConfigureAwait(true))
			{
				logger.LogError("Failed to configure parameters for page '{Page}'", request.Value);
				return false;
			}

			logger.LogDebug("Target page configured with specified parameters");

			if (container.Content is INavigablePageView currentPage)
				currentPage.Dispose();

			container.Content = page;

			logger.LogInformation("Navigated to {Page}", request.Value);

			return true;
		}
		catch (OperationCanceledException)
		{
			logger.LogWarning("Navigation cancelled");
			return false;
		}
		catch (Exception e)
		{
			logger.LogError(e, "Navigation failed");
			return false;
		}
	}
}
