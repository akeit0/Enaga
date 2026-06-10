using Silk.NET.Maths;
using SkiaSharp;

namespace Enaga.Rendering;

internal interface ISkiaWindowSurface : IDisposable
{
    SKCanvas Canvas { get; }
    GRContext? Context { get; }
    PresentDiagnosticsSnapshot LastDiagnostics { get; }
    bool RequiresPresentOnRenderWithoutDamage { get; }

    void Initialize(Vector2D<int> size);
    bool Resize(Vector2D<int> size);
    void Present(ReadOnlySpan<SceneDamageRect> dirtyRects = default);
}
