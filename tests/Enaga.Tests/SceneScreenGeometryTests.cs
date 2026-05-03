using Enaga.Input;
using Enaga.Rendering;
using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneScreenGeometryTests
{
    [Fact]
    public void TryGetNodeScreenBounds_SubtractsAncestorScrollOffsets()
    {
        var root = new SceneNodeId(1);
        var scroll = new SceneNodeId(2);
        var child = new SceneNodeId(3);
        var commit = new SceneLayoutCommit(
            root,
            new SceneViewport(320, 200),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [root] = new(SceneNodeKind.View, null, [scroll]),
                [scroll] = new(SceneNodeKind.ScrollView, root, [child]),
                [child] = new(SceneNodeKind.View, scroll, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [root] = new(SceneNodeKind.View, 0, 0, 320, 200),
                [scroll] = new(SceneNodeKind.ScrollView, 10, 20, 100, 80, ScrollX: 4, ScrollY: 12, IsScrollContainer: true, ContentWidth: 200, ContentHeight: 200),
                [child] = new(SceneNodeKind.View, 30, 50, 40, 20)
            },
            []);

        var found = SceneScreenGeometry.TryGetNodeScreenBounds(commit, child, out var bounds);

        Assert.True(found);
        Assert.Equal(26, bounds.Left);
        Assert.Equal(38, bounds.Top);
        Assert.Equal(66, bounds.Right);
        Assert.Equal(58, bounds.Bottom);
        Assert.Equal(2, bounds.Depth);
    }

    [Fact]
    public void WheelTargetLatch_ReusesActiveTargetDuringGesture()
    {
        var latch = new SceneWheelScrollTargetLatch<string>(timeoutMs: 350);

        var first = latch.TryUseActive(1000, out var activeFirst)
            ? activeFirst
            : latch.SetActive("outer");
        var second = latch.TryUseActive(1100, out var activeSecond)
            ? activeSecond
            : latch.SetActive("inner");
        var third = latch.TryUseActive(1501, out var activeThird)
            ? activeThird
            : latch.SetActive("inner");

        Assert.Equal("outer", first);
        Assert.Equal("outer", second);
        Assert.Equal("inner", third);
    }
}
