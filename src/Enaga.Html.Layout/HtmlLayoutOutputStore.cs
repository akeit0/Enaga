using Enaga.Layout;

namespace Enaga.Html;

internal sealed class HtmlLayoutOutputStore
{
    private HtmlParsedDocument? document;
    private uint styleStoreGeneration;
    private readonly Dictionary<LayoutNodeId, List<LayoutNodeId>> childrenByNode = new();
    private readonly Dictionary<LayoutNodeId, LayoutNodeId> parentByNode = new();
    private readonly HashSet<LayoutNodeId> propagationBoundaries = new();

    public LayoutOutputCache Outputs { get; } = new();

    public void BeginDocument(HtmlParsedDocument nextDocument, uint nextStyleStoreGeneration)
    {
        if (ReferenceEquals(document, nextDocument))
        {
            styleStoreGeneration = Math.Max(styleStoreGeneration, nextStyleStoreGeneration);
            return;
        }

        document = nextDocument;
        styleStoreGeneration = nextStyleStoreGeneration;
        Outputs.Clear();
        childrenByNode.Clear();
        parentByNode.Clear();
        propagationBoundaries.Clear();
    }

    public void UpdateLayoutTree(string rootId, IReadOnlyList<HtmlSceneNode> rootChildren)
    {
        var nodeCount = CountNodes(rootChildren) + 1;
        childrenByNode.EnsureCapacity(nodeCount);
        parentByNode.EnsureCapacity(Math.Max(0, nodeCount - 1));
        propagationBoundaries.EnsureCapacity(nodeCount);
        Outputs.EnsureCapacity(nodeCount, Math.Max(nodeCount, nodeCount * 4));

        ClearChildLists();
        parentByNode.Clear();
        propagationBoundaries.Clear();
        var rootNodeId = HtmlLayoutVersion.ToLayoutNodeId(rootId);
        var rootChildIds = GetChildList(rootNodeId, rootChildren.Count);
        for (var index = 0; index < rootChildren.Count; index++)
        {
            var child = rootChildren[index];
            var childId = HtmlLayoutVersion.ToLayoutNodeId(child.Id);
            parentByNode[childId] = rootNodeId;
            rootChildIds.Add(childId);
            AddChildren(child);
        }

        childrenByNode[rootNodeId] = rootChildIds;
    }

    public void InvalidateNodes(HtmlLayoutDirtySet dirtyNodes)
    {
        foreach (var (nodeId, bits) in dirtyNodes.Entries)
        {
            if ((bits & HtmlLayoutDirtyBits.Self) != 0)
                Outputs.InvalidateNode(nodeId);
            if ((bits & HtmlLayoutDirtyBits.Subtree) != 0)
                InvalidateDescendants(nodeId);
            if ((bits & HtmlLayoutDirtyBits.Ancestors) != 0)
                InvalidateAncestors(nodeId);
        }
    }

    private static int CountNodes(IReadOnlyList<HtmlSceneNode> nodes)
    {
        var count = 0;
        for (var index = 0; index < nodes.Count; index++)
        {
            count++;
            count += CountNodes(nodes[index].Children);
        }

        return count;
    }

    private void AddChildren(HtmlSceneNode node)
    {
        var nodeId = HtmlLayoutVersion.ToLayoutNodeId(node.Id);
        var childIds = GetChildList(nodeId, node.Children.Count);
        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            var childId = HtmlLayoutVersion.ToLayoutNodeId(child.Id);
            parentByNode[childId] = nodeId;
            childIds.Add(childId);
            AddChildren(child);
        }

        childrenByNode[nodeId] = childIds;
        if (node.Style.StopsLayoutDirtyPropagation)
            propagationBoundaries.Add(nodeId);
    }

    private void ClearChildLists()
    {
        foreach (var list in childrenByNode.Values)
            list.Clear();
    }

    private List<LayoutNodeId> GetChildList(LayoutNodeId nodeId, int capacity)
    {
        if (!childrenByNode.TryGetValue(nodeId, out var list))
        {
            list = new List<LayoutNodeId>(capacity);
            childrenByNode[nodeId] = list;
            return list;
        }

        if (list.Capacity < capacity)
            list.Capacity = capacity;
        return list;
    }

    private void InvalidateDescendants(LayoutNodeId nodeId)
    {
        if (!childrenByNode.TryGetValue(nodeId, out var children))
            return;

        for (var index = 0; index < children.Count; index++)
        {
            var childId = children[index];
            Outputs.InvalidateNode(childId);
            InvalidateDescendants(childId);
        }
    }

    private void InvalidateAncestors(LayoutNodeId nodeId)
    {
        var current = nodeId;
        while (parentByNode.TryGetValue(current, out var parentId))
        {
            Outputs.InvalidateNode(parentId);
            if (propagationBoundaries.Contains(parentId))
                break;

            current = parentId;
        }
    }
}
