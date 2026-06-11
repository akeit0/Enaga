using Enaga.Html.Dom;

namespace Enaga.Html.Style;

[Flags]
public enum RestyleHint : uint
{
    None = 0,
    MatchSelf = 1 << 0,
    MatchDescendants = 1 << 1,
    CascadeSelf = 1 << 2,
    CascadeDescendants = 1 << 3,
    ReplaceInlineStyle = 1 << 4,
    PseudoState = 1 << 5,
    MediaQuery = 1 << 6,
    RebuildFormattingTree = 1 << 7,
}

[Flags]
internal enum PipelineInvalidation : uint
{
    None = 0,
    DomSelf = 1 << 0,
    DomSubtree = 1 << 1,
    SelectorSelf = 1 << 2,
    SelectorDescendants = 1 << 3,
    CascadeSelf = 1 << 4,
    CascadeDescendants = 1 << 5,
    LayoutSelf = 1 << 6,
    LayoutDescendants = 1 << 7,
    FragmentSelf = 1 << 8,
    PaintSelf = 1 << 9,
    RasterSelf = 1 << 10,
    CompositeOnly = 1 << 11,
    HitTest = 1 << 12,
}

[Flags]
public enum RenderDamage : uint
{
    None = 0,
    RebuildStyle = 1 << 0,
    RebuildLayoutTree = 1 << 1,
    Relayout = 1 << 2,
    Refragment = 1 << 3,
    Repaint = 1 << 4,
    Reraster = 1 << 5,
    RebuildLayer = 1 << 6,
    RebuildHitTest = 1 << 7,
    FullFrame = 1 << 8,
}

[Flags]
public enum ElementStyleFlags : ushort
{
    None = 0,
    WasRestyled = 1 << 0,
    TraversedWithoutStyling = 1 << 1,
    PrimaryStyleReused = 1 << 2,
    SnapshotHandled = 1 << 3,
}

public enum RestyleKind : byte
{
    MatchAndCascade,
    CascadeWithReplacements,
    CascadeOnly,
}

public sealed class ElementStyleData<TComputedStyle>
    where TComputedStyle : class
{
    public HtmlNodeId NodeId { get; init; }
    public TComputedStyle? Style { get; set; }
    public TComputedStyle? PreviousStyle { get; set; }
    public RestyleHint Hint { get; set; }
    public RenderDamage Damage { get; set; }
    public ElementStyleFlags Flags { get; set; }
    public uint StyleVersion { get; set; }
    public uint LayoutVersion { get; set; }

    public bool NeedsTraversal => Hint != RestyleHint.None || Damage != RenderDamage.None;

    public void MarkRestyled(RenderDamage damage)
    {
        Damage |= damage;
        Flags |= ElementStyleFlags.WasRestyled;
        Flags &= ~ElementStyleFlags.TraversedWithoutStyling;
        StyleVersion++;
    }

    public void MarkTraversedWithoutStyling()
    {
        Flags |= ElementStyleFlags.TraversedWithoutStyling;
    }

    public void ClearRestyleState()
    {
        Hint = RestyleHint.None;
        Flags |= ElementStyleFlags.SnapshotHandled;
    }

    public void ClearDamage()
    {
        Damage = RenderDamage.None;
        Flags &= ~ElementStyleFlags.WasRestyled;
    }
}
