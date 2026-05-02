using Enaga.Html.Css;
using Enaga.Html.Dom;

namespace Enaga.Html;

internal sealed record HtmlParsedDocument(HtmlDomElement RootElement, HtmlStyleSheet StyleSheet, string? BasePath);

internal sealed class HtmlDocumentParser
{
    private readonly Enaga.Html.Dom.HtmlDocumentParser domParser = new();

    public HtmlParsedDocument Parse(HtmlDocument document)
    {
        var parsed = domParser.Parse(document.Html, document.BasePath);
        return new HtmlParsedDocument(
            parsed.RootElement,
            HtmlStyleSheet.Parse(parsed.AuthorStyleTexts, document.StyleSheet),
            parsed.BasePath);
    }
}
