using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneCommitPainterBoxSizingTests
{
    [Fact]
    public void ResolveBoxPaintGeometry_ContentBoxInsetsFillRectInsideBorder()
    {
        var geometry = SceneCommitPainter.ResolveBoxPaintGeometry(
            new SceneLayoutBox(
                SceneNodeKind.View,
                10,
                20,
                100,
                50,
                BorderWidth: 8,
                BorderRadius: 3,
                BoxSizing: SceneBoxSizing.ContentBox));

        Assert.True(Math.Abs(18 - geometry.FillRect.Left) < 0.001f);
        Assert.True(Math.Abs(28 - geometry.FillRect.Top) < 0.001f);
        Assert.True(Math.Abs(102 - geometry.FillRect.Right) < 0.001f);
        Assert.True(Math.Abs(62 - geometry.FillRect.Bottom) < 0.001f);
        Assert.True(Math.Abs(14 - geometry.BorderRect.Left) < 0.001f);
        Assert.True(Math.Abs(24 - geometry.BorderRect.Top) < 0.001f);
        Assert.True(Math.Abs(106 - geometry.BorderRect.Right) < 0.001f);
        Assert.True(Math.Abs(66 - geometry.BorderRect.Bottom) < 0.001f);
        Assert.True(Math.Abs(0 - geometry.FillRadius) < 0.001f);
        Assert.True(Math.Abs(0 - geometry.BorderRadius) < 0.001f);
    }

    [Fact]
    public void ResolveBoxPaintGeometry_BorderBoxInsetsFillRectInsideBorder()
    {
        var geometry = SceneCommitPainter.ResolveBoxPaintGeometry(
            new SceneLayoutBox(
                SceneNodeKind.View,
                10,
                20,
                100,
                50,
                BorderWidth: 8,
                BorderRadius: 12,
                BoxSizing: SceneBoxSizing.BorderBox));

        Assert.True(Math.Abs(18 - geometry.FillRect.Left) < 0.001f);
        Assert.True(Math.Abs(28 - geometry.FillRect.Top) < 0.001f);
        Assert.True(Math.Abs(102 - geometry.FillRect.Right) < 0.001f);
        Assert.True(Math.Abs(62 - geometry.FillRect.Bottom) < 0.001f);
        Assert.True(Math.Abs(14 - geometry.BorderRect.Left) < 0.001f);
        Assert.True(Math.Abs(24 - geometry.BorderRect.Top) < 0.001f);
        Assert.True(Math.Abs(106 - geometry.BorderRect.Right) < 0.001f);
        Assert.True(Math.Abs(66 - geometry.BorderRect.Bottom) < 0.001f);
        Assert.True(Math.Abs(4 - geometry.FillRadius) < 0.001f);
        Assert.True(Math.Abs(8 - geometry.BorderRadius) < 0.001f);
    }

    [Fact]
    public void ResolveBoxPaintGeometry_PaintGeometryDoesNotDependOnBoxSizing()
    {
        var contentBox = SceneCommitPainter.ResolveBoxPaintGeometry(
            new SceneLayoutBox(
                SceneNodeKind.View,
                10,
                20,
                100,
                50,
                BorderWidth: 8,
                BorderRadius: 12,
                BoxSizing: SceneBoxSizing.ContentBox));
        var borderBox = SceneCommitPainter.ResolveBoxPaintGeometry(
            new SceneLayoutBox(
                SceneNodeKind.View,
                10,
                20,
                100,
                50,
                BorderWidth: 8,
                BorderRadius: 12,
                BoxSizing: SceneBoxSizing.BorderBox));

        Assert.Equal(contentBox, borderBox);
    }

    [Fact]
    public void ResolveBoxPaintGeometry_BorderBoxCollapsesFillRectWhenBorderConsumesWholeBox()
    {
        var geometry = SceneCommitPainter.ResolveBoxPaintGeometry(
            new SceneLayoutBox(
                SceneNodeKind.View,
                10,
                20,
                12,
                12,
                BorderWidth: 8,
                BorderRadius: 6,
                BoxSizing: SceneBoxSizing.BorderBox));

        Assert.True(Math.Abs(0 - geometry.FillRect.Width) < 0.001f);
        Assert.True(Math.Abs(0 - geometry.FillRect.Height) < 0.001f);
    }

    [Fact]
    public void ResolveBoxPaintGeometry_ContentBoxZeroOrNegativeRadiusStaysSquare()
    {
        var zeroRadius = SceneCommitPainter.ResolveBoxPaintGeometry(
            new SceneLayoutBox(
                SceneNodeKind.View,
                10,
                20,
                100,
                50,
                BorderWidth: 8,
                BorderRadius: 0,
                BoxSizing: SceneBoxSizing.ContentBox));
        var negativeRadius = SceneCommitPainter.ResolveBoxPaintGeometry(
            new SceneLayoutBox(
                SceneNodeKind.View,
                10,
                20,
                100,
                50,
                BorderWidth: 8,
                BorderRadius: -10,
                BoxSizing: SceneBoxSizing.ContentBox));

        Assert.True(Math.Abs(0 - zeroRadius.FillRadius) < 0.001f);
        Assert.True(Math.Abs(0 - zeroRadius.BorderRadius) < 0.001f);
        Assert.True(Math.Abs(0 - negativeRadius.FillRadius) < 0.001f);
        Assert.True(Math.Abs(0 - negativeRadius.BorderRadius) < 0.001f);
    }
}
