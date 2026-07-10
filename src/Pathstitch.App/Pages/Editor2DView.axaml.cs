namespace Pathstitch.App.Pages;

public partial class Editor2DView : EditorInteractionControlBase
{
    public Editor2DView() => InitializeComponent();

    public void CancelActiveInteraction() => TwoDPreviewCanvas.CancelActiveInteraction();
}
