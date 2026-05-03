using Enaga.Rendering;
using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneDamageEstimatorTests
{
    private static readonly SceneNodeId Root = new(1);
    private static readonly SceneNodeId Sidebar = new(2);
    private static readonly SceneNodeId Content = new(3);
    private static readonly SceneNodeId Tooltip = new(4);
    private static readonly SceneNodeId Shader = new(5);

    [Fact]
    public void Resolve_DoesNotDirtyOpaqueParentWhenOnlyTooltipChildIsAdded()
    {
        var previousCommit = CreateCommit(includeTooltip: false);
        var nextCommit = CreateCommit(includeTooltip: true);
        using var resultBuffer = new SceneDamageRectBufferWriter(16);
        using var scratchBuffer = new SceneDamageRectBufferWriter(16);

        var dirtyRects = SceneDamageEstimator.Resolve(
            previousCommit,
            nextCommit,
            [],
            SceneDamageReason.None,
            800,
            600,
            false,
            resultBuffer,
            scratchBuffer);

        Assert.True(dirtyRects.Length > 0);
        Assert.False(Contains(dirtyRects, rect => rect.X < 220 && rect.X + rect.Width > 220));
        Assert.True(
            Contains(
                dirtyRects,
                rect => rect.X <= 40 &&
                        rect.Y <= 80 &&
                        rect.X + rect.Width >= 160 &&
                        rect.Y + rect.Height >= 108));
    }

    [Fact]
    public void Resolve_AnimationAlsoKeepsHostAnimatedShaderDirtyDuringTooltipChange()
    {
        var previousCommit = CreateAnimatedCommit(includeTooltip: false);
        var nextCommit = CreateAnimatedCommit(includeTooltip: true);
        using var resultBuffer = new SceneDamageRectBufferWriter(16);
        using var scratchBuffer = new SceneDamageRectBufferWriter(16);

        var dirtyRects = SceneDamageEstimator.Resolve(
            previousCommit,
            nextCommit,
            [],
            SceneDamageReason.Animation,
            800,
            600,
            false,
            resultBuffer,
            scratchBuffer);

        Assert.True(
            Contains(
                dirtyRects,
                rect => rect.X <= 40 &&
                        rect.Y <= 80 &&
                        rect.X + rect.Width >= 160 &&
                        rect.Y + rect.Height >= 108));
        Assert.True(
            Contains(
                dirtyRects,
                rect => rect.X <= 260 &&
                        rect.Y <= 120 &&
                        rect.X + rect.Width >= 620 &&
                        rect.Y + rect.Height >= 320));
    }

    [Fact]
    public void Resolve_AnimationKeepsHostAnimatedShaderDirtyEvenWithoutCommitChanges()
    {
        var commit = CreateAnimatedCommit(includeTooltip: false);
        using var resultBuffer = new SceneDamageRectBufferWriter(16);
        using var scratchBuffer = new SceneDamageRectBufferWriter(16);

        var dirtyRects = SceneDamageEstimator.Resolve(
            commit,
            commit,
            [],
            SceneDamageReason.Animation,
            800,
            600,
            false,
            resultBuffer,
            scratchBuffer);

        Assert.True(
            Contains(
                dirtyRects,
                rect => rect.X <= 260 &&
                        rect.Y <= 120 &&
                        rect.X + rect.Width >= 620 &&
                        rect.Y + rect.Height >= 320));
    }

    private static SceneLayoutCommit CreateCommit(bool includeTooltip)
    {
        var nodes = new Dictionary<SceneNodeId, SceneGraphNode>
        {
            [Root] = new(SceneNodeKind.View, null, includeTooltip ? [Sidebar, Content, Tooltip] : [Sidebar, Content]),
            [Sidebar] = new(SceneNodeKind.View, Root, []),
            [Content] = new(SceneNodeKind.View, Root, [])
        };
        var layout = new Dictionary<SceneNodeId, SceneLayoutBox>
        {
            [Root] = new(SceneNodeKind.View, 0, 0, 800, 600, "#08111f"),
            [Sidebar] = new(SceneNodeKind.View, 0, 0, 200, 600, "#0f172a"),
            [Content] = new(SceneNodeKind.View, 220, 0, 580, 600, "#111827")
        };

        if (includeTooltip)
        {
            nodes[Tooltip] = new(SceneNodeKind.View, Root, []);
            layout[Tooltip] = new(SceneNodeKind.View, 40, 80, 120, 28, "#020617");
        }

        return new SceneLayoutCommit(
            Root,
            new SceneViewport(800, 600),
            nodes,
            layout,
            []);
    }

    private static SceneLayoutCommit CreateAnimatedCommit(bool includeTooltip)
    {
        var nodes = new Dictionary<SceneNodeId, SceneGraphNode>
        {
            [Root] = new(SceneNodeKind.View, null, includeTooltip ? [Sidebar, Content, Tooltip] : [Sidebar, Content]),
            [Sidebar] = new(SceneNodeKind.View, Root, []),
            [Content] = new(SceneNodeKind.View, Root, [Shader])
        };
        var layout = new Dictionary<SceneNodeId, SceneLayoutBox>
        {
            [Root] = new(SceneNodeKind.View, 0, 0, 800, 600, "#08111f"),
            [Sidebar] = new(SceneNodeKind.View, 0, 0, 200, 600, "#0f172a"),
            [Content] = new(SceneNodeKind.View, 220, 0, 580, 600, "#111827"),
            [Shader] = new(
                SceneNodeKind.View,
                260,
                120,
                360,
                200,
                BackgroundShader: new SceneRuntimeShader("shader", "shader-source", true))
        };

        if (includeTooltip)
        {
            nodes[Tooltip] = new(SceneNodeKind.View, Root, []);
            layout[Tooltip] = new(SceneNodeKind.View, 40, 80, 120, 28, "#020617");
        }

        return new SceneLayoutCommit(
            Root,
            new SceneViewport(800, 600),
            nodes,
            layout,
            [Shader]);
    }

    private static bool Contains(ReadOnlySpan<SceneDamageRect> dirtyRects, Func<SceneDamageRect, bool> predicate)
    {
        foreach (var dirtyRect in dirtyRects)
        {
            if (predicate(dirtyRect))
                return true;
        }

        return false;
    }
}
