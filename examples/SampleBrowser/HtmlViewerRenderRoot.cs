using Enaga.Input;
using Enaga.Rendering;
using Enaga.Rendering.Skia;
using SkiaSharp;

namespace SampleBrowser;

internal sealed class SampleBrowserRenderRoot : IRenderRoot, IRenderGpuContextSink, IRenderSurfaceInvalidationSink, IInputSink, IPointerCursorSource, ITextCompositionRangeSink, IOverlayInputHitTestSource, IRenderWakeSource, IRenderDirtyRectSource, IRenderDiagnosticsProvider, IDisposable
{
    private readonly SceneRenderRoot contentRoot;
    private readonly SampleBrowserToolbarSource? toolbarSource;
    private readonly SceneRenderRoot? toolbarRoot;
    private float lastPointerX;
    private float lastPointerY;
    private bool toolbarFocused;
    private SceneDamageRect[] lastDirtyRects = [];
    private RenderRootDiagnosticsSnapshot lastDiagnostics;

    public SampleBrowserRenderRoot(ISceneFrameSource contentSource, SampleBrowserToolbarSource? toolbarSource)
    {
        contentRoot = new SceneRenderRoot(contentSource);
        this.toolbarSource = toolbarSource;
        if (toolbarSource is not null)
            toolbarRoot = new SceneRenderRoot(toolbarSource);
    }

    private int ToolbarHeight => toolbarRoot is null ? 0 : SampleBrowserToolbarSource.Height;

    public PointerCursorKind CurrentCursor
        => toolbarRoot is not null && lastPointerY < ToolbarHeight
            ? toolbarRoot.CurrentCursor
            : contentRoot.CurrentCursor;

    public event Action? RenderWakeRequested
    {
        add
        {
            contentRoot.RenderWakeRequested += value;
            if (toolbarRoot is not null)
                toolbarRoot.RenderWakeRequested += value;
        }
        remove
        {
            contentRoot.RenderWakeRequested -= value;
            if (toolbarRoot is not null)
                toolbarRoot.RenderWakeRequested -= value;
        }
    }

