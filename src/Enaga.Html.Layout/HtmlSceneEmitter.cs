using Enaga.Layout;
using Enaga.Scene;

namespace Enaga.Html;

internal sealed class HtmlSceneEmitter(
    HtmlSceneNodeId rootId,
    SceneNodeIdentityMap<HtmlSceneNodeId> sceneNodeIds,
    HtmlSceneTextStyleCache textStyleCache,
    Dictionary<SceneNodeId, SceneNodeId[]> childMap,
    Dictionary<SceneNodeId, SceneNodeId[]> childArrayCache,
    SceneNodeMap<SceneGraphNode> nodes,
    SceneNodeMap<SceneLayoutBox> layout,
    int width,
    int height,
    float rootLayoutWidth,
    float rootLayoutHeight,
    float viewportScale,
    SceneLayoutCommit? previousCommit)
{
    public SceneLayoutCommit Emit(
        SceneNodeKind rootKind,
        HtmlComputedStyle rootStyle,
        List<HtmlPlacedNode> placedNodes,
        List<HtmlChildRelation> childRelations,
        HtmlPipelineMetrics metrics)
    {
        metrics.AddDisplayListCommandsRebuilt(placedNodes.Count + 1);
        childMap.Clear();
        childMap.EnsureCapacity(childRelations.Count);
        for (var index = 0; index < childRelations.Count; index++)
        {
            var relation = childRelations[index];
            var parentId = ToSceneNodeId(relation.ParentId);
            var childIds = GetOrCreateChildArray(parentId, relation.ChildIds.Length);
            for (var childIndex = 0; childIndex < relation.ChildIds.Length; childIndex++)
                childIds[childIndex] = ToSceneNodeId(relation.ChildIds[childIndex]);
            childMap[parentId] = childIds;
        }

        var rootSceneNodeId = ToSceneNodeId(rootId);
        nodes.Clear();
        layout.Clear();
        nodes.EnsureCapacity(placedNodes.Count + 1);
        layout.EnsureCapacity(placedNodes.Count + 1);
        nodes[rootSceneNodeId] = new SceneGraphNode(
            rootKind,
            ParentId: null,
            childMap.TryGetValue(rootSceneNodeId, out var rootChildren) ? rootChildren : [],
            Label: null);
        layout[rootSceneNodeId] = CreateLayoutBox(rootSceneNodeId, rootKind, rootStyle, 0, 0, rootLayoutWidth, rootLayoutHeight, null, null, null, null, SceneControlKind.None, viewportScale);

        for (var index = 0; index < placedNodes.Count; index++)
        {
            var placed = placedNodes[index];
            var node = placed.Node;
            var sceneNodeId = ToSceneNodeId(placed.Id);
            nodes[sceneNodeId] = new SceneGraphNode(
                node.NodeKind,
                placed.ParentId is null ? null : ToSceneNodeId(placed.ParentId.Value),
                childMap.TryGetValue(sceneNodeId, out var childIds) ? childIds : [],
                node.Label);
            layout[sceneNodeId] = CreateLayoutBox(
                sceneNodeId,
                node.NodeKind,
                node.Style,
                placed.AbsLeft,
                placed.AbsTop,
                placed.Width,
                placed.Height,
                placed.TextContent,
                node.ImageSource,
                node.PlaceholderText,
                node.LinkHref,
                node.ControlKind,
                viewportScale);
        }

        return SceneLayoutCommitFactory.Create(rootSceneNodeId, new SceneViewport(width, height), nodes, layout);
    }

    private SceneNodeId[] GetOrCreateChildArray(SceneNodeId parentId, int length)
    {
        if (!childArrayCache.TryGetValue(parentId, out var childIds) || childIds.Length != length)
        {
            childIds = length == 0 ? [] : new SceneNodeId[length];
            childArrayCache[parentId] = childIds;
        }

        return childIds;
    }

    private SceneLayoutBox CreateLayoutBox(
        SceneNodeId id,
        SceneNodeKind nodeKind,
        HtmlComputedStyle style,
        float absLeft,
        float absTop,
        float width,
        float height,
        string? textContent,
        string? imageSource,
        string? placeholderText,
        string? linkHref,
        SceneControlKind controlKind,
        float viewportScale)
    {
        if (TryReuseLayoutBox(id, nodeKind, style, absLeft, absTop, width, height, textContent, imageSource, placeholderText, linkHref, controlKind, viewportScale, out var reused))
            return reused;

        var textStyle = nodeKind is SceneNodeKind.Text or SceneNodeKind.TextInput
            ? textStyleCache.GetTextStyle(style)
            : null;

        return new SceneLayoutBox(
            nodeKind,
            absLeft,
            absTop,
            width,
            height,
            style.BackgroundColor,
            style.BorderColor,
            style.BorderWidth,
            style.BorderRadius,
            style.BoxSizing,
            textContent,
            textStyle,
            placeholderText,
            style.PlaceholderColor ?? style.Color,
            style.PaddingLeft,
            style.PaddingTop,
            style.PaddingRight,
            style.PaddingBottom,
            nodeKind == SceneNodeKind.TextInput && style.Multiline,
            style.LineHeight,
            ImageSource: imageSource,
            ImageFit: style.ImageFit,
            IsScrollContainer: style.IsScrollContainer,
            ClipContent: style.ClipContent,
            HorizontalScrollEnabled: style.IsScrollContainer,
            BorderStyle: style.BorderStyle,
            LinkHref: linkHref,
            BackgroundImageSource: style.BackgroundImageSource,
            BackgroundImageFit: style.BackgroundImageFit,
            Border: CreateBorder(style),
            ScrollBarWidth: ResolveLayoutScrollBarWidth(style, viewportScale),
            ScrollBarTrackColor: style.ScrollbarTrackColor,
            ScrollBarThumbColor: style.ScrollbarThumbColor,
            BackgroundShadows: style.BackgroundShadows,
            IsPositioned: style.Position != PositionMode.Static,
            ControlKind: controlKind);
    }

    private bool TryReuseLayoutBox(
        SceneNodeId id,
        SceneNodeKind nodeKind,
        HtmlComputedStyle style,
        float absLeft,
        float absTop,
        float width,
        float height,
        string? textContent,
        string? imageSource,
        string? placeholderText,
        string? linkHref,
        SceneControlKind controlKind,
        float viewportScale,
        out SceneLayoutBox box)
    {
        box = null!;
        if (previousCommit is null ||
            !previousCommit.Layout.TryGetValue(id, out var previous) ||
            previous.NodeKind != nodeKind ||
            nodeKind == SceneNodeKind.ScrollView ||
            !Same(previous.AbsLeft, absLeft) ||
            !Same(previous.AbsTop, absTop) ||
            !Same(previous.Width, width) ||
            !Same(previous.Height, height) ||
            previous.BackgroundColor != style.BackgroundColor ||
            previous.BorderColor != style.BorderColor ||
            !Same(previous.BorderWidth, style.BorderWidth) ||
            !Same(previous.BorderRadius, style.BorderRadius) ||
            previous.BoxSizing != style.BoxSizing ||
            previous.TextContent != textContent ||
            previous.PlaceholderText != placeholderText ||
            previous.PlaceholderColor != (style.PlaceholderColor ?? style.Color) ||
            !Same(previous.PaddingLeft, style.PaddingLeft) ||
            !Same(previous.PaddingTop, style.PaddingTop) ||
            !Same(previous.PaddingRight, style.PaddingRight) ||
            !Same(previous.PaddingBottom, style.PaddingBottom) ||
            previous.Multiline != (nodeKind == SceneNodeKind.TextInput && style.Multiline) ||
            !Same(previous.LineHeight, style.LineHeight) ||
            previous.ImageSource != imageSource ||
            previous.ImageFit != style.ImageFit ||
            previous.IsScrollContainer != style.IsScrollContainer ||
            previous.ClipContent != style.ClipContent ||
            previous.HorizontalScrollEnabled != style.IsScrollContainer ||
            previous.BorderStyle != style.BorderStyle ||
            previous.ControlKind != controlKind ||
            previous.LinkHref != linkHref ||
            previous.BackgroundImageSource != style.BackgroundImageSource ||
            previous.BackgroundImageFit != style.BackgroundImageFit ||
            !Same(previous.ScrollBarWidth, ResolveLayoutScrollBarWidth(style, viewportScale)) ||
            previous.ScrollBarTrackColor != style.ScrollbarTrackColor ||
            previous.ScrollBarThumbColor != style.ScrollbarThumbColor ||
            previous.IsPositioned != (style.Position != PositionMode.Static))
        {
            return false;
        }

        if (!SameBorder(previous.Border, style))
            return false;

        if (!SameShadows(previous.BackgroundShadows, style.BackgroundShadows))
            return false;

        if (nodeKind is SceneNodeKind.Text or SceneNodeKind.TextInput)
        {
            if (!SameTextStyle(previous.TextStyle, style))
                return false;
        }
        else if (previous.TextStyle is not null)
        {
            return false;
        }

        box = previous;
        return true;
    }

    private static bool Same(float left, float right)
        => Math.Abs(left - right) <= 0.001f;

    private SceneNodeId ToSceneNodeId(HtmlSceneNodeId id)
        => sceneNodeIds.GetOrCreate(id);

    private static bool SameTextStyle(SceneTextStyle? previous, HtmlComputedStyle style)
    {
        if (previous is null)
            return false;

        return Same(previous.FontSize, style.FontSize) &&
               previous.Color == style.Color &&
               previous.TextAlign == style.TextAlign &&
               previous.WrapText == style.WrapText &&
               previous.Underline == style.Underline &&
               previous.TextOverflowEllipsis == style.TextOverflowEllipsis &&
               SameShadows(previous.TextShadows, style.TextShadows) &&
               Same(previous.Font.Size, style.FontSize) &&
               previous.Font.Family == style.FontFamily &&
               previous.Font.Weight == style.FontWeight &&
               previous.Font.Italic == style.Italic;
    }

    private static bool SameBorder(SceneBoxBorder? previous, HtmlComputedStyle style)
    {
        if (!style.HasAnyVisibleBorder)
            return previous is null;

        if (previous is null)
            return false;

        if (!(style.BorderLeftWidth > 0 && style.BorderLeftStyle != SceneBorderStyle.None ||
              style.BorderTopWidth > 0 && style.BorderTopStyle != SceneBorderStyle.None ||
              style.BorderRightWidth > 0 && style.BorderRightStyle != SceneBorderStyle.None ||
              style.BorderBottomWidth > 0 && style.BorderBottomStyle != SceneBorderStyle.None))
        {
            return Same(previous.LeftWidth, style.BorderWidth) &&
                   Same(previous.TopWidth, style.BorderWidth) &&
                   Same(previous.RightWidth, style.BorderWidth) &&
                   Same(previous.BottomWidth, style.BorderWidth) &&
                   previous.LeftStyle == style.BorderStyle &&
                   previous.TopStyle == style.BorderStyle &&
                   previous.RightStyle == style.BorderStyle &&
                   previous.BottomStyle == style.BorderStyle &&
                   previous.LeftColor == style.BorderColor &&
                   previous.TopColor == style.BorderColor &&
                   previous.RightColor == style.BorderColor &&
                   previous.BottomColor == style.BorderColor;
        }

        return Same(previous.LeftWidth, style.BorderLeftWidth) &&
               Same(previous.TopWidth, style.BorderTopWidth) &&
               Same(previous.RightWidth, style.BorderRightWidth) &&
               Same(previous.BottomWidth, style.BorderBottomWidth) &&
               previous.LeftStyle == style.BorderLeftStyle &&
               previous.TopStyle == style.BorderTopStyle &&
               previous.RightStyle == style.BorderRightStyle &&
               previous.BottomStyle == style.BorderBottomStyle &&
               previous.LeftColor == style.BorderLeftColor &&
               previous.TopColor == style.BorderTopColor &&
               previous.RightColor == style.BorderRightColor &&
               previous.BottomColor == style.BorderBottomColor;
    }

    private static bool SameShadows(SceneBoxShadow[]? left, SceneBoxShadow[]? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Length != right.Length)
            return false;
        for (var index = 0; index < left.Length; index++)
        {
            if (!Equals(left[index], right[index]))
                return false;
        }

        return true;
    }

    private static float ResolveLayoutScrollBarWidth(HtmlComputedStyle style, float viewportScale)
        => Math.Max(0, style.ScrollbarWidth) / Math.Max(0.001f, viewportScale);

    private static SceneBoxBorder? CreateBorder(HtmlComputedStyle style)
    {
        if (!style.HasAnyVisibleBorder)
            return null;

        var hasSideBorder =
            style.BorderLeftWidth > 0 && style.BorderLeftStyle != SceneBorderStyle.None ||
            style.BorderTopWidth > 0 && style.BorderTopStyle != SceneBorderStyle.None ||
            style.BorderRightWidth > 0 && style.BorderRightStyle != SceneBorderStyle.None ||
            style.BorderBottomWidth > 0 && style.BorderBottomStyle != SceneBorderStyle.None;
        if (!hasSideBorder)
        {
            return new SceneBoxBorder(
                style.BorderWidth,
                style.BorderWidth,
                style.BorderWidth,
                style.BorderWidth,
                style.BorderStyle,
                style.BorderStyle,
                style.BorderStyle,
                style.BorderStyle,
                style.BorderColor,
                style.BorderColor,
                style.BorderColor,
                style.BorderColor);
        }

        return new SceneBoxBorder(
            style.BorderLeftWidth,
            style.BorderTopWidth,
            style.BorderRightWidth,
            style.BorderBottomWidth,
            style.BorderLeftStyle,
            style.BorderTopStyle,
            style.BorderRightStyle,
            style.BorderBottomStyle,
            style.BorderLeftColor,
            style.BorderTopColor,
            style.BorderRightColor,
            style.BorderBottomColor);
    }
}
