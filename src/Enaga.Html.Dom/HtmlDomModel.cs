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
    string TextContent,
    string InnerText) : HtmlDomNode
{
    public string? GetAttribute(string name)
        => Attributes.TryGetValue(name, out var value) ? value : null;

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

            if (start < current && span[start..current].Equals(className.AsSpan(), StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

public sealed record HtmlDomScript(
    string TextContent,
    string? Source,
    string? Type)
{
    public bool HasSource => !string.IsNullOrWhiteSpace(Source);

    public bool IsClassicJavaScript =>
        string.IsNullOrWhiteSpace(Type) ||
        Type.StartsWith("text/javascript", StringComparison.OrdinalIgnoreCase) ||
        Type.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase);

    public bool IsExecutableInlineJavaScript => !HasSource && IsClassicJavaScript && !string.IsNullOrWhiteSpace(TextContent);
}

public sealed record HtmlParsedDomDocument(
    HtmlDomElement RootElement,
    IReadOnlyList<string> AuthorStyleTexts,
    IReadOnlyList<HtmlDomScript> AuthorScripts,
    string? BasePath)
{
    public HtmlDomDocument ToDomDocument() => new(RootElement, BasePath);

    public IReadOnlyList<string> GetExecutableInlineScriptTexts()
        => AuthorScripts
            .Where(static script => script.IsExecutableInlineJavaScript)
            .Select(static script => script.TextContent)
            .ToArray();
}
