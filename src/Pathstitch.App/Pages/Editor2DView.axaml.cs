namespace Pathstitch.App.Pages;

public partial class Editor2DView : EditorInteractionControlBase
{
    public Editor2DView()
    {
        InitializeComponent();
        TwoDPreviewCanvas.ReferenceImageTransformChanged += OnReferenceImageTransformChanged;
    }

    private void OnReferenceImageTransformChanged(
        string layerId,
        double x,
        double y,
        double width,
        double height,
        double rotationDegrees)
        => (DataContext as Domain.App.ViewModels.EditorPageViewModel)?.UpdateTwoDReferenceImageTransform(
            layerId, x, y, width, height, rotationDegrees);

    public void CancelActiveInteraction() => TwoDPreviewCanvas.CancelActiveInteraction();
}
