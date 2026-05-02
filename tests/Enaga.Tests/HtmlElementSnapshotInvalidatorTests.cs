using Enaga.Html.Dom;
using Enaga.Html.Style;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlElementSnapshotInvalidatorTests
{
    [Fact]
    public void Class_change_requests_self_and_descendant_selector_restyle()
    {
        var snapshot = new HtmlElementSnapshot(
            new HtmlNodeId(1),
            OldId: null,
            NewId: null,
            OldClass: "item",
            NewClass: "item active",
            HtmlAttributeChangeMask.Class,
            HtmlPseudoState.None,
            HtmlPseudoState.None);

        var invalidation = HtmlElementSnapshotInvalidator.Classify(snapshot);

        Assert.True(invalidation.RestyleHint.HasFlag(RestyleHint.MatchSelf));
        Assert.True(invalidation.RestyleHint.HasFlag(RestyleHint.MatchDescendants));
        Assert.True(invalidation.Invalidation.HasFlag(PipelineInvalidation.SelectorSelf));
        Assert.True(invalidation.Invalidation.HasFlag(PipelineInvalidation.SelectorDescendants));
        Assert.True(invalidation.Invalidation.HasFlag(PipelineInvalidation.LayoutDescendants));
        Assert.True(HtmlElementSnapshotInvalidator.EstimateDamage(invalidation.Invalidation).HasFlag(RenderDamage.RebuildStyle));
        Assert.True(HtmlElementSnapshotInvalidator.EstimateDamage(invalidation.Invalidation).HasFlag(RenderDamage.Relayout));
    }

    [Fact]
    public void Inline_style_change_uses_replacement_cascade_without_descendant_selector_match()
    {
        var snapshot = new HtmlElementSnapshot(
            new HtmlNodeId(2),
            OldId: null,
            NewId: null,
            OldClass: null,
            NewClass: null,
            HtmlAttributeChangeMask.Style,
            HtmlPseudoState.None,
            HtmlPseudoState.None);

        var invalidation = HtmlElementSnapshotInvalidator.Classify(snapshot);

        Assert.True(invalidation.RestyleHint.HasFlag(RestyleHint.ReplaceInlineStyle));
        Assert.True(invalidation.RestyleHint.HasFlag(RestyleHint.CascadeSelf));
        Assert.False(invalidation.RestyleHint.HasFlag(RestyleHint.MatchDescendants));
        Assert.False(invalidation.Invalidation.HasFlag(PipelineInvalidation.SelectorDescendants));
    }

    [Fact]
    public void Pseudo_state_change_requests_self_selector_and_paint_hit_test_work()
    {
        var snapshot = new HtmlElementSnapshot(
            new HtmlNodeId(3),
            OldId: null,
            NewId: null,
            OldClass: null,
            NewClass: null,
            HtmlAttributeChangeMask.None,
            HtmlPseudoState.None,
            HtmlPseudoState.Hover);

        var invalidation = HtmlElementSnapshotInvalidator.Classify(snapshot);

        Assert.True(invalidation.RestyleHint.HasFlag(RestyleHint.PseudoState));
        Assert.True(invalidation.RestyleHint.HasFlag(RestyleHint.MatchSelf));
        Assert.True(invalidation.Invalidation.HasFlag(PipelineInvalidation.SelectorSelf));
        Assert.True(invalidation.Invalidation.HasFlag(PipelineInvalidation.PaintSelf));
        Assert.True(invalidation.Invalidation.HasFlag(PipelineInvalidation.HitTest));
        Assert.False(invalidation.Invalidation.HasFlag(PipelineInvalidation.LayoutDescendants));
    }

    [Fact]
    public void Store_apply_snapshot_accumulates_hint_and_estimated_damage()
    {
        var store = new DomElementStyleStore<object>();
        var snapshot = new HtmlElementSnapshot(
            new HtmlNodeId(4),
            OldId: "old",
            NewId: "new",
            OldClass: null,
            NewClass: null,
            HtmlAttributeChangeMask.Id,
            HtmlPseudoState.None,
            HtmlPseudoState.None);

        var invalidation = store.ApplySnapshot(snapshot);
        var data = store.GetOrCreate(snapshot.NodeId);

        Assert.False(invalidation.IsEmpty);
        Assert.True(data.Hint.HasFlag(RestyleHint.MatchSelf));
        Assert.True(data.Damage.HasFlag(RenderDamage.RebuildStyle));
        Assert.True(data.Damage.HasFlag(RenderDamage.RebuildLayoutTree));
        Assert.Equal(1u, data.StyleVersion);
        Assert.Equal(1u, data.LayoutVersion);
    }

    [Fact]
    public void Invalidation_set_merges_multiple_snapshots_for_same_element()
    {
        var nodeId = new HtmlNodeId(5);
        var set = new HtmlStyleInvalidationSet();

        set.Add(new HtmlElementSnapshot(
            nodeId,
            OldId: null,
            NewId: null,
            OldClass: "item",
            NewClass: "selected",
            HtmlAttributeChangeMask.Class,
            HtmlPseudoState.None,
            HtmlPseudoState.None));
        set.Add(new HtmlElementSnapshot(
            nodeId,
            OldId: null,
            NewId: null,
            OldClass: null,
            NewClass: null,
            HtmlAttributeChangeMask.Style,
            HtmlPseudoState.None,
            HtmlPseudoState.None));

        Assert.Equal(1, set.Count);
        Assert.True(set.RestyleHint.HasFlag(RestyleHint.MatchDescendants));
        Assert.True(set.RestyleHint.HasFlag(RestyleHint.ReplaceInlineStyle));
        Assert.True(set.Invalidation.HasFlag(PipelineInvalidation.SelectorDescendants));
        Assert.True(set.Invalidation.HasFlag(PipelineInvalidation.LayoutSelf));
        Assert.True(set.EstimatedDamage.HasFlag(RenderDamage.RebuildStyle));
        Assert.True(set.EstimatedDamage.HasFlag(RenderDamage.Relayout));
        Assert.True(set.TryGet(nodeId, out var merged));
        Assert.True(merged.RestyleHint.HasFlag(RestyleHint.CascadeSelf));
    }
}
