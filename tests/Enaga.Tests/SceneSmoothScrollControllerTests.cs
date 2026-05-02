using Enaga.Input;
using Enaga.Rendering;
using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneSmoothScrollControllerTests
{
    [Fact]
    public void ApplyWheelTarget_ClampsTargetWithoutMovingCurrentOffset()
    {
        var state = new TestScrollState();
        var box = CreateScrollBox();

        var changed = SceneSmoothScrollController.ApplyWheelTarget(state, box, 0, -10, wheelScrollFactor: 20);

        Assert.True(changed);
        Assert.Equal(0, state.ScrollY);
        Assert.Equal(200, state.TargetScrollY);
    }

    [Fact]
    public void Advance_MovesTowardTargetAndSnapsWhenClose()
    {
        var state = new TestScrollState { TargetScrollY = 100 };
        var box = CreateScrollBox();

        var animating = SceneSmoothScrollController.Advance(state, box, 1.0 / 60.0);

        Assert.True(animating);
        Assert.InRange(state.ScrollY, 1, 100);

        state.ScrollY = 99.75f;
        animating = SceneSmoothScrollController.Advance(state, box, 1.0 / 60.0);

        Assert.False(animating);
        Assert.Equal(100, state.ScrollY);
    }

    private static SceneLayoutBox CreateScrollBox()
        => new(
            SceneNodeKind.ScrollView,
            0,
            0,
            100,
            100,
            ScrollY: 0,
            IsScrollContainer: true,
            ContentHeight: 300);

    private sealed class TestScrollState : ISceneScrollOffsetState
    {
        public float ScrollX { get; set; }
        public float ScrollY { get; set; }
        public float TargetScrollX { get; set; }
        public float TargetScrollY { get; set; }
    }
}
