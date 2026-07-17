using CommunityToolkit.Mvvm.Messaging;
using Domain.MVVM.Navigation;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class ApplicationCloseCoordinatorTests
{
    [Fact]
    public async Task TryApproveAsync_PreviewsDocumentsSequentiallyAndApprovesAfterAllConsent()
    {
        var events = new List<string>();
        var first = Messenger("first", cancel: false, events);
        var second = Messenger("second", cancel: false, events);
        var coordinator = new ApplicationCloseCoordinator();

        var approved = await coordinator.TryApproveAsync(
        [
            new(first, () => events.Add("approve-first")),
            new(second, () => events.Add("approve-second")),
        ]);

        Assert.True(approved);
        Assert.Equal(
            ["preview-first", "preview-second", "approve-first", "approve-second"],
            events);
    }

    [Fact]
    public async Task TryApproveAsync_CancelStopsLaterDocumentsAndApprovesNoWindows()
    {
        var events = new List<string>();
        var first = Messenger("first", cancel: false, events);
        var second = Messenger("second", cancel: true, events);
        var third = Messenger("third", cancel: false, events);
        var coordinator = new ApplicationCloseCoordinator();

        var approved = await coordinator.TryApproveAsync(
        [
            new(first, () => events.Add("approve-first")),
            new(second, () => events.Add("approve-second")),
            new(third, () => events.Add("approve-third")),
        ]);

        Assert.False(approved);
        Assert.Equal(["preview-first", "preview-second"], events);
    }

    [Fact]
    public async Task TryApproveAsync_NoRecipientOrFailedPreviewUsesSafeBehavior()
    {
        var noRecipient = new StrongReferenceMessenger();
        var failed = new StrongReferenceMessenger();
        var recipient = new object();
        failed.Register<PreviewApplicationClosingMessage>(recipient, (_, message) =>
        {
            var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            response.SetException(new IOException("prompt failed"));
            message.Reply(response);
        });
        var approvals = 0;
        var coordinator = new ApplicationCloseCoordinator();

        Assert.True(await coordinator.TryApproveAsync([new(noRecipient, () => approvals++)]));
        Assert.Equal(1, approvals);
        Assert.False(await coordinator.TryApproveAsync([new(failed, () => approvals++)]));
        Assert.Equal(1, approvals);
    }

    private static StrongReferenceMessenger Messenger(string name, bool cancel, ICollection<string> events)
    {
        var messenger = new StrongReferenceMessenger();
        var recipient = new object();
        messenger.Register<PreviewApplicationClosingMessage>(recipient, (_, message) =>
        {
            events.Add($"preview-{name}");
            var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            response.SetResult(cancel);
            message.Reply(response);
        });
        return messenger;
    }
}
