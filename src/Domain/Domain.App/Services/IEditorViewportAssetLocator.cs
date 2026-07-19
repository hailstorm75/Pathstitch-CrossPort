namespace Domain.App.Services;

public interface IEditorViewportAssetLocator
{
    string GetViewportHtml();

    Uri GetViewportBaseUri();
}
