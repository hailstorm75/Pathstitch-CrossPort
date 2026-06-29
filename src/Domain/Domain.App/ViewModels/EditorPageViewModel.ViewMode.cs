using System;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public bool CanEditSelectedBodyOffset => IsMoveToolActive && HasSelectedBody;

    public bool ThreeDOrthographic
    {
        get => _threeDOrthographic;
        private set
        {
            if (!SetProperty(ref _threeDOrthographic, value))
                return;

            SyncSidebarToolStates();
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }
}
