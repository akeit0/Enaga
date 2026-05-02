using Enaga.Layout;
using Enaga.Html.Dom;
using Enaga.Html.Style;

namespace Enaga.Html;

internal sealed class HtmlSceneVersionStore
{
    private static readonly HtmlSceneVersionAdapter Adapter = new();
    private readonly DomElementStyleStore<HtmlComputedStyle> domStore = new();
    private readonly Dictionary<HtmlNodeId, HtmlSceneNode> domLayoutIdentities = new();
    private readonly ElementStyleLayoutStore<string, HtmlComputedStyle, HtmlSceneNode> generatedStore = new(StringComparer.Ordinal);
    private readonly HtmlLayoutDirtySet layoutDirtyNodes = new();

    public RestyleHint LastInvalidationHints { get; private set; }

    public Enaga.Html.Style.RenderDamage LastDamage { get; private set; }

    public uint Generation { get; private set; }

    public HtmlLayoutDirtySet LastLayoutDirtyNodes => layoutDirtyNodes;

    public HtmlStyleInvalidationSet PendingInvalidations { get; } = new();

    public void MarkCacheHit()
    {
        LastInvalidationHints = RestyleHint.None;
        LastDamage = Enaga.Html.Style.RenderDamage.None;
        layoutDirtyNodes.Clear();
    }

    public IReadOnlyList<HtmlSceneNode> AssignVersions(IReadOnlyList<HtmlSceneNode> nodes)
    {
        LastInvalidationHints = RestyleHint.None;
        LastDamage = Enaga.Html.Style.RenderDamage.None;
        layoutDirtyNodes.Clear();
        var versioned = AssignVersionsCore(nodes);
        ConsumePendingInvalidations();
        Generation = Math.Max(Generation, domStore.Generation);
        return versioned;
    }

    public void ApplySnapshot(HtmlElementSnapshot snapshot)
    {
        var invalidation = domStore.ApplySnapshot(snapshot);
        PendingInvalidations.Add(invalidation);
        LastInvalidationHints |= invalidation.RestyleHint;
        LastDamage |= HtmlElementSnapshotInvalidator.EstimateDamage(invalidation.Invalidation);
        Generation = Math.Max(Generation, domStore.Generation);
    }

    public void ApplyPseudoStateSnapshot(IReadOnlySet<HtmlNodeId>? oldNodeIds, IReadOnlySet<HtmlNodeId>? newNodeIds, HtmlPseudoState pseudoState)
    {
        if (oldNodeIds is not null)
        {
            foreach (var nodeId in oldNodeIds)
            {
                if (newNodeIds?.Contains(nodeId) == true)
                    continue;

                ApplyPseudoStateSnapshot(nodeId, pseudoState, HtmlPseudoState.None);
            }
        }

        if (newNodeIds is not null)
        {
            foreach (var nodeId in newNodeIds)
            {
                if (oldNodeIds?.Contains(nodeId) == true)
                    continue;

                ApplyPseudoStateSnapshot(nodeId, HtmlPseudoState.None, pseudoState);
            }
        }
    }

    public void ApplyPseudoStateSnapshot(HtmlNodeId? oldNodeId, HtmlNodeId? newNodeId, HtmlPseudoState pseudoState)
    {
        if (oldNodeId == newNodeId)
            return;

        if (oldNodeId is { } oldId && oldId.IsValid)
            ApplyPseudoStateSnapshot(oldId, pseudoState, HtmlPseudoState.None);
        if (newNodeId is { } newId && newId.IsValid)
            ApplyPseudoStateSnapshot(newId, HtmlPseudoState.None, pseudoState);
    }

    private void ApplyPseudoStateSnapshot(HtmlNodeId nodeId, HtmlPseudoState oldState, HtmlPseudoState newState)
    {
        if (oldState == newState)
            return;

        ApplySnapshot(new HtmlElementSnapshot(
            nodeId,
            OldId: null,
            NewId: null,
            OldClass: null,
            NewClass: null,
            HtmlAttributeChangeMask.None,
            oldState,
            newState));
    }

    private IReadOnlyList<HtmlSceneNode> AssignVersionsCore(IReadOnlyList<HtmlSceneNode> nodes)
    {
        if (nodes.Count == 0)
            return nodes;

        var versioned = new HtmlSceneNode[nodes.Count];
        for (var index = 0; index < nodes.Count; index++)
            versioned[index] = AssignVersionsCore(nodes[index]);
        return versioned;
    }

    private HtmlSceneNode AssignVersionsCore(HtmlSceneNode node)
    {
        var children = AssignVersionsCore(node.Children);
        var candidate = ReferenceEquals(children, node.Children)
            ? node
            : node with { Children = children };
        if (!candidate.DomNodeId.IsValid)
        {
            var result = generatedStore.AssignVersions([candidate], Adapter);
            LastInvalidationHints |= result.InvalidationHints;
            LastDamage |= result.Damage;
            Generation = Math.Max(Generation, result.Generation);
            AddLayoutDirtyNode(candidate, previous: null, result.Damage);
            return result.Nodes[0];
        }

        domLayoutIdentities.TryGetValue(candidate.DomNodeId, out var previous);
        var layoutIdentityChanged =
            previous is null ||
            !Adapter.HasSameNodeLayoutIdentity(previous, candidate);
        var versions = domStore.AssignVersions(
            candidate.DomNodeId,
            candidate.Style,
            HtmlComputedStyle.HasSameLayoutIdentity,
            layoutIdentityChanged);
        domLayoutIdentities[candidate.DomNodeId] = candidate;
        LastInvalidationHints |= versions.InvalidationHints;
        LastDamage |= versions.Damage;
        Generation = Math.Max(Generation, versions.Generation);
        AddLayoutDirtyNode(candidate, previous, versions.Damage);
        return candidate with { StyleVersion = versions.StyleVersion, LayoutVersion = versions.LayoutVersion };
    }

