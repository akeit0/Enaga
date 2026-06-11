using Enaga.Html.Dom;
using Enaga.Html.Style;
using Enaga.Layout;

namespace Enaga.Html;

internal sealed class HtmlSceneVersionStore
{
    private readonly DomElementStyleStore<HtmlComputedStyle> domStore = new();
    private readonly Dictionary<HtmlNodeId, HtmlSceneNode> domLayoutIdentities = new();
    private readonly Dictionary<HtmlSceneNodeId, GeneratedSceneEntry> generatedEntries = new();
    private readonly HtmlLayoutDirtySet layoutDirtyNodes = new();
    private uint generatedGeneration;

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

    public HtmlSceneNode[] AssignVersions(HtmlSceneNode[] nodes)
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

    public void ApplyPseudoStateSnapshot(
        IReadOnlySet<HtmlNodeId>? oldNodeIds,
        IReadOnlySet<HtmlNodeId>? newNodeIds,
        HtmlPseudoState pseudoState
    )
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

    public void ApplyPseudoStateSnapshot(
        HtmlNodeId? oldNodeId,
        HtmlNodeId? newNodeId,
        HtmlPseudoState pseudoState
    )
    {
        if (oldNodeId == newNodeId)
            return;

        if (oldNodeId is { } oldId && oldId.IsValid)
            ApplyPseudoStateSnapshot(oldId, pseudoState, HtmlPseudoState.None);
        if (newNodeId is { } newId && newId.IsValid)
            ApplyPseudoStateSnapshot(newId, HtmlPseudoState.None, pseudoState);
    }

    private void ApplyPseudoStateSnapshot(
        HtmlNodeId nodeId,
        HtmlPseudoState oldState,
        HtmlPseudoState newState
    )
    {
        if (oldState == newState)
            return;

        ApplySnapshot(
            new HtmlElementSnapshot(
                nodeId,
                OldId: null,
                NewId: null,
                OldClass: null,
                NewClass: null,
                HtmlAttributeChangeMask.None,
                oldState,
                newState
            )
        );
    }

    private HtmlSceneNode[] AssignVersionsCore(HtmlSceneNode[] nodes)
    {
        if (nodes.Length == 0)
            return nodes;

        HtmlSceneNode[]? versioned = null;
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            var next = AssignVersionsCore(node);
            if (versioned is null && !ReferenceEquals(next, node))
            {
                versioned = new HtmlSceneNode[nodes.Length];
                for (var copyIndex = 0; copyIndex < index; copyIndex++)
                    versioned[copyIndex] = nodes[copyIndex];
            }

            if (versioned is not null)
                versioned[index] = next;
        }

