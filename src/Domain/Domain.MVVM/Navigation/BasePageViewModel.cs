using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace Domain.MVVM.Navigation;

public abstract partial class BasePageViewModel(
    ILogger<BasePageViewModel> logger,
    IMessenger? messenger = null) : ObservableValidator, INavigablePageViewModel
{
    private readonly CancellationTokenSource _pageLeaveCancellationSource = new();
    private CancellationTokenSource? _loadCancellationSource;
    private bool _isDisposed;
    protected IMessenger Messenger { get; } = messenger ?? WeakReferenceMessenger.Default;

    private bool _isLoaded;
    
    public abstract bool IsLoading { get; set; }

    [RelayCommand]
    private async Task LoadAsync()
        => await LoadCoreAsync(_pageLeaveCancellationSource.Token).ConfigureAwait(true);

    ValueTask INavigablePageViewModel.LoadAsync(CancellationToken cancellationToken)
        => LoadCoreAsync(cancellationToken);

    private async ValueTask LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
            return;

        using var scope = logger.BeginScope(nameof(LoadAsync));

        IsLoading = true;

        try
        {
            logger.LogInformation("Loading page started");
            var start = Stopwatch.GetTimestamp();
            
            _loadCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                _pageLeaveCancellationSource.Token,
                cancellationToken);
            await LoadPageAsync(_loadCancellationSource.Token).ConfigureAwait(true);

            logger.LogInformation("Loading page completed in {Time}ms", Stopwatch.GetElapsedTime(start));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Loading failed");
        }
        finally
        {
            IsLoading = false;
            _isLoaded = true;
        }
    }

    public async ValueTask<bool> ConfigureParametersAsync(IReadOnlyDictionary<string, object> parameters, CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(nameof(ConfigureParametersAsync));
        
        logger.LogInformation("Configuring parameters");
        
        var start = Stopwatch.GetTimestamp();

        if (!await LoadParametersAsync(parameters, cancellationToken).ConfigureAwait(true))
            return false;
        
        logger.LogInformation("Configuring parameters completed in {Time}ms", Stopwatch.GetElapsedTime(start));

        Messenger.Register<BeforeNavigationChangeMessage>(this, BeforePageLeave);
        Messenger.Register<NavigationChangeRequestMessage>(this, PageLeaving);

        return true;
    }

    private void PageLeaving(object recipient, NavigationChangeRequestMessage message) => _pageLeaveCancellationSource.Cancel();

    protected virtual ValueTask<bool> LoadParametersAsync(IReadOnlyDictionary<string, object> parameters, CancellationToken cancellationToken) => ValueTask.FromResult(true);

    protected virtual void BeforePageLeave(object recipient, BeforeNavigationChangeMessage message) {}

    protected virtual ValueTask LoadPageAsync(CancellationToken token) => ValueTask.CompletedTask;

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _pageLeaveCancellationSource.Cancel();
        Messenger.UnregisterAll(this);
        _loadCancellationSource?.Dispose();
        _pageLeaveCancellationSource.Dispose();
    }
}
