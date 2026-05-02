using Enaga.Rendering;
using Silk.NET.Windowing;
using Enaga.Rendering.Skia;

namespace Enaga.Hosting;

public interface INativeWindowPlatformIntegration : IDisposable
{
    bool HandlesTextInput { get; }
    bool HasPendingInput => false;
    bool ShouldForwardTextInput(char character) => true;
    void Attach(IWindow window, IRenderRoot renderRoot);
    void OnBeforeRender() { }
    void OnPointerDown(int button) { }
    void OnRendered();
}
