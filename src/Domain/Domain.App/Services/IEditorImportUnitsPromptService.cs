using Domain.App.Models;

namespace Domain.App.Services;

public interface IEditorImportUnitsPromptService
{
    Task<double?> PromptAsync(Editor2DImportUnitsInfo info, CancellationToken cancellationToken = default);
}
