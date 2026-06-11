namespace Enaga.Scene;

public sealed class SceneStore
{
    private sealed class MutableSceneNode
    {
        public List<SceneNodeId> Children { get; } = [];
        public SceneNodeKind Kind { get; set; }
        public string? Label { get; set; }
        public SceneNodeId? ParentId { get; set; }
    }

    private readonly Dictionary<SceneNodeId, SceneLayoutBox> layout = new();
    private readonly Dictionary<SceneNodeId, MutableSceneNode> nodes = new();
    private readonly SceneNodeMap<SceneLayoutBox>[] snapshotLayoutBuffers = [new(), new()];
    private readonly SceneNodeMap<SceneGraphNode>[] snapshotNodeBuffers = [new(), new()];
    private readonly object sync = new();
    private SceneLayoutCommit? cachedSnapshot;
    private SceneNodeId rootId;
    private int snapshotBufferIndex;
    private bool snapshotDirty = true;
    private SceneViewport viewport;

    public SceneStore(SceneNodeId rootId, SceneViewport viewport)
    {
        this.rootId = rootId;
        this.viewport = viewport;
        nodes[rootId] = new MutableSceneNode
        {
            Kind = SceneNodeKind.View,
            Label = rootId.ToString(),
        };
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

    public void Reset(SceneNodeId nextRootId, SceneViewport nextViewport)
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

    public void UpsertNode(
        SceneNodeId id,
        SceneNodeKind kind,
        SceneNodeId? parentId = null,
        string? label = null,
        SceneLayoutBox? nodeLayout = null
    )
    {
        lock (sync)
        {
            ApplyUpsert(id, kind, parentId, label);
            if (nodeLayout is not null)
                layout[id] = nodeLayout;
            snapshotDirty = true;
        }
    }

    public void SetChildren(SceneNodeId parentId, SceneNodeId[] children)
    {
        lock (sync)
        {
            ApplySetChildren(parentId, children);
            snapshotDirty = true;
        }
    }

    public void SetLayout(SceneNodeId id, SceneLayoutBox nodeLayout)
    {
        lock (sync)
        {
            layout[id] = nodeLayout;
            snapshotDirty = true;
        }
    }

    public void RemoveNode(SceneNodeId id)
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

            snapshotBufferIndex ^= 1;
            var snapshotNodes = snapshotNodeBuffers[snapshotBufferIndex];
            var snapshotLayout = snapshotLayoutBuffers[snapshotBufferIndex];
            snapshotNodes.Clear();
            snapshotNodes.EnsureCapacity(nodes.Count);
            foreach (var (id, node) in nodes)
                snapshotNodes[id] = new SceneGraphNode(
                    node.Kind,
                    node.ParentId,
                    [.. node.Children],
                    node.Label
                );

            snapshotLayout.CopyFrom(layout);
            cachedSnapshot = SceneLayoutCommitFactory.Create(
                rootId,
                viewport,
                snapshotNodes,
                snapshotLayout
            );
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

    private void ApplyReset(SceneNodeId nextRootId, SceneViewport nextViewport)
    {
        rootId = nextRootId;
        viewport = nextViewport;
        nodes.Clear();
        layout.Clear();
        nodes[rootId] = new MutableSceneNode
        {
            Kind = SceneNodeKind.View,
            Label = rootId.ToString(),
        };
    }

    private void ApplyUpsert(
        SceneNodeId id,
        SceneNodeKind kind,
        SceneNodeId? parentId,
        string? label
    )
    {
        if (!nodes.TryGetValue(id, out var node))
        {
            node = new MutableSceneNode();
            nodes[id] = node;
        }

        if (
            node.ParentId is { } previousParentId
            && previousParentId != parentId
            && nodes.TryGetValue(previousParentId, out var previousParent)
        )
        {
            previousParent.Children.Remove(id);
        }

        node.Kind = kind;
        node.ParentId = parentId;
        node.Label = label;

        if (parentId is { } resolvedParentId)
        {
            var parent = EnsureNode(resolvedParentId);
            if (!parent.Children.Contains(id))
                parent.Children.Add(id);
        }
    }

    private void ApplySetChildren(SceneNodeId parentId, ReadOnlySpan<SceneNodeId> children)
    {
        var parent = EnsureNode(parentId);
        parent.Children.Clear();
        parent.Children.EnsureCapacity(children.Length);
        for (var index = 0; index < children.Length; index++)
        {
            var childId = children[index];
            parent.Children.Add(childId);
            var child = EnsureNode(childId);
            child.ParentId = parentId;
        }
    }

    private MutableSceneNode EnsureNode(SceneNodeId id)
    {
        if (nodes.TryGetValue(id, out var node))
            return node;

        node = new MutableSceneNode();
        nodes[id] = node;
        return node;
    }

    private void RemoveNodeCore(SceneNodeId id)
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
