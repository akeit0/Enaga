using Enaga.Input;
using Enaga.Scene;

namespace Enaga.Html;

internal static class HtmlFragmentTreeFactory
{
    public static HtmlFragmentTree Create(
        HtmlSceneNodeId rootId,
        SceneNodeIdentityMap<HtmlSceneNodeId> sceneNodeIds,
        float rootWidth,
        float rootHeight,
        List<HtmlPlacedNode> placedNodes,
        SceneNodeMap<SceneLayoutBox>? layout = null,
        HtmlFragmentTree? previousTree = null
    )
    {
        var rootFragmentId = CreateFragmentId(rootId);
        var rootSceneNodeId = sceneNodeIds.GetOrCreate(rootId);
        var fragments = new List<HtmlFragment>(placedNodes.Count + 5);
        var rootRect = new HtmlLayoutRect(0, 0, rootWidth, rootHeight);
        fragments.Add(
            CreateOrReuseFragment(
                previousTree,
                rootFragmentId,
                CreateFormattingNodeId(rootId),
                ParentId: null,
                Children: [],
                HtmlFragmentKind.BlockBox,
                rootRect,
                rootRect,
                PaintVersion: 1,
                rootSceneNodeId,
                rootId,
                HtmlGeneratedFragmentRole.None
            )
        );
        if (layout is not null && layout.TryGetValue(rootSceneNodeId, out var rootBox))
            AddScrollBarFragments(fragments, previousTree, rootId, rootId, rootFragmentId, rootBox);

        for (var index = 0; index < placedNodes.Count; index++)
        {
            var placed = placedNodes[index];
            var id = placed.Id;
            var sceneNodeId = sceneNodeIds.GetOrCreate(id);
            var kind = ResolveFragmentKind(placed.Node);
            var rect = new HtmlLayoutRect(
                placed.AbsLeft,
                placed.AbsTop,
                placed.Width,
                placed.Height
            );
            fragments.Add(
                CreateOrReuseFragment(
                    previousTree,
                    CreateFragmentId(id),
                    CreateFormattingNodeId(placed.Node.Id),
                    ParentId: placed.ParentId is null
                        ? rootFragmentId
                        : CreateFragmentId(placed.ParentId.Value),
                    Children: [],
                    kind,
                    borderBox: rect,
                    visualOverflow: ResolveVisualOverflow(placed, rect),
                    PaintVersion: ResolvePaintVersion(placed),
                    sceneNodeId,
                    placed.Node.Id,
                    HtmlGeneratedFragmentRole.None
                )
            );
            if (layout is not null && layout.TryGetValue(sceneNodeId, out var box))
                AddScrollBarFragments(
                    fragments,
                    previousTree,
                    id,
                    placed.Node.Id,
                    CreateFragmentId(id),
                    box
                );
        }

        return new HtmlFragmentTree(rootFragmentId, fragments);
    }

    private static HtmlFragmentKind ResolveFragmentKind(HtmlPlacedNode placed) =>
        ResolveFragmentKind(placed.Node);

    private static HtmlFragmentKind ResolveFragmentKind(HtmlSceneNode node)
    {
        if (node.Role == HtmlSceneNodeRole.ListMarker)
            return HtmlFragmentKind.ListMarker;
        if (node.Role == HtmlSceneNodeRole.TableCell)
            return HtmlFragmentKind.TableCell;

        return node.NodeKind switch
        {
            Enaga.Scene.SceneNodeKind.Text => HtmlFragmentKind.TextRun,
            Enaga.Scene.SceneNodeKind.Image => HtmlFragmentKind.Image,
            Enaga.Scene.SceneNodeKind.ScrollView => HtmlFragmentKind.BlockBox,
            _ => HtmlFragmentKind.BlockBox,
        };
    }

