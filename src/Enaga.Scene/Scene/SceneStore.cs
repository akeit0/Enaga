namespace Enaga.Scene;

public sealed class SceneStore
{
    private sealed class MutableSceneNode
    {
        public List<string> Children { get; } = [];
        public SceneNodeKind Kind { get; set; }
        public string? Label { get; set; }
        public string? ParentId { get; set; }
    }

    private readonly Dictionary<string, SceneLayoutBox> layout = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableSceneNode> nodes = new(StringComparer.Ordinal);
    private readonly object sync = new();
    private SceneLayoutCommit? cachedSnapshot;
    private string rootId;
    private bool snapshotDirty = true;
    private SceneViewport viewport;

    public SceneStore(string rootId, SceneViewport viewport)
    {
        this.rootId = rootId;
        this.viewport = viewport;
        nodes[rootId] = new MutableSceneNode { Kind = SceneNodeKind.View, Label = rootId };
    }

    public void Apply(SceneMutation mutation)
    {
        lock (sync)
        {
            ApplyMutation(mutation);
            snapshotDirty = true;
        }
    }

    public void Apply(ReadOnlySpan<SceneMutation> mutations)
    {
        lock (sync)
        {
            if (mutations.IsEmpty)
                return;

            for (var index = 0; index < mutations.Length; index++)
                ApplyMutation(mutations[index]);

            snapshotDirty = true;
        }
    }

    public void Reset(string nextRootId, SceneViewport nextViewport)
    {
        lock (sync)
        {
            ApplyReset(nextRootId, nextViewport);
            snapshotDirty = true;
        }
    }

    public void SetViewport(SceneViewport nextViewport)
    {
        lock (sync)
        {
            viewport = nextViewport;
            snapshotDirty = true;
        }
    }

    public void UpsertNode(string id, SceneNodeKind kind, string? parentId = null, string? label = null, SceneLayoutBox? nodeLayout = null)
    {
        lock (sync)
        {
            ApplyUpsert(id, kind, parentId, label);
            if (nodeLayout is not null)
                layout[id] = nodeLayout;
            snapshotDirty = true;
        }
    }

    public void SetChildren(string parentId, IReadOnlyList<string> children)
    {
        lock (sync)
        {
            ApplySetChildren(parentId, children);
            snapshotDirty = true;
        }
    }

    public void SetLayout(string id, SceneLayoutBox nodeLayout)
    {
        lock (sync)
        {
            layout[id] = nodeLayout;
            snapshotDirty = true;
        }
    }

    public void RemoveNode(string id)
    {
        lock (sync)
        {
            RemoveNodeCore(id);
            snapshotDirty = true;
        }
    }

    public SceneLayoutCommit Snapshot()
    {
        lock (sync)
        {
            if (!snapshotDirty && cachedSnapshot is not null)
                return cachedSnapshot;

            var nodeSnapshot = new Dictionary<string, SceneGraphNode>(nodes.Count, StringComparer.Ordinal);
            foreach (var (id, node) in nodes)
                nodeSnapshot[id] = new SceneGraphNode(node.Kind, node.ParentId, [.. node.Children], node.Label);

            var layoutSnapshot = new Dictionary<string, SceneLayoutBox>(layout, StringComparer.Ordinal);
            cachedSnapshot = SceneLayoutCommitFactory.Create(
                rootId,
                viewport,
                nodeSnapshot,
                layoutSnapshot);
            snapshotDirty = false;
            return cachedSnapshot;
        }
    }

    private void ApplyMutation(SceneMutation mutation)
    {
        switch (mutation)
        {
            case ResetSceneMutation reset:
                ApplyReset(reset.RootId, reset.Viewport);
                break;
            case SetViewportMutation setViewport:
                viewport = setViewport.Viewport;
                break;
            case UpsertNodeMutation upsert:
                ApplyUpsert(upsert.Id, upsert.Kind, upsert.ParentId, upsert.Label);
                break;
            case SetChildrenMutation setChildren:
                ApplySetChildren(setChildren.ParentId, setChildren.Children);
                break;
            case SetLayoutMutation setLayout:
                layout[setLayout.Id] = setLayout.Layout;
                break;
            case RemoveNodeMutation remove:
                RemoveNodeCore(remove.Id);
                break;
        }
    }

    private void ApplyReset(string nextRootId, SceneViewport nextViewport)
    {
        rootId = nextRootId;
        viewport = nextViewport;
        nodes.Clear();
        layout.Clear();
        nodes[rootId] = new MutableSceneNode { Kind = SceneNodeKind.View, Label = rootId };
    }

    private void ApplyUpsert(string id, SceneNodeKind kind, string? parentId, string? label)
    {
        if (!nodes.TryGetValue(id, out var node))
        {
            node = new MutableSceneNode();
            nodes[id] = node;
        }

        if (node.ParentId is { } previousParentId &&
            previousParentId != parentId &&
            nodes.TryGetValue(previousParentId, out var previousParent))
        {
            previousParent.Children.Remove(id);
        }

        node.Kind = kind;
        node.ParentId = parentId;
        node.Label = label;

        if (parentId is not null)
        {
            var parent = EnsureNode(parentId);
            if (!parent.Children.Contains(id))
                parent.Children.Add(id);
        }
    }

    private void ApplySetChildren(string parentId, IReadOnlyList<string> children)
    {
        var parent = EnsureNode(parentId);
        parent.Children.Clear();
        parent.Children.AddRange(children);
        foreach (var childId in children)
        {
            var child = EnsureNode(childId);
            child.ParentId = parentId;
        }
    }

    private MutableSceneNode EnsureNode(string id)
    {
        if (nodes.TryGetValue(id, out var node))
            return node;

        node = new MutableSceneNode();
        nodes[id] = node;
        return node;
    }

    private void RemoveNodeCore(string id)
    {
        if (id == rootId || !nodes.TryGetValue(id, out var node))
            return;

        for (var index = node.Children.Count - 1; index >= 0; index--)
            RemoveNodeCore(node.Children[index]);

        if (node.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent))
            parent.Children.Remove(id);

        nodes.Remove(id);
        layout.Remove(id);
    }
}
