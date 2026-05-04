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

    public IReadOnlyList<HtmlDomNode> ParseFragment(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return [];

        var parsedDocument = htmlParser.ParseDocument("<body>" + html + "</body>");
        var body = parsedDocument.Body ?? parsedDocument.DocumentElement;
        if (body is null)
            return [];

        return ConvertChildNodes(body.ChildNodes, new HtmlDomNodeIdGenerator());
    }

    private static IReadOnlyList<string> LoadAuthorStyleTexts(AngleSharp.Html.Dom.IHtmlDocument document, string? basePath)
    {
        var styles = new List<string>();
        foreach (var element in document.QuerySelectorAll("link, style"))
        {
            if (string.Equals(element.LocalName, "style", StringComparison.OrdinalIgnoreCase))
            {
                styles.Add(GetDirectTextContent(element));
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
                GetDirectTextContent(element),
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
        var children = ConvertChildNodes(element.ChildNodes, idGenerator);

        var attributes = new Dictionary<string, string>(element.Attributes.Length, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < element.Attributes.Length; index++)
        {
            var attribute = element.Attributes[index];
            if (attribute is null)
                continue;

            attributes[attribute.Name] = attribute.Value;
        }

        return new HtmlDomElement(
            nodeId,
            element.LocalName,
            string.IsNullOrWhiteSpace(element.Id) ? null : element.Id,
            element.GetAttribute("class"),
            attributes,
            children);
    }

    private static IReadOnlyList<HtmlDomNode> ConvertChildNodes(INodeList childNodes, HtmlDomNodeIdGenerator idGenerator)
    {
        var children = new List<HtmlDomNode>(childNodes.Length);
        for (var index = 0; index < childNodes.Length; index++)
        {
            var child = childNodes[index];
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

        return children;
    }

    private static bool IsNonRenderedTextElement(string localName)
        => string.Equals(localName, "script", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(localName, "style", StringComparison.OrdinalIgnoreCase);

    private static string GetDirectTextContent(IElement element)
    {
        if (element.ChildNodes.Length == 0)
            return string.Empty;

        string? textContent = null;
        System.Text.StringBuilder? builder = null;
        for (var index = 0; index < element.ChildNodes.Length; index++)
        {
            if (element.ChildNodes[index] is not IText text)
                continue;

            AppendText(text.Data, ref textContent, ref builder);
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

    private sealed class HtmlDomNodeIdGenerator
    {
        private int nextId;

        public HtmlNodeId Next()
            => new(++nextId);
    }
}
