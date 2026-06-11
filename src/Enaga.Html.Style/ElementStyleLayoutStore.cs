namespace Enaga.Html.Style;

using Enaga.Html.Dom;

public readonly record struct StyleLayoutVersionResult<TNode>(
    IReadOnlyList<TNode> Nodes,
    RestyleHint InvalidationHints,
    RenderDamage Damage,
    uint Generation
);

public interface IStyleLayoutVersionAdapter<TNode, TStyle, TKey>
    where TKey : notnull
{
    HtmlNodeId GetNodeId(TNode node);
    TKey GetKey(TNode node);
    TStyle GetStyle(TNode node);
    IReadOnlyList<TNode> GetChildren(TNode node);
    TNode WithChildren(TNode node, IReadOnlyList<TNode> children);
    TNode WithVersions(TNode node, uint styleVersion, uint layoutVersion);
    bool HasSameStyleLayoutIdentity(TStyle previous, TStyle next);
    bool HasSameNodeLayoutIdentity(TNode previous, TNode next);
}

public sealed class ElementStyleLayoutStore<TKey, TStyle, TNode>(
    IEqualityComparer<TKey>? keyComparer = null
)
    where TKey : notnull
    where TStyle : class
{
    private readonly Dictionary<TKey, Entry> entries = new(keyComparer);

    public uint Generation { get; private set; }

    public StyleLayoutVersionResult<TNode> AssignVersions(
        IReadOnlyList<TNode> nodes,
        IStyleLayoutVersionAdapter<TNode, TStyle, TKey> adapter
    )
    {
        var invalidation = RestyleHint.None;
        var damage = RenderDamage.None;
        var versioned = AssignVersions(nodes, adapter, ref invalidation, ref damage);
        return new StyleLayoutVersionResult<TNode>(versioned, invalidation, damage, Generation);
    }

    public TNode AssignVersion(
        TNode node,
        IStyleLayoutVersionAdapter<TNode, TStyle, TKey> adapter,
        out RestyleHint invalidation,
        out RenderDamage damage,
        out uint generation
    )
    {
        invalidation = RestyleHint.None;
        damage = RenderDamage.None;
        var versioned = AssignVersions(node, adapter, ref invalidation, ref damage);
        generation = Generation;
        return versioned;
    }

    private IReadOnlyList<TNode> AssignVersions(
        IReadOnlyList<TNode> nodes,
        IStyleLayoutVersionAdapter<TNode, TStyle, TKey> adapter,
        ref RestyleHint invalidation,
        ref RenderDamage damage
    )
    {
        if (nodes.Count == 0)
            return nodes;

        TNode[]? versioned = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var next = AssignVersions(node, adapter, ref invalidation, ref damage);
            if (versioned is null && !ReferenceEquals(next, node))
            {
                versioned = new TNode[nodes.Count];
                for (var copyIndex = 0; copyIndex < index; copyIndex++)
                    versioned[copyIndex] = nodes[copyIndex];
            }

            if (versioned is not null)
                versioned[index] = next;
        }

        return versioned ?? nodes;
    }

    private TNode AssignVersions(
        TNode node,
        IStyleLayoutVersionAdapter<TNode, TStyle, TKey> adapter,
        ref RestyleHint invalidation,
        ref RenderDamage damage
    )
    {
        var children = AssignVersions(
            adapter.GetChildren(node),
            adapter,
            ref invalidation,
            ref damage
        );
        var candidate = ReferenceEquals(children, adapter.GetChildren(node))
            ? node
            : adapter.WithChildren(node, children);
        var key = adapter.GetKey(candidate);
        var style = adapter.GetStyle(candidate);
        if (!entries.TryGetValue(key, out var entry))
        {
            entry = new Entry(adapter.GetNodeId(candidate), style, candidate);
            entries[key] = entry;
            Generation++;
            invalidation |=
                RestyleHint.MatchSelf | RestyleHint.CascadeSelf | RestyleHint.RebuildFormattingTree;
            damage |=
                RenderDamage.RebuildStyle
                | RenderDamage.RebuildLayoutTree
                | RenderDamage.Relayout
                | RenderDamage.Refragment
                | RenderDamage.Repaint
                | RenderDamage.RebuildHitTest;
            return adapter.WithVersions(
                candidate,
                entry.Data.StyleVersion,
                entry.Data.LayoutVersion
            );
        }

        var styleChanged = !adapter.HasSameStyleLayoutIdentity(entry.Style, style);
        if (styleChanged)
        {
            entry.Style = style;
            entry.Data.PreviousStyle = entry.Data.Style;
            entry.Data.Style = style;
            entry.Data.StyleVersion++;
            entry.Data.Hint |= RestyleHint.CascadeSelf;
            entry.Data.Damage |=
                RenderDamage.RebuildStyle
                | RenderDamage.Relayout
                | RenderDamage.Refragment
                | RenderDamage.Repaint;
            entry.Data.Flags |= ElementStyleFlags.WasRestyled;
            entry.Data.Flags &= ~ElementStyleFlags.TraversedWithoutStyling;
            Generation++;
            invalidation |= RestyleHint.CascadeSelf;
            damage |= entry.Data.Damage;
        }

        if (styleChanged || !adapter.HasSameNodeLayoutIdentity(entry.Node, candidate))
        {
            entry.Node = candidate;
            entry.Data.LayoutVersion++;
            entry.Data.Hint |= RestyleHint.RebuildFormattingTree;
            entry.Data.Damage |=
                RenderDamage.RebuildLayoutTree
                | RenderDamage.Relayout
                | RenderDamage.Refragment
                | RenderDamage.Repaint
                | RenderDamage.RebuildHitTest;
            Generation++;
            invalidation |= RestyleHint.RebuildFormattingTree;
            damage |= entry.Data.Damage;
        }

        return adapter.WithVersions(candidate, entry.Data.StyleVersion, entry.Data.LayoutVersion);
    }

    private sealed class Entry(HtmlNodeId nodeId, TStyle style, TNode node)
    {
        public TStyle Style { get; set; } = style;
        public TNode Node { get; set; } = node;
        public ElementStyleData<TStyle> Data { get; } =
            new()
            {
                NodeId = nodeId,
                Style = style,
                StyleVersion = 1,
                LayoutVersion = 1,
            };
    }
}

