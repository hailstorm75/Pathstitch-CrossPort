using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class SvgPreviewDocumentTests
{
    [Fact]
    public async Task LoadPreviewDocumentAsync_ParsesCommonSvgPrimitives()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-{Guid.NewGuid():N}.svg");
        await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"><line x1=\"0\" y1=\"1\" x2=\"4\" y2=\"5\" /><rect x=\"1\" y=\"2\" width=\"10\" height=\"5\" /><circle cx=\"4\" cy=\"6\" r=\"2\" /></svg>");

        try
        {
            var document = await new DxfOutputPreviewService().LoadPreviewDocumentAsync(path);

            Assert.NotNull(document);
            Assert.Equal(3, document.Paths.Count);
            Assert.Contains(document.Paths, path => path.EntityType == "LINE");
            Assert.Contains(document.Paths, path => path.EntityType == "RECTANGLE");
            Assert.Contains(document.Paths, path => path.EntityType == "CIRCLE");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
