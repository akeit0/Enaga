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

    public Enaga.Html.Style.RestyleHint LastInvalidationHints => versionStore.LastInvalidationHints;

    public Enaga.Html.Style.RenderDamage LastDamage => versionStore.LastDamage;

    public HtmlLayoutDirtySet LastLayoutDirtyNodes => versionStore.LastLayoutDirtyNodes;

    public void ApplyHoverSnapshot(IReadOnlySet<HtmlNodeId>? oldHoveredNodeIds, IReadOnlySet<HtmlNodeId>? newHoveredNodeIds)
        => versionStore.ApplyPseudoStateSnapshot(oldHoveredNodeIds, newHoveredNodeIds, Enaga.Html.Dom.HtmlPseudoState.Hover);

    public void ApplyActiveSnapshot(HtmlNodeId? oldActiveNodeId, HtmlNodeId? newActiveNodeId)
        => versionStore.ApplyPseudoStateSnapshot(oldActiveNodeId, newActiveNodeId, Enaga.Html.Dom.HtmlPseudoState.Active);

    private HtmlStyledSceneTree BuildTree(HtmlParsedDocument document, int viewportWidth, int viewportHeight, HtmlComputedStyleTree styleTree)
    {
        var body = document.RootElement;
        var rootStyle = ResolveElementStyle(body, styleTree);
        rootStyle.ApplyRootDefaults(options);
        var rootChildren = versionStore.AssignVersions(BuildChildren(body, rootStyle, new HtmlNodeIdGenerator(), inheritedLinkHref: null, document.BasePath, viewportWidth, viewportHeight, styleTree));
        return new HtmlStyledSceneTree(rootStyle, rootChildren, versionStore.Generation, body.NodeId);
    }

    private IReadOnlyList<HtmlSceneNode> BuildChildren(
        HtmlDomElement parent,
        HtmlComputedStyle inheritedStyle,
        HtmlNodeIdGenerator idGenerator,
        string? inheritedLinkHref,
        string? basePath,
        int viewportWidth,
        int viewportHeight,
        HtmlComputedStyleTree styleTree)
    {
        var children = new List<HtmlSceneNode>();
        var listItemIndex = 0;
        var parentIsOrderedList = string.Equals(parent.LocalName, "ol", StringComparison.OrdinalIgnoreCase);
        var parentIsList = parentIsOrderedList || string.Equals(parent.LocalName, "ul", StringComparison.OrdinalIgnoreCase);
        var parentAllowsInlineContent = IsPhrasingContainer(parent) || HasDirectInlineElement(parent);
        foreach (var child in parent.Children)
        {
            switch (child)
            {
                case HtmlDomElement element:
                    if (parentAllowsInlineContent && IsInlineElement(element))
                    {
                        children.AddRange(BuildInlineElementNodes(
                            element,
                            inheritedStyle,
                            idGenerator,
                            inheritedLinkHref,
                            basePath,
                            viewportWidth,
                            viewportHeight,
                            styleTree));
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
                        children.AddRange(BuildInlineTextNodes(text, inheritedStyle, idGenerator, inheritedLinkHref));
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

        return GroupInlineRuns(parent, parentAllowsInlineContent, NormalizeInlinePunctuation(parentAllowsInlineContent, children), inheritedStyle, idGenerator);
    }

    private static bool IsTableRowNode(HtmlSceneNode node)
        => node.Id.StartsWith("tr-", StringComparison.Ordinal);

    private static bool IsTableCellNode(HtmlSceneNode node)
        => node.Id.StartsWith("td-", StringComparison.Ordinal) ||
           node.Id.StartsWith("th-", StringComparison.Ordinal);

    private static bool IsTableFormattingNode(HtmlSceneNode node)
        => node.Id.StartsWith("table-", StringComparison.Ordinal) ||
           node.Id.StartsWith("tbody-", StringComparison.Ordinal) ||
           node.Id.StartsWith("thead-", StringComparison.Ordinal) ||
           node.Id.StartsWith("tfoot-", StringComparison.Ordinal) ||
           IsTableRowNode(node) ||
           IsTableCellNode(node);

    private IReadOnlyList<HtmlSceneNode> BuildInlineElementNodes(
        HtmlDomElement element,
        HtmlComputedStyle inheritedStyle,
        HtmlNodeIdGenerator idGenerator,
        string? inheritedLinkHref,
        string? basePath,
        int viewportWidth,
        int viewportHeight,
        HtmlComputedStyleTree styleTree)
    {
        var firstNodeId = idGenerator.Next(element.LocalName);
        var linkHref = ResolveLinkHref(element, inheritedLinkHref, basePath);
        var style = ResolveElementStyle(element, styleTree);
        if (style.Display == HtmlDisplay.None)
            return [];

        if (style.Display != HtmlDisplay.Inline &&
            style.Display != HtmlDisplay.InlineBlock &&
            !string.Equals(element.LocalName, "br", StringComparison.OrdinalIgnoreCase))
        {
            var nodeKind = ResolveNodeKind(element, style);
            var children = nodeKind is SceneNodeKind.View or SceneNodeKind.ScrollView
                ? BuildChildren(element, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree)
                : [];
            return
            [
                new HtmlSceneNode(
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
                    ControlKind: ResolveControlKind(element))
            ];
        }

        if (style.Display == HtmlDisplay.InlineBlock)
        {
            style.ApplyInlineBlockDefaults();
            var nodeKind = ResolveNodeKind(element, style);
            var children = nodeKind is SceneNodeKind.View or SceneNodeKind.ScrollView
                ? BuildChildren(element, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree)
                : [];
            return
            [
                new HtmlSceneNode(
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
                    ControlKind: ResolveControlKind(element))
            ];
        }

        if (string.Equals(element.LocalName, "br", StringComparison.OrdinalIgnoreCase))
        {
            var breakStyle = style.CreateTextStyle();
            breakStyle.ApplyInlineBreakDefaults();
            return
            [
                new HtmlSceneNode(
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
                ControlKind: ResolveControlKind(element))
            ];
        }

        if (style.HasInlineBoxMetrics)
        {
            style.ApplyInlineBoxDefaults();
            var nodeKind = ResolveNodeKind(element, style);
            var children = nodeKind is SceneNodeKind.View or SceneNodeKind.ScrollView
                ? BuildChildren(element, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree)
                : [];
            return
            [
                new HtmlSceneNode(
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
                    ControlKind: ResolveControlKind(element))
            ];
        }

        var nodes = new List<HtmlSceneNode>();
        foreach (var child in element.Children)
        {
            switch (child)
            {
                case HtmlDomText text:
                    nodes.AddRange(BuildInlineTextNodes(text, style, idGenerator, linkHref));
                    break;
                case HtmlDomElement childElement when IsInlineElement(childElement):
                    nodes.AddRange(BuildInlineElementNodes(childElement, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree));
                    break;
                case HtmlDomElement childElement:
                    var built = BuildElementNode(childElement, style, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, markerText: null, isListItem: false, styleTree);
                    if (built is not null)
                        nodes.Add(built);
                    break;
            }
        }

        if (nodes.Count > 0 &&
            string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase) &&
            linkHref is not null)
        {
            nodes[0] = nodes[0] with { Id = firstNodeId, Label = element.Id ?? nodes[0].Label, DomNodeId = element.NodeId };
        }
        else if (nodes.Count > 0 && element.Id is not null && nodes[0].Label is null)
        {
            nodes[0] = nodes[0] with { Id = firstNodeId, Label = element.Id, DomNodeId = element.NodeId };
        }

        return nodes;
    }

    private static IReadOnlyList<HtmlSceneNode> NormalizeInlinePunctuation(bool parentAllowsInlineContent, IReadOnlyList<HtmlSceneNode> children)
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
                normalized[^1] = previous with { TextContent = previous.TextContent + text };
                continue;
            }

            normalized?.Add(child);
        }

        return normalized ?? children;
    }

    private static IReadOnlyList<HtmlSceneNode> GroupInlineRuns(
        HtmlDomElement parent,
        bool parentAllowsInlineContent,
        IReadOnlyList<HtmlSceneNode> children,
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
                    grouped.Add(children[start] with { Style = HtmlComputedStyle.CreateAlignedInlineTextDefault(children[start].Style) });
                }
                else if (NeedsInlineAlignmentContainer(parent, parentStyle) &&
                         IsInlineRunCandidate(parent, parentAllowsInlineContent, children[start]))
                {
                    grouped ??= CopyPrefix(children, start);
                    grouped.Add(new HtmlSceneNode(
                        idGenerator.Next("inline-run"),
                        SceneNodeKind.View,
                        CreateInlineRunContainerStyle(parent, parentStyle, children, start, 1),
                        [children[start]],
                        null,
                        null,
                        null,
                        null,
                        null));
                }
                else
                {
                    grouped?.Add(children[start]);
                }

                continue;
            }

            grouped ??= CopyPrefix(children, start);
            grouped.Add(new HtmlSceneNode(
                idGenerator.Next("inline-run"),
                SceneNodeKind.View,
                CreateInlineRunContainerStyle(parent, parentStyle, children, start, index - start),
                CopyInlineRunRange(children, start, index - start),
                null,
                null,
                null,
                null,
                null));
        }

        return grouped ?? children;
    }

    private static HtmlComputedStyle CreateInlineRunContainerStyle(
        HtmlDomElement parent,
        HtmlComputedStyle parentStyle,
        IReadOnlyList<HtmlSceneNode> children,
        int start,
        int count)
    {
        if (ContainsInlineControl(children, start, count))
            return HtmlComputedStyle.CreateInlineControlRunDefault(parentStyle);

        return (parentStyle.Display == HtmlDisplay.Block && !IsTableCellElement(parent)) ||
               (IsPhrasingContainer(parent) &&
                parentStyle.Display != HtmlDisplay.Inline &&
                parentStyle.Display != HtmlDisplay.InlineBlock &&
                !IsTableCellElement(parent))
            ? HtmlComputedStyle.CreateInlineFlowDefault(parentStyle)
            : HtmlComputedStyle.CreateInlineRunDefault(parentStyle);
    }

    private static bool ContainsInlineControl(IReadOnlyList<HtmlSceneNode> children, int start, int count)
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
               node.Children.Count > 0 &&
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

    private static bool StartsInlineRun(HtmlDomElement parent, bool parentAllowsInlineContent, IReadOnlyList<HtmlSceneNode> children, int index)
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
           node.Children.Count == 0 &&
           node.Style.IsWidthPercent &&
           node.Style.Width >= 100 &&
           Math.Abs(node.Style.Height) < 0.001f;

    private static bool IsPhrasingContainer(HtmlDomElement parent)
        => parent.LocalName is "p" or "li" or "td" or "th" or "h1" or "h2" or "h3" or "a" or "span" or "strong" or "b" or "em" or "i" or "u" or "small" or "font";

    private static bool HasDirectInlineElement(HtmlDomElement parent)
    {
        if (string.Equals(parent.LocalName, "body", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var child in parent.Children)
        {
            if (child is HtmlDomElement element && IsInlineElement(element))
                return true;
        }

        return false;
    }

    private static bool IsInlineElement(HtmlDomElement element)
        => element.LocalName is "a" or "span" or "strong" or "b" or "em" or "i" or "u" or "small" or "font" or "br" or "button" or "select";

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

        var elementNodeId = idGenerator.Next(element.LocalName);
        var style = ResolveElementStyle(element, styleTree);
        if (style.Display == HtmlDisplay.None)
            return null;

        var nodeKind = ResolveNodeKind(element, style);
        var linkHref = ResolveLinkHref(element, inheritedLinkHref, basePath);
        if (isListItem && style.SuppressListMarker)
            markerText = null;
        if (isListItem && style.Display == HtmlDisplay.Inline)
            markerText = null;
        HtmlComputedStyle? listItemContentStyle = null;
        if (isListItem && markerText is not null)
        {
            listItemContentStyle = style.CreateListItemContentStyle();
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
                element.NodeId);
        }

        var childInheritedStyle = listItemContentStyle ?? style;
        var children = IsInputButtonElement(element)
            ? BuildInputButtonChildren(element, style, idGenerator, linkHref)
            : nodeKind is SceneNodeKind.View or SceneNodeKind.ScrollView
                ? BuildChildren(element, childInheritedStyle, idGenerator, linkHref, basePath, viewportWidth, viewportHeight, styleTree)
                : [];
        if (markerText is not null && children.Count > 0)
        {
            var markerStyle = style.CreateTextStyle();
            markerStyle.ApplyInlineTextDefaults();
            markerStyle.ApplyListMarkerDefaults(markerText);
            var markerNode = new HtmlSceneNode(
                idGenerator.Next("marker"),
                SceneNodeKind.Text,
                markerStyle,
                [],
                markerText,
                null,
                null,
                null,
                null);
            var contentNode = new HtmlSceneNode(
                idGenerator.Next("list-content"),
                SceneNodeKind.View,
                listItemContentStyle ?? style.CreateListItemContentStyle(),
                children,
                null,
                null,
                null,
                null,
                null);
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
            style.ApplyIntrinsicImageSize(intrinsicWidth, intrinsicHeight);
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
            ControlKind: ResolveControlKind(element));
    }

    private HtmlComputedStyle ResolveElementStyle(HtmlDomElement element, HtmlComputedStyleTree styleTree)
    {
        if (styleTree.Styles.TryGetValue(element.NodeId, out var precomputedStyle))
            return precomputedStyle.CloneForFormatting();

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

        foreach (var child in element.Children)
        {
            if (child is not HtmlDomText)
                return false;
        }

        textContent = HtmlTextNormalizer.Normalize(element.TextContent);
        return textContent.Length > 0;
    }

    private static string ResolveTextInputValue(HtmlDomElement element)
    {
        if (string.Equals(element.LocalName, "textarea", StringComparison.OrdinalIgnoreCase))
            return element.TextContent ?? string.Empty;

        if (string.Equals(element.LocalName, "select", StringComparison.OrdinalIgnoreCase))
            return ResolveSelectDisplayText(element);

        return element.GetAttribute("value") ?? string.Empty;
    }

    private static IReadOnlyList<HtmlSceneNode> BuildInputButtonChildren(
        HtmlDomElement element,
        HtmlComputedStyle style,
        HtmlNodeIdGenerator idGenerator,
        string? linkHref)
    {
        var value = element.GetAttribute("value");
        if (string.IsNullOrWhiteSpace(value))
        {
            var type = element.GetAttribute("type");
            value = string.Equals(type, "reset", StringComparison.OrdinalIgnoreCase) ? "Reset" : "Submit";
        }

        var textStyle = style.CreateTextStyle();
        textStyle.ApplyInlineTextDefaults();
        return
        [
            new HtmlSceneNode(
                idGenerator.Next("input-text"),
                SceneNodeKind.Text,
                textStyle,
                [],
                value,
                null,
                null,
                linkHref,
                null,
                element.NodeId)
        ];
    }

    private static string ResolveSelectDisplayText(HtmlDomElement element)
    {
        HtmlDomElement? firstOption = null;
        foreach (var option in EnumerateOptionElements(element))
        {
            firstOption ??= option;
            if (option.Attributes.ContainsKey("selected"))
                return option.InnerText;
        }

        return firstOption?.InnerText ?? string.Empty;
    }

    private static IEnumerable<HtmlDomElement> EnumerateOptionElements(HtmlDomElement element)
    {
        foreach (var child in element.Children)
        {
            if (child is not HtmlDomElement childElement)
                continue;

            if (string.Equals(childElement.LocalName, "option", StringComparison.OrdinalIgnoreCase))
                yield return childElement;

            foreach (var nested in EnumerateOptionElements(childElement))
                yield return nested;
        }
    }

    private HtmlSceneNode? BuildTextNode(HtmlDomText text, HtmlComputedStyle inheritedStyle, HtmlNodeIdGenerator idGenerator, string? inheritedLinkHref, bool inlineText = false)
    {
        var normalized = ApplyTextTransform(HtmlTextNormalizer.Normalize(text.Text, inheritedStyle.WhiteSpace), inheritedStyle.TextTransform);
        if (normalized.Length == 0)
            return null;

        var textStyle = inheritedStyle.CreateTextStyle();
        if (inlineText)
            textStyle.ApplyInlineTextDefaults();

        return new HtmlSceneNode(
            idGenerator.Next("text"),
            SceneNodeKind.Text,
            textStyle,
            [],
            normalized,
            null,
            null,
            inheritedLinkHref,
            null);
    }

    private static IReadOnlyList<HtmlSceneNode> BuildInlineTextNodes(
        HtmlDomText text,
        HtmlComputedStyle inheritedStyle,
        HtmlNodeIdGenerator idGenerator,
        string? inheritedLinkHref)
    {
        var normalized = ApplyTextTransform(HtmlTextNormalizer.Normalize(text.Text, inheritedStyle.WhiteSpace), inheritedStyle.TextTransform);
        if (normalized.Length == 0)
            return [];

        if (ShouldWrapAsUnsegmentedInlineText(normalized))
        {
            return
            [
                new HtmlSceneNode(
                    idGenerator.Next("text"),
                    SceneNodeKind.Text,
                    HtmlComputedStyle.CreateInlineWrappedTextDefault(inheritedStyle.CreateTextStyle()),
                    [],
                    normalized,
                    null,
                    null,
                    inheritedLinkHref,
                    null)
            ];
        }

        if (inheritedStyle.WhiteSpace is HtmlWhiteSpace.Pre or HtmlWhiteSpace.PreWrap or HtmlWhiteSpace.PreLine or HtmlWhiteSpace.NoWrap)
        {
            var textStyle = inheritedStyle.CreateTextStyle();
            textStyle.ApplyInlineTextDefaults(preserveWhiteSpaceWrapping: true);
            return
            [
                new HtmlSceneNode(
                    idGenerator.Next("text"),
                    SceneNodeKind.Text,
                    textStyle,
                    [],
                    normalized,
                    null,
                    null,
                    inheritedLinkHref,
                    null)
            ];
        }

        var nodes = new List<HtmlSceneNode>();
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
                nodes[^1] = previous with { TextContent = previous.TextContent + word };
                continue;
            }

            var wordStyle = inheritedStyle.CreateTextStyle();
            wordStyle.ApplyInlineTextDefaults();
            nodes.Add(new HtmlSceneNode(
                idGenerator.Next("text"),
                SceneNodeKind.Text,
                wordStyle,
                [],
                word,
                null,
                null,
                inheritedLinkHref,
                null));
        }

        return nodes;
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

    private static List<HtmlSceneNode> CopyPrefix(IReadOnlyList<HtmlSceneNode> source, int count)
    {
        var list = new List<HtmlSceneNode>(source.Count);
        for (var index = 0; index < count; index++)
            list.Add(source[index]);
        return list;
    }

    private static HtmlSceneNode[] CopyRange(IReadOnlyList<HtmlSceneNode> source, int start, int count)
    {
        if (count <= 0)
            return [];

        var range = new HtmlSceneNode[count];
        for (var index = 0; index < count; index++)
            range[index] = source[start + index];
        return range;
    }

    private static HtmlSceneNode[] CopyInlineRunRange(IReadOnlyList<HtmlSceneNode> source, int start, int count)
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
                var style = node.Style.CreateTextStyle();
                style.ApplyInlineTextDefaults();
                node = node with { Style = style };
            }
            else if (node.NodeKind == SceneNodeKind.Image)
            {
                node = node with { Style = node.Style.CreateInlineImageStyle() };
            }

            range[index] = node;
        }

        return range;
    }

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
        if (IsInputButtonElement(element))
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
            "input" => SceneControlKind.TextInput,
            _ => SceneControlKind.None
        };

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
    private static string? ResolveLinkHref(HtmlDomElement element, string? inheritedLinkHref, string? basePath)
    {
        if (!string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase))
            return inheritedLinkHref;

        var href = element.GetAttribute("href");
        return string.IsNullOrWhiteSpace(href)
            ? inheritedLinkHref
            : HtmlUrlResolver.Resolve(href, basePath);
    }
}

