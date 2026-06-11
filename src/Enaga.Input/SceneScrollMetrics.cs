using Enaga.Scene;

namespace Enaga.Input;

public static class SceneScrollMetrics
{
    public const float DefaultWheelScrollFactor = 24f;

    public static float MaxScrollX(SceneLayoutBox box) =>
        MaxScrollX(box.Width, box.ContentWidth, box.HorizontalScrollEnabled);

    public static float MaxScrollY(SceneLayoutBox box) => MaxScrollY(box.Height, box.ContentHeight);

    public static float ClampScrollX(SceneLayoutBox box, float scrollX) =>
        ClampScrollX(scrollX, box.Width, box.ContentWidth, box.HorizontalScrollEnabled);

    public static float ClampScrollY(SceneLayoutBox box, float scrollY) =>
        ClampScrollY(scrollY, box.Height, box.ContentHeight);

    public static float MaxScrollX(
        float viewportWidth,
        float contentWidth,
        bool horizontalScrollEnabled
    ) => horizontalScrollEnabled ? Math.Max(0, contentWidth - viewportWidth) : 0;

    public static float MaxScrollY(float viewportHeight, float contentHeight) =>
        Math.Max(0, contentHeight - viewportHeight);

    public static float ClampScrollX(
        float scrollX,
        float viewportWidth,
        float contentWidth,
        bool horizontalScrollEnabled
    ) => Math.Clamp(scrollX, 0, MaxScrollX(viewportWidth, contentWidth, horizontalScrollEnabled));

    public static float ClampScrollY(float scrollY, float viewportHeight, float contentHeight) =>
        Math.Clamp(scrollY, 0, MaxScrollY(viewportHeight, contentHeight));

    public static bool CanScrollBy(
        float scrollX,
        float scrollY,
        float viewportWidth,
        float viewportHeight,
        float contentWidth,
        float contentHeight,
        bool horizontalScrollEnabled,
        float deltaX,
        float deltaY,
        float wheelScrollFactor = DefaultWheelScrollFactor
    )
    {
        var nextScrollX = ClampScrollX(
            scrollX + deltaX * wheelScrollFactor,
            viewportWidth,
            contentWidth,
            horizontalScrollEnabled
        );
        var nextScrollY = ClampScrollY(
            scrollY - deltaY * wheelScrollFactor,
            viewportHeight,
            contentHeight
        );
        return Math.Abs(nextScrollX - scrollX) > 0.001f || Math.Abs(nextScrollY - scrollY) > 0.001f;
    }
}
