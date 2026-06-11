using System.Diagnostics.CodeAnalysis;
using System.Xml;
using SkiaSharp;
using Svg.Skia;

namespace Enaga.Rendering.Skia;

internal sealed class SkiaImageAsset : IDisposable
{
    private SkiaImageAsset(SKImage? rasterImage, SKPicture? vectorPicture, SKRect sourceRect)
    {
        RasterImage = rasterImage;
        VectorPicture = vectorPicture;
        SourceRect = sourceRect;
    }

    public SKImage? RasterImage { get; }
    public SKPicture? VectorPicture { get; }
    public SKRect SourceRect { get; }

    public static SkiaImageAsset CreateRaster(SKImage rasterImage) =>
        new(rasterImage, null, SKRect.Create(rasterImage.Width, rasterImage.Height));

    public static SkiaImageAsset CreateVector(SKPicture vectorPicture, SKRect sourceRect) =>
        new(
            null,
            vectorPicture,
            sourceRect.Width > 0 && sourceRect.Height > 0 ? sourceRect : SKRect.Create(1, 1)
        );

    public void Dispose()
    {
        RasterImage?.Dispose();
        VectorPicture?.Dispose();
    }
}

internal static class SkiaImageAssetLoader
{
    public static bool TryLoadFromPath(
        string? localPath,
        out SkiaImageAsset? asset,
        out string? error
    )
    {
        asset = null;
        error = null;

        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            error = string.IsNullOrWhiteSpace(localPath)
                ? "Image path is empty."
                : $"Image file was not found: {localPath}";
            return false;
        }

        try
        {
            asset = IsSvgPath(localPath) ? LoadVectorImage(localPath) : LoadRasterImage(localPath);

            if (asset is not null)
                return true;

            error = $"Image decode failed: {localPath}";
            return false;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (XmlException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static SkiaImageAsset? LoadFromPath(string? localPath)
    {
        _ = TryLoadFromPath(localPath, out var asset, out _);
        return asset;
    }

    private static bool IsSvgPath(string localPath) =>
        string.Equals(Path.GetExtension(localPath), ".svg", StringComparison.OrdinalIgnoreCase);

    private static SkiaImageAsset? LoadRasterImage(string localPath)
    {
        using var data = SKData.Create(localPath);
        if (data is null)
            return null;

        var image = SKImage.FromEncodedData(data);
        return image is null ? null : SkiaImageAsset.CreateRaster(image);
    }

    [SuppressMessage("Usage", "IL2026:Calls Svg.Skia.SKSvg.Load(Stream)")]
    private static SkiaImageAsset? LoadVectorImage(string localPath)
    {
        using var stream = File.OpenRead(localPath);

        var svg = new SKSvg();
        var picture = svg.Load(stream);
        if (picture is null)
            return null;

        return SkiaImageAsset.CreateVector(picture, picture.CullRect);
    }
}
