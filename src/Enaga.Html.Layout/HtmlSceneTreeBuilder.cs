using System.Globalization;
using Enaga.Html.Dom;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

internal sealed class HtmlSceneTreeBuilder
{
    private static readonly string[] OrderedListMarkerTextCache = CreateOrderedListMarkerTextCache();
    private readonly HtmlOptions options;
    private readonly LayoutEngineConfig layoutConfig;
    private readonly HtmlPipelineMetrics metrics;
    private readonly HtmlStyledSceneTreeCache cache = new();
    private readonly HtmlSceneVersionStore versionStore = new();
    private readonly HtmlFormattingStyleCache formattingStyleCache = new();

    public HtmlSceneTreeBuilder(HtmlOptions options, LayoutEngineConfig layoutConfig, HtmlPipelineMetrics metrics)
    {
        this.options = options;
        this.layoutConfig = layoutConfig;
        this.metrics = metrics;
    }

    public HtmlStyledSceneTree GetOrCreate(
        HtmlParsedDocument document,
        int viewportWidth,
        int viewportHeight,
        HtmlComputedStyleTree styleTree)
    {
        if (cache.TryGet(document, viewportWidth, viewportHeight, styleTree.Version, out var cachedTree))
        {
            versionStore.MarkCacheHit();
            return cachedTree;
        }

        return cache.Set(
            document,
            viewportWidth,
            viewportHeight,
            styleTree.Version,
            BuildTree(document, viewportWidth, viewportHeight, styleTree));
    }

    public void InvalidateResourceDependentLayout()
        => cache.Clear();

    public Enaga.Html.Style.RestyleHint LastInvalidationHints => versionStore.LastInvalidationHints;

    public Enaga.Html.Style.RenderDamage LastDamage => versionStore.LastDamage;

    public HtmlLayoutDirtySet LastLayoutDirtyNodes => versionStore.LastLayoutDirtyNodes;

    public void ApplyHoverSnapshot(IReadOnlySet<HtmlNodeId>? oldHoveredNodeIds, IReadOnlySet<HtmlNodeId>? newHoveredNodeIds)
        => versionStore.ApplyPseudoStateSnapshot(oldHoveredNodeIds, newHoveredNodeIds, Enaga.Html.Dom.HtmlPseudoState.Hover);

    public void ApplyActiveSnapshot(HtmlNodeId? oldActiveNodeId, HtmlNodeId? newActiveNodeId)
        => versionStore.ApplyPseudoStateSnapshot(oldActiveNodeId, newActiveNodeId, Enaga.Html.Dom.HtmlPseudoState.Active);

    private HtmlStyledSceneTree BuildTree(HtmlParsedDocument document, int viewportWidth, int viewportHeight, HtmlComputedStyleTree styleTree)
    {
        formattingStyleCache.Clear();
        var body = document.RootElement;
        var rootStyle = ResolveElementStyle(body, styleTree).CloneForFormatting();
        rootStyle.ApplyRootDefaults(options);
        var rootChildren = versionStore.AssignVersions(BuildChildren(body, rootStyle, new HtmlNodeIdGenerator(), inheritedLinkHref: null, document.BasePath, viewportWidth, viewportHeight, styleTree));
        return new HtmlStyledSceneTree(rootStyle, rootChildren, versionStore.Generation, body.NodeId);
    }

    private HtmlSceneNode[] BuildChildren(
        HtmlDomElement parent,
        HtmlComputedStyle inheritedStyle,
        HtmlNodeIdGenerator idGenerator,
        string? inheritedLinkHref,
        string? basePath,
        int viewportWidth,
        int viewportHeight,
        HtmlComputedStyleTree styleTree)
    {
        var children = new List<HtmlSceneNode>(parent.Children.Count);
        var listItemIndex = 0;
        var parentIsOrderedList = string.Equals(parent.LocalName, "ol", StringComparison.OrdinalIgnoreCase);
        var parentIsList = parentIsOrderedList || string.Equals(parent.LocalName, "ul", StringComparison.OrdinalIgnoreCase);
        var parentAllowsInlineContent = IsPhrasingContainer(parent) || HasDirectInlineElement(parent);
        for (var childIndex = 0; childIndex < parent.Children.Count; childIndex++)
        {
            var child = parent.Children[childIndex];
            switch (child)
            {
                case HtmlDomElement element:
                    if (parentAllowsInlineContent && IsInlineElement(element))
                    {
                        AddInlineElementNodes(
                            children,
                            element,
                            inheritedStyle,
                            idGenerator,
                            inheritedLinkHref,
                            basePath,
                            viewportWidth,
                            viewportHeight,
                            styleTree);
                        break;
                    }

                    var childStyle = ResolveElementStyle(element, styleTree);
                    if (childStyle.Display == HtmlDisplay.Contents)
                    {
                        children.AddRange(BuildChildren(element, childStyle, idGenerator, inheritedLinkHref, basePath, viewportWidth, viewportHeight, styleTree));
                        break;
                    }

                    string? markerText = null;
                    if (parentIsList &&
                        !inheritedStyle.SuppressListMarker &&
                        string.Equals(element.LocalName, "li", StringComparison.OrdinalIgnoreCase))
                    {
                        markerText = parentIsOrderedList
                            ? GetOrderedListMarkerText(++listItemIndex)
                            : inheritedStyle.UnorderedListMarkerText;
                    }

                    var built = BuildElementNode(element, inheritedStyle, idGenerator, inheritedLinkHref, basePath, viewportWidth, viewportHeight, markerText, parentIsList, styleTree);
                    if (built is not null)
                        children.Add(built);
                    break;
                case HtmlDomText text:
                    if (parentAllowsInlineContent)
                    {
                        AddInlineTextNodes(children, text, inheritedStyle, idGenerator, inheritedLinkHref);
                    }
                    else
                    {
                        var textNode = BuildTextNode(text, inheritedStyle, idGenerator, inheritedLinkHref);
                        if (textNode is not null)
                            children.Add(textNode);
                    }
                    break;
            }
        }

        return GroupInlineRuns(parent, parentAllowsInlineContent, ApplyFlexOrder(inheritedStyle, NormalizeInlinePunctuation(parentAllowsInlineContent, children)), inheritedStyle, idGenerator);
    }

