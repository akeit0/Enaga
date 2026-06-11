namespace Enaga.Html.Dom;

public abstract record HtmlDomNode;

public sealed record HtmlDomText(string Text) : HtmlDomNode;

public sealed record HtmlDomElement(
    HtmlNodeId NodeId,
    string LocalName,
    string? Id,
    string? ClassName,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<HtmlDomNode> Children,
    string? InitialTextContent = null,
    string? InitialInnerText = null
) : HtmlDomNode
{
    private string? textContent;
    private string? innerText;

    public string TextContent => textContent ??= InitialTextContent ?? BuildTextContent(Children);

    public string InnerText =>
        innerText ??=
            InitialInnerText
            ?? (IsNonRenderedTextElement(LocalName) ? string.Empty : BuildInnerText(Children));

    public string? GetAttribute(string name) =>
        Attributes.TryGetValue(name, out var value) ? value : null;

    public bool HasClass(string className)
    {
        if (string.IsNullOrWhiteSpace(ClassName))
            return false;

        var span = ClassName.AsSpan();
        var current = 0;
        while (current < span.Length)
        {
            while (current < span.Length && char.IsWhiteSpace(span[current]))
                current += 1;

            var start = current;
            while (current < span.Length && !char.IsWhiteSpace(span[current]))
                current += 1;

            if (
                start < current
                && span[start..current].Equals(className.AsSpan(), StringComparison.Ordinal)
            )
                return true;
        }

        return false;
    }

    private static string BuildTextContent(IReadOnlyList<HtmlDomNode> children)
    {
        if (children.Count == 0)
            return string.Empty;

        string? textContent = null;
        System.Text.StringBuilder? builder = null;
        for (var index = 0; index < children.Count; index++)
        {
            switch (children[index])
            {
                case HtmlDomText text:
                    AppendText(text.Text, ref textContent, ref builder);
                    break;
                case HtmlDomElement childElement:
                    AppendText(childElement.TextContent, ref textContent, ref builder);
                    break;
            }
        }

        return builder?.ToString() ?? textContent ?? string.Empty;
    }

    private static string BuildInnerText(IReadOnlyList<HtmlDomNode> children)
    {
        if (children.Count == 0)
            return string.Empty;

        string? textContent = null;
        System.Text.StringBuilder? builder = null;
        for (var index = 0; index < children.Count; index++)
        {
            switch (children[index])
            {
                case HtmlDomText text:
                    AppendText(text.Text, ref textContent, ref builder);
                    break;
                case HtmlDomElement childElement
                    when !IsNonRenderedTextElement(childElement.LocalName):
                    AppendText(childElement.InnerText, ref textContent, ref builder);
                    break;
            }
        }

        return builder?.ToString() ?? textContent ?? string.Empty;
    }

    private static void AppendText(
        string text,
        ref string? textContent,
        ref System.Text.StringBuilder? builder
    )
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

    private static bool IsNonRenderedTextElement(string localName) =>
        string.Equals(localName, "script", StringComparison.OrdinalIgnoreCase)
        || string.Equals(localName, "style", StringComparison.OrdinalIgnoreCase);
}

public sealed record HtmlDomScript(string TextContent, string? Source, string? Type)
{
    public bool HasSource => !string.IsNullOrWhiteSpace(Source);

    public bool IsClassicJavaScript =>
        string.IsNullOrWhiteSpace(Type)
        || Type.StartsWith("text/javascript", StringComparison.OrdinalIgnoreCase)
        || Type.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase);

    public bool IsExecutableInlineJavaScript =>
        !HasSource && IsClassicJavaScript && !string.IsNullOrWhiteSpace(TextContent);
}

public sealed record HtmlParsedDomDocument(
    HtmlDomElement RootElement,
    IReadOnlyList<string> AuthorStyleTexts,
    IReadOnlyList<HtmlDomScript> AuthorScripts,
    string? BasePath
)
{
    public HtmlDomDocument ToDomDocument() => new(RootElement, BasePath);

    public IReadOnlyList<string> GetExecutableInlineScriptTexts() =>
        AuthorScripts
            .Where(static script => script.IsExecutableInlineJavaScript)
            .Select(static script => script.TextContent)
            .ToArray();
}