        return versioned ?? nodes;
    }

    private HtmlSceneNode AssignVersionsCore(HtmlSceneNode node)
    {
        var children = AssignVersionsCore(node.Children);
        var candidate = ReferenceEquals(children, node.Children)
            ? node
            : node with
            {
                Children = children,
            };
        if (!candidate.DomNodeId.IsValid)
        {
            var versioned = AssignGeneratedVersion(
                candidate,
                out var invalidation,
                out var damage,
                out var generation
            );
            LastInvalidationHints |= invalidation;
            LastDamage |= damage;
            Generation = Math.Max(Generation, generation);
            AddLayoutDirtyNode(candidate, previous: null, damage);
            return versioned;
        }

        domLayoutIdentities.TryGetValue(candidate.DomNodeId, out var previous);
        var layoutIdentityChanged =
            previous is null || !HasSameNodeLayoutIdentity(previous, candidate);
        var versions = domStore.AssignVersions(
            candidate.DomNodeId,
            candidate.Style,
            HtmlComputedStyle.HasSameLayoutIdentity,
            layoutIdentityChanged
        );
        LastInvalidationHints |= versions.InvalidationHints;
        LastDamage |= versions.Damage;
        Generation = Math.Max(Generation, versions.Generation);
        AddLayoutDirtyNode(candidate, previous, versions.Damage);
        ApplyVersions(candidate, versions.StyleVersion, versions.LayoutVersion);
        domLayoutIdentities[candidate.DomNodeId] = candidate;
        return candidate;
    }

    private void AddLayoutDirtyNode(
        HtmlSceneNode node,
        HtmlSceneNode? previous,
        Enaga.Html.Style.RenderDamage damage
    )
    {
        if (
            (
                damage
                & (
                    Enaga.Html.Style.RenderDamage.RebuildLayoutTree
                    | Enaga.Html.Style.RenderDamage.Relayout
                    | Enaga.Html.Style.RenderDamage.Refragment
                )
            ) == 0
        )
        {
            return;
        }

        var bits =
            (damage & Enaga.Html.Style.RenderDamage.RebuildLayoutTree) != 0
                ? HtmlLayoutDirtyBits.Self | HtmlLayoutDirtyBits.Subtree
                : HtmlLayoutDirtyBits.Self;
        if (CanAffectAncestorLayout(previous, node))
            bits |= HtmlLayoutDirtyBits.Ancestors;

        layoutDirtyNodes.Add(HtmlLayoutVersion.ToLayoutNodeId(node.Id), bits);
    }

    private static bool CanAffectAncestorLayout(HtmlSceneNode? previous, HtmlSceneNode node)
    {
        if (
            previous is not null
            && node.Style.StopsLayoutDirtyPropagation
            && previous.Style.StopsLayoutDirtyPropagation
            && HtmlComputedStyle.HasSameOuterLayoutDependency(previous.Style, node.Style)
        )
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

    private HtmlSceneNode AssignGeneratedVersion(
        HtmlSceneNode candidate,
        out RestyleHint invalidation,
        out Enaga.Html.Style.RenderDamage damage,
        out uint generation
    )
    {
        invalidation = RestyleHint.None;
        damage = Enaga.Html.Style.RenderDamage.None;
        if (!generatedEntries.TryGetValue(candidate.Id, out var entry))
        {
            entry = new GeneratedSceneEntry(
                candidate.Style,
                candidate,
                styleVersion: 1,
                layoutVersion: 1
            );
            generatedEntries[candidate.Id] = entry;
            generatedGeneration++;
            generation = generatedGeneration;
            invalidation =
                RestyleHint.MatchSelf | RestyleHint.CascadeSelf | RestyleHint.RebuildFormattingTree;
            damage =
                Enaga.Html.Style.RenderDamage.RebuildStyle
                | Enaga.Html.Style.RenderDamage.RebuildLayoutTree
                | Enaga.Html.Style.RenderDamage.Relayout
                | Enaga.Html.Style.RenderDamage.Refragment
                | Enaga.Html.Style.RenderDamage.Repaint
                | Enaga.Html.Style.RenderDamage.RebuildHitTest;
            ApplyVersions(candidate, entry.StyleVersion, entry.LayoutVersion);
            entry.Node = candidate;
            return candidate;
        }

        var styleChanged = !HtmlComputedStyle.HasSameLayoutIdentity(entry.Style, candidate.Style);
        if (styleChanged)
        {
            entry.Style = candidate.Style;
            entry.StyleVersion++;
            entry.LayoutVersion++;
            generatedGeneration++;
            invalidation |= RestyleHint.CascadeSelf;
            damage |=
                Enaga.Html.Style.RenderDamage.RebuildStyle
                | Enaga.Html.Style.RenderDamage.Relayout
                | Enaga.Html.Style.RenderDamage.Refragment
                | Enaga.Html.Style.RenderDamage.Repaint;
        }

        if (styleChanged || !HasSameNodeLayoutIdentity(entry.Node, candidate))
        {
            entry.Node = candidate;
            entry.LayoutVersion++;
            generatedGeneration++;
            invalidation |= RestyleHint.RebuildFormattingTree;
            damage |=
                Enaga.Html.Style.RenderDamage.RebuildLayoutTree
                | Enaga.Html.Style.RenderDamage.Relayout
                | Enaga.Html.Style.RenderDamage.Refragment
                | Enaga.Html.Style.RenderDamage.Repaint
                | Enaga.Html.Style.RenderDamage.RebuildHitTest;
        }

        generation = generatedGeneration;
        ApplyVersions(candidate, entry.StyleVersion, entry.LayoutVersion);
        entry.Node = candidate;
        return candidate;
    }

    private static void ApplyVersions(HtmlSceneNode node, uint styleVersion, uint layoutVersion)
    {
        node.StyleVersion = styleVersion;
        node.LayoutVersion = layoutVersion;
    }

    private static bool HasSameNodeLayoutIdentity(HtmlSceneNode previous, HtmlSceneNode next)
    {
        if (
            previous.NodeKind != next.NodeKind
            || previous.RowSpan != next.RowSpan
            || previous.ColSpan != next.ColSpan
            || !string.Equals(previous.TextContent, next.TextContent, StringComparison.Ordinal)
            || !string.Equals(
                previous.PlaceholderText,
                next.PlaceholderText,
                StringComparison.Ordinal
            )
            || !string.Equals(previous.ImageSource, next.ImageSource, StringComparison.Ordinal)
            || !string.Equals(previous.LinkHref, next.LinkHref, StringComparison.Ordinal)
            || !string.Equals(previous.Label, next.Label, StringComparison.Ordinal)
            || previous.Children.Length != next.Children.Length
        )
        {
            return false;
        }

        for (var index = 0; index < previous.Children.Length; index++)
        {
            var previousChild = previous.Children[index];
            var nextChild = next.Children[index];
            if (
                previousChild.Id != nextChild.Id
                || previousChild.StyleVersion != nextChild.StyleVersion
                || previousChild.LayoutVersion != nextChild.LayoutVersion
            )
            {
                return false;
            }
        }

        return true;
    }

    private sealed class GeneratedSceneEntry(
        HtmlComputedStyle style,
        HtmlSceneNode node,
        uint styleVersion,
        uint layoutVersion
    )
    {
        public HtmlComputedStyle Style { get; set; } = style;
        public HtmlSceneNode Node { get; set; } = node;
        public uint StyleVersion { get; set; } = styleVersion;
        public uint LayoutVersion { get; set; } = layoutVersion;
    }
}

internal static class HtmlLayoutVersion
{
    public static LayoutNodeId ToLayoutNodeId(HtmlSceneNodeId nodeId) =>
        new(HashCode.Combine(nodeId.Value, nodeId.FragmentIndex));
}

[Flags]
internal enum HtmlLayoutDirtyBits : byte
{
    None = 0,
    Self = 1 << 0,
    Subtree = 1 << 1,
    Ancestors = 1 << 2,
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

        nodes[nodeId] = nodes.TryGetValue(nodeId, out var existing) ? existing | bits : bits;
    }

    public void Clear() => nodes.Clear();
}
