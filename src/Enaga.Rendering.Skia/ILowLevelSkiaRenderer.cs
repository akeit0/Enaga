using SkiaSharp;

namespace Enaga.Rendering.Skia;

public interface ILowLevelSkiaRenderer
{
    ReadOnlySpan<SceneDamageRect> ConsumeLowLevelDirtyRects(int width, int height);

    void RenderLowLevelSkia(
        SKCanvas canvas,
        int width,
        int height,
        TimeSpan elapsed,
        ReadOnlySpan<SceneDamageRect> dirtyRects
    );
}

public interface ILowLevelSkiaLayer
{
    bool TryGetRenderBounds(out SceneDamageRect bounds);

    void RenderLowLevelSkia(
        SKCanvas canvas,
        int width,
        int height,
        TimeSpan elapsed,
        ReadOnlySpan<SceneDamageRect> dirtyRects
    );
}
