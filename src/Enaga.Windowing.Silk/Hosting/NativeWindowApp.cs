using Enaga.Input;
using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Silk.NET.Input.Glfw;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using SkiaSharp;
using SilkGlfw = Silk.NET.GLFW.Glfw;

namespace Enaga.Hosting;

public sealed class NativeWindowApp : IDisposable
{
    private const double DefaultFramesPerSecond = 60;
    private const double IdleFramesPerSecond = 0;
    private const int ResizeSettlePresentFrames = 6;
    private readonly double activeFramesPerSecond;
    private readonly RenderGraphicsBackend graphicsBackend;
    private readonly INativeWindowPlatformIntegration? platformIntegration;
    private readonly IRenderRoot renderRoot;
    private readonly WindowRenderDiagnosticsCollector diagnosticsCollector;
    private readonly SkiaWindowSurfaceManager surfaceManager;
    private readonly TimeProvider timeProvider;
    private readonly long startTimestamp;
    private readonly NativeWindowLoop windowLoop;
    private readonly IRenderWakeSource? renderWakeSource;
    private readonly SilkGlfw? glfwApi;
    private readonly ManualResetEventSlim wakeSignal = new(false);
    private readonly IWindow window;
    private bool disposed;
    private bool initialized;
    private volatile bool hostUpdatePending = true;
    private bool renderingFrame;
    private bool startupFramePending = true;
    private bool startupFrameReadyToShow;
    private int resizeSettlePresentFramesRemaining;
    private SilkWindowInputRouter? inputRouter;
    private SKColor? startupClearColor;

    public NativeWindowApp(
        IRenderRoot renderRoot,
        string? windowTitle = null,
        INativeWindowPlatformIntegration? platformIntegration = null,
        double framesPerSecond = DefaultFramesPerSecond,
        RenderTraceLogFlags traceLogFlags = RenderTraceLogFlags.None,
        RenderGraphicsBackend graphicsBackend = RenderGraphicsBackend.OpenGl,
        SKColor? startupClearColor = null
    )
        : this(
            renderRoot,
            NativeWindowOptions.FromCompatibility(
                windowTitle,
                platformIntegration,
                framesPerSecond,
                traceLogFlags,
                graphicsBackend,
                startupClearColor
            )
        ) { }

    public NativeWindowApp(IRenderRoot renderRoot, NativeWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        activeFramesPerSecond = options.FramesPerSecond;
        graphicsBackend = options.GraphicsBackend;
        timeProvider =
            options.TimeProvider ?? throw new ArgumentNullException(nameof(options.TimeProvider));
        startTimestamp = timeProvider.GetTimestamp();
        this.renderRoot = renderRoot;
        renderWakeSource = renderRoot as IRenderWakeSource;
        platformIntegration = options.PlatformIntegration;
        startupClearColor = options.StartupClearColor;
        diagnosticsCollector = new WindowRenderDiagnosticsCollector(
            options.TraceLogFlags,
            options.Diagnostics
        );
        var silkWindowOptions = options.ToSilkWindowOptions();
        GlfwWindowing.Use();
        GlfwInput.RegisterPlatform();

        window = Window.Create(silkWindowOptions);
        surfaceManager = new SkiaWindowSurfaceManager(window, graphicsBackend, timeProvider);
        glfwApi = SilkGlfw.GetApi();
        windowLoop = new NativeWindowLoop(
            window,
            glfwApi,
            activeFramesPerSecond,
            () => hostUpdatePending || platformIntegration?.HasPendingInput == true,
            wakeSignal,
            timeProvider
        );
        window.Load += OnLoad;
        window.Render += OnRender;
        window.Resize += OnResize;
        window.FramebufferResize += OnFramebufferResize;
        window.Closing += OnClosing;
        if (renderWakeSource is not null)
            renderWakeSource.RenderWakeRequested += OnRenderWakeRequested;
    }

    public static NativeWindowApp Create(
        IRenderRoot renderRoot,
        string? windowTitle = null,
        INativeWindowPlatformIntegration? platformIntegration = null,
        double framesPerSecond = DefaultFramesPerSecond,
        RenderTraceLogFlags traceLogFlags = RenderTraceLogFlags.None,
        RenderGraphicsBackend graphicsBackend = RenderGraphicsBackend.OpenGl,
        SKColor? startupClearColor = null
    )
    {
        return new NativeWindowApp(
            renderRoot,
            windowTitle,
            platformIntegration,
            framesPerSecond,
            traceLogFlags,
            graphicsBackend,
            startupClearColor
        );
    }

    public static NativeWindowApp Create(IRenderRoot renderRoot, NativeWindowOptions options)
    {
        return new NativeWindowApp(renderRoot, options);
    }

    public void Run()
    {
        initialized = true;
        windowLoop.Run();
    }

