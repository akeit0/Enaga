using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SkiaSharp;

namespace Enaga.Rendering;

internal sealed class MetalSkiaWindowSurface : ISkiaWindowSurface
{
    private const ulong MTLPixelFormatBGRA8Unorm = 80;
    private const ulong MTLTextureUsageShaderRead = 1;
    private const ulong MTLTextureUsageRenderTarget = 4;

    private readonly IWindow window;
    private readonly TimeProvider timeProvider;
    private nint device;
    private nint commandQueue;
    private nint metalLayer;
    private nint contentTexture;
    private nint currentDrawable;
    private GRMtlBackendContext? backendContext;
    private GRContext? context;
    private GRBackendRenderTarget? contentBackendRenderTarget;
    private GRBackendRenderTarget? drawableBackendRenderTarget;
    private SKSurface? contentSurface;
    private SKSurface? drawableSurface;
    private int height;
    private int width;

    public MetalSkiaWindowSurface(IWindow window, TimeProvider? timeProvider = null)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The Metal Skia backend is only supported on macOS.");

        this.window = window;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public SKCanvas Canvas
    {
        get
        {
            return contentSurface?.Canvas ?? throw new InvalidOperationException("Metal Skia content surface is not initialized.");
        }
    }

    public GRContext? Context => context;

    public bool RequiresPresentOnRenderWithoutDamage => false;

    public PresentDiagnosticsSnapshot LastDiagnostics { get; private set; }

    public void Initialize(Vector2D<int> size)
    {
        var nsWindow = window.Native?.Cocoa ?? 0;
        if (nsWindow == 0)
            throw new InvalidOperationException("Unable to resolve the native Cocoa window for Metal rendering.");

        device = ObjectiveC.MTLCreateSystemDefaultDevice();
        if (device == 0)
            throw new InvalidOperationException("Unable to create the default Metal device.");

        commandQueue = ObjectiveC.IntPtr_objc_msgSend(device, ObjectiveC.Selectors.NewCommandQueue);
        if (commandQueue == 0)
            throw new InvalidOperationException("Unable to create a Metal command queue.");

        backendContext = new GRMtlBackendContext
        {
            DeviceHandle = device,
            QueueHandle = commandQueue,
        };
        context = GRContext.CreateMetal(backendContext)
            ?? throw new InvalidOperationException("Unable to create a Metal-backed Skia GRContext.");

        AttachMetalLayer(nsWindow);
        Resize(size);
    }

    public bool Resize(Vector2D<int> size)
    {
        width = Math.Max(1, size.X);
        height = Math.Max(1, size.Y);
        ReleaseDrawableSurface();
        ReleaseContentSurface();
        if (metalLayer != 0)
        {
            ObjectiveC.Void_objc_msgSend_Double(metalLayer, ObjectiveC.Selectors.SetContentsScale, GetContentScale());
            ObjectiveC.Void_objc_msgSend_CGSize(metalLayer, ObjectiveC.Selectors.SetDrawableSize, new CGSize(width, height));
        }

        RecreateContentSurface();
        return true;
    }

    public void Present(ReadOnlySpan<SceneDamageRect> dirtyRects = default)
    {
        var startTimestamp = timeProvider.GetTimestamp();
        if (contentSurface is null)
            return;

        EnsureDrawableSurface();
        if (drawableSurface is null)
            return;

        contentSurface.Canvas.Flush();
        var canvas = drawableSurface.Canvas;
        canvas.Clear(SKColors.Black);
        canvas.DrawSurface(contentSurface, 0, 0, null);
        drawableSurface.Canvas.Flush();
        context?.Flush(submit: true, synchronous: false);

        var commandBuffer = ObjectiveC.IntPtr_objc_msgSend(commandQueue, ObjectiveC.Selectors.CommandBuffer);
        if (commandBuffer != 0)
        {
            ObjectiveC.Void_objc_msgSend_IntPtr(commandBuffer, ObjectiveC.Selectors.PresentDrawable, currentDrawable);
            ObjectiveC.Void_objc_msgSend(commandBuffer, ObjectiveC.Selectors.Commit);
        }
        else
        {
            ObjectiveC.Void_objc_msgSend(currentDrawable, ObjectiveC.Selectors.Present);
        }

        ReleaseDrawableSurface();
        LastDiagnostics = new PresentDiagnosticsSnapshot(
            timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds,
            0,
            0,
            dirtyRects.Length,
            false,
            width,
            height);
    }

