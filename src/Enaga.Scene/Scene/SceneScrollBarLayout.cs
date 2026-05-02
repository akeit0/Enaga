using Enaga.Scene;

namespace Enaga.Input;

public static class SceneScrollBarLayout
{
    public static VerticalScrollBarMetrics? ResolveVerticalScrollBar(SceneLayoutBox box)
    {
        if (box.NodeKind != SceneNodeKind.ScrollView)
            return null;

        var viewportHeight = Math.Max(0, box.Height);
        var contentHeight = Math.Max(viewportHeight, box.ContentHeight);
        var maxScroll = Math.Max(0, contentHeight - viewportHeight);
        if (viewportHeight <= 0 || maxScroll <= 0)
            return null;

        var gutterWidth = Math.Max(0, box.ScrollBarWidth);
        if (gutterWidth <= 0)
            return null;

        var margin = ResolveScrollBarMargin(gutterWidth);
        var barWidth = Math.Max(0, gutterWidth - margin * 2);
        var minThumbHeight = gutterWidth * 2;
        var trackHeight = Math.Max(0, viewportHeight - margin * 2);
        if (trackHeight <= 0 || barWidth <= 0)
            return null;

        var thumbHeight = Math.Min(
            trackHeight,
            Math.Max(minThumbHeight, trackHeight * Math.Min(1, viewportHeight / contentHeight)));
        var thumbTravel = Math.Max(0, trackHeight - thumbHeight);
        var progress = maxScroll > 0 ? Math.Clamp(box.ScrollY / maxScroll, 0, 1) : 0;
        var trackLeft = box.AbsLeft + box.Width - gutterWidth + margin;
        var trackTop = box.AbsTop + margin;
        var radius = barWidth * 0.5f;
        var trackRect = new ScrollBarRect(trackLeft, trackTop, barWidth, trackHeight);
        var thumbRect = new ScrollBarRect(trackLeft, trackTop + thumbTravel * progress, barWidth, thumbHeight);
        return new VerticalScrollBarMetrics(trackRect, thumbRect, radius);
    }

    public static HorizontalScrollBarMetrics? ResolveHorizontalScrollBar(SceneLayoutBox box)
    {
        if (box.NodeKind != SceneNodeKind.ScrollView || !box.HorizontalScrollEnabled)
            return null;

        var viewportWidth = Math.Max(0, box.Width);
        var contentWidth = Math.Max(viewportWidth, box.ContentWidth);
        var maxScroll = Math.Max(0, contentWidth - viewportWidth);
        if (viewportWidth <= 0 || maxScroll <= 0)
            return null;

        var gutterHeight = Math.Max(0, box.ScrollBarWidth);
        if (gutterHeight <= 0)
            return null;

        var margin = ResolveScrollBarMargin(gutterHeight);
        var barHeight = Math.Max(0, gutterHeight - margin * 2);
        var minThumbWidth = gutterHeight * 2;
        var trackWidth = Math.Max(0, viewportWidth - margin * 2);
        if (trackWidth <= 0 || barHeight <= 0)
            return null;

        var thumbWidth = Math.Min(
            trackWidth,
            Math.Max(minThumbWidth, trackWidth * Math.Min(1, viewportWidth / contentWidth)));
        var thumbTravel = Math.Max(0, trackWidth - thumbWidth);
        var progress = maxScroll > 0 ? Math.Clamp(box.ScrollX / maxScroll, 0, 1) : 0;
        var trackLeft = box.AbsLeft + margin;
        var trackTop = box.AbsTop + box.Height - gutterHeight + margin;
        var radius = barHeight * 0.5f;
        var trackRect = new ScrollBarRect(trackLeft, trackTop, trackWidth, barHeight);
        var thumbRect = new ScrollBarRect(trackLeft + thumbTravel * progress, trackTop, thumbWidth, barHeight);
        return new HorizontalScrollBarMetrics(trackRect, thumbRect, radius);
    }

    public static bool TryHitVerticalScrollBarThumb(SceneLayoutBox box, float x, float y, out VerticalScrollBarMetrics metrics, out float grabOffsetY)
    {
        var resolved = ResolveVerticalScrollBar(box);
        if (resolved is not { } scrollBar || !scrollBar.ThumbRect.Contains(x, y))
        {
            metrics = default;
            grabOffsetY = 0;
            return false;
        }

        metrics = scrollBar;
        grabOffsetY = y - scrollBar.ThumbRect.Top;
        return true;
    }

    public static bool TryHitHorizontalScrollBarThumb(SceneLayoutBox box, float x, float y, out HorizontalScrollBarMetrics metrics, out float grabOffsetX)
    {
        var resolved = ResolveHorizontalScrollBar(box);
        if (resolved is not { } scrollBar || !scrollBar.ThumbRect.Contains(x, y))
        {
            metrics = default;
            grabOffsetX = 0;
            return false;
        }

        metrics = scrollBar;
        grabOffsetX = x - scrollBar.ThumbRect.Left;
        return true;
    }

    public static float ResolveVerticalScrollOffsetFromThumbTop(SceneLayoutBox box, float thumbTop)
    {
        var metrics = ResolveVerticalScrollBar(box);
        if (metrics is not { } scrollBar)
            return 0;

        var viewportHeight = Math.Max(0, box.Height);
        var contentHeight = Math.Max(viewportHeight, box.ContentHeight);
        var maxScroll = Math.Max(0, contentHeight - viewportHeight);
        if (maxScroll <= 0)
            return 0;

        var thumbTravel = Math.Max(0, scrollBar.TrackRect.Height - scrollBar.ThumbRect.Height);
        if (thumbTravel <= 0)
            return 0;

        var normalized = Math.Clamp((thumbTop - scrollBar.TrackRect.Top) / thumbTravel, 0, 1);
        return maxScroll * normalized;
    }

    public static float ResolveHorizontalScrollOffsetFromThumbLeft(SceneLayoutBox box, float thumbLeft)
    {
        var metrics = ResolveHorizontalScrollBar(box);
        if (metrics is not { } scrollBar)
            return 0;

        var viewportWidth = Math.Max(0, box.Width);
        var contentWidth = Math.Max(viewportWidth, box.ContentWidth);
        var maxScroll = Math.Max(0, contentWidth - viewportWidth);
        if (maxScroll <= 0)
            return 0;

        var thumbTravel = Math.Max(0, scrollBar.TrackRect.Width - scrollBar.ThumbRect.Width);
        if (thumbTravel <= 0)
            return 0;

        var normalized = Math.Clamp((thumbLeft - scrollBar.TrackRect.Left) / thumbTravel, 0, 1);
        return maxScroll * normalized;
    }

    public readonly record struct ScrollBarRect(float Left, float Top, float Width, float Height)
    {
        public float Right => Left + Width;

        public float Bottom => Top + Height;

        public bool Contains(float x, float y)
        {
            return x >= Left && x <= Right && y >= Top && y <= Bottom;
        }
    }

    public readonly record struct VerticalScrollBarMetrics(ScrollBarRect TrackRect, ScrollBarRect ThumbRect, float Radius);

    public readonly record struct HorizontalScrollBarMetrics(ScrollBarRect TrackRect, ScrollBarRect ThumbRect, float Radius);

    private static float ResolveScrollBarMargin(float gutterSize)
        => Math.Min(2, Math.Max(0, gutterSize) / 6);
}
