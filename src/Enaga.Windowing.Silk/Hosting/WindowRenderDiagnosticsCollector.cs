using Enaga.Rendering;
using Enaga.Rendering.Skia;

namespace Enaga.Hosting;

internal sealed class WindowRenderDiagnosticsCollector
{
    private const string SourceName = nameof(NativeWindowApp);
    private const double LogIntervalMs = 1000;

    private readonly RenderTraceLogFlags traceLogFlags;
    private readonly IRuntimeDiagnosticsSink diagnostics;
    private int sampleCount;
    private int commitReuseCount;
    private int pictureReuseCount;
    private int textureResizeCount;
    private double windowStartMs = double.NaN;
    private double totalFrameMs;
    private double sourceFrameMs;
    private double paintMs;
    private double presentMs;
    private int dirtyRectCount;
    private int viewCallCount;
    private long uploadBytes;
    private long dirtyPixels;
    private long uploadPixels;
    private int uploadRectCount;
    private SceneDamageReason damageReasons;

    public WindowRenderDiagnosticsCollector(RenderTraceLogFlags traceLogFlags, IRuntimeDiagnosticsSink diagnostics)
    {
        this.traceLogFlags = traceLogFlags;
        this.diagnostics = diagnostics ?? RuntimeDiagnosticsSink.None;
    }

    public void Record(double nowMs, double totalFrameMs, RenderRootDiagnosticsSnapshot rootDiagnostics, PresentDiagnosticsSnapshot presentDiagnostics)
    {
        if (!rootDiagnostics.DiagnosticsEnabled)
            return;
        if (double.IsNaN(windowStartMs))
            windowStartMs = nowMs;

        sampleCount++;
        this.totalFrameMs += totalFrameMs;
        sourceFrameMs += rootDiagnostics.SourceFrameMs;
        paintMs += rootDiagnostics.PaintMs;
        presentMs += presentDiagnostics.PresentMs;
        dirtyRectCount += rootDiagnostics.DirtyRectCount;
        viewCallCount += rootDiagnostics.RuntimeState.ViewCallCount;
        dirtyPixels += rootDiagnostics.DirtyPixels;
        uploadBytes += presentDiagnostics.UploadBytes;
        uploadPixels += presentDiagnostics.UploadPixels;
        uploadRectCount += presentDiagnostics.UploadRectCount;
        damageReasons |= rootDiagnostics.DamageReasons;
        if (rootDiagnostics.CommitReused)
            commitReuseCount++;
        if (rootDiagnostics.PictureReused)
            pictureReuseCount++;
        if (presentDiagnostics.TextureStorageResized)
            textureResizeCount++;

        var windowDurationMs = nowMs - windowStartMs;
        if (windowDurationMs < LogIntervalMs)
            return;

        var framesPerSecond = sampleCount * 1000d / Math.Max(1d, windowDurationMs);
        Flush(rootDiagnostics, presentDiagnostics, Math.Max(1, sampleCount), framesPerSecond);
        Reset(nowMs);
    }

    private void Flush(RenderRootDiagnosticsSnapshot rootDiagnostics, PresentDiagnosticsSnapshot presentDiagnostics, int safeSampleCount, double framesPerSecond)
    {
        if (traceLogFlags.HasFlag(RenderTraceLogFlags.Paint))
        {
            diagnostics.Write(new RuntimeDiagnosticEvent(
                RuntimeDiagnosticArea.Rendering,
                SourceName,
                $"fps={framesPerSecond:F1} total={totalFrameMs / safeSampleCount:F2}ms source={sourceFrameMs / safeSampleCount:F2}ms paint={paintMs / safeSampleCount:F2}ms present={presentMs / safeSampleCount:F2}ms commitReuse={(double)commitReuseCount / safeSampleCount:P0} pictureReuse={(double)pictureReuseCount / safeSampleCount:P0}"));
        }

        if (traceLogFlags.HasFlag(RenderTraceLogFlags.ViewPerFrame))
        {
            diagnostics.Write(new RuntimeDiagnosticEvent(
                RuntimeDiagnosticArea.Rendering,
                SourceName,
                $"views/frame={(double)viewCallCount / safeSampleCount:F1}"));
        }

        if (traceLogFlags.HasFlag(RenderTraceLogFlags.Damage) &&
            (damageReasons != SceneDamageReason.None ||
             dirtyRectCount > 0 ||
             dirtyPixels > 0 ||
             uploadBytes > 0 ||
             uploadRectCount > 0 ||
             presentDiagnostics.TextureStorageResized))
        {
            diagnostics.Write(new RuntimeDiagnosticEvent(
                RuntimeDiagnosticArea.Damage,
                SourceName,
                $"damage={damageReasons} dirtyRects/frame={(double)dirtyRectCount / safeSampleCount:F1} dirtyPixels/frame={dirtyPixels / safeSampleCount} upload={FormatUploadBytes(uploadBytes, safeSampleCount)} rects/frame={(double)uploadRectCount / safeSampleCount:F1} pixels/frame={uploadPixels / safeSampleCount} textureResize={textureResizeCount}"));
        }

        if (traceLogFlags.HasFlag(RenderTraceLogFlags.Runtime) &&
            (rootDiagnostics.RuntimeState.ImeOpen ||
             rootDiagnostics.RuntimeState.CompositionActive ||
             rootDiagnostics.RuntimeState.AnimationEnabled ||
             rootDiagnostics.RuntimeState.ShaderAnimationEnabled ||
             rootDiagnostics.RuntimeState.RenderInvalidated))
        {
            diagnostics.Write(new RuntimeDiagnosticEvent(
                RuntimeDiagnosticArea.Rendering,
                SourceName,
                $"ime={rootDiagnostics.RuntimeState.ImeOpen} composition={rootDiagnostics.RuntimeState.CompositionActive} anim={rootDiagnostics.RuntimeState.AnimationEnabled} shaderAnim={rootDiagnostics.RuntimeState.ShaderAnimationEnabled} invalidated={rootDiagnostics.RuntimeState.RenderInvalidated}"));
        }
    }

    private void Reset(double nowMs)
    {
        sampleCount = 0;
        commitReuseCount = 0;
        pictureReuseCount = 0;
        textureResizeCount = 0;
        totalFrameMs = 0;
        sourceFrameMs = 0;
        paintMs = 0;
        presentMs = 0;
        dirtyRectCount = 0;
        viewCallCount = 0;
        dirtyPixels = 0;
        uploadBytes = 0;
        uploadPixels = 0;
        uploadRectCount = 0;
        damageReasons = SceneDamageReason.None;
        windowStartMs = nowMs;
    }

    private static string FormatUploadBytes(long totalUploadBytes, int sampleCount)
    {
        var bytesPerFrame = totalUploadBytes / Math.Max(1, sampleCount);
        if (bytesPerFrame >= 1024 * 1024)
            return $"{bytesPerFrame / (1024d * 1024d):F2} MiB/frame";
        if (bytesPerFrame >= 1024)
            return $"{bytesPerFrame / 1024d:F1} KiB/frame";
        return $"{bytesPerFrame} B/frame";
    }
}
