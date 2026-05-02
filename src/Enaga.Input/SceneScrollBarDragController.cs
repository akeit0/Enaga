using Enaga.Scene;

namespace Enaga.Input;

public enum SceneScrollBarDragAxis
{
    None,
    Horizontal,
    Vertical
}

public sealed class SceneScrollBarDragState
{
    public string? ScrollViewId { get; private set; }

    public SceneScrollBarDragAxis Axis { get; private set; }

    public float ThumbOffset { get; private set; }

    public bool IsActive => ScrollViewId is not null && Axis != SceneScrollBarDragAxis.None;

    public void Begin(string scrollViewId, SceneScrollBarDragAxis axis, float thumbOffset)
    {
        if (axis == SceneScrollBarDragAxis.None)
            throw new ArgumentException("A scrollbar drag must have an axis.", nameof(axis));

        ScrollViewId = scrollViewId;
        Axis = axis;
        ThumbOffset = thumbOffset;
    }

    public bool Clear()
    {
        if (ScrollViewId is null && Axis == SceneScrollBarDragAxis.None && ThumbOffset == 0)
            return false;

        ScrollViewId = null;
        Axis = SceneScrollBarDragAxis.None;
        ThumbOffset = 0;
        return true;
    }
}

public static class SceneScrollBarDragController
{
    public static bool TryHitThumb(
        SceneLayoutBox box,
        float x,
        float y,
        out SceneScrollBarDragAxis axis,
        out float thumbOffset)
    {
        if (SceneScrollBarLayout.TryHitHorizontalScrollBarThumb(box, x, y, out _, out var horizontalOffset))
        {
            axis = SceneScrollBarDragAxis.Horizontal;
            thumbOffset = horizontalOffset;
            return true;
        }

        if (SceneScrollBarLayout.TryHitVerticalScrollBarThumb(box, x, y, out _, out var verticalOffset))
        {
            axis = SceneScrollBarDragAxis.Vertical;
            thumbOffset = verticalOffset;
            return true;
        }

        axis = SceneScrollBarDragAxis.None;
        thumbOffset = 0;
        return false;
    }

    public static bool TryUpdate(
        SceneScrollBarDragState drag,
        SceneLayoutBox box,
        ISceneScrollOffsetState state,
        float pointerX,
        float pointerY)
    {
        if (!drag.IsActive)
            return false;

        if (drag.Axis == SceneScrollBarDragAxis.Horizontal)
        {
            var nextScrollX = SceneScrollMetrics.ClampScrollX(
                box,
                SceneScrollBarLayout.ResolveHorizontalScrollOffsetFromThumbLeft(box, pointerX - drag.ThumbOffset));
            SceneSmoothScrollController.SetImmediate(state, box, nextScrollX, state.ScrollY);
            return true;
        }

        if (drag.Axis == SceneScrollBarDragAxis.Vertical)
        {
            var nextScrollY = SceneScrollMetrics.ClampScrollY(
                box,
                SceneScrollBarLayout.ResolveVerticalScrollOffsetFromThumbTop(box, pointerY - drag.ThumbOffset));
            SceneSmoothScrollController.SetImmediate(state, box, state.ScrollX, nextScrollY);
            return true;
        }

        return false;
    }
}
