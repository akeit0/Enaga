using Silk.NET.Maths;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace Enaga.Rendering;

internal sealed class OpenGlSkiaWindowSurface : ISkiaWindowSurface
{
    private readonly GL gl;
    private readonly TimeProvider timeProvider;
    private uint contentColorTexture;
    private uint contentFramebuffer;
    private uint contentDepthStencilRenderbuffer;
    private GRBackendRenderTarget? contentBackendRenderTarget;
    private GRBackendRenderTarget? windowBackendRenderTarget;
    private GRContext? context;
    private SKSurface? contentSurface;
    private SKSurface? windowSurface;
    private int contentHeight;
    private int contentWidth;
    private int height;
    private int width;
    private bool contentStorageResized;

    public OpenGlSkiaWindowSurface(GL gl, TimeProvider? timeProvider = null)
    {
        this.gl = gl;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public SKCanvas Canvas => contentSurface?.Canvas ?? throw new InvalidOperationException("GPU Skia surface is not initialized.");

    public GRContext? Context => context;

    public bool RequiresPresentOnRenderWithoutDamage => true;

    public PresentDiagnosticsSnapshot LastDiagnostics { get; private set; }

    public void Initialize(Vector2D<int> size)
    {
        context ??= GRContext.CreateGl();
        Resize(size);
    }

    public bool Resize(Vector2D<int> size)
    {
        gl.Viewport(size);
        return RecreateSurfaces(size.X, size.Y);
    }

    public void Present(ReadOnlySpan<SceneDamageRect> dirtyRects = default)
    {
        var startTimestamp = timeProvider.GetTimestamp();
        if (contentSurface is null || windowSurface is null)
            return;

        var canvas = windowSurface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.ClipRect(SKRect.Create(0, 0, width, height));
        canvas.DrawSurface(contentSurface, 0, 0, null);
        canvas.Restore();
        context?.Flush();
        LastDiagnostics = new PresentDiagnosticsSnapshot(
            timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds,
            0,
            0,
            dirtyRects.Length,
            contentStorageResized,
            width,
            height);
        contentStorageResized = false;
    }

    public void Dispose()
    {
        contentSurface?.Dispose();
        windowSurface?.Dispose();
        contentBackendRenderTarget?.Dispose();
        windowBackendRenderTarget?.Dispose();
        ReleaseContentFramebufferAttachments();
        context?.Dispose();
    }

    private unsafe bool RecreateSurfaces(int width, int height)
    {
        var safeWidth = Math.Max(1, width);
        var safeHeight = Math.Max(1, height);
        this.width = safeWidth;
        this.height = safeHeight;

        var contentRecreated = EnsureContentSurface(safeWidth, safeHeight);
        contentStorageResized |= contentRecreated;
        RecreateWindowSurface(safeWidth, safeHeight);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        return contentRecreated;
    }

    private unsafe bool EnsureContentSurface(int requiredWidth, int requiredHeight)
    {
        if (contentSurface is not null &&
            requiredWidth <= contentWidth &&
            requiredHeight <= contentHeight)
        {
            return false;
        }

        contentSurface?.Dispose();
        contentBackendRenderTarget?.Dispose();
        ReleaseContentFramebufferAttachments();

        contentWidth = GrowContentExtent(requiredWidth);
        contentHeight = GrowContentExtent(requiredHeight);

        contentColorTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, contentColorTexture);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            (int)InternalFormat.Rgba8,
            (uint)contentWidth,
            (uint)contentHeight,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            null);

        contentFramebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, contentFramebuffer);
        gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            contentColorTexture,
            0);

        contentDepthStencilRenderbuffer = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, contentDepthStencilRenderbuffer);
        gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer,
            InternalFormat.Depth24Stencil8,
            (uint)contentWidth,
            (uint)contentHeight);
        gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer,
            contentDepthStencilRenderbuffer);

        contentBackendRenderTarget = new GRBackendRenderTarget(
            contentWidth,
            contentHeight,
            0,
            8,
            new GRGlFramebufferInfo(contentFramebuffer, (uint)InternalFormat.Rgba8));
        contentSurface = SKSurface.Create(
            context!,
            contentBackendRenderTarget,
            GRSurfaceOrigin.TopLeft,
            SKColorType.Rgba8888);

        contentSurface.Canvas.Clear(SKColors.Transparent);
        return true;
    }

    private unsafe void RecreateWindowSurface(int safeWidth, int safeHeight)
    {
        windowSurface?.Dispose();
        windowBackendRenderTarget?.Dispose();

        windowBackendRenderTarget = new GRBackendRenderTarget(
            safeWidth,
            safeHeight,
            0,
            8,
            new GRGlFramebufferInfo(0, (uint)InternalFormat.Rgba8));
        windowSurface = SKSurface.Create(
            context!,
            windowBackendRenderTarget,
            GRSurfaceOrigin.BottomLeft,
            SKColorType.Rgba8888);
    }

    private static int GrowContentExtent(int required)
    {
        var minimum = Math.Max(1, required);
        var grown = Math.Max(minimum, (int)MathF.Ceiling(minimum * 1.25f));
        return Math.Max(minimum, AlignUp(grown, 64));
    }

    private static int AlignUp(int value, int alignment)
        => ((value + alignment - 1) / alignment) * alignment;

    private void ReleaseContentFramebufferAttachments()
    {
        if (contentDepthStencilRenderbuffer != 0)
        {
            gl.DeleteRenderbuffer(contentDepthStencilRenderbuffer);
            contentDepthStencilRenderbuffer = 0;
        }

        if (contentFramebuffer != 0)
        {
            gl.DeleteFramebuffer(contentFramebuffer);
            contentFramebuffer = 0;
        }

        if (contentColorTexture != 0)
        {
            gl.DeleteTexture(contentColorTexture);
            contentColorTexture = 0;
        }
    }

}