    public void Dispose()
    {
        ReleaseDrawableSurface();
        ReleaseContentSurface();
        context?.Dispose();
        backendContext?.Dispose();
        if (commandQueue != 0)
        {
            ObjectiveC.Void_objc_msgSend(commandQueue, ObjectiveC.Selectors.Release);
            commandQueue = 0;
        }

        device = 0;
        metalLayer = 0;
    }

    private void AttachMetalLayer(nint nsWindow)
    {
        var contentView = ObjectiveC.IntPtr_objc_msgSend(nsWindow, ObjectiveC.Selectors.ContentView);
        if (contentView == 0)
            throw new InvalidOperationException("Unable to resolve the Cocoa content view for Metal rendering.");

        var layerClass = ObjectiveC.objc_getClass("CAMetalLayer");
        if (layerClass == 0)
            throw new InvalidOperationException("Unable to resolve CAMetalLayer.");

        metalLayer = ObjectiveC.IntPtr_objc_msgSend(ObjectiveC.IntPtr_objc_msgSend(layerClass, ObjectiveC.Selectors.Alloc), ObjectiveC.Selectors.Init);
        if (metalLayer == 0)
            throw new InvalidOperationException("Unable to create a CAMetalLayer.");

        ObjectiveC.Void_objc_msgSend_IntPtr(metalLayer, ObjectiveC.Selectors.SetDevice, device);
        ObjectiveC.Void_objc_msgSend_UInt64(metalLayer, ObjectiveC.Selectors.SetPixelFormat, MTLPixelFormatBGRA8Unorm);
        ObjectiveC.Void_objc_msgSend_Bool(metalLayer, ObjectiveC.Selectors.SetFramebufferOnly, false);
        ObjectiveC.Void_objc_msgSend_Double(metalLayer, ObjectiveC.Selectors.SetContentsScale, GetContentScale());
        ObjectiveC.Void_objc_msgSend_Bool(contentView, ObjectiveC.Selectors.SetWantsLayer, true);
        ObjectiveC.Void_objc_msgSend_IntPtr(contentView, ObjectiveC.Selectors.SetLayer, metalLayer);
    }

    private void RecreateContentSurface()
    {
        if (context is null || device == 0)
            throw new InvalidOperationException("Metal Skia surface is not initialized.");

        var descriptorClass = ObjectiveC.objc_getClass("MTLTextureDescriptor");
        if (descriptorClass == 0)
            throw new InvalidOperationException("Unable to resolve MTLTextureDescriptor.");

        var descriptor = ObjectiveC.IntPtr_objc_msgSend_UInt64_UInt64_UInt64_Bool(
            descriptorClass,
            ObjectiveC.Selectors.Texture2DDescriptorWithPixelFormat,
            MTLPixelFormatBGRA8Unorm,
            (ulong)width,
            (ulong)height,
            false);
        if (descriptor == 0)
            throw new InvalidOperationException("Unable to create a Metal texture descriptor.");

        ObjectiveC.Void_objc_msgSend_UInt64(
            descriptor,
            ObjectiveC.Selectors.SetUsage,
            MTLTextureUsageShaderRead | MTLTextureUsageRenderTarget);

        contentTexture = ObjectiveC.IntPtr_objc_msgSend_IntPtr(device, ObjectiveC.Selectors.NewTextureWithDescriptor, descriptor);
        if (contentTexture == 0)
            throw new InvalidOperationException("Unable to create the Metal content texture.");

        contentBackendRenderTarget = new GRBackendRenderTarget(width, height, new GRMtlTextureInfo(contentTexture));
        contentSurface = SKSurface.Create(
            context,
            contentBackendRenderTarget,
            GRSurfaceOrigin.TopLeft,
            SKColorType.Bgra8888)
            ?? throw new InvalidOperationException("Unable to create a Metal-backed Skia content surface.");
        contentSurface.Canvas.Clear(SKColors.Transparent);
    }

    private void EnsureDrawableSurface()
    {
        if (drawableSurface is not null)
            return;

        if (context is null || metalLayer == 0)
            throw new InvalidOperationException("Metal Skia surface is not initialized.");

        currentDrawable = ObjectiveC.IntPtr_objc_msgSend(metalLayer, ObjectiveC.Selectors.NextDrawable);
        if (currentDrawable == 0)
            throw new InvalidOperationException("Unable to acquire a CAMetalDrawable.");

        var texture = ObjectiveC.IntPtr_objc_msgSend(currentDrawable, ObjectiveC.Selectors.Texture);
        if (texture == 0)
            throw new InvalidOperationException("Unable to acquire the drawable Metal texture.");

        drawableBackendRenderTarget = new GRBackendRenderTarget(width, height, new GRMtlTextureInfo(texture));
        drawableSurface = SKSurface.Create(
            context,
            drawableBackendRenderTarget,
            GRSurfaceOrigin.TopLeft,
            SKColorType.Bgra8888)
            ?? throw new InvalidOperationException("Unable to create a Metal-backed Skia drawable surface.");
    }

