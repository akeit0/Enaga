namespace Enaga.Scene;

public static class SceneLayoutCommitFactory
{
    public static SceneLayoutCommit Create(
        SceneNodeId rootId,
        SceneViewport viewport,
        SceneNodeMap<SceneGraphNode> nodes,
        SceneNodeMap<SceneLayoutBox> layout)
    {
        var resolvedLayout = ResolveLayoutContentSizes(nodes, layout);
        var hostAnimatedShaderRootIds = BuildHostAnimatedShaderRootIds(nodes, resolvedLayout);
        return new SceneLayoutCommit(rootId, viewport, nodes, resolvedLayout, hostAnimatedShaderRootIds)
        {
            PaintOrderIds = BuildPaintOrderIds(rootId, nodes, resolvedLayout)
        };
    }

    public static SceneLayoutCommit Create(
        SceneNodeId rootId,
        SceneViewport viewport,
        IReadOnlyDictionary<SceneNodeId, SceneGraphNode> nodes,
        IReadOnlyDictionary<SceneNodeId, SceneLayoutBox> layout)
        => Create(
            rootId,
            viewport,
            nodes as SceneNodeMap<SceneGraphNode> ?? new SceneNodeMap<SceneGraphNode>(nodes),
            layout as SceneNodeMap<SceneLayoutBox> ?? new SceneNodeMap<SceneLayoutBox>(layout));

    private static SceneNodeMap<SceneLayoutBox> ResolveLayoutContentSizes(
        SceneNodeMap<SceneGraphNode> nodes,
        SceneNodeMap<SceneLayoutBox> layout)
    {
        foreach (var (id, node) in nodes)
        {
            if (node.NodeKind != SceneNodeKind.ScrollView || !layout.TryGetValue(id, out var box))
                continue;

            var resolvedContentWidth = box.HorizontalScrollEnabled
                ? Math.Max(box.Width, box.ContentWidth)
                : box.Width;
            var resolvedContentHeight = Math.Max(box.Height, box.ContentHeight);
            if (box.HorizontalScrollEnabled && box.ContentWidth <= box.Width + 0.001f)
            {
                var inferredContentWidth = InferScrollContentWidth(id, box, nodes, layout);
                resolvedContentWidth = Math.Max(resolvedContentWidth, inferredContentWidth);
            }

            if (box.ContentHeight <= box.Height + 0.001f)
            {
                var inferredContentHeight = InferScrollContentHeight(id, box, nodes, layout);
                resolvedContentHeight = Math.Max(resolvedContentHeight, inferredContentHeight);
            }

            if (Math.Abs(resolvedContentWidth - box.ContentWidth) <= 0.001f &&
                Math.Abs(resolvedContentHeight - box.ContentHeight) <= 0.001f)
            {
                continue;
            }

            layout[id] = box with
            {
                ContentWidth = resolvedContentWidth,
                ContentHeight = resolvedContentHeight
            };
        }

        return layout;
    }

    private static float InferScrollContentWidth(
        SceneNodeId scrollViewId,
        SceneLayoutBox scrollBox,
        SceneNodeMap<SceneGraphNode> nodes,
        SceneNodeMap<SceneLayoutBox> layout)
    {
        if (!nodes.TryGetValue(scrollViewId, out var scrollNode) || scrollNode.Children.Count == 0)
            return scrollBox.Width;

        var maxRight = scrollBox.AbsLeft + scrollBox.PaddingLeft;
        var pending = new Stack<SceneNodeId>(scrollNode.Children);
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
        SceneNodeId scrollViewId,
        SceneLayoutBox scrollBox,
        SceneNodeMap<SceneGraphNode> nodes,
        SceneNodeMap<SceneLayoutBox> layout)
    {
        if (!nodes.TryGetValue(scrollViewId, out var scrollNode) || scrollNode.Children.Count == 0)
            return scrollBox.Height;

        var maxBottom = scrollBox.AbsTop + scrollBox.PaddingTop;
        var pending = new Stack<SceneNodeId>(scrollNode.Children);
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

    private static SceneNodeId[] BuildHostAnimatedShaderRootIds(
        SceneNodeMap<SceneGraphNode> nodes,
        SceneNodeMap<SceneLayoutBox> layout)
    {
        var roots = new List<SceneNodeId>();
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

    private static SceneNodeId[] BuildPaintOrderIds(
        SceneNodeId rootId,
        SceneNodeMap<SceneGraphNode> nodes,
        SceneNodeMap<SceneLayoutBox> layout)
    {
        if (!nodes.ContainsKey(rootId) || !layout.ContainsKey(rootId))
            return [];

        var order = new List<SceneNodeId>(layout.Count);
        var visited = new HashSet<SceneNodeId>();
        var pending = new Stack<SceneNodeId>();
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
