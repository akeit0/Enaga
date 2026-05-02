using Enaga.Rendering;
using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneDamageEstimatorTests
{
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
        var nodes = new Dictionary<string, SceneGraphNode>(StringComparer.Ordinal)
        {
            ["root"] = new(SceneNodeKind.View, null, includeTooltip ? ["sidebar", "content", "tooltip"] : ["sidebar", "content"]),
            ["sidebar"] = new(SceneNodeKind.View, "root", []),
            ["content"] = new(SceneNodeKind.View, "root", [])
        };
        var layout = new Dictionary<string, SceneLayoutBox>(StringComparer.Ordinal)
        {
            ["root"] = new(SceneNodeKind.View, 0, 0, 800, 600, "#08111f"),
            ["sidebar"] = new(SceneNodeKind.View, 0, 0, 200, 600, "#0f172a"),
            ["content"] = new(SceneNodeKind.View, 220, 0, 580, 600, "#111827")
        };

        if (includeTooltip)
        {
            nodes["tooltip"] = new(SceneNodeKind.View, "root", []);
            layout["tooltip"] = new(SceneNodeKind.View, 40, 80, 120, 28, "#020617");
        }

        return new SceneLayoutCommit(
            "root",
            new SceneViewport(800, 600),
            nodes,
            layout,
            []);
    }

    private static SceneLayoutCommit CreateAnimatedCommit(bool includeTooltip)
    {
        var nodes = new Dictionary<string, SceneGraphNode>(StringComparer.Ordinal)
        {
            ["root"] = new(SceneNodeKind.View, null, includeTooltip ? ["sidebar", "content", "tooltip"] : ["sidebar", "content"]),
            ["sidebar"] = new(SceneNodeKind.View, "root", []),
            ["content"] = new(SceneNodeKind.View, "root", ["shader"])
        };
        var layout = new Dictionary<string, SceneLayoutBox>(StringComparer.Ordinal)
        {
            ["root"] = new(SceneNodeKind.View, 0, 0, 800, 600, "#08111f"),
            ["sidebar"] = new(SceneNodeKind.View, 0, 0, 200, 600, "#0f172a"),
            ["content"] = new(SceneNodeKind.View, 220, 0, 580, 600, "#111827"),
            ["shader"] = new(
                SceneNodeKind.View,
                260,
                120,
                360,
                200,
                BackgroundShader: new SceneRuntimeShader("shader", "shader-source", true))
        };

        if (includeTooltip)
        {
            nodes["tooltip"] = new(SceneNodeKind.View, "root", []);
            layout["tooltip"] = new(SceneNodeKind.View, 40, 80, 120, 28, "#020617");
        }

        return new SceneLayoutCommit(
            "root",
            new SceneViewport(800, 600),
            nodes,
            layout,
            ["shader"]);
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
