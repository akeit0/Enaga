using Enaga.Rendering;
using Enaga.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace Enaga.Tests;

public sealed class SkiaImageAssetLoaderTests
{
    [Fact]
    public void LoadFromPath_DecodesRasterImage()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        using (var bitmap = new SKBitmap(12, 8))
        using (var canvas = new SKCanvas(bitmap))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.OpenWrite(filePath))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            data.SaveTo(stream);
        }

        try
        {
            using var asset = SkiaImageAssetLoader.LoadFromPath(filePath);

            Assert.NotNull(asset);
            Assert.NotNull(asset!.RasterImage);
            Assert.Null(asset.VectorPicture);
            Assert.Equal(12, asset.SourceRect.Width);
            Assert.Equal(8, asset.SourceRect.Height);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void LoadFromPath_DecodesSvgImage()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.svg");
        File.WriteAllText(filePath, """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 20" fill="none">
              <rect width="32" height="20" rx="4" fill="#2563EB"/>
            </svg>
            """);

        try
        {
            using var asset = SkiaImageAssetLoader.LoadFromPath(filePath);

            Assert.NotNull(asset);
            Assert.Null(asset!.RasterImage);
            Assert.NotNull(asset.VectorPicture);
            Assert.Equal(32, asset.SourceRect.Width);
            Assert.Equal(20, asset.SourceRect.Height);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void LoadFromPath_InvalidSvg_DoesNotThrowAndReturnsNull()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.svg");
        File.WriteAllText(filePath, "<svg><g><invalid");

        try
        {
            var exception = Record.Exception(() => SkiaImageAssetLoader.LoadFromPath(filePath));
            var asset = SkiaImageAssetLoader.LoadFromPath(filePath);
            var loaded = SkiaImageAssetLoader.TryLoadFromPath(filePath, out var tryAsset, out var error);

            Assert.Null(exception);
            Assert.Null(asset);
            Assert.False(loaded);
            Assert.Null(tryAsset);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Resolve_FileUri_LocalImage_IsReady()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.svg");
        File.WriteAllText(filePath, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\" />");
        try
        {
            var result = WebImageCache.Resolve(new Uri(filePath).AbsoluteUri);

            Assert.Equal(WebImageCacheState.Ready, result.State);
            Assert.Equal(filePath, result.LocalPath);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void AssetCache_ResolvesRasterImageAsynchronously()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        using (var bitmap = new SKBitmap(10, 6))
        using (var canvas = new SKCanvas(bitmap))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.OpenWrite(filePath))
        {
            canvas.Clear(SKColors.Orange);
            data.SaveTo(stream);
        }

        try
        {
            var first = SkiaImageAssetCache.Resolve(filePath);
            Assert.Equal(SkiaImageAssetState.Pending, first.State);

            var completed = SpinWait.SpinUntil(
                () => SkiaImageAssetCache.Resolve(filePath).State != SkiaImageAssetState.Pending,
                TimeSpan.FromSeconds(5));

            Assert.True(completed);
            var resolved = SkiaImageAssetCache.Resolve(filePath);
            Assert.Equal(SkiaImageAssetState.Ready, resolved.State);
            Assert.NotNull(resolved.Asset?.RasterImage);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