    private static List<HtmlSceneNode> ApplyFlexOrder(HtmlComputedStyle parentStyle, List<HtmlSceneNode> children)
    {
        if (children.Count < 2 || parentStyle.Display != HtmlDisplay.Flex)
            return children;

        List<(HtmlSceneNode Node, int Index)>? ordered = null;
        for (var index = 0; index < children.Count; index++)
        {
            if (children[index].Style.Order == 0)
                continue;

            ordered = new List<(HtmlSceneNode Node, int Index)>(children.Count);
            for (var copyIndex = 0; copyIndex < children.Count; copyIndex++)
                ordered.Add((children[copyIndex], copyIndex));
            break;
        }

        if (ordered is null)
            return children;

        ordered.Sort(static (left, right) =>
        {
            var order = left.Node.Style.Order.CompareTo(right.Node.Style.Order);
            return order != 0 ? order : left.Index.CompareTo(right.Index);
        });

        var result = new List<HtmlSceneNode>(ordered.Count);
        foreach (var item in ordered)
            result.Add(item.Node);
        return result;
    }

    private static bool IsTableRowNode(HtmlSceneNode node)
        => node.Role == HtmlSceneNodeRole.TableRow;

    private static bool IsTableCellNode(HtmlSceneNode node)
        => node.Role == HtmlSceneNodeRole.TableCell;

    private static bool IsTableFormattingNode(HtmlSceneNode node)
        => node.Role is HtmlSceneNodeRole.Table or HtmlSceneNodeRole.TableSection ||
           IsTableRowNode(node) ||
           IsTableCellNode(node);

