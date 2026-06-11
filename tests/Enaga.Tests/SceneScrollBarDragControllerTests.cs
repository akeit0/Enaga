using Enaga.Input;
using Enaga.Rendering;
using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneScrollBarDragControllerTests
{
    [Fact]
    public void TryHitThumb_ReturnsVerticalDragOffset()
    {
        var box = CreateScrollBox();
        var vertical = SceneScrollBarLayout.ResolveVerticalScrollBar(box);
        Assert.NotNull(vertical);

        var hit = SceneScrollBarDragController.TryHitThumb(
            box,
            vertical.Value.ThumbRect.Left + 1,
            vertical.Value.ThumbRect.Top + 4,
            out var axis,
            out var thumbOffset
        );

        Assert.True(hit);
        Assert.Equal(SceneScrollBarDragAxis.Vertical, axis);
        Assert.Equal(4, thumbOffset);
    }

    [Fact]
    public void TryUpdate_UpdatesVerticalScrollImmediately()
    {
        var box = CreateScrollBox();
        var vertical = SceneScrollBarLayout.ResolveVerticalScrollBar(box);
        Assert.NotNull(vertical);

        var drag = new SceneScrollBarDragState<SceneNodeId>();
        drag.Begin(new SceneNodeId(1), SceneScrollBarDragAxis.Vertical, 0);
        var state = new TestScrollState();

        var changed = SceneScrollBarDragController.TryUpdate(
            drag,
            box,
            state,
            vertical.Value.ThumbRect.Left + 1,
            vertical.Value.TrackRect.Bottom
        );

        Assert.True(changed);
        Assert.Equal(300, state.ScrollY);
        Assert.Equal(300, state.TargetScrollY);
    }

    private static SceneLayoutBox CreateScrollBox() =>
        new(
            SceneNodeKind.ScrollView,
            10,
            20,
            100,
            100,
            ScrollY: 0,
            IsScrollContainer: true,
            ContentHeight: 400
        );

    private sealed class TestScrollState : ISceneScrollOffsetState
    {
        public float ScrollX { get; set; }

        public float ScrollY { get; set; }

        public float TargetScrollX { get; set; }

        public float TargetScrollY { get; set; }
    }
}
