using Enaga.Hosting;
using Enaga.Rendering;

namespace Enaga.React.OkojoRuntime;

internal sealed class ShaderTraceLogger(IRuntimeDiagnosticsSink diagnostics)
{
    private const double IntervalMs = 1000;
    private const string SourceName = nameof(OkojoNodeReactHost);

    private int observedFrames;
    private int shaderOnlyFrames;
    private int fullRenderFrames;
    private int noDamageFrames;
    private int shaderDirtyRectCount;
    private long shaderDirtyPixels;
    private SceneDamageReason fullRenderReasons;
    private double nextTraceAtMs = IntervalMs;

    public void ObserveFrame(bool shaderAnimationEnabled)
    {
        if (!IsEnabled() || !shaderAnimationEnabled)
            return;

        observedFrames++;
    }

    public void RecordShaderOnly(ReadOnlySpan<SceneDamageRect> dirtyRects, bool shaderAnimationEnabled)
    {
        if (!IsEnabled() || !shaderAnimationEnabled || dirtyRects.Length == 0)
            return;

        shaderOnlyFrames++;
        shaderDirtyRectCount += dirtyRects.Length;
        foreach (var rect in dirtyRects)
            shaderDirtyPixels += rect.PixelCount;
    }

    public void RecordNoDamage(bool shaderAnimationEnabled)
    {
        if (!IsEnabled() || !shaderAnimationEnabled)
            return;

        noDamageFrames++;
    }

    public void RecordFullRender(SceneDamageReason damageReasons, bool shaderAnimationEnabled)
    {
        if (!IsEnabled() || !shaderAnimationEnabled)
            return;

        fullRenderFrames++;
        fullRenderReasons |= damageReasons;
    }

    public void FlushIfDue(double elapsedMs, int frameCount, bool shaderAnimationEnabled, bool renderInvalidated)
    {
        if (!IsEnabled() || elapsedMs < nextTraceAtMs)
            return;

        diagnostics.Write(new RuntimeDiagnosticEvent(
            RuntimeDiagnosticArea.ShaderTrace,
            SourceName,
            $"frame={frameCount} " +
            $"elapsed={elapsedMs:F1}ms " +
            $"enabled={shaderAnimationEnabled} " +
            $"invalidated={renderInvalidated} " +
            $"observed={observedFrames} " +
            $"shaderOnly={shaderOnlyFrames} " +
            $"fullRender={fullRenderFrames} " +
            $"noDamage={noDamageFrames} " +
            $"dirtyRects={shaderDirtyRectCount} " +
            $"dirtyPixels={shaderDirtyPixels} " +
            $"fullRenderReasons={fullRenderReasons}"));

        observedFrames = 0;
        shaderOnlyFrames = 0;
        fullRenderFrames = 0;
        noDamageFrames = 0;
        shaderDirtyRectCount = 0;
        shaderDirtyPixels = 0;
        fullRenderReasons = SceneDamageReason.None;
        nextTraceAtMs += IntervalMs;
        if (elapsedMs >= nextTraceAtMs)
            nextTraceAtMs = elapsedMs + IntervalMs;
    }

    private bool IsEnabled()
    {
        return diagnostics.IsEnabled(RuntimeDiagnosticArea.ShaderTrace);
    }
}
