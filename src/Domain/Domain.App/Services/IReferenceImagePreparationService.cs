namespace Domain.App.Services;

public sealed record PreparedReferenceImage(byte[] Data, int PixelWidth, int PixelHeight);

public interface IReferenceImagePreparationService
{
    bool TryPrepare(byte[] sourceData, bool cropTransparentMargins, out PreparedReferenceImage? image);
}