    private void AddInlineElementNodes(
        List<HtmlSceneNode> nodes,
        HtmlDomElement element,
        HtmlComputedStyle inheritedStyle,
        HtmlNodeIdGenerator idGenerator,
        string? inheritedLinkHref,
        string? basePath,
        int viewportWidth,
        int viewportHeight,
        HtmlComputedStyleTree styleTree)
    {
        if (IsHiddenInputElement(element))
            return;

        var linkHref = ResolveLinkHref(element, inheritedLinkHref, basePath);
        var style = ResolveElementStyle(element, styleTree);
        if (style.Display == HtmlDisplay.None)
            return;
        if (style.Display == HtmlDisplay.Contents)
        {
            nodes.AddRange(BuildChildren(element, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree));
            return;
        }

        var firstNodeId = idGenerator.Next();
        var firstNodeIndex = nodes.Count;
        var role = ResolveSceneNodeRole(element);
        if (style.Display != HtmlDisplay.Inline &&
            style.Display != HtmlDisplay.InlineBlock &&
            !string.Equals(element.LocalName, "br", StringComparison.OrdinalIgnoreCase))
        {
            var nodeKind = ResolveNodeKind(element, style);
            var children = BuildElementChildren(element, nodeKind, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree);
            nodes.Add(new HtmlSceneNode(
                firstNodeId,
                nodeKind,
                style,
                children,
                nodeKind == SceneNodeKind.TextInput ? ResolveTextInputValue(element) : null,
                nodeKind == SceneNodeKind.TextInput ? element.GetAttribute("placeholder") : null,
                null,
                linkHref,
                element.Id,
                element.NodeId,
                ControlKind: ResolveControlKind(element),
                Role: role,
                IsChecked: IsCheckedInputElement(element)));
            return;
        }

        if (style.Display == HtmlDisplay.InlineBlock)
        {
            style = style.CloneForFormatting();
            style.ApplyInlineBlockDefaults();
            var nodeKind = ResolveNodeKind(element, style);
            var children = BuildElementChildren(element, nodeKind, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree);
            nodes.Add(new HtmlSceneNode(
                firstNodeId,
                nodeKind,
                style,
                children,
                nodeKind == SceneNodeKind.TextInput ? ResolveTextInputValue(element) : null,
                nodeKind == SceneNodeKind.TextInput ? element.GetAttribute("placeholder") : null,
                null,
                linkHref,
                element.Id,
                element.NodeId,
                ControlKind: ResolveControlKind(element),
                Role: role,
                IsChecked: IsCheckedInputElement(element)));
            return;
        }

        if (string.Equals(element.LocalName, "br", StringComparison.OrdinalIgnoreCase))
        {
            var breakStyle = formattingStyleCache.GetInlineBreakStyle(style);
            nodes.Add(new HtmlSceneNode(
                firstNodeId,
                SceneNodeKind.View,
                breakStyle,
                [],
                null,
                null,
                null,
                null,
                element.Id,
                element.NodeId,
                ControlKind: ResolveControlKind(element),
                Role: role,
                IsChecked: IsCheckedInputElement(element)));
            return;
        }

        if (style.HasInlineBoxMetrics)
        {
            style = style.CloneForFormatting();
            style.ApplyInlineBoxDefaults();
            var nodeKind = ResolveNodeKind(element, style);
            var children = BuildElementChildren(element, nodeKind, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree);
            nodes.Add(new HtmlSceneNode(
                firstNodeId,
                nodeKind,
                style,
                children,
                nodeKind == SceneNodeKind.TextInput ? ResolveTextInputValue(element) : null,
                nodeKind == SceneNodeKind.TextInput ? element.GetAttribute("placeholder") : null,
                null,
                linkHref,
                element.Id,
                element.NodeId,
                ControlKind: ResolveControlKind(element),
                Role: role,
                IsChecked: IsCheckedInputElement(element)));
            return;
        }

        for (var childIndex = 0; childIndex < element.Children.Count; childIndex++)
        {
            var child = element.Children[childIndex];
            switch (child)
            {
                case HtmlDomText text:
                    AddInlineTextNodes(nodes, text, style, idGenerator, linkHref);
                    break;
                case HtmlDomElement childElement when IsInlineElement(childElement):
                    AddInlineElementNodes(nodes, childElement, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree);
                    break;
                case HtmlDomElement childElement:
                    var built = BuildElementNode(childElement, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, markerText: null, isListItem: false, styleTree);
                    if (built is not null)
                        nodes.Add(built);
                    break;
            }
        }

        if (nodes.Count > firstNodeIndex &&
            string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase) &&
            linkHref is not null)
        {
            nodes[firstNodeIndex].Id = firstNodeId;
            nodes[firstNodeIndex].Label = element.Id ?? nodes[firstNodeIndex].Label;
            nodes[firstNodeIndex].DomNodeId = element.NodeId;
        }
        else if (nodes.Count > firstNodeIndex && element.Id is not null && nodes[firstNodeIndex].Label is null)
        {
            nodes[firstNodeIndex].Id = firstNodeId;
            nodes[firstNodeIndex].Label = element.Id;
            nodes[firstNodeIndex].DomNodeId = element.NodeId;
        }
    }

    private static List<HtmlSceneNode> NormalizeInlinePunctuation(bool parentAllowsInlineContent, List<HtmlSceneNode> children)
    {
        if (!parentAllowsInlineContent || children.Count < 2)
            return children;

        List<HtmlSceneNode>? normalized = null;
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (child.NodeKind == SceneNodeKind.Text &&
                child.TextContent is { } text &&
                IsPunctuationOnly(text) &&
                index > 0 &&
                CanMergeInlinePunctuation(normalized is null ? children[index - 1] : normalized[^1], child))
            {
                normalized ??= CopyPrefix(children, index);
                var previous = normalized[^1];
                previous.TextContent += text;
                continue;
            }

            normalized?.Add(child);
        }

        return normalized is null ? children : normalized;
    }

    private HtmlSceneNode[] GroupInlineRuns(
        HtmlDomElement parent,
        bool parentAllowsInlineContent,
        List<HtmlSceneNode> children,
        HtmlComputedStyle parentStyle,
        HtmlNodeIdGenerator idGenerator)
    {
        List<HtmlSceneNode>? grouped = null;
        var index = 0;
        while (index < children.Count)
        {
            if (!StartsInlineRun(parent, parentAllowsInlineContent, children, index))
            {
                grouped?.Add(children[index]);
                index += 1;
                continue;
            }

            var start = index;
            while (index < children.Count && IsInlineRunMember(parent, parentAllowsInlineContent, children[index]))
                index += 1;

            if (index - start == 1)
            {
                if (NeedsInlineAlignmentContainer(parent, parentStyle) &&
                    children[start].NodeKind == SceneNodeKind.Text)
                {
                    grouped ??= CopyPrefix(children, start);
                    children[start].Style = formattingStyleCache.GetAlignedInlineTextStyle(children[start].Style);
                    grouped.Add(children[start]);
                }
                else if (NeedsInlineAlignmentContainer(parent, parentStyle) &&
                         IsInlineRunCandidate(parent, parentAllowsInlineContent, children[start]))
                {
                    grouped ??= CopyPrefix(children, start);
                    grouped.Add(new HtmlSceneNode(
                        idGenerator.Next(),
                        SceneNodeKind.View,
                        CreateInlineRunContainerStyle(parent, parentStyle, children, start, 1),
                        [children[start]],
                        null,
                        null,
                        null,
                        null,
                        null,
                        Role: HtmlSceneNodeRole.InlineRun));
                }
                else
                {
                    grouped?.Add(children[start]);
                }

                continue;
            }

            grouped ??= CopyPrefix(children, start);
            grouped.Add(new HtmlSceneNode(
                idGenerator.Next(),
                SceneNodeKind.View,
                CreateInlineRunContainerStyle(parent, parentStyle, children, start, index - start),
                CopyInlineRunRange(children, start, index - start),
                null,
                null,
                null,
                null,
                null,
                Role: HtmlSceneNodeRole.InlineRun));
        }

        return grouped is null ? ToArray(children) : grouped.ToArray();
    }

    private HtmlComputedStyle CreateInlineRunContainerStyle(
        HtmlDomElement parent,
        HtmlComputedStyle parentStyle,
        List<HtmlSceneNode> children,
        int start,
        int count)
    {
        if (ContainsInlineControl(children, start, count))
            return formattingStyleCache.GetInlineControlRunStyle(parentStyle);

        return (parentStyle.Display == HtmlDisplay.Block && !IsTableCellElement(parent)) ||
               (IsPhrasingContainer(parent) &&
                parentStyle.Display != HtmlDisplay.Inline &&
                parentStyle.Display != HtmlDisplay.InlineBlock &&
                !IsTableCellElement(parent))
            ? formattingStyleCache.GetInlineFlowStyle(parentStyle)
            : formattingStyleCache.GetInlineRunStyle(parentStyle);
    }

    private static bool ContainsInlineControl(List<HtmlSceneNode> children, int start, int count)
    {
        for (var index = 0; index < count; index++)
        {
            if (IsInlineControl(children[start + index]))
                return true;
        }

        return false;
    }

    private static bool NeedsInlineAlignmentContainer(HtmlDomElement parent, HtmlComputedStyle parentStyle)
        => IsPhrasingContainer(parent) &&
           parentStyle.TextAlign is SceneTextAlign.Center or SceneTextAlign.Right;

    private static bool IsInlineControl(HtmlSceneNode node)
    {
        if (node.NodeKind == SceneNodeKind.TextInput &&
            (node.Style.Display == HtmlDisplay.InlineBlock || node.ControlKind == SceneControlKind.Select) &&
            node.Style.PreferIntrinsicWidth &&
            node.Style.Float == HtmlFloat.None)
        {
            return true;
        }

        return node.NodeKind == SceneNodeKind.View &&
               node.Style.PreferIntrinsicWidth &&
               node.Children.Length > 0 &&
               node.Style.Float == HtmlFloat.None &&
               !IsTableFormattingNode(node);
    }

    private static bool IsInlineRunCandidate(HtmlDomElement parent, HtmlSceneNode node)
        => IsInlineControl(node) ||
           (node.NodeKind == SceneNodeKind.View && node.Style.Display == HtmlDisplay.Inline) ||
           (IsPhrasingContainer(parent) && (node.NodeKind == SceneNodeKind.Text || IsInlineBreak(node)));

    private static bool IsInlineRunCandidate(HtmlDomElement parent, bool parentAllowsInlineContent, HtmlSceneNode node)
        => IsInlineControl(node) ||
           (node.NodeKind == SceneNodeKind.View && node.Style.Display == HtmlDisplay.Inline) ||
           (parentAllowsInlineContent && (node.NodeKind == SceneNodeKind.Text || IsInlineBreak(node)));

    private static bool StartsInlineRun(HtmlDomElement parent, bool parentAllowsInlineContent, List<HtmlSceneNode> children, int index)
    {
        var node = children[index];
        if (IsInlineRunCandidate(parent, parentAllowsInlineContent, node))
            return true;

        return node.NodeKind == SceneNodeKind.Image &&
               index + 1 < children.Count &&
               children[index + 1].NodeKind == SceneNodeKind.Text;
    }

    private static bool IsInlineRunMember(HtmlDomElement parent, bool parentAllowsInlineContent, HtmlSceneNode node)
        => IsInlineRunCandidate(parent, parentAllowsInlineContent, node) || node.NodeKind is SceneNodeKind.Text or SceneNodeKind.Image;

    private static bool IsInlineBreak(HtmlSceneNode node)
        => node.NodeKind == SceneNodeKind.View &&
           node.Children.Length == 0 &&
           node.Style.IsWidthPercent &&
           node.Style.Width >= 100 &&
           Math.Abs(node.Style.Height) < 0.001f;

    private static bool IsPhrasingContainer(HtmlDomElement parent)
        => parent.LocalName is "p" or "li" or "td" or "th" or "h1" or "h2" or "h3" or "a" or "span" or "strong" or "b" or "em" or "i" or "u" or "small" or "font";

    private static bool HasDirectInlineElement(HtmlDomElement parent)
    {
        if (string.Equals(parent.LocalName, "body", StringComparison.OrdinalIgnoreCase))
            return false;

        for (var index = 0; index < parent.Children.Count; index++)
        {
            var child = parent.Children[index];
            if (child is HtmlDomElement element && IsInlineElement(element))
                return true;
        }

        return false;
    }

    private static bool IsInlineElement(HtmlDomElement element)
        => element.LocalName is "a" or "span" or "strong" or "b" or "em" or "i" or "u" or "small" or "font" or "br" or "button" or "select" or "input";

    private HtmlSceneNode? BuildElementNode(
        HtmlDomElement element,
        HtmlComputedStyle inheritedStyle,
        HtmlNodeIdGenerator idGenerator,
        string? inheritedLinkHref,
        string? basePath,
        int viewportWidth,
        int viewportHeight,
        string? markerText,
        bool isListItem,
        HtmlComputedStyleTree styleTree)
    {
        if (element.LocalName is "style" or "script" or "meta" or "link" or "head" ||
            IsHiddenInputElement(element))
        {
            return null;
        }

        var style = ResolveElementStyle(element, styleTree);
        if (style.Display == HtmlDisplay.None)
            return null;

        var elementNodeId = idGenerator.Next();
        var role = ResolveSceneNodeRole(element);
        var nodeKind = ResolveNodeKind(element, style);
        var linkHref = ResolveLinkHref(element, inheritedLinkHref, basePath);
        if (isListItem && style.SuppressListMarker)
            markerText = null;
        if (isListItem && style.Display == HtmlDisplay.Inline)
            markerText = null;
        HtmlComputedStyle? listItemContentStyle = null;
        if (isListItem && markerText is not null)
        {
            listItemContentStyle = formattingStyleCache.GetListItemContentStyle(style);
            style = style.CloneForFormatting();
            style.ApplyListItemContainerDefaults(markerText);
        }
        if (markerText is null &&
            layoutConfig.CollapseTextOnlyElements &&
            nodeKind == SceneNodeKind.View &&
            style.CanCollapseTextOnlyContent &&
            style.WhiteSpace == HtmlWhiteSpace.Normal &&
            TryGetCollapsedTextContent(element, out var collapsedText))
        {
            if (markerText is not null)
                collapsedText = markerText + collapsedText;

            return new HtmlSceneNode(
                elementNodeId,
                SceneNodeKind.Text,
                style,
                [],
                collapsedText,
                null,
                null,
                linkHref,
                element.Id,
                element.NodeId,
                Role: role);
        }

        var childInheritedStyle = listItemContentStyle ?? style;
        var children = IsInputButtonElement(element)
            ? BuildInputButtonChildren(element, style, idGenerator, linkHref)
            : nodeKind is SceneNodeKind.View or SceneNodeKind.ScrollView
                ? BuildChildren(element, childInheritedStyle, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree)
                : [];
        if (markerText is not null && children.Length > 0)
        {
            var markerStyle = formattingStyleCache.GetListMarkerStyle(style, markerText);
            var markerNode = new HtmlSceneNode(
                idGenerator.Next(),
                SceneNodeKind.Text,
                markerStyle,
                [],
                markerText,
                null,
                null,
                null,
                null,
                Role: HtmlSceneNodeRole.ListMarker);
            var contentNode = new HtmlSceneNode(
                idGenerator.Next(),
                SceneNodeKind.View,
                listItemContentStyle ?? formattingStyleCache.GetListItemContentStyle(style),
                children,
                null,
                null,
                null,
                null,
                null,
                Role: HtmlSceneNodeRole.ListContent);
            children = [markerNode, contentNode];
        }

        var textContent = nodeKind == SceneNodeKind.TextInput
            ? ResolveTextInputValue(element)
            : null;
        var placeholderText = nodeKind == SceneNodeKind.TextInput
            ? element.GetAttribute("placeholder")
            : null;
        var imageSource = nodeKind == SceneNodeKind.Image
            ? HtmlUrlResolver.Resolve(element.GetAttribute("src"), basePath)
            : null;
        if (nodeKind == SceneNodeKind.Image &&
            imageSource is not null &&
            TryReadImageIntrinsicSize(imageSource, out var intrinsicWidth, out var intrinsicHeight))
        {
            style = style.CloneForFormatting();
            style.ApplyIntrinsicImageSize(intrinsicWidth, intrinsicHeight);
        }
        else if (nodeKind == SceneNodeKind.Image &&
                 imageSource is not null &&
                 TryReadRuntimeImageIntrinsicSize(imageSource, out intrinsicWidth, out intrinsicHeight))
        {
            style = style.CloneForFormatting();
            style.ApplyIntrinsicImageSize(intrinsicWidth, intrinsicHeight);
        }
        else if (nodeKind == SceneNodeKind.Image &&
                 IsRemoteHttpImageSource(imageSource))
        {
            style = style.CloneForFormatting();
            style.ApplyDefaultImageSize();
        }
        var rowSpan = IsTableCellElement(element) ? ParsePositiveInt(element.GetAttribute("rowspan"), 1) : 1;
        var colSpan = IsTableCellElement(element) ? ParsePositiveInt(element.GetAttribute("colspan"), 1) : 1;

        return new HtmlSceneNode(
            elementNodeId,
            nodeKind,
            style,
            children,
            textContent,
            placeholderText,
            imageSource,
            linkHref,
            element.Id,
            element.NodeId,
            RowSpan: rowSpan,
            ColSpan: colSpan,
            ControlKind: ResolveControlKind(element),
            Role: role,
            IsChecked: IsCheckedInputElement(element));
    }

    private HtmlSceneNode[] BuildElementChildren(
        HtmlDomElement element,
        SceneNodeKind nodeKind,
        HtmlComputedStyle style,
        HtmlNodeIdGenerator idGenerator,
        string? linkHref,
        string? basePath,
        int viewportWidth,
        int viewportHeight,
        HtmlComputedStyleTree styleTree)
        => IsInputButtonElement(element)
            ? BuildInputButtonChildren(element, style, idGenerator, linkHref)
            : nodeKind is SceneNodeKind.View or SceneNodeKind.ScrollView
                ? BuildChildren(element, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree)
                : [];

    private static HtmlSceneNodeRole ResolveSceneNodeRole(HtmlDomElement element)
        => element.LocalName switch
        {
            "table" => HtmlSceneNodeRole.Table,
            "thead" or "tbody" or "tfoot" => HtmlSceneNodeRole.TableSection,
            "tr" => HtmlSceneNodeRole.TableRow,
            "td" or "th" => HtmlSceneNodeRole.TableCell,
            "li" => HtmlSceneNodeRole.ListItem,
            _ => HtmlSceneNodeRole.Normal
        };

    private HtmlComputedStyle ResolveElementStyle(HtmlDomElement element, HtmlComputedStyleTree styleTree)
    {
        if (styleTree.Styles.TryGetValue(element.NodeId, out var precomputedStyle))
            return precomputedStyle;

        throw new InvalidOperationException($"Computed style not found for DOM node {element.NodeId.Value} ({element.LocalName}).");
    }

    private static bool IsTableCellElement(HtmlDomElement element)
        => element.LocalName is "td" or "th";

    private static bool TryReadImageIntrinsicSize(string imageSource, out float width, out float height)
    {
        width = 0;
        height = 0;
        if (!File.Exists(imageSource))
            return false;

        var extension = Path.GetExtension(imageSource);
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            return TryReadSvgIntrinsicSize(imageSource, out width, out height);

        return false;
    }

    private static bool IsRemoteHttpImageSource(string? imageSource)
        => Uri.TryCreate(imageSource, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private bool TryReadRuntimeImageIntrinsicSize(string imageSource, out float width, out float height)
    {
        width = 0;
        height = 0;

        var backendServices = options.BackendServices;
        if (backendServices is null || ReferenceEquals(backendServices, RuntimeBackendServices.Missing))
            return false;

        var result = backendServices.Images.ResolveImage(imageSource);
        return result.State == RuntimeImageResolveState.Ready &&
               !string.IsNullOrWhiteSpace(result.LocalPath) &&
               TryReadImageIntrinsicSize(result.LocalPath, out width, out height);
    }

    private static bool TryReadSvgIntrinsicSize(string path, out float width, out float height)
    {
        width = 0;
        height = 0;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (!TryReadSvgLengthAttribute(text, "width", out width) ||
            !TryReadSvgLengthAttribute(text, "height", out height))
        {
            if (!TryReadSvgViewBox(text, out width, out height))
                return false;
        }

        return width > 0 && height > 0;
    }

    private static bool TryReadSvgLengthAttribute(string text, string attributeName, out float value)
    {
        value = 0;
        var pattern = attributeName + "=\"";
        var start = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            pattern = attributeName + "='";
            start = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        }

        if (start < 0)
            return false;

        start += pattern.Length;
        var end = text.IndexOf(pattern[^1], start);
        if (end <= start)
            return false;

        var span = text.AsSpan(start, end - start).Trim();
        if (span.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase))
            span = span[..^2].Trim();

        return float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadSvgViewBox(string text, out float width, out float height)
    {
        width = 0;
        height = 0;
        var start = text.IndexOf("viewBox=\"", StringComparison.OrdinalIgnoreCase);
        var quote = '"';
        if (start < 0)
        {
            start = text.IndexOf("viewBox='", StringComparison.OrdinalIgnoreCase);
            quote = '\'';
        }

        if (start < 0)
            return false;

        start += "viewBox='".Length;
        var end = text.IndexOf(quote, start);
        if (end <= start)
            return false;

        var parts = text[start..end].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 4 &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out width) &&
               float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out height);
    }

    private static int ParsePositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static bool TryGetCollapsedTextContent(HtmlDomElement element, out string textContent)
    {
        textContent = string.Empty;
        if (element.Children.Count == 0)
            return false;

        for (var index = 0; index < element.Children.Count; index++)
        {
            var child = element.Children[index];
            if (child is not HtmlDomText)
                return false;
        }

        textContent = HtmlTextNormalizer.Normalize(ConcatDirectTextChildren(element));
        return textContent.Length > 0;
    }

    private static string ResolveTextInputValue(HtmlDomElement element)
    {
        if (string.Equals(element.LocalName, "textarea", StringComparison.OrdinalIgnoreCase))
            return ConcatDirectTextChildren(element);

        if (string.Equals(element.LocalName, "select", StringComparison.OrdinalIgnoreCase))
            return ResolveSelectDisplayText(element);

        return element.GetAttribute("value") ?? string.Empty;
    }

    private static string ConcatDirectTextChildren(HtmlDomElement element)
    {
        string? textContent = null;
        System.Text.StringBuilder? builder = null;
        for (var index = 0; index < element.Children.Count; index++)
        {
            if (element.Children[index] is not HtmlDomText text)
                continue;

            AppendText(text.Text, ref textContent, ref builder);
        }

        return builder?.ToString() ?? textContent ?? string.Empty;
    }

    private static void AppendText(string text, ref string? textContent, ref System.Text.StringBuilder? builder)
    {
        if (text.Length == 0)
            return;

        if (builder is not null)
        {
            builder.Append(text);
            return;
        }

        if (textContent is null)
        {
            textContent = text;
            return;
        }

        builder = new System.Text.StringBuilder(textContent.Length + text.Length);
        builder.Append(textContent);
        builder.Append(text);
        textContent = null;
    }

    private HtmlSceneNode[] BuildInputButtonChildren(
        HtmlDomElement element,
        HtmlComputedStyle style,
        HtmlNodeIdGenerator idGenerator,
        string? linkHref)
    {
        var value = element.GetAttribute("value");
        if (string.IsNullOrWhiteSpace(value))
        {
            value = ResolveInputButtonDefaultValue(element.GetAttribute("type"));
        }

        var textStyle = formattingStyleCache.GetInlineTextStyle(style);
        return
        [
            new HtmlSceneNode(
                idGenerator.Next(),
                SceneNodeKind.Text,
                textStyle,
                [],
                value,
                null,
                null,
                linkHref,
                null,
                element.NodeId,
                Role: HtmlSceneNodeRole.InputText)
        ];
    }

    private static string ResolveSelectDisplayText(HtmlDomElement element)
    {
        var firstOptionText = FindSelectOptionText(element, selectedOnly: false);
        return FindSelectOptionText(element, selectedOnly: true) ?? firstOptionText ?? string.Empty;
    }

    private static string? FindSelectOptionText(HtmlDomElement element, bool selectedOnly)
    {
        for (var index = 0; index < element.Children.Count; index++)
        {
            var child = element.Children[index];
            if (child is not HtmlDomElement childElement)
                continue;

            if (string.Equals(childElement.LocalName, "option", StringComparison.OrdinalIgnoreCase))
            {
                if (!selectedOnly || childElement.Attributes.ContainsKey("selected"))
                    return childElement.InnerText;
            }

            var nested = FindSelectOptionText(childElement, selectedOnly);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private HtmlSceneNode? BuildTextNode(HtmlDomText text, HtmlComputedStyle inheritedStyle, HtmlNodeIdGenerator idGenerator, string? inheritedLinkHref, bool inlineText = false)
    {
        var normalized = ApplyTextTransform(HtmlTextNormalizer.Normalize(text.Text, inheritedStyle.WhiteSpace), inheritedStyle.TextTransform);
        if (normalized.Length == 0)
            return null;

        var textStyle = inlineText
            ? formattingStyleCache.GetInlineTextStyle(inheritedStyle)
            : formattingStyleCache.GetTextStyle(inheritedStyle);

        return new HtmlSceneNode(
            idGenerator.Next(),
            SceneNodeKind.Text,
            textStyle,
            [],
            normalized,
            null,
            null,
            inheritedLinkHref,
            null,
            Role: HtmlSceneNodeRole.Text);
    }

    private void AddInlineTextNodes(
        List<HtmlSceneNode> nodes,
        HtmlDomText text,
        HtmlComputedStyle inheritedStyle,
        HtmlNodeIdGenerator idGenerator,
        string? inheritedLinkHref)
    {
        var normalized = ApplyTextTransform(HtmlTextNormalizer.Normalize(text.Text, inheritedStyle.WhiteSpace), inheritedStyle.TextTransform);
        if (normalized.Length == 0)
            return;

        if (ShouldWrapAsUnsegmentedInlineText(normalized))
        {
            nodes.Add(new HtmlSceneNode(
                idGenerator.Next(),
                SceneNodeKind.Text,
                formattingStyleCache.GetInlineWrappedTextStyle(inheritedStyle),
                [],
                normalized,
                null,
                null,
                inheritedLinkHref,
                null,
                Role: HtmlSceneNodeRole.Text));
            return;
        }

        if (inheritedStyle.WhiteSpace is HtmlWhiteSpace.Pre or HtmlWhiteSpace.PreWrap or HtmlWhiteSpace.PreLine or HtmlWhiteSpace.NoWrap)
        {
            var textStyle = formattingStyleCache.GetInlineTextStyle(inheritedStyle, preserveWhiteSpaceWrapping: true);
            nodes.Add(new HtmlSceneNode(
                idGenerator.Next(),
                SceneNodeKind.Text,
                textStyle,
                [],
                normalized,
                null,
                null,
                inheritedLinkHref,
                null,
                Role: HtmlSceneNodeRole.Text));
            return;
        }

        var index = 0;
        while (index < normalized.Length)
        {
            while (index < normalized.Length && normalized[index] == ' ')
                index++;

            var start = index;
            while (index < normalized.Length && normalized[index] != ' ')
                index++;

            if (start >= index)
                continue;

            var word = normalized[start..index];
            if (IsPunctuationOnly(word) && nodes.Count > 0)
            {
                var previous = nodes[^1];
                previous.TextContent += word;
                continue;
            }

            var wordStyle = formattingStyleCache.GetInlineTextStyle(inheritedStyle);
            nodes.Add(new HtmlSceneNode(
                idGenerator.Next(),
                SceneNodeKind.Text,
                wordStyle,
                [],
                word,
                null,
                null,
                inheritedLinkHref,
                null,
                Role: HtmlSceneNodeRole.Text));
        }
    }

    private static bool ShouldWrapAsUnsegmentedInlineText(string text)
    {
        if (text.Length < 16)
            return false;

        var hasCjk = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
                return false;

            if (IsCjkCharacter(ch))
                hasCjk = true;
        }

        return hasCjk;
    }

    private static string ApplyTextTransform(string text, HtmlTextTransform transform)
        => transform switch
        {
            HtmlTextTransform.Uppercase => text.ToUpperInvariant(),
            HtmlTextTransform.Lowercase => text.ToLowerInvariant(),
            _ => text
        };

    private static List<HtmlSceneNode> CopyPrefix(List<HtmlSceneNode> source, int count)
    {
        var list = new List<HtmlSceneNode>(source.Count);
        for (var index = 0; index < count; index++)
            list.Add(source[index]);
        return list;
    }

    private HtmlSceneNode[] CopyInlineRunRange(List<HtmlSceneNode> source, int start, int count)
    {
        if (count <= 0)
            return [];

        var range = new HtmlSceneNode[count];
        var containsImage = false;
        for (var index = 0; index < count; index++)
        {
            if (source[start + index].NodeKind == SceneNodeKind.Image)
            {
                containsImage = true;
                break;
            }
        }

        for (var index = 0; index < count; index++)
        {
            var node = source[start + index];
            if (node.NodeKind == SceneNodeKind.Text &&
                node.Style.Display != HtmlDisplay.Inline &&
                (!node.Style.WrapText || containsImage))
            {
                node.Style = formattingStyleCache.GetInlineTextStyle(node.Style);
            }
            else if (node.NodeKind == SceneNodeKind.Image)
            {
                node.Style = formattingStyleCache.GetInlineImageStyle(node.Style);
            }

            range[index] = node;
        }

        return range;
    }

    private static HtmlSceneNode[] ToArray(List<HtmlSceneNode> source)
        => source.Count == 0 ? [] : source.ToArray();

    private static bool IsPunctuationOnly(string text)
    {
        if (text.Length == 0)
            return false;

        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsPunctuation(text[index]))
                return false;
        }

        return true;
    }

    private static bool CanMergeInlinePunctuation(HtmlSceneNode previous, HtmlSceneNode punctuation)
    {
        return string.Equals(previous.LinkHref, punctuation.LinkHref, StringComparison.Ordinal) &&
               string.Equals(previous.Style.Color, punctuation.Style.Color, StringComparison.Ordinal) &&
               previous.Style.Underline == punctuation.Style.Underline;
    }

    private static bool IsCjkCharacter(char ch)
        => ch is >= '\u3040' and <= '\u30ff' ||
           ch is >= '\u3400' and <= '\u9fff' ||
           ch is >= '\uf900' and <= '\ufaff';

    private static string GetOrderedListMarkerText(int index)
    {
        if ((uint)index < (uint)OrderedListMarkerTextCache.Length)
            return OrderedListMarkerTextCache[index];

        return string.Create(CultureInfo.InvariantCulture, $"{index}.");
    }

    private static string[] CreateOrderedListMarkerTextCache()
    {
        const int count = 256;
        var cache = new string[count];
        cache[0] = string.Empty;
        for (var index = 1; index < cache.Length; index++)
            cache[index] = string.Create(CultureInfo.InvariantCulture, $"{index}.");
        return cache;
    }
    private static SceneNodeKind ResolveNodeKind(HtmlDomElement element, HtmlComputedStyle style)
    {
        if (element.LocalName == "img")
            return SceneNodeKind.Image;
        if (IsInputButtonElement(element) || IsRadioInputElement(element))
            return SceneNodeKind.View;
        if (element.LocalName is "input" or "textarea" or "select")
            return SceneNodeKind.TextInput;
        if (style.IsScrollContainer)
            return SceneNodeKind.ScrollView;
        return SceneNodeKind.View;
    }

    private static SceneControlKind ResolveControlKind(HtmlDomElement element)
        => element.LocalName switch
        {
            "select" => SceneControlKind.Select,
            "textarea" => SceneControlKind.TextArea,
            "button" => SceneControlKind.Button,
            "input" => ResolveInputControlKind(element),
            _ => SceneControlKind.None
        };

    private static SceneControlKind ResolveInputControlKind(HtmlDomElement element)
    {
        var type = element.GetAttribute("type");
        if (string.Equals(type, "radio", StringComparison.OrdinalIgnoreCase))
            return SceneControlKind.Radio;
        if (string.Equals(type, "submit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "button", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "reset", StringComparison.OrdinalIgnoreCase))
        {
            return SceneControlKind.Button;
        }

        return SceneControlKind.TextInput;
    }

    private static bool IsHiddenInputElement(HtmlDomElement element)
        => string.Equals(element.LocalName, "input", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(element.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase);

    private static bool IsInputButtonElement(HtmlDomElement element)
    {
        if (!string.Equals(element.LocalName, "input", StringComparison.OrdinalIgnoreCase))
            return false;

        var type = element.GetAttribute("type");
        return string.Equals(type, "submit", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "button", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "reset", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRadioInputElement(HtmlDomElement element)
        => string.Equals(element.LocalName, "input", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(element.GetAttribute("type"), "radio", StringComparison.OrdinalIgnoreCase);

    private static bool IsCheckedInputElement(HtmlDomElement element)
        => string.Equals(element.LocalName, "input", StringComparison.OrdinalIgnoreCase) &&
           element.Attributes.ContainsKey("checked");

    private static string ResolveInputButtonDefaultValue(string? type)
    {
        if (string.Equals(type, "reset", StringComparison.OrdinalIgnoreCase))
            return "Reset";
        if (string.Equals(type, "button", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return "Submit";
    }
    private static string? ResolveLinkHref(HtmlDomElement element, string? inheritedLinkHref, string? basePath)
    {
        if (!string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase))
            return inheritedLinkHref;

        var href = element.GetAttribute("href");
        return string.IsNullOrWhiteSpace(href)
            ? inheritedLinkHref
            : HtmlUrlResolver.Resolve(href, basePath);
    }

    private sealed class HtmlFormattingStyleCache
    {
        private readonly Dictionary<Key, HtmlComputedStyle> styles = new();

        public void Clear() => styles.Clear();

        public HtmlComputedStyle GetTextStyle(HtmlComputedStyle source)
            => GetOrCreate(source, FormattingStyleKind.Text, null, static source => source.CreateTextStyle());

        public HtmlComputedStyle GetInlineTextStyle(HtmlComputedStyle source, bool preserveWhiteSpaceWrapping = false)
            => GetOrCreate(
                source,
                preserveWhiteSpaceWrapping ? FormattingStyleKind.InlineTextPreserveWhiteSpace : FormattingStyleKind.InlineText,
                null,
                static source =>
                {
                    var style = source.CreateTextStyle();
                    style.ApplyInlineTextDefaults();
                    return style;
                },
                static source =>
                {
                    var style = source.CreateTextStyle();
                    style.ApplyInlineTextDefaults(preserveWhiteSpaceWrapping: true);
                    return style;
                });

        public HtmlComputedStyle GetInlineWrappedTextStyle(HtmlComputedStyle source)
            => GetOrCreate(source, FormattingStyleKind.InlineWrappedText, null, static source => HtmlComputedStyle.CreateInlineWrappedTextDefault(source.CreateTextStyle()));

        public HtmlComputedStyle GetAlignedInlineTextStyle(HtmlComputedStyle source)
            => GetOrCreate(source, FormattingStyleKind.AlignedInlineText, null, HtmlComputedStyle.CreateAlignedInlineTextDefault);

        public HtmlComputedStyle GetInlineRunStyle(HtmlComputedStyle source)
            => GetOrCreate(source, FormattingStyleKind.InlineRun, null, HtmlComputedStyle.CreateInlineRunDefault);

        public HtmlComputedStyle GetInlineFlowStyle(HtmlComputedStyle source)
            => GetOrCreate(source, FormattingStyleKind.InlineFlow, null, HtmlComputedStyle.CreateInlineFlowDefault);

        public HtmlComputedStyle GetInlineControlRunStyle(HtmlComputedStyle source)
            => GetOrCreate(source, FormattingStyleKind.InlineControlRun, null, HtmlComputedStyle.CreateInlineControlRunDefault);

        public HtmlComputedStyle GetInlineImageStyle(HtmlComputedStyle source)
            => GetOrCreate(source, FormattingStyleKind.InlineImage, null, static source => source.CreateInlineImageStyle());

        public HtmlComputedStyle GetInlineBreakStyle(HtmlComputedStyle source)
            => GetOrCreate(
                source,
                FormattingStyleKind.InlineBreak,
                null,
                static source =>
                {
                    var style = source.CreateTextStyle();
                    style.ApplyInlineBreakDefaults();
                    return style;
                });

        public HtmlComputedStyle GetListItemContentStyle(HtmlComputedStyle source)
            => GetOrCreate(source, FormattingStyleKind.ListItemContent, null, static source => source.CreateListItemContentStyle());

        public HtmlComputedStyle GetListMarkerStyle(HtmlComputedStyle source, string markerText)
            => GetOrCreate(
                source,
                FormattingStyleKind.ListMarker,
                markerText.Length > 1 ? "wide" : "narrow",
                static (source, markerText) =>
                {
                    var style = source.CreateTextStyle();
                    style.ApplyInlineTextDefaults();
                    style.ApplyListMarkerDefaults(markerText == "wide" ? "00" : "0");
                    return style;
                });

        private HtmlComputedStyle GetOrCreate(
            HtmlComputedStyle source,
            FormattingStyleKind kind,
            string? discriminator,
            Func<HtmlComputedStyle, HtmlComputedStyle> create)
        {
            var key = new Key(source, kind, discriminator);
            if (styles.TryGetValue(key, out var style))
                return style;

            style = create(source);
            styles[key] = style;
            return style;
        }

        private HtmlComputedStyle GetOrCreate(
            HtmlComputedStyle source,
            FormattingStyleKind kind,
            string? discriminator,
            Func<HtmlComputedStyle, HtmlComputedStyle> createDefault,
            Func<HtmlComputedStyle, HtmlComputedStyle> createAlternate)
        {
            var key = new Key(source, kind, discriminator);
            if (styles.TryGetValue(key, out var style))
                return style;

            style = kind == FormattingStyleKind.InlineTextPreserveWhiteSpace ? createAlternate(source) : createDefault(source);
            styles[key] = style;
            return style;
        }

        private HtmlComputedStyle GetOrCreate(
            HtmlComputedStyle source,
            FormattingStyleKind kind,
            string? discriminator,
            Func<HtmlComputedStyle, string, HtmlComputedStyle> create)
        {
            var key = new Key(source, kind, discriminator);
            if (styles.TryGetValue(key, out var style))
                return style;

            style = create(source, discriminator ?? string.Empty);
            styles[key] = style;
            return style;
        }

        private readonly record struct Key(HtmlComputedStyle Source, FormattingStyleKind Kind, string? Discriminator);
    }

    private enum FormattingStyleKind : byte
    {
        Text,
        InlineText,
        InlineTextPreserveWhiteSpace,
        InlineWrappedText,
        AlignedInlineText,
        InlineRun,
        InlineFlow,
        InlineControlRun,
        InlineImage,
        InlineBreak,
        ListItemContent,
        ListMarker
    }
}