    private void AddLayoutDirtyNode(HtmlSceneNode node, HtmlSceneNode? previous, Enaga.Html.Style.RenderDamage damage)
    {
        if ((damage & (
                Enaga.Html.Style.RenderDamage.RebuildLayoutTree |
                Enaga.Html.Style.RenderDamage.Relayout |
                Enaga.Html.Style.RenderDamage.Refragment)) == 0)
        {
            return;
        }

        var bits = (damage & Enaga.Html.Style.RenderDamage.RebuildLayoutTree) != 0
            ? HtmlLayoutDirtyBits.Self | HtmlLayoutDirtyBits.Subtree
            : HtmlLayoutDirtyBits.Self;
        if (CanAffectAncestorLayout(previous, node))
            bits |= HtmlLayoutDirtyBits.Ancestors;

        layoutDirtyNodes.Add(
            HtmlLayoutVersion.ToLayoutNodeId(node.Id),
            bits);
    }

    private static bool CanAffectAncestorLayout(HtmlSceneNode? previous, HtmlSceneNode node)
    {
        if (previous is not null &&
            node.Style.StopsLayoutDirtyPropagation &&
            previous.Style.StopsLayoutDirtyPropagation &&
            HtmlComputedStyle.HasSameOuterLayoutDependency(previous.Style, node.Style))
        {
            return false;
        }

        return true;
    }

    private void ConsumePendingInvalidations()
    {
        if (PendingInvalidations.IsEmpty)
            return;

        LastInvalidationHints |= PendingInvalidations.RestyleHint;
        LastDamage |= PendingInvalidations.EstimatedDamage;
        PendingInvalidations.Clear();
    }

    private sealed class HtmlSceneVersionAdapter : IStyleLayoutVersionAdapter<HtmlSceneNode, HtmlComputedStyle, string>
    {
        public HtmlNodeId GetNodeId(HtmlSceneNode node) => node.DomNodeId;

        public string GetKey(HtmlSceneNode node) => node.Id;

        public HtmlComputedStyle GetStyle(HtmlSceneNode node) => node.Style;

        public IReadOnlyList<HtmlSceneNode> GetChildren(HtmlSceneNode node) => node.Children;

        public HtmlSceneNode WithChildren(HtmlSceneNode node, IReadOnlyList<HtmlSceneNode> children)
            => node with { Children = children };

        public HtmlSceneNode WithVersions(HtmlSceneNode node, uint styleVersion, uint layoutVersion)
            => node with { StyleVersion = styleVersion, LayoutVersion = layoutVersion };

        public bool HasSameStyleLayoutIdentity(HtmlComputedStyle previous, HtmlComputedStyle next)
            => HtmlComputedStyle.HasSameLayoutIdentity(previous, next);

        public bool HasSameNodeLayoutIdentity(HtmlSceneNode previous, HtmlSceneNode next)
        {
            if (previous.NodeKind != next.NodeKind ||
                previous.RowSpan != next.RowSpan ||
                previous.ColSpan != next.ColSpan ||
                !string.Equals(previous.TextContent, next.TextContent, StringComparison.Ordinal) ||
                !string.Equals(previous.PlaceholderText, next.PlaceholderText, StringComparison.Ordinal) ||
                !string.Equals(previous.ImageSource, next.ImageSource, StringComparison.Ordinal) ||
                !string.Equals(previous.LinkHref, next.LinkHref, StringComparison.Ordinal) ||
                !string.Equals(previous.Label, next.Label, StringComparison.Ordinal) ||
                previous.Children.Count != next.Children.Count)
            {
                return false;
            }

            for (var index = 0; index < previous.Children.Count; index++)
            {
                var previousChild = previous.Children[index];
                var nextChild = next.Children[index];
                if (!string.Equals(previousChild.Id, nextChild.Id, StringComparison.Ordinal) ||
                    previousChild.StyleVersion != nextChild.StyleVersion ||
                    previousChild.LayoutVersion != nextChild.LayoutVersion)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

internal static class HtmlLayoutVersion
{
    public static LayoutNodeId ToLayoutNodeId(string nodeId)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in nodeId.AsSpan())
        {
            hash ^= ch;
            hash *= prime;
        }

        return new LayoutNodeId(unchecked((int)hash));
    }
}

[Flags]
internal enum HtmlLayoutDirtyBits : byte
{
    None = 0,
    Self = 1 << 0,
    Subtree = 1 << 1,
    Ancestors = 1 << 2
}

internal sealed class HtmlLayoutDirtySet
{
    private readonly Dictionary<LayoutNodeId, HtmlLayoutDirtyBits> nodes = new();

    public int Count => nodes.Count;

    public IEnumerable<KeyValuePair<LayoutNodeId, HtmlLayoutDirtyBits>> Entries => nodes;

    public void Add(LayoutNodeId nodeId, HtmlLayoutDirtyBits bits)
    {
        if (bits == HtmlLayoutDirtyBits.None)
            return;

        nodes[nodeId] = nodes.TryGetValue(nodeId, out var existing)
            ? existing | bits
            : bits;
    }

    public void Clear() => nodes.Clear();
}