    public void Tick()
    {
        if (disposed)
            return;

        if (!initialized)
        {
            initialized = true;
            window.Initialize();
        }

        if (window.IsClosing)
            return;

        window.DoEvents();
        window.DoUpdate();
        window.DoRender();
    }

    public void MoveAndResize(Vector2D<int> position, Vector2D<int> size)
    {
        window.Position = position;
        window.Size = size;
    }

    public bool HitTestOverlayInput(float x, float y)
    {
        return renderRoot is IOverlayInputHitTestSource hitTestSource
            && hitTestSource.HitTestOverlayInput(x, y);
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic = false)
    {
        if (renderRoot is not IInputSink inputSink)
            return;

        inputSink.PointerMove(x, y, buttons, synthetic);
        RequestHostUpdate();
    }

    public void PointerDown(int button, int buttons, bool synthetic = false)
    {
        if (renderRoot is not IInputSink inputSink)
            return;

        inputSink.PointerDown(button, buttons, synthetic);
        RequestHostUpdate();
    }

    public void PointerUp(int button, int buttons, bool synthetic = false)
    {
        if (renderRoot is not IInputSink inputSink)
            return;

        inputSink.PointerUp(button, buttons, synthetic);
        RequestHostUpdate();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        ReleaseGraphicsResources();
        if (renderWakeSource is not null)
            renderWakeSource.RenderWakeRequested -= OnRenderWakeRequested;
        if (renderRoot is IDisposable disposableRenderRoot)
            disposableRenderRoot.Dispose();
        window.Dispose();
        windowLoop.Dispose();
        glfwApi?.Dispose();
        wakeSignal.Dispose();
    }

    private void OnLoad()
    {
        platformIntegration?.Attach(window, renderRoot);
        surfaceManager.Initialize();
        ApplyRenderCadence(activeFramesPerSecond);
        hostUpdatePending = true;
        RequestInteractiveBurst();
        RequestResizeSettlePresent();
        inputRouter = new SilkWindowInputRouter(renderRoot, platformIntegration, RequestHostUpdate);
        inputRouter.Attach(window);
    }

    private void OnRender(double _)
    {
        RenderFrame(processStartupFrame: true);
    }

    private void RenderFrame(bool processStartupFrame)
    {
        if (startupFramePending)
        {
            if (!processStartupFrame)
                return;

            startupFramePending = false;
            startupFrameReadyToShow = true;
            return;
        }

        if (processStartupFrame && startupFrameReadyToShow)
        {
            if (startupClearColor.HasValue)
            {
                window.IsVisible = true;
                surfaceManager.PresentStartupFrame(startupClearColor.Value);
                startupFrameReadyToShow = false;
                RequestInteractiveBurst();
                RequestResizeSettlePresent();
                return;
            }
        }

        if (ShouldSkipIdleFrame())
        {
            ApplyRenderCadence(IdleFramesPerSecond);
            return;
        }

        ApplyRenderCadence(activeFramesPerSecond);

        var frameStartTimestamp = timeProvider.GetTimestamp();

        if (!surfaceManager.HasSurface)
            return;

        if (renderingFrame)
            return;

        renderingFrame = true;
        try
        {
            var frameTarget = surfaceManager.CaptureFrameTarget();
            InvalidateRenderSurfaceIfResized(surfaceManager.ResizeToFrameTarget(frameTarget));
            var presentFullFrame = resizeSettlePresentFramesRemaining > 0;
            platformIntegration?.OnBeforeRender();
            surfaceManager.RenderIntoCanvas(renderRoot, GetElapsed(), frameTarget);
            ReadOnlySpan<SceneDamageRect> dirtyRects = renderRoot
                is IRenderDirtyRectSource dirtyRectSource
                ? dirtyRectSource.GetLastDirtyRects()
                : ReadOnlySpan<SceneDamageRect>.Empty;
            platformIntegration?.OnRendered();
            var frameTargetStillCurrent = surfaceManager.IsCurrentFrameTarget(frameTarget);
            if (!frameTargetStillCurrent)
            {
                hostUpdatePending = true;
                RequestResizeSettlePresent();
                windowLoop.RequestImmediateFrame();
                WakeWindowEventLoop();
                RecordRenderDiagnostics(
                    timeProvider.GetElapsedTime(frameStartTimestamp).TotalMilliseconds,
                    surfaceManager.LastDiagnostics
                );
                return;
            }
            var physicalDirtyRects = presentFullFrame
                ? surfaceManager.FullFrameDirtyRect(frameTarget)
                : surfaceManager.ScaleDirtyRectsToFramebuffer(dirtyRects, frameTarget);
            hostUpdatePending = false;
            if (physicalDirtyRects.IsEmpty)
            {
                if (surfaceManager.RequiresPresentOnRenderWithoutDamage)
                    surfaceManager.Present(physicalDirtyRects);

                ApplyRenderCadence(IdleFramesPerSecond);
                RecordRenderDiagnostics(
                    timeProvider.GetElapsedTime(frameStartTimestamp).TotalMilliseconds,
                    surfaceManager.LastDiagnostics
                );
                return;
            }

            window.IsVisible = true;
            surfaceManager.Present(physicalDirtyRects);
            if (resizeSettlePresentFramesRemaining > 0)
                resizeSettlePresentFramesRemaining--;
            RecordRenderDiagnostics(
                timeProvider.GetElapsedTime(frameStartTimestamp).TotalMilliseconds,
                surfaceManager.LastDiagnostics
            );
            if (ShouldSkipIdleFrame())
                ApplyRenderCadence(IdleFramesPerSecond);
        }
        finally
        {
            renderingFrame = false;
        }
    }

