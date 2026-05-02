using System.Runtime.InteropServices;
using Okojo;
using Enaga.Rendering;
using Enaga.React.OkojoRuntime;
using SkiaSharp;
using Enaga.Input;
namespace Enaga.Rendering.Skia;


public sealed class SkiaRuntimeSceneHost : ISceneFrameSource, IInputSink, ITextCompositionRangeSink, IRenderRuntimeStateSource, IRenderWakeSource, IOverlayInputHitTestSource, ILowLevelSkiaRenderer, IRuntimeBackendServicesSource, IDisposable
{
    private readonly List<ILowLevelSkiaLayer> lowLevelSkiaLayers = [];
    private readonly Dictionary<ILowLevelSkiaLayer, List<LowLevelRepaintRequest>> lowLevelRepaintRequests = new();
    private readonly SceneDamageRectBufferWriter lowLevelDirtyRectBuffer = new(8);
    private readonly List<LowLevelRepaintEvent> pendingLowLevelRepaintEvents = [];

    public SkiaRuntimeSceneHost(OkojoNodeReactHost runtimeHost)
    {
        RuntimeHost = runtimeHost ?? throw new ArgumentNullException(nameof(runtimeHost));
    }

    public OkojoNodeReactHost RuntimeHost { get; }

    public RuntimeBackendServices BackendServices => RuntimeHost.BackendServices;

    public string? LastError => RuntimeHost.LastError;

    public event Action? RenderWakeRequested
    {
        add => RuntimeHost.RenderWakeRequested += value;
        remove => RuntimeHost.RenderWakeRequested -= value;
    }

    public SceneFrameResult RenderFrame(int width, int height, TimeSpan elapsed)
    {
        return RuntimeHost.RenderFrame(width, height, elapsed);
    }

    public void RequestRender(SceneDamageReason reason = SceneDamageReason.FullFrameFallback)
    {
        RuntimeHost.RequestRender(reason);
    }

    public bool TryInvokeGlobalFunction(string name, SceneDamageReason reason, params ReadOnlySpan<JsValue> args)
    {
        return RuntimeHost.TryInvokeGlobalFunction(name, reason, args);
    }

