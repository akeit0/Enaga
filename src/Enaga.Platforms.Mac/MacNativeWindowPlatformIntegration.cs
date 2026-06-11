using Enaga.Hosting;
using Enaga.Input;
using Enaga.Rendering.Skia;
using Silk.NET.Windowing;

namespace Enaga.Platforms.Mac;

public sealed class MacNativeWindowPlatformIntegration : INativeWindowPlatformIntegration
{
    private MacImeContext? imeContext;

    public bool HandlesTextInput => false;

    public bool HasPendingInput => imeContext?.HasPendingVisualUpdate ?? false;

    public void Attach(IWindow window, IRenderRoot renderRoot)
    {
        if (!OperatingSystem.IsMacOS() || renderRoot is not ITextCompositionSink compositionSink)
            return;

        var nsWindow = window.Native?.Cocoa ?? 0;
        if (nsWindow == 0)
            return;

        imeContext = MacImeContext.TryAttach(nsWindow, compositionSink);
    }

    public void OnBeforeRender()
    {
        imeContext?.ClearPendingVisualUpdate();
    }

    public void OnRendered() { }

    public void Dispose()
    {
        imeContext?.Dispose();
        imeContext = null;
    }
}