    private void OnResize(Vector2D<int> size)
    {
        hostUpdatePending = true;
        RequestInteractiveBurst();
        RequestResizeSettlePresent();
        if (graphicsBackend != RenderGraphicsBackend.Vulkan)
            InvalidateRenderSurfaceIfResized(ResizeSurfaceToCurrentFramebuffer());

        TryRenderLiveResizeFrame();
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        hostUpdatePending = true;
        RequestInteractiveBurst();
        RequestResizeSettlePresent();
        if (graphicsBackend != RenderGraphicsBackend.Vulkan)
            InvalidateRenderSurfaceIfResized(surfaceManager.Resize(size));

        TryRenderLiveResizeFrame();
    }

    private void OnClosing()
    {
        ReleaseGraphicsResources();
    }

    private void ReleaseGraphicsResources()
    {
        if (renderRoot is IRenderGpuContextSink gpuContextSink)
            gpuContextSink.SetRenderGpuContext(null);
        surfaceManager.Dispose();
        platformIntegration?.Dispose();
        inputRouter?.Dispose();
        inputRouter = null;
    }

    private bool ShouldSkipIdleFrame()
    {
        if (hostUpdatePending || renderRoot is not IRenderDiagnosticsProvider diagnosticsProvider)
            return false;

        if (platformIntegration?.HasPendingInput == true)
            return false;

        if (resizeSettlePresentFramesRemaining > 0)
            return false;

        if (inputRouter?.HasHeldKeyboardKeys == true)
            return false;

        var runtimeState = diagnosticsProvider.GetRenderRootDiagnosticsSnapshot().RuntimeState;
        return !runtimeState.ImeOpen
            && !runtimeState.CompositionActive
            && !runtimeState.AnimationEnabled
            && !runtimeState.ShaderAnimationEnabled
            && !runtimeState.RenderInvalidated;
    }

    private void RequestInteractiveBurst()
    {
        ApplyRenderCadence(activeFramesPerSecond);
        wakeSignal.Set();
    }

    private void RequestHostUpdate()
    {
        hostUpdatePending = true;
        RequestInteractiveBurst();
        windowLoop.RequestImmediateFrame();
        WakeWindowEventLoop();
    }

    private void OnRenderWakeRequested()
    {
        hostUpdatePending = true;
        RequestInteractiveBurst();
        windowLoop.RequestImmediateFrame();
        WakeWindowEventLoop();
    }

    private void ApplyRenderCadence(double framesPerSecond)
    {
        windowLoop.RequestCadence(framesPerSecond);
    }

    private void WakeWindowEventLoop()
    {
        glfwApi?.PostEmptyEvent();
    }

    private void RequestResizeSettlePresent()
    {
        resizeSettlePresentFramesRemaining = ResizeSettlePresentFrames;
    }

    private void TryRenderLiveResizeFrame()
    {
        if (!surfaceManager.HasSurface || renderingFrame || window.IsClosing)
        {
            return;
        }

        var nowMs = windowLoop.ElapsedMs;
        if (graphicsBackend == RenderGraphicsBackend.OpenGl)
        {
            window.DoRender();
        }
        else
        {
            RenderFrame(processStartupFrame: false);
        }

        windowLoop.ScheduleFrameNoLaterThan(nowMs);
    }

    private bool ResizeSurfaceToCurrentFramebuffer()
    {
        return surfaceManager.ResizeToCurrentFramebuffer();
    }

    private void InvalidateRenderSurfaceIfResized(bool resized)
    {
        if (resized && renderRoot is IRenderSurfaceInvalidationSink invalidationSink)
            invalidationSink.InvalidatePresentationSurface();
    }

    private void RecordRenderDiagnostics(
        double totalFrameMs,
        PresentDiagnosticsSnapshot presentDiagnostics
    )
    {
        if (renderRoot is not IRenderDiagnosticsProvider diagnosticsProvider)
            return;

        var rootDiagnostics = diagnosticsProvider.GetRenderRootDiagnosticsSnapshot();
        diagnosticsCollector.Record(
            GetElapsed().TotalMilliseconds,
            totalFrameMs,
            rootDiagnostics,
            presentDiagnostics
        );
    }

    private TimeSpan GetElapsed() => timeProvider.GetElapsedTime(startTimestamp);
}
