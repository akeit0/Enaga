using System.Globalization;
using Enaga.Html;
using Enaga.Input;
using Enaga.Rendering;
using Enaga.Scene;

namespace SampleBrowser;

internal sealed class SampleBrowserDiagnosticsFrameSource :
    ISceneFrameSource,
    IInputSink,
    IPointerCursorSource,
    IRenderWakeSource,
    IRenderViewportScaleController,
    IRuntimeBackendServicesSource,
    ITextCompositionRangeSink,
    IDisposable
{
    private readonly HtmlSceneFrameSource inner;
    private readonly TimeProvider timeProvider;
    private readonly long startTimestamp;
    private float pointerX;
    private float pointerY;
    private int pointerButtons;
    private int lastWidth;
    private int lastHeight;
    private float lastRootScrollY = float.NaN;
    private bool pendingInputLog;

    public SampleBrowserDiagnosticsFrameSource(HtmlSceneFrameSource inner, TimeProvider? timeProvider = null)
    {
        this.inner = inner;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        startTimestamp = this.timeProvider.GetTimestamp();
    }

    public string? LastError => inner.LastError;

    public PointerCursorKind CurrentCursor => inner.CurrentCursor;

    public RuntimeBackendServices BackendServices => inner.BackendServices;

    public float ViewportScale => inner.ViewportScale;

    public event Action? RenderWakeRequested
    {
        add => inner.RenderWakeRequested += value;
        remove => inner.RenderWakeRequested -= value;
    }

    public SceneFrameResult RenderFrame(int width, int height, TimeSpan elapsed)
    {
        var result = inner.RenderFrame(width, height, elapsed);
        var rootScrollY = result.Commit.Layout.TryGetValue(result.Commit.RootId, out var rootBox) ? rootBox.ScrollY : 0;
        var viewportChanged = width != lastWidth || height != lastHeight;
        var scrollChanged = float.IsNaN(lastRootScrollY) || Math.Abs(rootScrollY - lastRootScrollY) > 0.001f;

        var dynamicOverlayCount = result.Commit.DynamicOverlayRootIds.Length;
        if (viewportChanged || scrollChanged || pendingInputLog || dynamicOverlayCount > 0)
        {
            Write(
                "frame " +
                $"viewport={width}x{height} scale={Format(ViewportScale)} " +
                $"rootScroll=({Format(RootBoxOrDefault(result.Commit).ScrollX)},{Format(rootScrollY)}) " +
                $"mouse=({Format(pointerX)},{Format(pointerY)}) buttons={pointerButtons} " +
                $"damage={result.DamageReasons} dirty={result.DirtyRects.Length} overlays={dynamicOverlayCount}");
            WriteHitCandidates(result.Commit, pointerX, pointerY);
        }

        lastWidth = width;
        lastHeight = height;
        lastRootScrollY = rootScrollY;
        pendingInputLog = false;
        return result;
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic)
    {
        pointerX = x;
        pointerY = y;
        pointerButtons = buttons;
        pendingInputLog = true;
        Write($"pointer-move x={Format(x)} y={Format(y)} buttons={buttons} synthetic={synthetic}");
        inner.PointerMove(x, y, buttons, synthetic);
    }

    public void PointerDown(int button, int buttons, bool synthetic)
    {
        pointerButtons = buttons;
        pendingInputLog = true;
        Write($"pointer-down button={button} buttons={buttons} synthetic={synthetic}");
        inner.PointerDown(button, buttons, synthetic);
    }

    public void PointerUp(int button, int buttons, bool synthetic)
    {
        pointerButtons = buttons;
        pendingInputLog = true;
        Write($"pointer-up button={button} buttons={buttons} synthetic={synthetic}");
        inner.PointerUp(button, buttons, synthetic);
    }

    public void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0)
    {
        pendingInputLog = true;
        Write($"wheel dx={Format(deltaX)} dy={Format(deltaY)} modifiers={modifiers} synthetic={synthetic} mouse=({Format(pointerX)},{Format(pointerY)})");
        inner.Wheel(deltaX, deltaY, synthetic, modifiers);
    }

    public void KeyDown(string key, int modifiers, bool repeat, bool synthetic)
    {
        pendingInputLog = true;
        Write($"key-down key={key} modifiers={modifiers} repeat={repeat} synthetic={synthetic}");
        inner.KeyDown(key, modifiers, repeat, synthetic);
    }

    public void KeyUp(string key, int modifiers, bool synthetic)
        => inner.KeyUp(key, modifiers, synthetic);

    public void TextInput(string text, bool synthetic)
        => inner.TextInput(text, synthetic);

    public bool TryStepViewportScale(int direction)
    {
        var oldScale = inner.ViewportScale;
        var changed = inner.TryStepViewportScale(direction);
        if (changed)
        {
            pendingInputLog = true;
            Write($"viewport-scale old={Format(oldScale)} next={Format(inner.ViewportScale)} direction={direction}");
        }

        return changed;
    }

    public bool TryResetViewportScale()
    {
        var oldScale = inner.ViewportScale;
        var changed = inner.TryResetViewportScale();
        if (changed)
        {
            pendingInputLog = true;
            Write($"viewport-scale-reset old={Format(oldScale)} next={Format(inner.ViewportScale)}");
        }

        return changed;
    }

    public void StartTextComposition()
        => inner.StartTextComposition();

    public void StartTextComposition(int startIndex)
        => inner.StartTextComposition(startIndex);

    public void UpdateTextComposition(string text, int cursorPosition)
        => inner.UpdateTextComposition(text, cursorPosition);

    public void UpdateTextComposition(string text, int cursorPosition, int selectionStart, int selectionLength)
        => inner.UpdateTextComposition(text, cursorPosition, selectionStart, selectionLength);

    public void EndTextComposition()
        => inner.EndTextComposition();

    public void PrepareTextCompositionCommit()
        => inner.PrepareTextCompositionCommit();

    public void UpdateImeState(bool isOpen, string indicator)
        => inner.UpdateImeState(isOpen, indicator);

    public bool TryGetTextCompositionCursor(out TextCompositionCursor cursor)
        => inner.TryGetTextCompositionCursor(out cursor);

    public void Dispose()
        => inner.Dispose();

    private void WriteHitCandidates(SceneLayoutCommit commit, float x, float y)
    {
        var hits = new List<string>();
        var ids = commit.PaintOrderIds.Length > 0 ? commit.PaintOrderIds : commit.Layout.Keys.ToArray();
        for (var index = ids.Length - 1; index >= 0 && hits.Count < 8; index--)
        {
            var id = ids[index];
            if (!commit.Layout.TryGetValue(id, out var box))
                continue;

            var screenBox = Enaga.Input.SceneScreenGeometry.ResolveScreenBox(commit, commit.Layout, id, box);
            if (x < screenBox.AbsLeft ||
                x > screenBox.AbsLeft + screenBox.Width ||
                y < screenBox.AbsTop ||
                y > screenBox.AbsTop + screenBox.Height)
            {
                continue;
            }

            var label = commit.Nodes.TryGetValue(id, out var node) ? node.Label : null;
            hits.Add(
                $"{id} label={label ?? "-"} kind={box.NodeKind} " +
                $"rect=({Format(screenBox.AbsLeft)},{Format(screenBox.AbsTop)},{Format(screenBox.Width)},{Format(screenBox.Height)}) " +
                $"scroll=({Format(screenBox.ScrollX)},{Format(screenBox.ScrollY)}) " +
                $"bg={box.BackgroundColor ?? "-"} text={Trim(box.TextContent)} link={(string.IsNullOrWhiteSpace(box.LinkHref) ? "-" : "yes")}");
        }

        Write(hits.Count == 0 ? "hit none" : "hit " + string.Join(" | ", hits));
    }

    private void Write(string message)
        => Console.WriteLine($"[{timeProvider.GetElapsedTime(startTimestamp).TotalSeconds,8:F3}] {message}");

    private static SceneLayoutBox RootBoxOrDefault(SceneLayoutCommit commit)
        => commit.Layout.TryGetValue(commit.RootId, out var rootBox) ? rootBox : default!;

    private static string Format(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "-";

        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 32 ? normalized : normalized[..32] + "...";
    }
}