    private static void AddScrollBarFragments(
        List<HtmlFragment> fragments,
        HtmlFragmentTree? previousTree,
        HtmlSceneNodeId sceneNodeId,
        HtmlSceneNodeId sourceSceneNodeId,
        HtmlFragmentId parentId,
        SceneLayoutBox box
    )
    {
        if (box.NodeKind != SceneNodeKind.ScrollView)
            return;

        var paintVersion = ResolveScrollBarPaintVersion(box);
        if (SceneScrollBarLayout.ResolveVerticalScrollBar(box) is { } vertical)
        {
            var gutterWidth = Math.Max(0, box.ScrollBarWidth);
            AddGeneratedFragment(
                fragments,
                previousTree,
                sceneNodeId,
                sourceSceneNodeId,
                parentId,
                HtmlGeneratedFragmentRole.VerticalScrollBarGutter,
                new HtmlLayoutRect(
                    box.AbsLeft + box.Width - gutterWidth,
                    box.AbsTop,
                    gutterWidth,
                    box.Height
                ),
                paintVersion
            );
            AddGeneratedFragment(
                fragments,
                previousTree,
                sceneNodeId,
                sourceSceneNodeId,
                parentId,
                HtmlGeneratedFragmentRole.VerticalScrollBarThumb,
                ToLayoutRect(vertical.ThumbRect),
                paintVersion
            );
        }

        if (SceneScrollBarLayout.ResolveHorizontalScrollBar(box) is { } horizontal)
        {
            var gutterHeight = Math.Max(0, box.ScrollBarWidth);
            AddGeneratedFragment(
                fragments,
                previousTree,
                sceneNodeId,
                sourceSceneNodeId,
                parentId,
                HtmlGeneratedFragmentRole.HorizontalScrollBarGutter,
                new HtmlLayoutRect(
                    box.AbsLeft,
                    box.AbsTop + box.Height - gutterHeight,
                    box.Width,
                    gutterHeight
                ),
                paintVersion
            );
            AddGeneratedFragment(
                fragments,
                previousTree,
                sceneNodeId,
                sourceSceneNodeId,
                parentId,
                HtmlGeneratedFragmentRole.HorizontalScrollBarThumb,
                ToLayoutRect(horizontal.ThumbRect),
                paintVersion
            );
        }
    }

    private static void AddGeneratedFragment(
        List<HtmlFragment> fragments,
        HtmlFragmentTree? previousTree,
        HtmlSceneNodeId sceneNodeId,
        HtmlSceneNodeId sourceSceneNodeId,
        HtmlFragmentId parentId,
        HtmlGeneratedFragmentRole role,
        HtmlLayoutRect rect,
        uint paintVersion
    )
    {
        if (rect.IsEmpty)
            return;

        fragments.Add(
            CreateOrReuseFragment(
                previousTree,
                CreateGeneratedFragmentId(sceneNodeId, role),
                CreateFormattingNodeId(sourceSceneNodeId),
                parentId,
                Children: [],
                HtmlFragmentKind.ScrollBar,
                rect,
                rect,
                paintVersion,
                SceneNodeId: default,
                sourceSceneNodeId,
                role
            )
        );
    }

    private static HtmlFragment CreateOrReuseFragment(
        HtmlFragmentTree? previousTree,
        HtmlFragmentId id,
        HtmlFormattingNodeId sourceNodeId,
        HtmlFragmentId? ParentId,
        IReadOnlyList<HtmlFragmentId> Children,
        HtmlFragmentKind kind,
        HtmlLayoutRect borderBox,
        HtmlLayoutRect visualOverflow,
        uint PaintVersion,
        SceneNodeId SceneNodeId,
        HtmlSceneNodeId SourceSceneNodeId,
        HtmlGeneratedFragmentRole GeneratedRole
    )
    {
        if (
            previousTree is not null
            && previousTree.TryGetFragment(id, out var previous)
            && previous.SourceNodeId == sourceNodeId
            && previous.ParentId == ParentId
            && previous.Children.Count == Children.Count
            && previous.Kind == kind
            && previous.BorderBox == borderBox
            && previous.VisualOverflow == visualOverflow
            && previous.PaintVersion == PaintVersion
            && previous.SceneNodeId == SceneNodeId
            && previous.SourceSceneNodeId == SourceSceneNodeId
            && previous.GeneratedRole == GeneratedRole
        )
        {
            return previous;
        }

        return new HtmlFragment(
            id,
            sourceNodeId,
            ParentId,
            Children,
            kind,
            borderBox,
            visualOverflow,
            PaintVersion,
            SceneNodeId,
            SourceSceneNodeId,
            GeneratedRole
        );
    }

