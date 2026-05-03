using Enaga.Scene;
using Enaga.Html.Dom;

namespace Enaga.Html;

internal readonly record struct HtmlSceneNodeId(int Value, int FragmentIndex = 0)
{
    public static HtmlSceneNodeId Root { get; } = new(1);

    public bool IsFragment => FragmentIndex > 0;

    public static HtmlSceneNodeId Fragment(HtmlSceneNodeId source, int fragmentIndex)
        => new(source.Value, fragmentIndex + 1);

    public override string ToString()
        => IsFragment
            ? string.Concat(Value.ToString(System.Globalization.CultureInfo.InvariantCulture), ":frag:", (FragmentIndex - 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
            : Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal enum HtmlSceneNodeRole : byte
{
    Normal,
    Table,
    TableSection,
    TableRow,
    TableCell,
    ListItem,
    ListMarker,
    ListContent,
    InlineRun,
    Text,
    InputText
}

internal sealed record HtmlSceneNode(
    HtmlSceneNodeId Id,
    SceneNodeKind NodeKind,
    HtmlComputedStyle Style,
    IReadOnlyList<HtmlSceneNode> Children,
    string? TextContent,
    string? PlaceholderText,
    string? ImageSource,
    string? LinkHref,
    string? Label,
    HtmlNodeId DomNodeId = default,
    int RowSpan = 1,
    int ColSpan = 1,
    SceneControlKind ControlKind = SceneControlKind.None,
    HtmlSceneNodeRole Role = HtmlSceneNodeRole.Normal)
{
    public uint StyleVersion { get; init; } = 1;

    public uint LayoutVersion { get; init; } = 1;
}

internal sealed record HtmlStyledSceneTree(
    HtmlComputedStyle RootStyle,
    IReadOnlyList<HtmlSceneNode> RootChildren,
    uint StyleStoreGeneration,
    HtmlNodeId RootDomNodeId);

internal readonly record struct HtmlPlacedNode(
    HtmlSceneNode Node,
    HtmlSceneNodeId? ParentId,
    float AbsLeft,
    float AbsTop,
    float Width,
    float Height,
    int FragmentIndex = -1,
    string? TextContentOverride = null)
{
    public HtmlSceneNodeId Id => FragmentIndex >= 0 ? HtmlSceneNodeId.Fragment(Node.Id, FragmentIndex) : Node.Id;

    public string? TextContent => TextContentOverride ?? Node.TextContent;
}

internal readonly record struct HtmlChildRelation(HtmlSceneNodeId ParentId, HtmlSceneNodeId[] ChildIds);

internal sealed class HtmlNodeIdGenerator
{
    private int nextId = HtmlSceneNodeId.Root.Value;

    public HtmlSceneNodeId Next() => new(++nextId);
}
