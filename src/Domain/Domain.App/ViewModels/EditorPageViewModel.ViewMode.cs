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
            if (!SetWorkspaceFacadeValue(_threeDOrthographic, value, updated => _threeDOrthographic = updated))
                return;

            SyncSidebarToolStates();
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }
}
