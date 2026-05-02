namespace Enaga.Html.Dom;

[Flags]
public enum HtmlAttributeChangeMask : uint
{
    None = 0,
    Id = 1 << 0,
    Class = 1 << 1,
    Style = 1 << 2,
    Href = 1 << 3,
    Src = 1 << 4,
    Direction = 1 << 5,
    Lang = 1 << 6,
    Other = 1u << 31
}

[Flags]
public enum HtmlPseudoState : uint
{
    None = 0,
    Hover = 1 << 0,
    Active = 1 << 1,
    Focus = 1 << 2,
    Disabled = 1 << 3,
    Checked = 1 << 4,
    Visited = 1 << 5
}

public readonly record struct HtmlElementSnapshot(
    HtmlNodeId NodeId,
    string? OldId,
    string? NewId,
    string? OldClass,
    string? NewClass,
    HtmlAttributeChangeMask AttributeChanges,
    HtmlPseudoState OldPseudoState,
    HtmlPseudoState NewPseudoState)
{
    public bool HasAttributeChange => AttributeChanges != HtmlAttributeChangeMask.None;

    public HtmlPseudoState ChangedPseudoStates => OldPseudoState ^ NewPseudoState;
}
