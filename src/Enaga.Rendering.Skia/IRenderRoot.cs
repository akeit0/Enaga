using SkiaSharp;

namespace Enaga.Rendering.Skia;

public interface IRenderRoot
{
    void Render(SKCanvas canvas, int width, int height, TimeSpan elapsed);
}

public interface IRenderGpuContextSink
{
    void SetRenderGpuContext(GRContext? context);
}

public interface IRenderSurfaceInvalidationSink
{
    void InvalidatePresentationSurface();
}
