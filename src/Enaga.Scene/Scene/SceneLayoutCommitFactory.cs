namespace Enaga.Scene;

public static class SceneLayoutCommitFactory
{
    public static SceneLayoutCommit Create(
        string rootId,
        SceneViewport viewport,
        IReadOnlyDictionary<string, SceneGraphNode> nodes,
        IReadOnlyDictionary<string, SceneLayoutBox> layout)
    {
        var resolvedLayout = ResolveLayoutContentSizes(nodes, layout);
        var hostAnimatedShaderRootIds = BuildHostAnimatedShaderRootIds(nodes, resolvedLayout);
        return new SceneLayoutCommit(rootId, viewport, nodes, resolvedLayout, hostAnimatedShaderRootIds)
        {
            PaintOrderIds = BuildPaintOrderIds(rootId, nodes, resolvedLayout)
        };
    }

    private static IReadOnlyDictionary<string, SceneLayoutBox> ResolveLayoutContentSizes(
        IReadOnlyDictionary<string, SceneGraphNode> nodes,
        IReadOnlyDictionary<string, SceneLayoutBox> layout)
    {
        Dictionary<string, SceneLayoutBox>? resolvedLayout = null;
        foreach (var (id, node) in nodes)
        {
            var currentLayout = (IReadOnlyDictionary<string, SceneLayoutBox>?)resolvedLayout ?? layout;
            if (node.NodeKind != SceneNodeKind.ScrollView || !currentLayout.TryGetValue(id, out var box))
                continue;

            var resolvedContentWidth = box.HorizontalScrollEnabled
                ? Math.Max(box.Width, box.ContentWidth)
                : box.Width;
            var resolvedContentHeight = Math.Max(box.Height, box.ContentHeight);
            if (box.HorizontalScrollEnabled && box.ContentWidth <= box.Width + 0.001f)
            {
                var inferredContentWidth = InferScrollContentWidth(id, box, nodes, currentLayout);
                resolvedContentWidth = Math.Max(resolvedContentWidth, inferredContentWidth);
            }

            if (box.ContentHeight <= box.Height + 0.001f)
            {
                var inferredContentHeight = InferScrollContentHeight(id, box, nodes, currentLayout);
                resolvedContentHeight = Math.Max(resolvedContentHeight, inferredContentHeight);
            }

            if (Math.Abs(resolvedContentWidth - box.ContentWidth) <= 0.001f &&
                Math.Abs(resolvedContentHeight - box.ContentHeight) <= 0.001f)
            {
                continue;
            }

            resolvedLayout ??= new Dictionary<string, SceneLayoutBox>(layout, StringComparer.Ordinal);
            resolvedLayout[id] = box with
            {
                ContentWidth = resolvedContentWidth,
                ContentHeight = resolvedContentHeight
            };
        }

        return resolvedLayout ?? layout;
    }

    private static float InferScrollContentWidth(
        string scrollViewId,
        SceneLayoutBox scrollBox,
        IReadOnlyDictionary<string, SceneGraphNode> nodes,
        IReadOnlyDictionary<string, SceneLayoutBox> layout)
    {
        if (!nodes.TryGetValue(scrollViewId, out var scrollNode) || scrollNode.Children.Count == 0)
            return scrollBox.Width;

        var maxRight = scrollBox.AbsLeft + scrollBox.PaddingLeft;
        var pending = new Stack<string>(scrollNode.Children);
        while (pending.Count > 0)
        {
            var nodeId = pending.Pop();
            if (!layout.TryGetValue(nodeId, out var childBox))
                continue;

            maxRight = Math.Max(maxRight, childBox.AbsLeft + childBox.Width);
            if (!nodes.TryGetValue(nodeId, out var childNode) || childNode.NodeKind == SceneNodeKind.ScrollView)
                continue;

            for (var index = childNode.Children.Count - 1; index >= 0; index--)
                pending.Push(childNode.Children[index]);
        }

        return Math.Max(scrollBox.Width, maxRight - scrollBox.AbsLeft + scrollBox.PaddingRight);
    }

    private static float InferScrollContentHeight(
        string scrollViewId,
        SceneLayoutBox scrollBox,
        IReadOnlyDictionary<string, SceneGraphNode> nodes,
        IReadOnlyDictionary<string, SceneLayoutBox> layout)
    {
        if (!nodes.TryGetValue(scrollViewId, out var scrollNode) || scrollNode.Children.Count == 0)
            return scrollBox.Height;

        var maxBottom = scrollBox.AbsTop + scrollBox.PaddingTop;
        var pending = new Stack<string>(scrollNode.Children);
        while (pending.Count > 0)
        {
            var nodeId = pending.Pop();
            if (!layout.TryGetValue(nodeId, out var childBox))
                continue;

            maxBottom = Math.Max(maxBottom, childBox.AbsTop + childBox.Height);
            if (!nodes.TryGetValue(nodeId, out var childNode) || childNode.NodeKind == SceneNodeKind.ScrollView)
                continue;

            for (var index = childNode.Children.Count - 1; index >= 0; index--)
                pending.Push(childNode.Children[index]);
        }

        return Math.Max(scrollBox.Height, maxBottom - scrollBox.AbsTop + scrollBox.PaddingBottom);
    }

    private static string[] BuildHostAnimatedShaderRootIds(
        IReadOnlyDictionary<string, SceneGraphNode> nodes,
        IReadOnlyDictionary<string, SceneLayoutBox> layout)
    {
        var roots = new List<string>();
        foreach (var (id, box) in layout)
        {
            if (!IsHostAnimatedRuntimeShader(box))
                continue;

            var hasAnimatedAncestor = false;
            var currentId = id;
            while (nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
            {
                if (layout.TryGetValue(parentId, out var parentBox) && IsHostAnimatedRuntimeShader(parentBox))
                {
                    hasAnimatedAncestor = true;
                    break;
                }

                currentId = parentId;
            }

            if (!hasAnimatedAncestor)
                roots.Add(id);
        }

        return roots.Count == 0 ? [] : [.. roots];
    }

    private static string[] BuildPaintOrderIds(
        string rootId,
        IReadOnlyDictionary<string, SceneGraphNode> nodes,
        IReadOnlyDictionary<string, SceneLayoutBox> layout)
    {
        if (!nodes.ContainsKey(rootId) || !layout.ContainsKey(rootId))
            return [];

        var order = new List<string>(layout.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(rootId);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!visited.Add(id) ||
                !nodes.TryGetValue(id, out var node) ||
                !layout.ContainsKey(id))
            {
                continue;
            }

            order.Add(id);
            for (var index = node.Children.Count - 1; index >= 0; index--)
                pending.Push(node.Children[index]);
        }

        return order.Count == 0 ? [] : order.ToArray();
    }

    private static bool IsHostAnimatedRuntimeShader(SceneLayoutBox box)
        => box.BackgroundShader?.HostTime == true;
}
