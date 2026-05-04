using System.Text;

namespace Enaga.Html.Dom;

public sealed class HtmlDomDocument
{
    private readonly Dictionary<string, HtmlDomElement> elementsById = new(StringComparer.Ordinal);
    private readonly Dictionary<HtmlNodeId, HtmlDomElement> elementsByNodeId = [];
    private readonly Dictionary<HtmlNodeId, HtmlNodeId> parentNodeIds = [];
    private int nextGeneratedNodeId;

    public HtmlDomDocument(HtmlDomElement rootElement, string? basePath = null)
    {
        RootElement = rootElement;
        BasePath = basePath;
        IndexElement(rootElement, default);
    }

    public HtmlDomElement RootElement { get; private set; }

    public string? BasePath { get; }

    public HtmlDomElement? Body => string.Equals(RootElement.LocalName, "body", StringComparison.OrdinalIgnoreCase)
        ? RootElement
        : QuerySelector("body");

    public HtmlDomElement? GetElementById(string id)
        => elementsById.TryGetValue(id, out var element) ? element : null;

    public HtmlDomElement? GetElementByNodeId(HtmlNodeId nodeId)
        => elementsByNodeId.TryGetValue(nodeId, out var element) ? element : null;

    public HtmlDomElement CreateElement(string localName)
    {
        var normalizedLocalName = string.IsNullOrWhiteSpace(localName)
            ? "div"
            : localName.Trim().ToLowerInvariant();
        var element = new HtmlDomElement(
            new HtmlNodeId(Interlocked.Increment(ref nextGeneratedNodeId)),
            normalizedLocalName,
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [],
            string.Empty,
            string.Empty);
        elementsByNodeId[element.NodeId] = element;
        return element;
    }

    public HtmlDomElement? SetTextContent(HtmlNodeId nodeId, string? text)
    {
        if (!elementsByNodeId.TryGetValue(nodeId, out var element))
            return null;

        var value = text ?? string.Empty;
        var nextElement = element with
        {
            Children = value.Length == 0 ? [] : [new HtmlDomText(value)],
            InitialTextContent = value,
            InitialInnerText = IsNonRenderedTextElement(element.LocalName) ? string.Empty : value
        };
        ReplaceElement(nodeId, nextElement);
        return elementsByNodeId[nodeId];
    }

