using System.Net;
using System.Net.Http.Headers;
using System.Globalization;

namespace Enaga.Html.Loader;

public static partial class HtmlDocumentLoader
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private const int MaxCookieGateRetryCount = 1;
    private const string DefaultAcceptHeader = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    private static async Task<LoadedTextSource> ReadTextSourceAsync(
        string source,
        LoadedTextSource? relativeTo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Document source must not be empty.", nameof(source));

        if (TryResolveHttpUri(source, relativeTo?.BaseUri, out var httpUri))
            return await ReadHttpTextSourceAsync(httpUri, cancellationToken).ConfigureAwait(false);

        var path = ResolveLocalPath(source, relativeTo);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var text = DecodeText(bytes, null);
        var fullPath = Path.GetFullPath(path);
        return new LoadedTextSource(
            text,
            Path.GetDirectoryName(fullPath),
            new Uri(fullPath));
    }

    private static async Task<LoadedTextSource> ReadHttpTextSourceAsync(Uri uri, CancellationToken cancellationToken)
        => await ReadHttpTextSourceAsync(uri, referer: null, cookieGateRetryCount: 0, cancellationToken).ConfigureAwait(false);

    private static async Task<LoadedTextSource> ReadHttpTextSourceAsync(
        Uri uri,
        Uri? referer,
        int cookieGateRetryCount,
        CancellationToken cancellationToken)
    {
        using var request = CreateHttpRequest(uri, referer);
        using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        var declaredEncoding = TryGetCharset(response.Content.Headers.ContentType) ?? TryGetDeclaredEncoding(DecodeWithBomOrUtf8(bytes));
        var text = DecodeText(bytes, declaredEncoding);

        if (cookieGateRetryCount < MaxCookieGateRetryCount &&
            HasSetCookieHeader(response) &&
            TryResolveCookieGateLink(text, finalUri, out var cookieGateUri))
        {
            return await ReadHttpTextSourceAsync(cookieGateUri, finalUri, cookieGateRetryCount + 1, cancellationToken).ConfigureAwait(false);
        }

        var baseUri = GetBaseUri(finalUri);
        return new LoadedTextSource(
            text,
            baseUri.ToString(),
            baseUri);
    }

    public static HttpClient CreateHttpClient(HtmlDocumentHttpClientOptions? options = null)
    {
        options ??= HtmlDocumentHttpClientOptions.Default;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd(options.Accept);
        if (!string.IsNullOrWhiteSpace(options.AcceptLanguage))
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(options.AcceptLanguage);
        return client;
    }

    public static HtmlDocumentHttpClientOptions CreateDefaultHttpClientOptions()
        => new(
            "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko) Enaga.Html.Loader/1.0 Safari/537.36",
            DefaultAcceptHeader,
            CreateDefaultAcceptLanguage());

    private static string CreateDefaultAcceptLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        var cultureName = string.IsNullOrWhiteSpace(culture.Name) ? "en-US" : culture.Name;
        var parentName = string.IsNullOrWhiteSpace(culture.Parent.Name) ? null : culture.Parent.Name;

        if (string.Equals(cultureName, "en-US", StringComparison.OrdinalIgnoreCase))
            return "en-US,en;q=0.8";

        if (string.Equals(parentName, "en", StringComparison.OrdinalIgnoreCase))
            return $"{cultureName},en;q=0.8";

        return $"{cultureName},en-US;q=0.9,en;q=0.8";
    }

    private static HttpRequestMessage CreateHttpRequest(Uri uri, Uri? referer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (referer is not null)
            request.Headers.Referrer = referer;
        return request;
    }

    private static bool HasSetCookieHeader(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out _);

    private static string ResolveLocalPath(string source, LoadedTextSource? relativeTo)
    {
        if (TryCreateAbsoluteUri(source, out var uri))
        {
            if (!uri.IsFile)
                throw new NotSupportedException($"Unsupported URI scheme: {uri.Scheme}");

            return uri.LocalPath;
        }

        if (Path.IsPathFullyQualified(source) || relativeTo?.BasePath is null)
            return Path.GetFullPath(source);

        if (TryCreateAbsoluteUri(relativeTo.BasePath, out var baseUri) && !baseUri.IsFile)
            throw new NotSupportedException($"Relative local path '{source}' cannot be resolved against remote document '{relativeTo.BasePath}'.");

        return Path.GetFullPath(Path.Combine(relativeTo.BasePath, source));
    }

    private static bool TryResolveHttpUri(string? source, Uri? baseUri, out Uri uri)
    {
        uri = default!;
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var stripped = source.Trim();
        if (TryCreateAbsoluteUri(stripped, out var absolute))
        {
            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
                return false;

            uri = absolute;
            return true;
        }

        if (baseUri is null ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
            !Uri.TryCreate(baseUri, stripped, out var resolvedUri))
        {
            return false;
        }

        uri = resolvedUri;
        return true;
    }

    private static bool TryCreateAbsoluteUri(string source, out Uri uri)
        => Uri.TryCreate(source, UriKind.Absolute, out uri!);

    private static Uri GetBaseUri(Uri uri)
        => uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri, ".");

    private static string? TryGetCharset(MediaTypeHeaderValue? contentType)
    {
        var charset = contentType?.CharSet;
        return string.IsNullOrWhiteSpace(charset) ? null : charset;
    }

    private sealed record LoadedTextSource(string Text, string? BasePath, Uri? BaseUri);
}

public sealed record HtmlDocumentHttpClientOptions(
    string UserAgent,
    string Accept,
    string? AcceptLanguage)
{
    public static HtmlDocumentHttpClientOptions Default => HtmlDocumentLoader.CreateDefaultHttpClientOptions();
}
