using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;

namespace Enaga.Hosting;

internal sealed class SkiaWindowSurfaceManager : IDisposable
{
    private readonly RenderGraphicsBackend graphicsBackend;
    private readonly SceneDamageRectBufferWriter physicalDirtyRectBuffer = new(16);
    private readonly TimeProvider timeProvider;
    private readonly IWindow window;
    private bool disposed;
    private ISkiaWindowSurface? surface;
    private Vector2D<int> surfaceFramebufferSize;

    public SkiaWindowSurfaceManager(IWindow window, RenderGraphicsBackend graphicsBackend, TimeProvider? timeProvider = null)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        this.graphicsBackend = graphicsBackend;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ISkiaWindowSurface? Surface => surface;

    public bool HasSurface => surface is not null;

    public bool RequiresPresentOnRenderWithoutDamage => surface?.RequiresPresentOnRenderWithoutDamage == true;

    public SKCanvas Canvas => surface?.Canvas ?? throw new InvalidOperationException("Skia window surface is not initialized.");

    public PresentDiagnosticsSnapshot LastDiagnostics => surface?.LastDiagnostics ?? default;

    public Vector2D<int> FramebufferSize => GetSafeSize(window.FramebufferSize);

    public WindowSurfaceFrameTarget CaptureFrameTarget()
        => new(GetSafeSize(window.Size), FramebufferSize);

    public bool IsCurrentFrameTarget(WindowSurfaceFrameTarget target)
        => CaptureFrameTarget() == target;

    public void Initialize()
    {
        surface = graphicsBackend switch
        {
            RenderGraphicsBackend.Vulkan => new VulkanSkiaWindowSurface(window, timeProvider),
            RenderGraphicsBackend.Metal => new MetalSkiaWindowSurface(window, timeProvider),
            _ => new OpenGlSkiaWindowSurface(GL.GetApi(window), timeProvider),
        };

        var framebufferSize = FramebufferSize;
        surface.Initialize(framebufferSize);
        surfaceFramebufferSize = framebufferSize;
    }

    public bool ResizeToCurrentFramebuffer()
    {
        return Resize(FramebufferSize);
    }

    public bool ResizeToFrameTarget(WindowSurfaceFrameTarget target)
    {
        return Resize(target.FramebufferSize);
    }

    public bool Resize(Vector2D<int> size)
    {
        if (surface is null)
            return false;

        var safeSize = GetSafeSize(size);
        if (safeSize == surfaceFramebufferSize)
            return false;

        var contentInvalidated = surface.Resize(safeSize);
        surfaceFramebufferSize = safeSize;
        return contentInvalidated;
    }

    public void PresentStartupFrame(SKColor clearColor)
    {
        if (surface is null)
            return;

        surface.Canvas.Clear(clearColor);
        Span<SceneDamageRect> dirtyRects =
        [
            new SceneDamageRect(0, 0, Math.Max(1, FramebufferSize.X), Math.Max(1, FramebufferSize.Y)),
        ];
        surface.Present(dirtyRects);
    }

    public void RenderIntoCanvas(IRenderRoot renderRoot, TimeSpan elapsed)
    {
        RenderIntoCanvas(renderRoot, elapsed, CaptureFrameTarget());
    }

    public void RenderIntoCanvas(IRenderRoot renderRoot, TimeSpan elapsed, WindowSurfaceFrameTarget target)
    {
        ArgumentNullException.ThrowIfNull(renderRoot);

        var safeLogicalSize = target.LogicalSize;
        var framebufferSize = target.FramebufferSize;
        var scaleX = framebufferSize.X / (float)safeLogicalSize.X;
        var scaleY = framebufferSize.Y / (float)safeLogicalSize.Y;
        using var restore = new SKAutoCanvasRestore(Canvas, true);
        Canvas.Scale(scaleX, scaleY);
        if (renderRoot is IRenderGpuContextSink gpuContextSink)
            gpuContextSink.SetRenderGpuContext(surface?.Context);
        renderRoot.Render(Canvas, safeLogicalSize.X, safeLogicalSize.Y, elapsed);
    }

    public ReadOnlySpan<SceneDamageRect> ScaleDirtyRectsToFramebuffer(ReadOnlySpan<SceneDamageRect> dirtyRects)
    {
        return ScaleDirtyRectsToFramebuffer(dirtyRects, CaptureFrameTarget());
    }

    public ReadOnlySpan<SceneDamageRect> ScaleDirtyRectsToFramebuffer(ReadOnlySpan<SceneDamageRect> dirtyRects, WindowSurfaceFrameTarget target)
    {
        return FramebufferDirtyRectScaler.Scale(dirtyRects, target.LogicalSize, target.FramebufferSize, physicalDirtyRectBuffer);
    }

    public ReadOnlySpan<SceneDamageRect> FullFrameDirtyRect(WindowSurfaceFrameTarget target)
    {
        physicalDirtyRectBuffer.Clear();
        physicalDirtyRectBuffer.Add(new SceneDamageRect(0, 0, Math.Max(1, target.FramebufferSize.X), Math.Max(1, target.FramebufferSize.Y)));
        return physicalDirtyRectBuffer.WrittenSpan;
    }

    public void Present(ReadOnlySpan<SceneDamageRect> physicalDirtyRects)
    {
        surface?.Present(physicalDirtyRects);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        surface?.Dispose();
        surface = null;
        physicalDirtyRectBuffer.Dispose();
    }

    private static Vector2D<int> GetSafeSize(Vector2D<int> size)
    {
        return new Vector2D<int>(Math.Max(1, size.X), Math.Max(1, size.Y));
    }
}

internal readonly record struct WindowSurfaceFrameTarget(Vector2D<int> LogicalSize, Vector2D<int> FramebufferSize);
