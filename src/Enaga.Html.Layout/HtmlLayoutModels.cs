using Enaga.Scene;
using Enaga.Html.Dom;

namespace Enaga.Html;

internal sealed record HtmlSceneNode(
    string Id,
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
    SceneControlKind ControlKind = SceneControlKind.None)
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
    string? ParentId,
    float AbsLeft,
    float AbsTop,
    float Width,
    float Height,
    string? FragmentId = null,
    string? TextContentOverride = null)
{
    public string Id => FragmentId ?? Node.Id;

    public string? TextContent => TextContentOverride ?? Node.TextContent;
}

internal readonly record struct HtmlChildRelation(string ParentId, string[] ChildIds);

internal sealed class HtmlNodeIdGenerator
{
    private int nextId;

    public string Next(string prefix)
    {
        nextId += 1;
        Span<char> buffer = stackalloc char[@prefix.Length + 10];
        prefix.AsSpan().CopyTo(buffer);
        buffer[prefix.Length] = '-';
        nextId.TryFormat(buffer[(prefix.Length + 1)..], out var charsWritten);
        return buffer[..(prefix.Length + 1 + charsWritten)].ToString();
    }
}
