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
    private const int MaxResolvedStyleCacheEntries = 262_144;
    private readonly HtmlStyleResolver resolver;
    private readonly HtmlComputedStyleInterner styleInterner = new();
    private readonly Dictionary<ResolvedStyleCacheKey, HtmlComputedStyle> resolvedStyleCache = new();
    private HtmlParsedDocument? cachedDocument;
    private bool cachedResolveHadPseudoState;

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

        styleInterner.Clear();
        var hasPseudoState = hoveredNodeIds is { Count: > 0 } || activeNodeId is not null;
        if (!ReferenceEquals(cachedDocument, document) ||
            hasPseudoState ||
            cachedResolveHadPseudoState ||
            resolvedStyleCache.Count > MaxResolvedStyleCacheEntries)
        {
            resolvedStyleCache.Clear();
            cachedDocument = document;
        }
        cachedResolveHadPseudoState = hasPseudoState;

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
        var isActive = activeNodeId == element.NodeId;
        var styleCacheKey = CreateResolvedStyleCacheKey(
            element,
            inherited,
            ancestors,
            ancestorHoverStates,
            elementHovered,
            isActive,
            viewportWidth,
            viewportHeight);
        if (!resolvedStyleCache.TryGetValue(styleCacheKey, out var style))
        {
            style = resolver.Resolve(
                element,
                inherited,
                ancestors,
                ancestorHoverStates,
                styleSheet,
                elementHovered,
                isActive,
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
            style = styleInterner.Intern(style);
            resolvedStyleCache[styleCacheKey] = style;
        }
        styles[element.NodeId] = style;
        AddStyleVersionHash(ref versionHash, element.NodeId, style);

        ancestors.Add(element);
        ancestorHoverStates.Add(elementHovered);
        try
        {
            for (var index = 0; index < element.Children.Count; index++)
            {
                var child = element.Children[index];
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
        for (var index = 0; index < root.Children.Count; index++)
        {
            var child = root.Children[index];
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

    private static ResolvedStyleCacheKey CreateResolvedStyleCacheKey(
        HtmlDomElement element,
        HtmlComputedStyle? inherited,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        bool isHovered,
        bool isActive,
        int viewportWidth,
        int viewportHeight)
        => new(
            element.LocalName,
            element.Id,
            element.ClassName,
            inherited,
            HashAttributes(element),
            HashAncestors(ancestors, ancestorHoverStates),
            IsFirstElementChild(element, ancestors.Count > 0 ? ancestors[^1] : null),
            isHovered,
            isActive,
            viewportWidth,
            viewportHeight);

    private static int HashAncestors(
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates)
    {
        var hash = new HashCode();
        hash.Add(ancestors.Count);
        for (var index = 0; index < ancestors.Count; index++)
        {
            var ancestor = ancestors[index];
            hash.Add(ancestor.LocalName);
            hash.Add(ancestor.Id);
            hash.Add(ancestor.ClassName);
            hash.Add(HashAttributes(ancestor));
            hash.Add(index < ancestorHoverStates.Count && ancestorHoverStates[index]);
            hash.Add(IsFirstElementChild(ancestor, index > 0 ? ancestors[index - 1] : null));
        }

        return hash.ToHashCode();
    }

    private static int HashAttributes(HtmlDomElement element)
    {
        var hash = new HashCode();
        hash.Add(element.Attributes.Count);
        if (element.Attributes is Dictionary<string, string> dictionary)
        {
            foreach (var (name, value) in dictionary)
            {
                hash.Add(name, StringComparer.OrdinalIgnoreCase);
                hash.Add(value);
            }

            return hash.ToHashCode();
        }

        foreach (var pair in element.Attributes)
        {
            hash.Add(pair.Key, StringComparer.OrdinalIgnoreCase);
            hash.Add(pair.Value);
        }

        return hash.ToHashCode();
    }

    private static bool IsFirstElementChild(HtmlDomElement element, HtmlDomElement? parent)
    {
        if (parent is null)
            return false;

        for (var index = 0; index < parent.Children.Count; index++)
        {
            if (parent.Children[index] is not HtmlDomElement childElement)
                continue;

            return ReferenceEquals(childElement, element) || childElement == element;
        }

        return false;
    }

    private static bool MarkHoveredSubtreeNodes(HtmlDomElement element, IReadOnlySet<HtmlNodeId> hoveredNodeIds, HashSet<HtmlNodeId> hoveredSubtreeNodeIds)
    {
        var containsHoveredNode = hoveredNodeIds.Contains(element.NodeId);

        for (var index = 0; index < element.Children.Count; index++)
        {
            var child = element.Children[index];
            if (child is not HtmlDomElement childElement)
                continue;

            containsHoveredNode |= MarkHoveredSubtreeNodes(childElement, hoveredNodeIds, hoveredSubtreeNodeIds);
        }

        if (containsHoveredNode)
            hoveredSubtreeNodeIds.Add(element.NodeId);

        return containsHoveredNode;
    }

    private sealed class HtmlComputedStyleInterner
    {
        private readonly Dictionary<int, List<HtmlComputedStyle>> stylesByHash = new();

        public void Clear() => stylesByHash.Clear();

        public HtmlComputedStyle Intern(HtmlComputedStyle style)
        {
            var hash = style.GetStyleSharingHash();
            if (!stylesByHash.TryGetValue(hash, out var candidates))
            {
                candidates = new List<HtmlComputedStyle>(1);
                stylesByHash[hash] = candidates;
                candidates.Add(style);
                return style;
            }

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (HtmlComputedStyle.HasSameStyleSharingIdentity(candidate, style))
                    return candidate;
            }

            candidates.Add(style);
            return style;
        }
    }

    private readonly record struct ResolvedStyleCacheKey(
        string LocalName,
        string? Id,
        string? ClassName,
        HtmlComputedStyle? Inherited,
        int AttributeHash,
        int AncestorHash,
        bool IsFirstElementChild,
        bool IsHovered,
        bool IsActive,
        int ViewportWidth,
        int ViewportHeight);
}
