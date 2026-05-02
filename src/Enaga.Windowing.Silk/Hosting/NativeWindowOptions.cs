using Silk.NET.Maths;
using Silk.NET.Windowing;
using SkiaSharp;

namespace Enaga.Hosting;

public sealed class NativeWindowOptions
{
    public string Title { get; init; } = "Native React Host";

    public Vector2D<int> InitialSize { get; init; } = new(1280, 800);

    public INativeWindowPlatformIntegration? PlatformIntegration { get; init; }

    public double FramesPerSecond { get; init; } = 60;

    public RenderTraceLogFlags TraceLogFlags { get; init; } = RenderTraceLogFlags.None;

    public RenderGraphicsBackend GraphicsBackend { get; init; } = RenderGraphicsBackend.OpenGl;

    public SKColor? StartupClearColor { get; init; }

    public IRuntimeDiagnosticsSink Diagnostics { get; init; } = RuntimeDiagnosticsSink.None;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public WindowBorder WindowBorder { get; init; } = WindowBorder.Resizable;

    public bool TransparentFramebuffer { get; init; }

    public bool TopMost { get; init; }

    internal WindowOptions ToSilkWindowOptions()
    {
        var options = GraphicsBackend == RenderGraphicsBackend.Vulkan
            ? WindowOptions.DefaultVulkan
            : WindowOptions.Default;
        options.Title = string.IsNullOrWhiteSpace(Title) ? "Native React Host" : Title;
        options.Size = InitialSize;
        options.WindowBorder = WindowBorder;
        options.TransparentFramebuffer = TransparentFramebuffer;
        options.TopMost = TopMost;
        if (GraphicsBackend == RenderGraphicsBackend.OpenGl)
            options.API = new(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));
        else if (GraphicsBackend == RenderGraphicsBackend.Metal)
            options.API = new(ContextAPI.None, ContextProfile.Core, ContextFlags.Default, new APIVersion(1, 0));
        options.IsVisible = false;
        options.IsEventDriven = false;
        options.ShouldSwapAutomatically = true;
        options.FramesPerSecond = FramesPerSecond;
        options.UpdatesPerSecond = FramesPerSecond;
        return options;
    }

    internal static NativeWindowOptions FromCompatibility(
        string? windowTitle,
        INativeWindowPlatformIntegration? platformIntegration,
        double framesPerSecond,
        RenderTraceLogFlags traceLogFlags,
        RenderGraphicsBackend graphicsBackend,
        SKColor? startupClearColor)
    {
        return new NativeWindowOptions
        {
            Title = string.IsNullOrWhiteSpace(windowTitle) ? "Native React Host" : windowTitle,
            PlatformIntegration = platformIntegration,
            FramesPerSecond = framesPerSecond,
            TraceLogFlags = traceLogFlags,
            GraphicsBackend = graphicsBackend,
            StartupClearColor = startupClearColor,
            Diagnostics = CreateDiagnostics(traceLogFlags)
        };
    }

    private static IRuntimeDiagnosticsSink CreateDiagnostics(RenderTraceLogFlags traceLogFlags)
    {
        List<RuntimeDiagnosticArea> areas = [];
        if (traceLogFlags.HasFlag(RenderTraceLogFlags.Paint) || traceLogFlags.HasFlag(RenderTraceLogFlags.Runtime))
            areas.Add(RuntimeDiagnosticArea.Rendering);
        if (traceLogFlags.HasFlag(RenderTraceLogFlags.Damage))
            areas.Add(RuntimeDiagnosticArea.Damage);
        if (traceLogFlags.HasFlag(RenderTraceLogFlags.ViewPerFrame))
            areas.Add(RuntimeDiagnosticArea.Rendering);

        return areas.Count == 0
            ? RuntimeDiagnosticsSink.None
            : RuntimeDiagnosticsSink.Console([.. areas]);
    }
}
