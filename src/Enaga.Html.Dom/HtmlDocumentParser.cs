using AngleSharp.Dom;

namespace Enaga.Html.Dom;

public sealed class HtmlDocumentParser
{
    private readonly AngleSharp.Html.Parser.HtmlParser htmlParser = new();

    public HtmlParsedDomDocument Parse(string? html, string? basePath)
    {
        var source = string.IsNullOrWhiteSpace(html) ? "<body></body>" : html;
        var parsedDocument = htmlParser.ParseDocument(source);
        var body = parsedDocument.Body ?? parsedDocument.DocumentElement;
        var rootElement = body is null
            ? new HtmlDomElement(new HtmlNodeId(1), "body", null, null, new Dictionary<string, string>(), [], string.Empty, string.Empty)
            : ConvertElement(body, new HtmlDomNodeIdGenerator());
        return new HtmlParsedDomDocument(
            rootElement,
            LoadAuthorStyleTexts(parsedDocument, basePath),
            LoadAuthorScripts(parsedDocument),
            basePath);
    }

    private static IReadOnlyList<string> LoadAuthorStyleTexts(AngleSharp.Html.Dom.IHtmlDocument document, string? basePath)
    {
        var styles = new List<string>();
        foreach (var element in document.QuerySelectorAll("link, style"))
        {
            if (string.Equals(element.LocalName, "style", StringComparison.OrdinalIgnoreCase))
            {
                styles.Add(element.TextContent ?? string.Empty);
                continue;
            }

            if (!IsStyleSheetLink(element) ||
                !TryResolveLocalStyleSheetPath(element.GetAttribute("href"), basePath, out var stylePath) ||
                !File.Exists(stylePath))
            {
                continue;
            }

            styles.Add(File.ReadAllText(stylePath));
        }

        return styles;
    }

    private static IReadOnlyList<HtmlDomScript> LoadAuthorScripts(AngleSharp.Html.Dom.IHtmlDocument document)
    {
        var scripts = new List<HtmlDomScript>();
        foreach (var element in document.QuerySelectorAll("script"))
        {
            scripts.Add(new HtmlDomScript(
                element.TextContent ?? string.Empty,
                element.GetAttribute("src"),
                element.GetAttribute("type")));
        }

        return scripts;
    }

    private static bool IsStyleSheetLink(IElement element)
    {
        var rel = element.GetAttribute("rel");
        if (string.IsNullOrWhiteSpace(rel))
            return false;

        var span = rel.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            while (index < span.Length && char.IsWhiteSpace(span[index]))
                index++;

            var start = index;
            while (index < span.Length && !char.IsWhiteSpace(span[index]))
                index++;

            if (start < index && span[start..index].Equals("stylesheet".AsSpan(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryResolveLocalStyleSheetPath(string? href, string? basePath, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(href))
            return false;

        var trimmed = StripUrlSuffix(href.Trim());
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            if (!absolute.IsFile)
                return false;

            path = absolute.LocalPath;
            return true;
        }

        if (string.IsNullOrWhiteSpace(basePath))
            return false;

        if (Uri.TryCreate(basePath, UriKind.Absolute, out var baseUri))
        {
            if (!baseUri.IsFile)
                return false;

            path = Path.GetFullPath(Path.Combine(baseUri.LocalPath, Uri.UnescapeDataString(trimmed)));
            return true;
        }

        path = Path.GetFullPath(Path.Combine(basePath, Uri.UnescapeDataString(trimmed)));
        return true;
    }

    private static string StripUrlSuffix(string value)
    {
        var suffixIndex = value.AsSpan().IndexOfAny('#', '?');
        return suffixIndex >= 0 ? value[..suffixIndex] : value;
    }

    private static HtmlDomElement ConvertElement(IElement element, HtmlDomNodeIdGenerator idGenerator)
    {
        var nodeId = idGenerator.Next();
        var children = new List<HtmlDomNode>();
        foreach (var child in element.ChildNodes)
        {
            switch (child)
            {
                case IElement childElement:
                    children.Add(ConvertElement(childElement, idGenerator));
                    break;
                case IText text:
                    children.Add(new HtmlDomText(text.Data));
                    break;
            }
        }

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in element.Attributes)
            attributes[attribute.Name] = attribute.Value;

        return new HtmlDomElement(
            nodeId,
            element.LocalName,
            string.IsNullOrWhiteSpace(element.Id) ? null : element.Id,
            element.GetAttribute("class"),
            attributes,
            children,
            element.TextContent ?? string.Empty,
            ResolveInnerText(element, children));
    }

    private static string ResolveInnerText(IElement element, IReadOnlyList<HtmlDomNode> children)
    {
        if (IsNonRenderedTextElement(element))
            return string.Empty;

        var parts = new List<string>();
        CollectInnerText(children, parts);
        return string.Concat(parts);
    }

    private static void CollectInnerText(IEnumerable<HtmlDomNode> nodes, List<string> parts)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case HtmlDomText text:
                    parts.Add(text.Text);
                    break;
                case HtmlDomElement element when !IsNonRenderedTextElement(element.LocalName):
                    parts.Add(element.InnerText);
                    break;
            }
        }
    }

    private static bool IsNonRenderedTextElement(IElement element)
        => IsNonRenderedTextElement(element.LocalName);

    private static bool IsNonRenderedTextElement(string localName)
        => string.Equals(localName, "script", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(localName, "style", StringComparison.OrdinalIgnoreCase);

    private sealed class HtmlDomNodeIdGenerator
    {
        private int nextId;

        public HtmlNodeId Next()
            => new(++nextId);
    }
}
