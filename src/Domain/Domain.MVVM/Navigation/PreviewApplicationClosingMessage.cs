using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Domain.MVVM.Navigation;

/// <summary>
/// Previews application shutdown. Complete response with <see langword="true"/> to cancel shutdown.
/// </summary>
public sealed class PreviewApplicationClosingMessage : AsyncRequestMessage<TaskCompletionSource<bool>>;
