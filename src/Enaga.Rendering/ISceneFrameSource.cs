using Enaga.Scene;

namespace Enaga.Rendering;

public interface ISceneFrameSource
{
    string? LastError { get; }

    SceneFrameResult RenderFrame(int width, int height, TimeSpan elapsed);
}

public interface IRenderViewportScaleSource
{
    float ViewportScale { get; }
}

public interface IRenderViewportScaleController : IRenderViewportScaleSource
{
    bool TryStepViewportScale(int direction);

    bool TryResetViewportScale();
}