    private static HtmlLayoutRect ToLayoutRect(SceneScrollBarLayout.ScrollBarRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static HtmlLayoutRect ResolveVisualOverflow(
        HtmlPlacedNode placed,
        HtmlLayoutRect borderBox
    )
    {
        var style = placed.Node.Style;
        var shadowOutset = 0f;
        if (style.BackgroundShadows is { Length: > 0 } shadows)
        {
            for (var index = 0; index < shadows.Length; index++)
                shadowOutset = Math.Max(
                    shadowOutset,
                    Math.Abs(shadows[index].OffsetX)
                        + Math.Abs(shadows[index].OffsetY)
                        + shadows[index].Blur
                        + shadows[index].Spread
                );
        }
        if (style.TextShadows is { Length: > 0 } textShadows)
        {
            for (var index = 0; index < textShadows.Length; index++)
                shadowOutset = Math.Max(
                    shadowOutset,
                    Math.Abs(textShadows[index].OffsetX)
                        + Math.Abs(textShadows[index].OffsetY)
                        + textShadows[index].Blur
                );
        }

        var borderOutset = Math.Max(
            Math.Max(style.BorderLeftWidth, style.BorderRightWidth),
            Math.Max(style.BorderTopWidth, style.BorderBottomWidth)
        );
        var outset = Math.Max(0, Math.Max(shadowOutset, borderOutset));
        return outset <= 0
            ? borderBox
            : new HtmlLayoutRect(
                borderBox.Left - outset,
                borderBox.Top - outset,
                borderBox.Width + outset * 2,
                borderBox.Height + outset * 2
            );
    }

    private static uint ResolvePaintVersion(HtmlPlacedNode placed)
    {
        var hash = new HashCode();
        hash.Add(placed.Node.StyleVersion);
        hash.Add(placed.Node.LayoutVersion);
        hash.Add(placed.Node.Style.BackgroundColor);
        hash.Add(placed.Node.Style.BackgroundImageSource);
        hash.Add(placed.Node.Style.BackgroundImageFit);
        hash.Add(placed.Node.Style.BorderColor);
        hash.Add(placed.Node.Style.BorderWidth);
        hash.Add(placed.Node.Style.BorderRadius);
        hash.Add(placed.Node.Style.BorderStyle);
        hash.Add(placed.Node.Style.BorderLeftColor);
        hash.Add(placed.Node.Style.BorderTopColor);
        hash.Add(placed.Node.Style.BorderRightColor);
        hash.Add(placed.Node.Style.BorderBottomColor);
        hash.Add(placed.Node.Style.BorderLeftWidth);
        hash.Add(placed.Node.Style.BorderTopWidth);
        hash.Add(placed.Node.Style.BorderRightWidth);
        hash.Add(placed.Node.Style.BorderBottomWidth);
        hash.Add(placed.Node.Style.Color);
        hash.Add(placed.Node.Style.FontFamily);
        hash.Add(placed.Node.Style.FontSize);
        hash.Add(placed.Node.Style.FontWeight);
        hash.Add(placed.Node.Style.Italic);
        hash.Add(placed.Node.Style.Underline);
        hash.Add(placed.TextContent);
        hash.Add(placed.Node.ImageSource);
        hash.Add(placed.Node.LinkHref);
        return unchecked((uint)hash.ToHashCode());
    }

    private static uint ResolveScrollBarPaintVersion(SceneLayoutBox box)
    {
        var hash = new HashCode();
        hash.Add(box.ScrollBarWidth);
        hash.Add(box.ScrollBarTrackColor);
        hash.Add(box.ScrollBarThumbColor);
        hash.Add(box.Width);
        hash.Add(box.Height);
        hash.Add(box.ContentWidth);
        hash.Add(box.ContentHeight);
        hash.Add(box.HorizontalScrollEnabled);
        return unchecked((uint)hash.ToHashCode());
    }

    private static HtmlFragmentId CreateFragmentId(HtmlSceneNodeId id) => new(StableHash(id));

    private static HtmlFragmentId CreateGeneratedFragmentId(
        HtmlSceneNodeId id,
        HtmlGeneratedFragmentRole role
    ) => new(StableHash(id, (int)role));

    private static HtmlFormattingNodeId CreateFormattingNodeId(HtmlSceneNodeId id) =>
        new(StableHash(id));

    private static int StableHash(HtmlSceneNodeId id) => StableHash(id, 0);

    private static int StableHash(HtmlSceneNodeId id, int discriminator)
    {
        var hash = new HashCode();
        hash.Add(id.Value);
        hash.Add(id.FragmentIndex);
        hash.Add(discriminator);
        return hash.ToHashCode() & 0x7fffffff;
    }
}
