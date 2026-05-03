using Enaga.Html.Css;
using Enaga.Html.Dom;
using Enaga.Layout;

namespace Enaga.Html;

internal sealed record HtmlComputedStyleTree(
    HtmlNodeId RootNodeId,
    IReadOnlyDictionary<HtmlNodeId, HtmlComputedStyle> Styles,
    uint Version);

internal sealed class HtmlStyleTraversal
{
    private readonly HtmlStyleResolver resolver;

    public HtmlStyleTraversal(HtmlOptions options, LayoutEngineConfig layoutConfig)
    {
        resolver = new HtmlStyleResolver(options, layoutConfig);
    }

    public HtmlComputedStyleTree Resolve(
        HtmlParsedDocument document,
        int viewportWidth,
        int viewportHeight,
        IReadOnlySet<HtmlNodeId>? hoveredNodeIds = null,
        HtmlNodeId? activeNodeId = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var styles = new Dictionary<HtmlNodeId, HtmlComputedStyle>(CountElements(document.RootElement));
        HashSet<HtmlNodeId>? hoveredSubtreeNodeIds = null;
        if (hoveredNodeIds is { Count: > 0 })
        {
            hoveredSubtreeNodeIds = new HashSet<HtmlNodeId>();
            MarkHoveredSubtreeNodes(document.RootElement, hoveredNodeIds, hoveredSubtreeNodeIds);
        }

        var ancestors = new List<HtmlDomElement>();
        var ancestorHoverStates = new List<bool>();
        var versionHash = new HashCode();
        ResolveElement(
            document.RootElement,
            inherited: null,
            document.StyleSheet,
            document.BasePath,
            Math.Max(1, viewportWidth),
            Math.Max(1, viewportHeight),
            hoveredSubtreeNodeIds,
            activeNodeId,
            ancestors,
            ancestorHoverStates,
            styles,
            ref versionHash);

        return new HtmlComputedStyleTree(document.RootElement.NodeId, styles, unchecked((uint)versionHash.ToHashCode()));
    }

    private void ResolveElement(
        HtmlDomElement element,
        HtmlComputedStyle? inherited,
        HtmlStyleSheet styleSheet,
        string? basePath,
        int viewportWidth,
        int viewportHeight,
        HashSet<HtmlNodeId>? hoveredSubtreeNodeIds,
        HtmlNodeId? activeNodeId,
        List<HtmlDomElement> ancestors,
        List<bool> ancestorHoverStates,
        Dictionary<HtmlNodeId, HtmlComputedStyle> styles,
        ref HashCode versionHash)
    {
        var elementHovered = hoveredSubtreeNodeIds?.Contains(element.NodeId) == true;
        var style = resolver.Resolve(
            element,
            inherited,
            ancestors,
            ancestorHoverStates,
            styleSheet,
            elementHovered,
            activeNodeId == element.NodeId,
            viewportWidth,
            viewportHeight,
            basePath);
        if (string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(element.GetAttribute("href")))
        {
            style.ApplyAnchorDefaults();
        }

        if (IsInlineStyleElement(element))
            style.ApplyInlineElementDefaults(element.LocalName);
        styles[element.NodeId] = style;
        AddStyleVersionHash(ref versionHash, element.NodeId, style);

        ancestors.Add(element);
        ancestorHoverStates.Add(elementHovered);
        try
        {
            foreach (var child in element.Children)
            {
                if (child is HtmlDomElement childElement)
                {
                    ResolveElement(
                        childElement,
                        style,
                        styleSheet,
                        basePath,
                        viewportWidth,
                        viewportHeight,
                        hoveredSubtreeNodeIds,
                        activeNodeId,
                        ancestors,
                        ancestorHoverStates,
                        styles,
                        ref versionHash);
                }
            }
        }
        finally
        {
            ancestors.RemoveAt(ancestors.Count - 1);
            ancestorHoverStates.RemoveAt(ancestorHoverStates.Count - 1);
        }
    }

    private static int CountElements(HtmlDomElement root)
    {
        var count = 1;
        foreach (var child in root.Children)
        {
            if (child is HtmlDomElement childElement)
                count += CountElements(childElement);
        }

        return count;
    }

    private static void AddStyleVersionHash(ref HashCode hash, HtmlNodeId nodeId, HtmlComputedStyle style)
    {
        hash.Add(nodeId.Value);
        hash.Add(style.Display);
        hash.Add(style.Width);
        hash.Add(style.Height);
        hash.Add(style.BackgroundColor);
        hash.Add(style.BorderColor);
        hash.Add(style.BorderLeftColor);
        hash.Add(style.BorderTopColor);
        hash.Add(style.BorderRightColor);
        hash.Add(style.BorderBottomColor);
        hash.Add(style.BorderWidth);
        hash.Add(style.BorderLeftWidth);
        hash.Add(style.BorderTopWidth);
        hash.Add(style.BorderRightWidth);
        hash.Add(style.BorderBottomWidth);
        hash.Add(style.BorderStyle);
        hash.Add(style.BorderLeftStyle);
        hash.Add(style.BorderTopStyle);
        hash.Add(style.BorderRightStyle);
        hash.Add(style.BorderBottomStyle);
        hash.Add(style.Color);
        hash.Add(style.FontSize);
        hash.Add(style.FontWeight);
        hash.Add(style.Italic);
        hash.Add(style.Underline);
        hash.Add(style.TextAlign);
        hash.Add(style.TextTransform);
        hash.Add(style.TextOverflowEllipsis);
        hash.Add(style.BackgroundImageSource);
        hash.Add(style.BackgroundImageFit);
        hash.Add(style.Containment);
    }

    private static bool IsInlineStyleElement(HtmlDomElement element)
        => element.LocalName is "a" or "span" or "strong" or "b" or "em" or "i" or "u" or "small" or "font" or "br";

    private static bool MarkHoveredSubtreeNodes(HtmlDomElement element, IReadOnlySet<HtmlNodeId> hoveredNodeIds, HashSet<HtmlNodeId> hoveredSubtreeNodeIds)
    {
        var containsHoveredNode = hoveredNodeIds.Contains(element.NodeId);

        foreach (var child in element.Children)
        {
            if (child is not HtmlDomElement childElement)
                continue;

            containsHoveredNode |= MarkHoveredSubtreeNodes(childElement, hoveredNodeIds, hoveredSubtreeNodeIds);
        }

        if (containsHoveredNode)
            hoveredSubtreeNodeIds.Add(element.NodeId);

        return containsHoveredNode;
    }
}
