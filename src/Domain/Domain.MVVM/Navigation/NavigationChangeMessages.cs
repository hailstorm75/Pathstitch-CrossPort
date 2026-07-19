using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Domain.MVVM.Navigation;

public sealed class BeforeNavigationChangeMessage(NavigationChangeRequestMessage request) : AsyncRequestMessage<TaskCompletionSource<bool>>
{
    public NavigationChangeRequestMessage Request => request;
}