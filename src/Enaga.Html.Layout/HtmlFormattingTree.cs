using Enaga.Html.Dom;

namespace Enaga.Html;

internal readonly record struct HtmlFormattingNodeId(int Value);

internal enum HtmlFormattingNodeKind : byte
{
    Block,
    Inline,
    Text,
    AnonymousBlock,
    FlexContainer,
    Table,
    TableRow,
    TableCell,
    ListMarker,
    Replaced,
    Absolute,
    Fixed,
    ScrollContainer
}

internal sealed record HtmlFormattingNode(
    HtmlFormattingNodeId Id,
    HtmlFormattingNodeKind Kind,
    HtmlNodeId? SourceNodeId,
    HtmlFormattingNodeId? ParentId,
    IReadOnlyList<HtmlFormattingNodeId> Children,
    uint StyleVersion,
    uint LayoutVersion);

internal sealed class HtmlFormattingTree
{
    private readonly Dictionary<HtmlFormattingNodeId, HtmlFormattingNode> nodes;
    private readonly Dictionary<HtmlNodeId, List<HtmlFormattingNodeId>> nodesBySource;

    public HtmlFormattingTree(HtmlFormattingNodeId rootId, IEnumerable<HtmlFormattingNode> nodes)
    {
        RootId = rootId;
        this.nodes = new Dictionary<HtmlFormattingNodeId, HtmlFormattingNode>();
        nodesBySource = new Dictionary<HtmlNodeId, List<HtmlFormattingNodeId>>();

        foreach (var node in nodes)
        {
            this.nodes[node.Id] = node;
            if (node.SourceNodeId is not { } sourceNodeId)
                continue;

            if (!nodesBySource.TryGetValue(sourceNodeId, out var sourceNodes))
            {
                sourceNodes = [];
                nodesBySource[sourceNodeId] = sourceNodes;
            }

            sourceNodes.Add(node.Id);
        }
    }

    public HtmlFormattingNodeId RootId { get; }

    public IReadOnlyDictionary<HtmlFormattingNodeId, HtmlFormattingNode> Nodes => nodes;

    public bool TryGetNode(HtmlFormattingNodeId id, out HtmlFormattingNode node)
        => nodes.TryGetValue(id, out node!);

    public IReadOnlyList<HtmlFormattingNodeId> GetNodesForSource(HtmlNodeId sourceNodeId)
        => nodesBySource.TryGetValue(sourceNodeId, out var sourceNodes) ? sourceNodes : [];
}

internal readonly record struct HtmlFragmentId(int Value);

internal enum HtmlFragmentKind : byte
{
    BlockBox,
    InlineBox,
    LineBox,
    TextRun,
    Image,
    TableCell,
    ListMarker,
    ScrollBar,
    Absolute,
    Fixed
}

internal enum HtmlGeneratedFragmentRole : byte
{
    None,
    VerticalScrollBarGutter,
    VerticalScrollBarThumb,
    HorizontalScrollBarGutter,
    HorizontalScrollBarThumb
}

internal readonly record struct HtmlLayoutRect(float Left, float Top, float Width, float Height)
{
    public float Right => Left + Width;

    public float Bottom => Top + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static HtmlLayoutRect Empty => new(0, 0, 0, 0);

    public HtmlLayoutRect Union(HtmlLayoutRect other)
    {
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;

        var left = Math.Min(Left, other.Left);
        var top = Math.Min(Top, other.Top);
        var right = Math.Max(Right, other.Right);
        var bottom = Math.Max(Bottom, other.Bottom);
        return new HtmlLayoutRect(left, top, right - left, bottom - top);
    }

    public HtmlDirtyRect ToDirtyRect()
    {
        if (IsEmpty)
            return HtmlDirtyRect.Empty;

        var left = (int)MathF.Floor(Left);
        var top = (int)MathF.Floor(Top);
        var right = (int)MathF.Ceiling(Right);
        var bottom = (int)MathF.Ceiling(Bottom);
        return new HtmlDirtyRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

internal readonly record struct HtmlDirtyRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static HtmlDirtyRect Empty => new(0, 0, 0, 0);
}

internal sealed record HtmlFragment(
    HtmlFragmentId Id,
    HtmlFormattingNodeId SourceNodeId,
    HtmlFragmentId? ParentId,
    IReadOnlyList<HtmlFragmentId> Children,
    HtmlFragmentKind Kind,
    HtmlLayoutRect BorderBox,
    HtmlLayoutRect VisualOverflow,
    uint PaintVersion,
    Enaga.Scene.SceneNodeId SceneNodeId = default,
    HtmlSceneNodeId SourceSceneNodeId = default,
    HtmlGeneratedFragmentRole GeneratedRole = HtmlGeneratedFragmentRole.None);

internal sealed class HtmlFragmentTree
{
    private readonly Dictionary<HtmlFragmentId, HtmlFragment> fragments;
    private readonly IReadOnlyList<HtmlFragment> orderedFragments;
    private Dictionary<HtmlFormattingNodeId, List<HtmlFragmentId>>? fragmentsBySource;

    public HtmlFragmentTree(HtmlFragmentId rootId, IReadOnlyList<HtmlFragment> fragments)
    {
        RootId = rootId;
        orderedFragments = fragments;
        this.fragments = new Dictionary<HtmlFragmentId, HtmlFragment>(fragments.Count);

        for (var index = 0; index < fragments.Count; index++)
        {
            var fragment = fragments[index];
            this.fragments[fragment.Id] = fragment;
        }
    }

    public HtmlFragmentId RootId { get; }

    public IReadOnlyDictionary<HtmlFragmentId, HtmlFragment> Fragments => fragments;

    public IReadOnlyList<HtmlFragment> OrderedFragments => orderedFragments;

    public bool TryGetFragment(HtmlFragmentId id, out HtmlFragment fragment)
        => fragments.TryGetValue(id, out fragment!);

    public IReadOnlyList<HtmlFragmentId> GetFragmentsForSource(HtmlFormattingNodeId sourceNodeId)
    {
        fragmentsBySource ??= BuildFragmentsBySource();
        return fragmentsBySource.TryGetValue(sourceNodeId, out var sourceFragments) ? sourceFragments : [];
    }

    private Dictionary<HtmlFormattingNodeId, List<HtmlFragmentId>> BuildFragmentsBySource()
    {
        var bySource = new Dictionary<HtmlFormattingNodeId, List<HtmlFragmentId>>(orderedFragments.Count);
        for (var index = 0; index < orderedFragments.Count; index++)
        {
            var fragment = orderedFragments[index];
            if (!bySource.TryGetValue(fragment.SourceNodeId, out var sourceFragments))
            {
                sourceFragments = [];
                bySource[fragment.SourceNodeId] = sourceFragments;
            }

            sourceFragments.Add(fragment.Id);
        }

        return bySource;
    }
}
