namespace Enaga.Rendering;

public readonly record struct RenderRuntimeStateSnapshot(
    bool ImeOpen,
    bool CompositionActive,
    bool AnimationEnabled,
    bool ShaderAnimationEnabled,
    bool RenderInvalidated,
    int ViewCallCount);

public interface IRenderRuntimeStateSource
{
    RenderRuntimeStateSnapshot GetRenderRuntimeStateSnapshot();
}

public readonly record struct RenderRootDiagnosticsSnapshot(
    bool DiagnosticsEnabled,
    double SourceFrameMs,
    double PaintMs,
    bool PaintedFrame,
    bool CommitReused,
    bool PictureReused,
    SceneDamageRect[]? DirtyRects,
    int DirtyRectCount,
    long DirtyPixels,
    SceneDamageReason DamageReasons,
    int Width,
    int Height,
    RenderRuntimeStateSnapshot RuntimeState);

public interface IRenderDiagnosticsProvider
{
    RenderRootDiagnosticsSnapshot GetRenderRootDiagnosticsSnapshot();
}

public interface IRenderDirtyRectSource
{
    ReadOnlySpan<SceneDamageRect> GetLastDirtyRects();
}

public readonly record struct PresentDiagnosticsSnapshot(
    double PresentMs,
    long UploadBytes,
    long UploadPixels,
    int UploadRectCount,
    bool TextureStorageResized,
    int Width,
    int Height);
