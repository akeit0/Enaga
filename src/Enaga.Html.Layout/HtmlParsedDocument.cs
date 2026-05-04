using Enaga.Html.Css;
using Enaga.Html.Dom;

namespace Enaga.Html;

internal sealed record HtmlParsedDocument(HtmlDomElement RootElement, HtmlStyleSheet StyleSheet, string? BasePath)
{
    public bool CanHoverAffectElement(HtmlDomElement element)
        => StyleSheet.CanHoverAffectElement(element);

    public bool HasHoverDependencies => StyleSheet.HasHoverDependencies;

    public bool TryResolvePaintOnlyHoveredTextColor(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        int viewportWidth,
        int viewportHeight,
        out string? color)
        => StyleSheet.TryResolvePaintOnlyHoveredTextColor(
            element,
            ancestors,
            ancestorHoverStates,
            viewportWidth,
            viewportHeight,
            out color);

    public bool TryResolvePaintOnlyHoveredBackgroundColor(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        bool isHovered,
        int viewportWidth,
        int viewportHeight,
        out bool matched,
        out string? color)
        => StyleSheet.TryResolvePaintOnlyHoveredBackgroundColor(
            element,
            ancestors,
            ancestorHoverStates,
            isHovered,
            viewportWidth,
            viewportHeight,
            out matched,
            out color);
}

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
