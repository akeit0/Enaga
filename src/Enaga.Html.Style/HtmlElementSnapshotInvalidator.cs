using Enaga.Html.Dom;

namespace Enaga.Html.Style;

internal readonly record struct HtmlElementSnapshotInvalidation(
    HtmlNodeId NodeId,
    RestyleHint RestyleHint,
    PipelineInvalidation Invalidation
)
{
    public bool IsEmpty =>
        RestyleHint == RestyleHint.None && Invalidation == PipelineInvalidation.None;
}

internal static class HtmlElementSnapshotInvalidator
{
    private const HtmlAttributeChangeMask SelectorIdentityAttributes =
        HtmlAttributeChangeMask.Id | HtmlAttributeChangeMask.Class;

    private const HtmlAttributeChangeMask InheritedStyleAttributes =
        HtmlAttributeChangeMask.Direction | HtmlAttributeChangeMask.Lang;

    public static HtmlElementSnapshotInvalidation Classify(HtmlElementSnapshot snapshot)
    {
        var restyle = RestyleHint.None;
        var invalidation = PipelineInvalidation.None;

        var attributeChanges = snapshot.AttributeChanges;
        if (
            (attributeChanges & SelectorIdentityAttributes) != 0
            || !StringEquals(snapshot.OldId, snapshot.NewId)
            || !StringEquals(snapshot.OldClass, snapshot.NewClass)
        )
        {
            restyle |=
                RestyleHint.MatchSelf
                | RestyleHint.MatchDescendants
                | RestyleHint.RebuildFormattingTree;
            invalidation |=
                PipelineInvalidation.SelectorSelf
                | PipelineInvalidation.SelectorDescendants
                | PipelineInvalidation.CascadeSelf
                | PipelineInvalidation.CascadeDescendants
                | PipelineInvalidation.LayoutSelf
                | PipelineInvalidation.LayoutDescendants
                | PipelineInvalidation.FragmentSelf
                | PipelineInvalidation.PaintSelf
                | PipelineInvalidation.HitTest;
        }

        if ((attributeChanges & HtmlAttributeChangeMask.Style) != 0)
        {
            restyle |=
                RestyleHint.ReplaceInlineStyle
                | RestyleHint.CascadeSelf
                | RestyleHint.RebuildFormattingTree;
            invalidation |=
                PipelineInvalidation.CascadeSelf
                | PipelineInvalidation.LayoutSelf
                | PipelineInvalidation.FragmentSelf
                | PipelineInvalidation.PaintSelf
                | PipelineInvalidation.HitTest;
        }

        if ((attributeChanges & InheritedStyleAttributes) != 0)
        {
            restyle |=
                RestyleHint.CascadeSelf
                | RestyleHint.CascadeDescendants
                | RestyleHint.RebuildFormattingTree;
            invalidation |=
                PipelineInvalidation.CascadeSelf
                | PipelineInvalidation.CascadeDescendants
                | PipelineInvalidation.LayoutSelf
                | PipelineInvalidation.LayoutDescendants
                | PipelineInvalidation.FragmentSelf
                | PipelineInvalidation.PaintSelf
                | PipelineInvalidation.HitTest;
        }

        if ((attributeChanges & HtmlAttributeChangeMask.Src) != 0)
        {
            invalidation |=
                PipelineInvalidation.LayoutSelf
                | PipelineInvalidation.FragmentSelf
                | PipelineInvalidation.PaintSelf
                | PipelineInvalidation.RasterSelf
                | PipelineInvalidation.HitTest;
        }

        if ((attributeChanges & HtmlAttributeChangeMask.Href) != 0)
        {
            invalidation |= PipelineInvalidation.PaintSelf | PipelineInvalidation.HitTest;
        }

        if ((attributeChanges & HtmlAttributeChangeMask.Other) != 0)
        {
            restyle |= RestyleHint.MatchSelf | RestyleHint.RebuildFormattingTree;
            invalidation |=
                PipelineInvalidation.SelectorSelf
                | PipelineInvalidation.CascadeSelf
                | PipelineInvalidation.LayoutSelf
                | PipelineInvalidation.FragmentSelf
                | PipelineInvalidation.PaintSelf
                | PipelineInvalidation.HitTest;
        }

        if (snapshot.ChangedPseudoStates != HtmlPseudoState.None)
        {
            restyle |= RestyleHint.PseudoState | RestyleHint.MatchSelf;
            invalidation |=
                PipelineInvalidation.SelectorSelf
                | PipelineInvalidation.CascadeSelf
                | PipelineInvalidation.PaintSelf
                | PipelineInvalidation.HitTest;
        }

        return new HtmlElementSnapshotInvalidation(snapshot.NodeId, restyle, invalidation);
    }

    public static RenderDamage EstimateDamage(PipelineInvalidation invalidation)
    {
        var damage = RenderDamage.None;
        if (
            (
                invalidation
                & (
                    PipelineInvalidation.SelectorSelf
                    | PipelineInvalidation.SelectorDescendants
                    | PipelineInvalidation.CascadeSelf
                    | PipelineInvalidation.CascadeDescendants
                )
            ) != 0
        )
            damage |= RenderDamage.RebuildStyle;

        if (
            (
                invalidation
                & (PipelineInvalidation.LayoutSelf | PipelineInvalidation.LayoutDescendants)
            ) != 0
        )
            damage |= RenderDamage.RebuildLayoutTree | RenderDamage.Relayout;

        if ((invalidation & PipelineInvalidation.FragmentSelf) != 0)
            damage |= RenderDamage.Refragment;

        if ((invalidation & PipelineInvalidation.PaintSelf) != 0)
            damage |= RenderDamage.Repaint;

        if ((invalidation & PipelineInvalidation.RasterSelf) != 0)
            damage |= RenderDamage.Reraster;

        if ((invalidation & PipelineInvalidation.CompositeOnly) != 0)
            damage |= RenderDamage.RebuildLayer;

        if ((invalidation & PipelineInvalidation.HitTest) != 0)
            damage |= RenderDamage.RebuildHitTest;

        return damage;
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
