using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Enaga.Html.Loader;

public static partial class HtmlDocumentLoader
{
    private static readonly HtmlParser HtmlParser = new();

    private static async Task LoadLinkedStyleSheetsAsync(
        LoadedTextSource documentSource,
        List<string> styleSheets,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var document = await HtmlParser.ParseDocumentAsync(documentSource.Text, cancellationToken).ConfigureAwait(false);
        foreach (var element in document.QuerySelectorAll("link"))
        {
            if (!IsStyleSheetLink(element) ||
                string.IsNullOrWhiteSpace(element.GetAttribute("href")))
            {
                continue;
            }

            var styleSheet = await ReadTextSourceAsync(element.GetAttribute("href")!, documentSource, httpClient, cancellationToken).ConfigureAwait(false);
            styleSheets.Add(styleSheet.Text);
        }
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

    private static bool TryResolveCookieGateLink(string html, Uri pageUri, out Uri resolvedUri)
    {
        resolvedUri = default!;
        var document = HtmlParser.ParseDocument(html);
        var anchors = document.QuerySelectorAll("a[href]");
        if (anchors.Length != 1)
            return false;

        var href = anchors[0].GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href) ||
            !Uri.TryCreate(pageUri, href.Trim(), out var linkUri) ||
            !IsSameDocumentUri(pageUri, linkUri))
        {
            return false;
        }

        resolvedUri = linkUri;
        return true;
    }

    private static bool IsSameDocumentUri(Uri left, Uri right)
        => string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
           left.Port == right.Port &&
           string.Equals(left.AbsolutePath, right.AbsolutePath, StringComparison.Ordinal) &&
           string.Equals(left.Query, right.Query, StringComparison.Ordinal);
}
