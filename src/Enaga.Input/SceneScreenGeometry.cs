using Enaga.Scene;

namespace Enaga.Input;

public readonly record struct SceneScreenBounds(
    float Left,
    float Top,
    float Right,
    float Bottom,
    int Depth
)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;

    public bool Contains(float x, float y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    public static bool IsHigherPriority(
        SceneScreenBounds candidate,
        int candidateZOrder,
        SceneScreenBounds current,
        int currentZOrder
    ) =>
        candidate.Depth > current.Depth
        || (candidate.Depth == current.Depth && candidateZOrder > currentZOrder);
}

public static class SceneScreenGeometry
{
    public static SceneLayoutBox ResolveScreenBox(SceneLayoutCommit commit, SceneNodeId id)
    {
        if (!commit.Layout.TryGetValue(id, out var box))
            return default!;

        return ResolveScreenBox(commit, commit.Layout, id, box);
    }

    public static SceneLayoutBox ResolveScreenBox(
        SceneLayoutCommit commit,
        IReadOnlyDictionary<SceneNodeId, SceneLayoutBox> layout,
        SceneNodeId id,
        SceneLayoutBox box
    )
    {
        var geometry = box.Geometry;
        var left = geometry.AbsLeft;
        var top = geometry.AbsTop;
        if (!commit.Nodes.TryGetValue(id, out var node))
            return box;

        var parentId = node.ParentId;
        while (parentId is { } resolvedParentId)
        {
            if (
                layout.TryGetValue(resolvedParentId, out var parentBox)
                && parentBox.Scroll is { IsScrollContainer: true } scroll
            )
            {
                left -= scroll.ScrollX;
                top -= scroll.ScrollY;
            }

            if (!commit.Nodes.TryGetValue(resolvedParentId, out var parentNode))
                break;

            parentId = parentNode.ParentId;
        }

        return
            Math.Abs(left - geometry.AbsLeft) < 0.001f && Math.Abs(top - geometry.AbsTop) < 0.001f
            ? box
            : box with
            {
                AbsLeft = left,
                AbsTop = top,
            };
    }

    public static bool TryGetNodeScreenBounds(
        SceneLayoutCommit commit,
        SceneNodeId nodeId,
        out SceneScreenBounds bounds
    ) => TryGetNodeScreenBounds(commit, commit.Layout, nodeId, out bounds);

    public static bool TryGetNodeScreenBounds(
        SceneLayoutCommit commit,
        IReadOnlyDictionary<SceneNodeId, SceneLayoutBox> layout,
        SceneNodeId nodeId,
        out SceneScreenBounds bounds
    )
    {
        bounds = default;
        if (
            !layout.TryGetValue(nodeId, out var box)
            || !commit.Nodes.TryGetValue(nodeId, out var node)
        )
            return false;

        var geometry = box.Geometry;
        var left = geometry.AbsLeft;
        var top = geometry.AbsTop;
        var depth = 0;
        var parentId = node.ParentId;
        while (parentId is { } resolvedParentId)
        {
            depth++;
            if (
                layout.TryGetValue(resolvedParentId, out var parentBox)
                && parentBox.Scroll is { IsScrollContainer: true } scroll
            )
            {
                left -= scroll.ScrollX;
                top -= scroll.ScrollY;
            }

            if (!commit.Nodes.TryGetValue(resolvedParentId, out var parentNode))
                break;

            parentId = parentNode.ParentId;
        }

        bounds = new SceneScreenBounds(
            left,
            top,
            left + geometry.Width,
            top + geometry.Height,
            depth
        );
        return true;
    }

    public static bool TryGetScrollViewScreenBox(
        SceneLayoutCommit commit,
        SceneNodeId nodeId,
        out SceneLayoutBox box,
        out SceneScreenBounds bounds
    )
    {
        box = default!;
        bounds = default;
        if (
            !commit.Layout.TryGetValue(nodeId, out var layoutBox)
            || layoutBox.NodeKind != SceneNodeKind.ScrollView
            || !TryGetNodeScreenBounds(commit, nodeId, out bounds)
        )
        {
            return false;
        }

        box = layoutBox with { AbsLeft = bounds.Left, AbsTop = bounds.Top };
        return true;
    }
}
