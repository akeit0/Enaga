using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneCommitPainterScrollBarTests
{
    [Fact]
    public void ResolveVerticalScrollBar_ReturnsNullWhenContentFitsViewport()
    {
        var metrics = SceneCommitPainter.ResolveVerticalScrollBar(
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                20,
                40,
                200,
                180,
                ContentHeight: 180));

        Assert.Null(metrics);
    }

    [Fact]
    public void ResolveHorizontalScrollBar_ReturnsNullWhenHorizontalScrollIsDisabled()
    {
        var metrics = SceneCommitPainter.ResolveHorizontalScrollBar(
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                20,
                40,
                200,
                100,
                ContentWidth: 400));

        Assert.Null(metrics);
    }

    [Fact]
    public void ResolveVerticalScrollBar_PlacesThumbOnRightEdgeAndMovesWithScroll()
    {
        var metrics = SceneCommitPainter.ResolveVerticalScrollBar(
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                20,
                40,
                200,
                100,
                ScrollY: 100,
                ContentHeight: 300));

        Assert.NotNull(metrics);
        Assert.True(Math.Abs(210f - metrics.Value.TrackRect.Left) < 0.001f);
        Assert.True(Math.Abs(218f - metrics.Value.TrackRect.Right) < 0.001f);
        Assert.True(Math.Abs(42f - metrics.Value.TrackRect.Top) < 0.001f);
        Assert.True(Math.Abs(138f - metrics.Value.TrackRect.Bottom) < 0.001f);
        Assert.True(metrics.Value.ThumbRect.Top > metrics.Value.TrackRect.Top);
        Assert.True(metrics.Value.ThumbRect.Bottom < metrics.Value.TrackRect.Bottom);
    }

    [Fact]
    public void ResolveVerticalScrollBar_KeepsThumbInsideScaledGutter()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            0,
            0,
            100,
            100,
            ScrollBarWidth: 6,
            ContentHeight: 300);

        var metrics = SceneCommitPainter.ResolveVerticalScrollBar(box);

        Assert.NotNull(metrics);
        var gutterLeft = box.AbsLeft + box.Width - box.ScrollBarWidth;
        Assert.True(metrics.Value.ThumbRect.Left >= gutterLeft);
        Assert.True(metrics.Value.ThumbRect.Right <= box.AbsLeft + box.Width);
        Assert.True(metrics.Value.TrackRect.Left >= gutterLeft);
        Assert.True(metrics.Value.TrackRect.Right <= box.AbsLeft + box.Width);
    }

    [Fact]
    public void ResolvePresentationVerticalScrollBar_SnapsToStableDevicePixels()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            0,
            0,
            100,
            100,
            ScrollBarWidth: 12 / 1.3f,
            ContentHeight: 300);

        var metrics = SceneCommitPainter.ResolvePresentationVerticalScrollBar(box, 1.3f, 130, 130);

        Assert.NotNull(metrics);
        Assert.Equal(12, metrics.Value.GutterRect.Width);
        Assert.Equal(130, metrics.Value.GutterRect.Right);
        Assert.Equal(118, metrics.Value.GutterRect.Left);
        Assert.True(metrics.Value.ThumbRect.Left >= metrics.Value.GutterRect.Left);
        Assert.True(metrics.Value.ThumbRect.Right <= metrics.Value.GutterRect.Right);
    }

    [Fact]
    public void ResolvePresentationVerticalScrollBar_ClampsRootRightEdgeToPresentationViewport()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            0,
            0,
            93,
            100,
            ScrollBarWidth: 12 / 1.3f,
            ContentHeight: 300);

        var metrics = SceneCommitPainter.ResolvePresentationVerticalScrollBar(box, 1.3f, 120, 130);

        Assert.NotNull(metrics);
        Assert.Equal(120, metrics.Value.GutterRect.Right);
        Assert.Equal(108, metrics.Value.GutterRect.Left);
        Assert.Equal(12, metrics.Value.GutterRect.Width);
    }

    [Fact]
    public void ResolvePresentationVerticalScrollBar_AlignsGutterToScaledContentRight()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            0,
            0,
            93,
            100,
            ScrollBarWidth: 12,
            ContentHeight: 300);

        var metrics = SceneCommitPainter.ResolvePresentationVerticalScrollBar(box, 1, 140, 180);

        Assert.NotNull(metrics);
        Assert.Equal(93, metrics.Value.GutterRect.Right);
        Assert.Equal(81, metrics.Value.GutterRect.Left);
        Assert.Equal(0, metrics.Value.GutterRect.Top);
        Assert.Equal(100, metrics.Value.GutterRect.Bottom);
    }

    [Fact]
    public void ResolvePresentationVerticalScrollBar_AlignsScaledGutterToScaledContentRight()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            0,
            0,
            80,
            100,
            ScrollBarWidth: 12,
            ContentHeight: 300);

        var metrics = SceneCommitPainter.ResolvePresentationVerticalScrollBar(box, 3, 240, 300);

        Assert.NotNull(metrics);
        Assert.Equal(36, metrics.Value.GutterRect.Width);
        Assert.Equal(240, metrics.Value.GutterRect.Right);
        Assert.Equal(204, metrics.Value.GutterRect.Left);
    }

    [Fact]
    public void ResolvePresentationVerticalScrollBar_KeepsPhysicalWidthStableWhenBoxWidthIsScaleAdjusted()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            0,
            0,
            80,
            100,
            ScrollBarWidth: 4,
            ContentHeight: 300);

        var metrics = SceneCommitPainter.ResolvePresentationVerticalScrollBar(box, 3, 240, 300);

        Assert.NotNull(metrics);
        Assert.Equal(12, metrics.Value.GutterRect.Width);
        Assert.Equal(240, metrics.Value.GutterRect.Right);
        Assert.Equal(228, metrics.Value.GutterRect.Left);
    }

    [Fact]
    public void PaintScrollBars_PaintsGutterAtScaledContentRight()
    {
        using var bitmap = new SkiaSharp.SKBitmap(140, 180);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        canvas.Clear(SkiaSharp.SKColors.White);
        var nodes = new Dictionary<string, SceneGraphNode>(StringComparer.Ordinal)
        {
            ["root"] = new(SceneNodeKind.View, null, ["body"]),
            ["body"] = new(SceneNodeKind.ScrollView, null, []),
        };
        var layout = new Dictionary<string, SceneLayoutBox>(StringComparer.Ordinal)
        {
            ["root"] = new(SceneNodeKind.View, 0, 0, 100, 100),
            ["body"] = new(SceneNodeKind.ScrollView, 0, 0, 93, 100, ScrollBarWidth: 12, ContentHeight: 300, ScrollBarTrackColor: "#1f1f1f"),
        };
        var commit = new SceneLayoutCommit("root", new SceneViewport(140, 180), nodes, layout, []);

        painter.PaintScrollBars(canvas, commit, 1, 140, 180);

        Assert.Equal(new SkiaSharp.SKColor(31, 31, 31, 255), bitmap.GetPixel(92, 90));
        Assert.Equal(SkiaSharp.SKColors.White, bitmap.GetPixel(80, 90));
        Assert.Equal(SkiaSharp.SKColors.White, bitmap.GetPixel(139, 90));
    }

    [Fact]
    public void PaintScrollBars_MovesNestedScrollBarWithAncestorScrollOffset()
    {
        using var bitmap = new SkiaSharp.SKBitmap(120, 120);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        canvas.Clear(SkiaSharp.SKColors.White);
        var nodes = new Dictionary<string, SceneGraphNode>(StringComparer.Ordinal)
        {
            ["root"] = new(SceneNodeKind.View, null, ["body"]),
            ["body"] = new(SceneNodeKind.ScrollView, "root", ["pane"]),
            ["pane"] = new(SceneNodeKind.ScrollView, "body", []),
        };
        var layout = new Dictionary<string, SceneLayoutBox>(StringComparer.Ordinal)
        {
            ["root"] = new(SceneNodeKind.View, 0, 0, 100, 100),
            ["body"] = new(SceneNodeKind.ScrollView, 0, 0, 100, 100, ScrollY: 40, ContentHeight: 240, ScrollBarTrackColor: "#1f1f1f"),
            ["pane"] = new(SceneNodeKind.ScrollView, 10, 80, 50, 40, ContentHeight: 120, ScrollBarWidth: 10, ScrollBarTrackColor: "#ff0000"),
        };
        var commit = new SceneLayoutCommit("root", new SceneViewport(120, 120), nodes, layout, []);

        painter.PaintScrollBars(canvas, commit, 1, 120, 120);

        Assert.Equal(new SkiaSharp.SKColor(255, 0, 0, 255), bitmap.GetPixel(55, 75));
        Assert.Equal(SkiaSharp.SKColors.White, bitmap.GetPixel(55, 105));
    }

    [Fact]
    public void TryHitVerticalScrollBarThumb_ReturnsGrabOffsetInsideThumb()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            20,
            40,
            200,
            100,
            ScrollY: 100,
            ContentHeight: 300);
        var metrics = SceneCommitPainter.ResolveVerticalScrollBar(box);

        Assert.NotNull(metrics);
        var hit = SceneCommitPainter.TryHitVerticalScrollBarThumb(
            box,
            metrics.Value.ThumbRect.MidX,
            metrics.Value.ThumbRect.Top + 6,
            out var resolved,
            out var grabOffsetY);

        Assert.True(hit);
        Assert.Equal(metrics.Value, resolved);
        Assert.True(Math.Abs(6 - grabOffsetY) < 0.001f);
    }

    [Fact]
    public void ResolveVerticalScrollOffsetFromThumbTop_MapsTrackRangeToScrollRange()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            20,
            40,
            200,
            100,
            ContentHeight: 300);
        var metrics = SceneCommitPainter.ResolveVerticalScrollBar(box);

        Assert.NotNull(metrics);
        var topScroll = SceneCommitPainter.ResolveVerticalScrollOffsetFromThumbTop(box, metrics.Value.TrackRect.Top);
        var bottomScroll = SceneCommitPainter.ResolveVerticalScrollOffsetFromThumbTop(
            box,
            metrics.Value.TrackRect.Bottom - metrics.Value.ThumbRect.Height);

        Assert.True(Math.Abs(0 - topScroll) < 0.001f);
        Assert.True(Math.Abs(200 - bottomScroll) < 0.001f);
    }

    [Fact]
    public void TryHitHorizontalScrollBarThumb_ReturnsGrabOffsetInsideThumb()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            20,
            40,
            200,
            100,
            ScrollX: 100,
            ContentWidth: 400,
            HorizontalScrollEnabled: true);
        var metrics = SceneCommitPainter.ResolveHorizontalScrollBar(box);

        Assert.NotNull(metrics);
        var hit = SceneCommitPainter.TryHitHorizontalScrollBarThumb(
            box,
            metrics.Value.ThumbRect.Left + 8,
            metrics.Value.ThumbRect.MidY,
            out var resolved,
            out var grabOffsetX);

        Assert.True(hit);
        Assert.Equal(metrics.Value, resolved);
        Assert.True(Math.Abs(8 - grabOffsetX) < 0.001f);
    }

    [Fact]
    public void ResolveHorizontalScrollOffsetFromThumbLeft_MapsTrackRangeToScrollRange()
    {
        var box = new SceneLayoutBox(
            SceneNodeKind.ScrollView,
            20,
            40,
            200,
            100,
            ContentWidth: 400,
            HorizontalScrollEnabled: true);
        var metrics = SceneCommitPainter.ResolveHorizontalScrollBar(box);

        Assert.NotNull(metrics);
        var leftScroll = SceneCommitPainter.ResolveHorizontalScrollOffsetFromThumbLeft(box, metrics.Value.TrackRect.Left);
        var rightScroll = SceneCommitPainter.ResolveHorizontalScrollOffsetFromThumbLeft(
            box,
            metrics.Value.TrackRect.Right - metrics.Value.ThumbRect.Width);

        Assert.True(Math.Abs(0 - leftScroll) < 0.001f);
        Assert.True(Math.Abs(200 - rightScroll) < 0.001f);
    }
}