    public void Render(SKCanvas canvas, int width, int height, TimeSpan elapsed)
    {
        var toolbarHeight = Math.Min(ToolbarHeight, Math.Max(0, height));
        var contentHeight = Math.Max(1, height - toolbarHeight);
        canvas.Save();
        canvas.ClipRect(new SKRect(0, toolbarHeight, width, height));
        canvas.Translate(0, toolbarHeight);
        contentRoot.Render(canvas, width, contentHeight, elapsed);
        canvas.Restore();

        if (toolbarRoot is not null)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, width, toolbarHeight));
            toolbarRoot.Render(canvas, width, toolbarHeight, elapsed);
            canvas.Restore();
        }

        CaptureDirtyRects(toolbarHeight);
        CaptureDiagnostics(width, height);
    }

    public void SetRenderGpuContext(GRContext? context)
    {
        contentRoot.SetRenderGpuContext(context);
        toolbarRoot?.SetRenderGpuContext(context);
    }

    public void InvalidatePresentationSurface()
    {
        contentRoot.InvalidatePresentationSurface();
        toolbarRoot?.InvalidatePresentationSurface();
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic)
    {
        lastPointerX = x;
        lastPointerY = y;
        if (toolbarRoot is not null && y < ToolbarHeight)
        {
            toolbarRoot.PointerMove(x, y, buttons, synthetic);
            return;
        }

        contentRoot.PointerMove(x, y - ToolbarHeight, buttons, synthetic);
    }

    public void PointerDown(int button, int buttons, bool synthetic)
    {
        if (toolbarRoot is not null && lastPointerY < ToolbarHeight)
        {
            toolbarFocused = true;
            toolbarRoot.PointerDown(button, buttons, synthetic);
            return;
        }

        toolbarFocused = false;
        toolbarSource?.ClearUrlFocus();
        contentRoot.PointerDown(button, buttons, synthetic);
    }

    public void PointerUp(int button, int buttons, bool synthetic)
    {
        if (toolbarFocused && toolbarRoot is not null)
        {
            toolbarRoot.PointerUp(button, buttons, synthetic);
            return;
        }

        contentRoot.PointerUp(button, buttons, synthetic);
    }

    public void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0)
        => contentRoot.Wheel(deltaX, deltaY, synthetic, modifiers);

    public void KeyDown(string key, int modifiers, bool repeat, bool synthetic)
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.KeyDown(key, modifiers, repeat, synthetic);
        else
            contentRoot.KeyDown(key, modifiers, repeat, synthetic);
    }

    public void KeyUp(string key, int modifiers, bool synthetic)
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.KeyUp(key, modifiers, synthetic);
        else
            contentRoot.KeyUp(key, modifiers, synthetic);
    }

    public void TextInput(string text, bool synthetic)
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.TextInput(text, synthetic);
        else
            contentRoot.TextInput(text, synthetic);
    }

    public void StartTextComposition()
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.StartTextComposition();
        else
            contentRoot.StartTextComposition();
    }

    public void StartTextComposition(int startIndex)
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.StartTextComposition(startIndex);
        else
            contentRoot.StartTextComposition(startIndex);
    }

    public void UpdateTextComposition(string text, int cursorPosition)
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.UpdateTextComposition(text, cursorPosition);
        else
            contentRoot.UpdateTextComposition(text, cursorPosition);
    }

    public void UpdateTextComposition(string text, int cursorPosition, int selectionStart, int selectionLength)
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.UpdateTextComposition(text, cursorPosition, selectionStart, selectionLength);
        else
            contentRoot.UpdateTextComposition(text, cursorPosition, selectionStart, selectionLength);
    }

    public void EndTextComposition()
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.EndTextComposition();
        else
            contentRoot.EndTextComposition();
    }

    public void PrepareTextCompositionCommit()
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.PrepareTextCompositionCommit();
        else
            contentRoot.PrepareTextCompositionCommit();
    }

    public void UpdateImeState(bool isOpen, string indicator)
    {
        if (toolbarFocused && toolbarRoot is not null)
            toolbarRoot.UpdateImeState(isOpen, indicator);
        else
            contentRoot.UpdateImeState(isOpen, indicator);
    }

    public bool TryGetTextCompositionCursor(out TextCompositionCursor cursor)
    {
        if (toolbarFocused && toolbarRoot is not null)
            return toolbarRoot.TryGetTextCompositionCursor(out cursor);

        if (contentRoot.TryGetTextCompositionCursor(out cursor))
        {
            cursor = cursor with { Y = cursor.Y + ToolbarHeight };
            return true;
        }

        return false;
    }

    public bool HitTestOverlayInput(float x, float y)
        => toolbarRoot is not null && y < ToolbarHeight ||
           contentRoot.HitTestOverlayInput(x, y - ToolbarHeight);

    public ReadOnlySpan<SceneDamageRect> GetLastDirtyRects()
        => lastDirtyRects;

    public RenderRootDiagnosticsSnapshot GetRenderRootDiagnosticsSnapshot()
        => lastDiagnostics;

    public void Dispose()
    {
        contentRoot.Dispose();
        toolbarRoot?.Dispose();
    }

    private void CaptureDirtyRects(int toolbarHeight)
    {
        var dirtyRects = new List<SceneDamageRect>();
        foreach (var rect in contentRoot.GetLastDirtyRects())
            dirtyRects.Add(new SceneDamageRect(rect.X, rect.Y + toolbarHeight, rect.Width, rect.Height));

        if (toolbarRoot is not null)
        {
            foreach (var rect in toolbarRoot.GetLastDirtyRects())
                dirtyRects.Add(rect);
        }

        lastDirtyRects = dirtyRects.ToArray();
    }

    private void CaptureDiagnostics(int width, int height)
    {
        var contentDiagnostics = contentRoot.GetRenderRootDiagnosticsSnapshot();
        var toolbarDiagnostics = toolbarRoot?.GetRenderRootDiagnosticsSnapshot() ?? default;
        var runtimeState = CombineRuntimeState(contentDiagnostics.RuntimeState, toolbarDiagnostics.RuntimeState);
        var dirtyPixels = 0L;
        foreach (var rect in lastDirtyRects)
            dirtyPixels += rect.PixelCount;

        lastDiagnostics = new RenderRootDiagnosticsSnapshot(
            contentDiagnostics.DiagnosticsEnabled || toolbarDiagnostics.DiagnosticsEnabled,
            contentDiagnostics.SourceFrameMs + toolbarDiagnostics.SourceFrameMs,
            contentDiagnostics.PaintMs + toolbarDiagnostics.PaintMs,
            contentDiagnostics.PaintedFrame || toolbarDiagnostics.PaintedFrame,
            contentDiagnostics.CommitReused && toolbarDiagnostics.CommitReused,
            contentDiagnostics.PictureReused && toolbarDiagnostics.PictureReused,
            lastDirtyRects,
            lastDirtyRects.Length,
            dirtyPixels,
            contentDiagnostics.DamageReasons | toolbarDiagnostics.DamageReasons,
            width,
            height,
            runtimeState);
    }

    private static RenderRuntimeStateSnapshot CombineRuntimeState(RenderRuntimeStateSnapshot content, RenderRuntimeStateSnapshot toolbar)
        => new(
            content.ImeOpen || toolbar.ImeOpen,
            content.CompositionActive || toolbar.CompositionActive,
            content.AnimationEnabled || toolbar.AnimationEnabled,
            content.ShaderAnimationEnabled || toolbar.ShaderAnimationEnabled,
            content.RenderInvalidated || toolbar.RenderInvalidated,
            content.ViewCallCount + toolbar.ViewCallCount);
}
