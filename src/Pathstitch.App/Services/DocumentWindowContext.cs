using Avalonia.Controls;

namespace Pathstitch.App.Services;

public interface IDocumentWindowContext
{
    Window? Owner { get; set; }
}

internal sealed class DocumentWindowContext : IDocumentWindowContext
{
    public Window? Owner { get; set; }
}
