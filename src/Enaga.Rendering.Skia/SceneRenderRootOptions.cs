namespace Enaga.Rendering.Skia;

public sealed class SceneRenderRootOptions
{
    public bool DiagnosticsEnabled { get; init; }

    public bool RequiresFullFramePresentation { get; init; }

    public bool ViewCounter { get; init; }

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