    private void ReleaseDrawableSurface()
    {
        drawableSurface?.Dispose();
        drawableSurface = null;
        drawableBackendRenderTarget?.Dispose();
        drawableBackendRenderTarget = null;
        currentDrawable = 0;
    }

    private void ReleaseContentSurface()
    {
        contentSurface?.Dispose();
        contentSurface = null;
        contentBackendRenderTarget?.Dispose();
        contentBackendRenderTarget = null;
        if (contentTexture != 0)
        {
            ObjectiveC.Void_objc_msgSend(contentTexture, ObjectiveC.Selectors.Release);
            contentTexture = 0;
        }
    }

    private double GetContentScale()
    {
        var logicalWidth = Math.Max(1, window.Size.X);
        var logicalHeight = Math.Max(1, window.Size.Y);
        var scaleX = (double)Math.Max(1, window.FramebufferSize.X) / logicalWidth;
        var scaleY = (double)Math.Max(1, window.FramebufferSize.Y) / logicalHeight;
        return Math.Max(1, Math.Max(scaleX, scaleY));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CGSize(double Width, double Height);

    private static class ObjectiveC
    {
        private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
        private const string MetalLibrary = "/System/Library/Frameworks/Metal.framework/Metal";

        [DllImport(MetalLibrary)]
        public static extern nint MTLCreateSystemDefaultDevice();

        [DllImport(ObjCLibrary)]
        public static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(ObjCLibrary)]
        public static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern nint IntPtr_objc_msgSend(nint receiver, nint selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern nint IntPtr_objc_msgSend_IntPtr(nint receiver, nint selector, nint value);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern nint IntPtr_objc_msgSend_UInt64_UInt64_UInt64_Bool(nint receiver, nint selector, ulong value1, ulong value2, ulong value3, [MarshalAs(UnmanagedType.Bool)] bool value4);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend(nint receiver, nint selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend_IntPtr(nint receiver, nint selector, nint value);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend_Bool(nint receiver, nint selector, [MarshalAs(UnmanagedType.Bool)] bool value);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend_UInt64(nint receiver, nint selector, ulong value);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend_Double(nint receiver, nint selector, double value);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend_CGSize(nint receiver, nint selector, CGSize value);

        public static class Selectors
        {
            public static readonly nint Alloc = sel_registerName("alloc");
            public static readonly nint Init = sel_registerName("init");
            public static readonly nint Release = sel_registerName("release");
            public static readonly nint ContentView = sel_registerName("contentView");
            public static readonly nint SetWantsLayer = sel_registerName("setWantsLayer:");
            public static readonly nint SetLayer = sel_registerName("setLayer:");
            public static readonly nint NewCommandQueue = sel_registerName("newCommandQueue");
            public static readonly nint CommandBuffer = sel_registerName("commandBuffer");
            public static readonly nint Commit = sel_registerName("commit");
            public static readonly nint Present = sel_registerName("present");
            public static readonly nint PresentDrawable = sel_registerName("presentDrawable:");
            public static readonly nint SetDevice = sel_registerName("setDevice:");
            public static readonly nint SetPixelFormat = sel_registerName("setPixelFormat:");
            public static readonly nint SetFramebufferOnly = sel_registerName("setFramebufferOnly:");
            public static readonly nint SetContentsScale = sel_registerName("setContentsScale:");
            public static readonly nint SetDrawableSize = sel_registerName("setDrawableSize:");
            public static readonly nint Texture2DDescriptorWithPixelFormat = sel_registerName("texture2DDescriptorWithPixelFormat:width:height:mipmapped:");
            public static readonly nint SetUsage = sel_registerName("setUsage:");
            public static readonly nint NewTextureWithDescriptor = sel_registerName("newTextureWithDescriptor:");
            public static readonly nint NextDrawable = sel_registerName("nextDrawable");
            public static readonly nint Texture = sel_registerName("texture");
        }
    }
}