    public HtmlDomElement? SetAttribute(HtmlNodeId nodeId, string name, string? value)
    {
        if (!elementsByNodeId.TryGetValue(nodeId, out var element) ||
            string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalizedName = name.Trim();
        var attributes = new Dictionary<string, string>(element.Attributes, StringComparer.OrdinalIgnoreCase)
        {
            [normalizedName] = value ?? string.Empty
        };
        var nextElement = element with
        {
            Attributes = attributes,
            Id = string.Equals(normalizedName, "id", StringComparison.OrdinalIgnoreCase) ? value : element.Id,
            ClassName = string.Equals(normalizedName, "class", StringComparison.OrdinalIgnoreCase) ? value : element.ClassName
        };
        ReplaceElement(nodeId, nextElement);
        return elementsByNodeId[nodeId];
    }

    public HtmlDomElement? RemoveAttribute(HtmlNodeId nodeId, string name)
    {
        if (!elementsByNodeId.TryGetValue(nodeId, out var element) ||
            string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalizedName = name.Trim();
        var attributes = new Dictionary<string, string>(element.Attributes, StringComparer.OrdinalIgnoreCase);
        attributes.Remove(normalizedName);
        var nextElement = element with
        {
            Attributes = attributes,
            Id = string.Equals(normalizedName, "id", StringComparison.OrdinalIgnoreCase) ? null : element.Id,
            ClassName = string.Equals(normalizedName, "class", StringComparison.OrdinalIgnoreCase) ? null : element.ClassName
        };
        ReplaceElement(nodeId, nextElement);
        return elementsByNodeId[nodeId];
    }

    public HtmlDomElement? AppendChild(HtmlNodeId parentNodeId, HtmlNodeId childNodeId)
    {
        if (!elementsByNodeId.TryGetValue(parentNodeId, out var parent) ||
            !elementsByNodeId.TryGetValue(childNodeId, out var child))
        {
            return null;
        }

        var nextParent = RecalculateElement(parent with { Children = [.. parent.Children, child] });
        ReplaceElement(parentNodeId, nextParent);
        return elementsByNodeId[parentNodeId];
    }

    public HtmlNodeId GetParentNodeId(HtmlNodeId nodeId)
        => parentNodeIds.TryGetValue(nodeId, out var parentNodeId) ? parentNodeId : default;

    public HtmlDomElement? QuerySelector(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return null;

        var trimmed = selector.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
            return GetElementById(trimmed[1..]);
        if (trimmed.StartsWith(".", StringComparison.Ordinal))
            return FindFirst(RootElement, element => element.HasClass(trimmed[1..]));

        return FindFirst(RootElement, element => string.Equals(element.LocalName, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<HtmlDomElement> GetElementsByTagName(string localName)
    {
        if (string.IsNullOrWhiteSpace(localName))
            return [];

        var elements = new List<HtmlDomElement>();
        CollectElements(RootElement, element =>
        {
            if (string.Equals(element.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                elements.Add(element);
        });
        return elements;
    }

    public IEnumerable<HtmlDomElement> EnumerateSelfAndAncestors(HtmlNodeId nodeId)
    {
        var current = nodeId;
        while (current.IsValid && elementsByNodeId.TryGetValue(current, out var element))
        {
            yield return element;
            current = GetParentNodeId(current);
        }
    }

    public string ToHtml()
    {
        var builder = new StringBuilder();
        AppendElement(builder, RootElement);
        return builder.ToString();
    }

    private void IndexElement(HtmlDomElement element, HtmlNodeId parentNodeId)
    {
        elementsByNodeId[element.NodeId] = element;
        nextGeneratedNodeId = Math.Max(nextGeneratedNodeId, element.NodeId.Value);
        if (parentNodeId.IsValid)
            parentNodeIds[element.NodeId] = parentNodeId;
        if (!string.IsNullOrWhiteSpace(element.Id))
            elementsById[element.Id] = element;

        foreach (var child in element.Children)
            if (child is HtmlDomElement childElement)
                IndexElement(childElement, element.NodeId);
    }

    private void ReplaceElement(HtmlNodeId nodeId, HtmlDomElement nextElement)
    {
        if (!TryReplaceElement(RootElement, nodeId, nextElement, out var nextRoot))
        {
            elementsByNodeId[nodeId] = nextElement;
            if (!string.IsNullOrWhiteSpace(nextElement.Id))
                elementsById[nextElement.Id] = nextElement;
            nextGeneratedNodeId = Math.Max(nextGeneratedNodeId, nextElement.NodeId.Value);
            return;
        }

        RootElement = nextRoot;
        RebuildIndexes();
    }

    private static bool TryReplaceElement(HtmlDomElement current, HtmlNodeId nodeId, HtmlDomElement replacement, out HtmlDomElement next)
    {
        if (current.NodeId == nodeId)
        {
            next = replacement;
            return true;
        }

        var changed = false;
        var children = new HtmlDomNode[current.Children.Count];
        for (var index = 0; index < current.Children.Count; index++)
        {
            if (current.Children[index] is HtmlDomElement childElement &&
                TryReplaceElement(childElement, nodeId, replacement, out var nextChild))
            {
                children[index] = nextChild;
                changed = true;
            }
            else
            {
                children[index] = current.Children[index];
            }
        }

        next = changed ? RecalculateElement(current with { Children = children }) : current;
        return changed;
    }

    private void RebuildIndexes()
    {
        elementsById.Clear();
        elementsByNodeId.Clear();
        parentNodeIds.Clear();
        nextGeneratedNodeId = 0;
        IndexElement(RootElement, default);
    }

    private static HtmlDomElement RecalculateElement(HtmlDomElement element)
        => element with
        {
            InitialTextContent = BuildTextContent(element.Children),
            InitialInnerText = IsNonRenderedTextElement(element.LocalName)
                ? string.Empty
                : BuildInnerText(element.Children)
        };

    private static string BuildTextContent(IReadOnlyList<HtmlDomNode> children)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < children.Count; index++)
        {
            switch (children[index])
            {
                case HtmlDomText text:
                    builder.Append(text.Text);
                    break;
                case HtmlDomElement childElement:
                    builder.Append(childElement.TextContent);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string BuildInnerText(IReadOnlyList<HtmlDomNode> children)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < children.Count; index++)
        {
            switch (children[index])
            {
                case HtmlDomText text:
                    builder.Append(text.Text);
                    break;
                case HtmlDomElement childElement when !IsNonRenderedTextElement(childElement.LocalName):
                    builder.Append(childElement.InnerText);
                    break;
            }
        }

        return builder.ToString();
    }

    private static HtmlDomElement? FindFirst(HtmlDomElement element, Func<HtmlDomElement, bool> predicate)
    {
        if (predicate(element))
            return element;

        foreach (var child in element.Children)
            if (child is HtmlDomElement childElement && FindFirst(childElement, predicate) is { } found)
                return found;

        return null;
    }

    private static void CollectElements(HtmlDomElement element, Action<HtmlDomElement> add)
    {
        add(element);
        foreach (var child in element.Children)
            if (child is HtmlDomElement childElement)
                CollectElements(childElement, add);
    }

    private static void AppendElement(StringBuilder builder, HtmlDomElement element)
    {
        builder.Append('<').Append(element.LocalName);
        foreach (var pair in element.Attributes)
        {
            builder.Append(' ').Append(pair.Key);
            if (!string.IsNullOrEmpty(pair.Value))
            {
                builder.Append("=\"");
                AppendEscapedAttribute(builder, pair.Value);
                builder.Append('"');
            }
        }

        builder.Append('>');
        AppendChildren(builder, element.Children);
        builder.Append("</").Append(element.LocalName).Append('>');
    }

    private static void AppendChildren(StringBuilder builder, IReadOnlyList<HtmlDomNode> children)
    {
        for (var index = 0; index < children.Count; index++)
        {
            switch (children[index])
            {
                case HtmlDomText text:
                    AppendEscapedText(builder, text.Text);
                    break;
                case HtmlDomElement element:
                    AppendElement(builder, element);
                    break;
            }
        }
    }

    private static void AppendEscapedText(StringBuilder builder, string value)
    {
        foreach (var ch in value)
        {
            _ = ch switch
            {
                '&' => builder.Append("&amp;"),
                '<' => builder.Append("&lt;"),
                '>' => builder.Append("&gt;"),
                _ => builder.Append(ch)
            };
        }
    }

    private static void AppendEscapedAttribute(StringBuilder builder, string value)
    {
        foreach (var ch in value)
        {
            _ = ch switch
            {
                '&' => builder.Append("&amp;"),
                '<' => builder.Append("&lt;"),
                '>' => builder.Append("&gt;"),
                '"' => builder.Append("&quot;"),
                _ => builder.Append(ch)
            };
        }
    }

    private static bool IsNonRenderedTextElement(string localName)
        => string.Equals(localName, "script", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(localName, "style", StringComparison.OrdinalIgnoreCase);
}
