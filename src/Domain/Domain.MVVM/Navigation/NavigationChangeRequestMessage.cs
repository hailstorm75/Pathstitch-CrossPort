using System.Collections.Immutable;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Domain.MVVM.Navigation;

public sealed class NavigationChangeRequestMessage(string page, IReadOnlyDictionary<string, object>? parameters = null) : ValueChangedMessage<string>(page)
{
	public IReadOnlyDictionary<string, object> Parameters { get; } = parameters ?? ImmutableDictionary<string, object>.Empty;
}