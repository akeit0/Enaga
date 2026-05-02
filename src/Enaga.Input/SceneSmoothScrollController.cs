using Enaga.Scene;

namespace Enaga.Input;

public interface ISceneScrollOffsetState
{
    float ScrollX { get; set; }
    float ScrollY { get; set; }
    float TargetScrollX { get; set; }
    float TargetScrollY { get; set; }
}

public static class SceneSmoothScrollController
{
    public const double DefaultResponse = 18;

    public static void ResetTarget(ISceneScrollOffsetState state)
    {
        state.TargetScrollX = state.ScrollX;
        state.TargetScrollY = state.ScrollY;
    }

    public static void SetImmediate(ISceneScrollOffsetState state, SceneLayoutBox box, float scrollX, float scrollY)
    {
        state.ScrollX = SceneScrollMetrics.ClampScrollX(box, scrollX);
        state.ScrollY = SceneScrollMetrics.ClampScrollY(box, scrollY);
        ResetTarget(state);
    }

    public static bool ApplyWheelTarget(
        ISceneScrollOffsetState state,
        SceneLayoutBox box,
        float deltaX,
        float deltaY,
        float wheelScrollFactor = SceneScrollMetrics.DefaultWheelScrollFactor)
    {
        var nextScrollX = SceneScrollMetrics.ClampScrollX(box, state.TargetScrollX + deltaX * wheelScrollFactor);
        var nextScrollY = SceneScrollMetrics.ClampScrollY(box, state.TargetScrollY - deltaY * wheelScrollFactor);
        if (Math.Abs(nextScrollX - state.TargetScrollX) <= 0.001f &&
            Math.Abs(nextScrollY - state.TargetScrollY) <= 0.001f)
        {
            return false;
        }

        state.TargetScrollX = nextScrollX;
        state.TargetScrollY = nextScrollY;
        return true;
    }

    public static bool Advance(
        ISceneScrollOffsetState state,
        SceneLayoutBox box,
        double deltaSeconds,
        double response = DefaultResponse)
    {
        state.TargetScrollX = SceneScrollMetrics.ClampScrollX(box, state.TargetScrollX);
        state.TargetScrollY = SceneScrollMetrics.ClampScrollY(box, state.TargetScrollY);
        var dx = state.TargetScrollX - state.ScrollX;
        var dy = state.TargetScrollY - state.ScrollY;
        if (Math.Abs(dx) <= 0.5f && Math.Abs(dy) <= 0.5f)
        {
            state.ScrollX = state.TargetScrollX;
            state.ScrollY = state.TargetScrollY;
            return false;
        }

        var alpha = (float)(1 - Math.Exp(-Math.Max(deltaSeconds, 1.0 / 60.0) * response));
        state.ScrollX = SceneScrollMetrics.ClampScrollX(box, state.ScrollX + dx * alpha);
        state.ScrollY = SceneScrollMetrics.ClampScrollY(box, state.ScrollY + dy * alpha);
        return true;
    }
}
