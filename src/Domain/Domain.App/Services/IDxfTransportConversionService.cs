namespace Domain.App.Services;

public interface IDxfTransportConversionService
{
    Task<string> ConvertBinaryToAsciiAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}