    public bool TryInvokeGlobalFunctionWhenChanged(string name, SceneDamageReason reason, params ReadOnlySpan<JsValue> args)
    {
        return RuntimeHost.TryInvokeGlobalFunctionWhenChanged(name, reason, args);
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic)
    {
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.PointerMove, x, y));
        RuntimeHost.PointerMove(x, y, buttons, synthetic);
    }

    public bool HitTestOverlayInput(float x, float y)
    {
        return RuntimeHost.HitTestOverlayInput(x, y);
    }

    public void PointerDown(int button, int buttons, bool synthetic)
    {
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.PointerDown, RuntimeHost.MouseX, RuntimeHost.MouseY));
        RuntimeHost.PointerDown(button, buttons, synthetic);
    }

    public void PointerUp(int button, int buttons, bool synthetic)
    {
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.PointerUp, RuntimeHost.MouseX, RuntimeHost.MouseY));
        RuntimeHost.PointerUp(button, buttons, synthetic);
    }

    public void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0)
    {
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.Wheel, RuntimeHost.MouseX, RuntimeHost.MouseY));
        RuntimeHost.Wheel(deltaX, deltaY, synthetic, modifiers);
    }

    public void KeyDown(string key, int modifiers, bool repeat, bool synthetic)
    {
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.KeyDown));
        RuntimeHost.KeyDown(key, modifiers, repeat, synthetic);
    }

    public void KeyUp(string key, int modifiers, bool synthetic)
    {
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.KeyUp));
        RuntimeHost.KeyUp(key, modifiers, synthetic);
    }

    public void TextInput(string text, bool synthetic)
    {
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.TextInput));
        RuntimeHost.TextInput(text, synthetic);
    }

    public void StartTextComposition()
    {
        RuntimeHost.StartTextComposition();
    }

    public void StartTextComposition(int startIndex)
    {
        RuntimeHost.StartTextComposition(startIndex);
    }

    public void UpdateTextComposition(string text, int cursorPosition)
    {
        RuntimeHost.UpdateTextComposition(text, cursorPosition);
    }

    public void UpdateTextComposition(string text, int cursorPosition, int selectionStart, int selectionLength)
    {
        RuntimeHost.UpdateTextComposition(text, cursorPosition, selectionStart, selectionLength);
    }

    public void EndTextComposition()
    {
        RuntimeHost.EndTextComposition();
    }

    public void PrepareTextCompositionCommit()
    {
        RuntimeHost.PrepareTextCompositionCommit();
    }

    public void UpdateImeState(bool isOpen, string indicator)
    {
        RuntimeHost.UpdateImeState(isOpen, indicator);
    }

    public bool TryGetTextCompositionCursor(out TextCompositionCursor cursor)
    {
        return RuntimeHost.TryGetTextCompositionCursor(out cursor);
    }

    public RenderRuntimeStateSnapshot GetRenderRuntimeStateSnapshot()
    {
        return RuntimeHost.GetRenderRuntimeStateSnapshot();
    }

    public void RegisterLowLevelSkiaLayer(ILowLevelSkiaLayer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (lowLevelSkiaLayers.Contains(renderer))
            return;

        lowLevelSkiaLayers.Add(renderer);
        lowLevelRepaintRequests[renderer] = [];
    }

    public bool UnregisterLowLevelSkiaLayer(ILowLevelSkiaLayer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        lowLevelRepaintRequests.Remove(renderer);
        return lowLevelSkiaLayers.Remove(renderer);
    }

    public void RequestLowLevelSkiaRepaint(ILowLevelSkiaLayer renderer, LowLevelRepaintRequest request)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (!lowLevelSkiaLayers.Contains(renderer))
            throw new InvalidOperationException("Renderer must be registered before requesting repaint.");

        lowLevelRepaintRequests[renderer].Add(request);
    }

    public ReadOnlySpan<SceneDamageRect> ConsumeLowLevelDirtyRects(int width, int height)
    {
        if (lowLevelSkiaLayers.Count == 0)
            return ReadOnlySpan<SceneDamageRect>.Empty;

        lowLevelDirtyRectBuffer.Clear();
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.Frame));
        var pendingEvents = CollectionsMarshal.AsSpan(pendingLowLevelRepaintEvents);
        foreach (var renderer in lowLevelSkiaLayers)
        {
            if (!lowLevelRepaintRequests.TryGetValue(renderer, out var requests) || requests.Count == 0)
                continue;

            for (var requestIndex = requests.Count - 1; requestIndex >= 0; requestIndex--)
            {
                var request = requests[requestIndex];
                if (!LowLevelRepaintMatcher.IsMatch(request, pendingEvents))
                    continue;

                lowLevelDirtyRectBuffer.Add(request.RepaintRect);
                requests.RemoveAt(requestIndex);
            }
        }

        pendingLowLevelRepaintEvents.Clear();
        return lowLevelDirtyRectBuffer.WrittenSpan;
    }

    public void RenderLowLevelSkia(SKCanvas canvas, int width, int height, TimeSpan elapsed, ReadOnlySpan<SceneDamageRect> dirtyRects)
    {
        foreach (var renderer in lowLevelSkiaLayers)
        {
            if (renderer.TryGetRenderBounds(out var bounds) && !IntersectsAnyDirtyRect(bounds, dirtyRects))
                continue;

            renderer.RenderLowLevelSkia(canvas, width, height, elapsed, dirtyRects);
        }
    }

    public void Dispose()
    {
        RuntimeHost.Dispose();
        lowLevelDirtyRectBuffer.Dispose();
    }

    private static bool IntersectsAnyDirtyRect(SceneDamageRect bounds, ReadOnlySpan<SceneDamageRect> dirtyRects)
    {
        if (dirtyRects.IsEmpty)
            return false;

        foreach (var dirtyRect in dirtyRects)
        {
            if (dirtyRect.X < bounds.X + bounds.Width &&
                dirtyRect.X + dirtyRect.Width > bounds.X &&
                dirtyRect.Y < bounds.Y + bounds.Height &&
                dirtyRect.Y + dirtyRect.Height > bounds.Y)
            {
                return true;
            }
        }

        return false;
    }
}
