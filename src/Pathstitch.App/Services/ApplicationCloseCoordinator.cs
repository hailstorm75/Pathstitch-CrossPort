using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Domain.MVVM.Navigation;

namespace Pathstitch.App.Services;

internal sealed record ApplicationCloseTarget(IMessenger Messenger, Action ApproveWindowClose);

internal sealed class ApplicationCloseCoordinator
{
    public async Task<bool> TryApproveAsync(IReadOnlyList<ApplicationCloseTarget> targets)
    {
        foreach (var target in targets)
        {
            var message = new PreviewApplicationClosingMessage();
            try
            {
                _ = target.Messenger.Send(message);
                if (!message.HasReceivedResponse)
                    continue;

                var cancellationSource = await message;
                if (await cancellationSource.Task.ConfigureAwait(true))
                    return false;
            }
            catch
            {
                return false;
            }
        }

        foreach (var target in targets)
            target.ApproveWindowClose();
        return true;
    }
}
