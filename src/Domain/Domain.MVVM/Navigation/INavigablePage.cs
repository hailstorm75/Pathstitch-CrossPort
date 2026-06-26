namespace Domain.MVVM.Navigation;

public interface INavigablePage : IDisposable
{
	ValueTask<bool> ConfigureParametersAsync(IReadOnlyDictionary<string, object> parameters, CancellationToken cancellationToken);
}