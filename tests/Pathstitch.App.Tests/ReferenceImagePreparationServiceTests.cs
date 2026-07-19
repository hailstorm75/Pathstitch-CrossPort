using Pathstitch.App.Services;
using SkiaSharp;
using Domain.App.Services;

namespace Pathstitch.App.Tests;

public sealed class ReferenceImagePreparationServiceTests
{
    private readonly AvaloniaReferenceImagePreparationService _service = new();

    [Fact]
    public void Prepare_CropDisabledPreservesOriginalPayloadAndDimensions()
    {
        var source = CreatePng(4, 3, bitmap => bitmap.SetPixel(2, 1, SKColors.Red));

        var succeeded = _service.TryPrepare(source, cropTransparentMargins: false, out var prepared);

        Assert.True(succeeded);
        Assert.NotNull(prepared);
        Assert.Same(source, prepared.Data);
        Assert.Equal(4, prepared.PixelWidth);
        Assert.Equal(3, prepared.PixelHeight);
    }

    [Fact]
    public void Prepare_AsymmetricTransparentBorderCropsToOpaqueBounds()
    {
        var source = CreatePng(7, 6, bitmap =>
        {
            for (var y = 2; y <= 4; y++)
            for (var x = 1; x <= 3; x++)
                bitmap.SetPixel(x, y, SKColors.CornflowerBlue);
        });

        var succeeded = _service.TryPrepare(source, cropTransparentMargins: true, out var prepared);

        Assert.True(succeeded);
        Assert.NotNull(prepared);
        Assert.Equal(3, prepared.PixelWidth);
        Assert.Equal(3, prepared.PixelHeight);
        using var cropped = SKBitmap.Decode(prepared.Data);
        Assert.NotNull(cropped);
        Assert.Equal(SKColors.CornflowerBlue, cropped.GetPixel(0, 0));
        Assert.Equal(SKColors.CornflowerBlue, cropped.GetPixel(2, 2));
    }

    [Fact]
    public void Prepare_FullyTransparentImageRemainsValidAndUnchanged()
    {
        var source = CreatePng(5, 4, _ => { });

        var succeeded = _service.TryPrepare(source, cropTransparentMargins: true, out var prepared);

        Assert.True(succeeded);
        Assert.NotNull(prepared);
        Assert.Same(source, prepared.Data);
        Assert.Equal(5, prepared.PixelWidth);
        Assert.Equal(4, prepared.PixelHeight);
    }

    [Fact]
    public void Prepare_UndecodablePayloadFails()
    {
        Assert.False(_service.TryPrepare([1, 2, 3, 4], cropTransparentMargins: true, out var prepared));
        Assert.Null(prepared);
    }

    [Fact]
    public async Task ActivatedImageImport_UsesCropSettingAndStoresCroppedPayload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-crop-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, CreatePng(8, 6, bitmap =>
        {
            for (var y = 1; y <= 2; y++)
            for (var x = 3; x <= 6; x++)
                bitmap.SetPixel(x, y, SKColors.Black);
        }));
        try
        {
            var editor = EditorPageViewModelModeTests.CreateViewModelForTests(
                referenceImagePreparationService: _service);
            editor.AutoCropTransparentReferenceImages = true;

            await editor.OpenActivatedFilesAsync([path]);

            var image = Assert.Single(editor.TwoDReferenceImages);
            Assert.Equal(4, image.PixelWidth);
            Assert.Equal(2, image.PixelHeight);
            using var stored = SKBitmap.Decode(Convert.FromBase64String(image.DataBase64));
            Assert.Equal(4, stored.Width);
            Assert.Equal(2, stored.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PickerImageImport_UsesSameCropPreparationPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-picker-crop-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, CreatePng(6, 5, bitmap =>
        {
            bitmap.SetPixel(2, 1, SKColors.Red);
            bitmap.SetPixel(3, 1, SKColors.Red);
        }));
        try
        {
            var editor = EditorPageViewModelModeTests.CreateViewModelForTests(
                projectFileDialogService: new ReferenceImageDialog(path),
                referenceImagePreparationService: _service);
            editor.AutoCropTransparentReferenceImages = true;

            await editor.ImportTwoDReferenceImageAsync();

            var image = Assert.Single(editor.TwoDReferenceImages);
            Assert.Equal(2, image.PixelWidth);
            Assert.Equal(1, image.PixelHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DroppedImage_UsesWorldInsertionPointWithoutChangingCropResult()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-positioned-crop-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, CreatePng(5, 4, bitmap => bitmap.SetPixel(3, 2, SKColors.Black)));
        try
        {
            var editor = EditorPageViewModelModeTests.CreateViewModelForTests(
                referenceImagePreparationService: _service);
            editor.AutoCropTransparentReferenceImages = true;

            await editor.OpenDroppedFilesAsync([path], new Domain.App.Models.Editor2DPoint(12.5, -7.25));

            var image = Assert.Single(editor.TwoDReferenceImages);
            Assert.Equal(1, image.PixelWidth);
            Assert.Equal(1, image.PixelHeight);
            Assert.Equal(12.5, image.X);
            Assert.Equal(-7.25, image.Y);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] CreatePng(int width, int height, Action<SKBitmap> draw)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(SKColors.Transparent);
        draw(bitmap);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class ReferenceImageDialog(string path) : IProjectFileDialogService
    {
        public Task<string?> PickReferenceImageFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(path);

        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }
}
