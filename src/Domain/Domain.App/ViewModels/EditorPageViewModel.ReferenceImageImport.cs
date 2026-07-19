using Domain.App.Services;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private bool _autoCropTransparentReferenceImages;

    public bool AutoCropTransparentReferenceImages
    {
        get => _autoCropTransparentReferenceImages;
        set => SetProperty(ref _autoCropTransparentReferenceImages, value);
    }

    private bool TryPrepareReferenceImage(byte[] sourceData, out PreparedReferenceImage? image)
        => _referenceImagePreparationService.TryPrepare(
            sourceData,
            AutoCropTransparentReferenceImages,
            out image);
}
