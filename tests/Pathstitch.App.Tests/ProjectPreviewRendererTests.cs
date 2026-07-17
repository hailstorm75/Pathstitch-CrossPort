using Domain.App.Models;
using Pathstitch.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Tests;

public sealed class ProjectPreviewRendererTests
{
    [Fact]
    public async Task BlankProject_RendersOpaqueBrandedPlaceholder()
    {
        var bytes = await new ProjectPreviewRenderer().RenderAsync(null);

        Assert.NotNull(bytes);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(640, bitmap.Width);
        Assert.Equal(400, bitmap.Height);
        Assert.Equal(byte.MaxValue, bitmap.GetPixel(0, 0).Alpha);
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(286, 140));
    }

    [Fact]
    public async Task GeometryProject_RendersFramedOpaquePreview()
    {
        var line = new Editor2DPreviewPath(
            "line",
            "LINE",
            [new Editor2DPoint(0, 0), new Editor2DPoint(100, 20)],
            false);
        var geometry = new Editor2DPreviewDocument(
            [line],
            new Editor2DBounds(0, 0, 100, 20),
            new Dictionary<string, int> { ["LINE"] = 1 },
            []);
        var document = new Editor2DExportDocument(
            geometry,
            new Dictionary<string, Editor2DExportPathMetadata>
            {
                [line.Id] = new("Cut", "#FF0000"),
            });

        var bytes = await new ProjectPreviewRenderer().RenderAsync(document);

        Assert.NotNull(bytes);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(640, Math.Max(bitmap.Width, bitmap.Height));
        Assert.Equal(byte.MaxValue, bitmap.GetPixel(0, 0).Alpha);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Red > pixel.Green + 40);
    }
}
