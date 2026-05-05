using Enaga.Html;

namespace Enaga.Html.Loader;

public static partial class HtmlDocumentLoader
{
    public static HtmlDocument Load(string documentSource, string? styleSheetSource = null)
        => LoadAsync(documentSource, styleSheetSource).GetAwaiter().GetResult();

    public static HtmlDocument Load(
        string documentSource,
        string? styleSheetSource,
        HtmlDocumentHttpClientOptions? httpClientOptions)
        => LoadAsync(documentSource, styleSheetSource, httpClientOptions).GetAwaiter().GetResult();

    public static async Task<HtmlDocument> LoadAsync(
        string documentSource,
        string? styleSheetSource = null,
        CancellationToken cancellationToken = default)
        => await LoadAsync(documentSource, styleSheetSource, httpClientOptions: null, cancellationToken).ConfigureAwait(false);

    public static async Task<HtmlDocument> LoadAsync(
        string documentSource,
        string? styleSheetSource,
        HtmlDocumentHttpClientOptions? httpClientOptions,
        CancellationToken cancellationToken = default)
    {
        using var httpClientLease = httpClientOptions is null ? null : CreateHttpClient(httpClientOptions);
        var httpClient = httpClientLease ?? SharedHttpClient;
        var document = await ReadTextSourceAsync(documentSource, null, httpClient, cancellationToken).ConfigureAwait(false);
        var styleSheets = new List<string>();
        await LoadLinkedStyleSheetsAsync(document, styleSheets, httpClient, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(styleSheetSource))
        {
            var explicitStyleSheet = await ReadTextSourceAsync(styleSheetSource, document, httpClient, cancellationToken).ConfigureAwait(false);
            styleSheets.Add(explicitStyleSheet.Text);
        }

        return new HtmlDocument(
            document.Text,
            styleSheets.Count == 0 ? null : string.Join(Environment.NewLine, styleSheets),
            document.BasePath);
    }

    public static bool TryLoad(string documentSource, string? styleSheetSource, out HtmlDocument document)
        => TryLoad(documentSource, styleSheetSource, httpClientOptions: null, out document);

    public static bool TryLoad(
        string documentSource,
        string? styleSheetSource,
        HtmlDocumentHttpClientOptions? httpClientOptions,
        out HtmlDocument document)
    {
        document = default!;
        try
        {
            document = Load(documentSource, styleSheetSource, httpClientOptions);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    public static bool IsLocalFileSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return !TryCreateAbsoluteUri(source, out var uri) || uri.IsFile;
    }

    public static string GetLocalPath(string source)
    {
        if (TryCreateAbsoluteUri(source, out var uri) && uri.IsFile)
            return uri.LocalPath;

        return Path.GetFullPath(source);
    }
}