public sealed class DomElementStyleStore<TStyle>
    where TStyle : class
{
    private readonly Dictionary<HtmlNodeId, ElementStyleData<TStyle>> elements = new();

    public uint Generation { get; private set; }

    public ElementStyleData<TStyle> GetOrCreate(HtmlNodeId nodeId)
    {
        if (elements.TryGetValue(nodeId, out var data))
            return data;

        data = new ElementStyleData<TStyle>
        {
            NodeId = nodeId,
            StyleVersion = 1,
            LayoutVersion = 1,
        };
        elements[nodeId] = data;
        Generation++;
        return data;
    }

    internal HtmlElementSnapshotInvalidation ApplySnapshot(HtmlElementSnapshot snapshot)
    {
        var invalidation = HtmlElementSnapshotInvalidator.Classify(snapshot);
        if (invalidation.IsEmpty)
            return invalidation;

        var data = GetOrCreate(snapshot.NodeId);
        data.Hint |= invalidation.RestyleHint;
        data.Damage |= HtmlElementSnapshotInvalidator.EstimateDamage(invalidation.Invalidation);
        data.Flags &= ~ElementStyleFlags.SnapshotHandled;
        Generation++;
        return invalidation;
    }

    public StyleLayoutVersionPair AssignVersions(
        HtmlNodeId nodeId,
        TStyle style,
        Func<TStyle, TStyle, bool> hasSameStyleLayoutIdentity,
        bool layoutIdentityChanged
    )
    {
        var data = GetOrCreate(nodeId);
        var invalidation = RestyleHint.None;
        var damage = RenderDamage.None;
        var styleChanged = data.Style is null || !hasSameStyleLayoutIdentity(data.Style, style);
        if (styleChanged)
        {
            data.PreviousStyle = data.Style;
            data.Style = style;
            data.StyleVersion++;
            data.LayoutVersion++;
            data.Hint |= RestyleHint.CascadeSelf | RestyleHint.RebuildFormattingTree;
            data.Damage |=
                RenderDamage.RebuildStyle
                | RenderDamage.RebuildLayoutTree
                | RenderDamage.Relayout
                | RenderDamage.Refragment
                | RenderDamage.Repaint
                | RenderDamage.RebuildHitTest;
            data.Flags |= ElementStyleFlags.WasRestyled;
            data.Flags &= ~ElementStyleFlags.TraversedWithoutStyling;
            invalidation |= RestyleHint.CascadeSelf | RestyleHint.RebuildFormattingTree;
            damage |=
                RenderDamage.RebuildStyle
                | RenderDamage.RebuildLayoutTree
                | RenderDamage.Relayout
                | RenderDamage.Refragment
                | RenderDamage.Repaint
                | RenderDamage.RebuildHitTest;
            Generation++;
        }
        else if (layoutIdentityChanged)
        {
            data.LayoutVersion++;
            data.Hint |= RestyleHint.RebuildFormattingTree;
            data.Damage |=
                RenderDamage.RebuildLayoutTree
                | RenderDamage.Relayout
                | RenderDamage.Refragment
                | RenderDamage.Repaint
                | RenderDamage.RebuildHitTest;
            invalidation |= RestyleHint.RebuildFormattingTree;
            damage |=
                RenderDamage.RebuildLayoutTree
                | RenderDamage.Relayout
                | RenderDamage.Refragment
                | RenderDamage.Repaint
                | RenderDamage.RebuildHitTest;
            Generation++;
        }
        else
        {
            data.MarkTraversedWithoutStyling();
        }

        return new StyleLayoutVersionPair(
            data.StyleVersion,
            data.LayoutVersion,
            invalidation,
            damage,
            Generation
        );
    }
}

public readonly record struct StyleLayoutVersionPair(
    uint StyleVersion,
    uint LayoutVersion,
    RestyleHint InvalidationHints,
    RenderDamage Damage,
    uint Generation
);
