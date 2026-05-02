using Enaga.Scene;

namespace Enaga.Input;

public readonly record struct SceneScreenBounds(float Left, float Top, float Right, float Bottom, int Depth)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;

    public bool Contains(float x, float y)
        => x >= Left && x <= Right && y >= Top && y <= Bottom;

    public static bool IsHigherPriority(SceneScreenBounds candidate, int candidateZOrder, SceneScreenBounds current, int currentZOrder)
        => candidate.Depth > current.Depth ||
           (candidate.Depth == current.Depth && candidateZOrder > currentZOrder);
}

public static class SceneScreenGeometry
{
    public static SceneLayoutBox ResolveScreenBox(SceneLayoutCommit commit, string id)
    {
        if (!commit.Layout.TryGetValue(id, out var box))
            return default!;

        return ResolveScreenBox(commit, commit.Layout, id, box);
    }

    public static SceneLayoutBox ResolveScreenBox(
        SceneLayoutCommit commit,
        IReadOnlyDictionary<string, SceneLayoutBox> layout,
        string id,
        SceneLayoutBox box)
    {
        var left = box.AbsLeft;
        var top = box.AbsTop;
        if (!commit.Nodes.TryGetValue(id, out var node))
            return box;

        var parentId = node.ParentId;
        while (parentId is not null)
        {
            if (layout.TryGetValue(parentId, out var parentBox) && parentBox.NodeKind == SceneNodeKind.ScrollView)
            {
                left -= parentBox.ScrollX;
                top -= parentBox.ScrollY;
            }

            if (!commit.Nodes.TryGetValue(parentId, out var parentNode))
                break;

            parentId = parentNode.ParentId;
        }

        return Math.Abs(left - box.AbsLeft) < 0.001f && Math.Abs(top - box.AbsTop) < 0.001f
            ? box
            : box with { AbsLeft = left, AbsTop = top };
    }

    public static bool TryGetNodeScreenBounds(SceneLayoutCommit commit, string nodeId, out SceneScreenBounds bounds)
        => TryGetNodeScreenBounds(commit, commit.Layout, nodeId, out bounds);

    public static bool TryGetNodeScreenBounds(
        SceneLayoutCommit commit,
        IReadOnlyDictionary<string, SceneLayoutBox> layout,
        string nodeId,
        out SceneScreenBounds bounds)
    {
        bounds = default;
        if (!layout.TryGetValue(nodeId, out var box) || !commit.Nodes.TryGetValue(nodeId, out var node))
            return false;

        var left = box.AbsLeft;
        var top = box.AbsTop;
        var depth = 0;
        var parentId = node.ParentId;
        while (parentId is not null)
        {
            depth++;
            if (layout.TryGetValue(parentId, out var parentBox) && parentBox.NodeKind == SceneNodeKind.ScrollView)
            {
                left -= parentBox.ScrollX;
                top -= parentBox.ScrollY;
            }

            if (!commit.Nodes.TryGetValue(parentId, out var parentNode))
                break;

            parentId = parentNode.ParentId;
        }

        bounds = new SceneScreenBounds(left, top, left + box.Width, top + box.Height, depth);
        return true;
    }

    public static bool TryGetScrollViewScreenBox(SceneLayoutCommit commit, string nodeId, out SceneLayoutBox box, out SceneScreenBounds bounds)
    {
        box = default!;
        bounds = default;
        if (!commit.Layout.TryGetValue(nodeId, out var layoutBox) ||
            layoutBox.NodeKind != SceneNodeKind.ScrollView ||
            !TryGetNodeScreenBounds(commit, nodeId, out bounds))
        {
            return false;
        }

        box = layoutBox with
        {
            AbsLeft = bounds.Left,
            AbsTop = bounds.Top
        };
        return true;
    }
}
