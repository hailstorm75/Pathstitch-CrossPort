using Domain.App.Models;

namespace Domain.App.Services;

public interface IProjectPreviewRenderer
{
    Task<byte[]?> RenderAsync(
        Editor2DExportDocument? document,
        CancellationToken cancellationToken = default);
}
