using System.Globalization;
using System.Net;
using System.Text;
using Okojo.Runtime;

namespace Enaga.Browser;

internal sealed class BrowserWorkerModuleLoader : IModuleSourceLoader
{
    private const string WorkerUserAgent = "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko) Enaga.Browser/1.0 Safari/537.36";
    private const string WorkerAcceptHeader = "text/javascript, application/javascript, application/ecmascript, */*;q=0.8";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly string documentSource;
    private readonly string? basePath;

    public BrowserWorkerModuleLoader(string documentSource, string? basePath)
    {
        this.documentSource = documentSource;
        this.basePath = basePath;
    }

    public string ResolveSpecifier(string specifier, string? referrer)
    {
        var trimmed = specifier.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.IsFile ? absoluteUri.LocalPath : absoluteUri.ToString();

        if (!string.IsNullOrWhiteSpace(referrer))
            return ResolveAgainst(referrer, trimmed);

        if (!string.IsNullOrWhiteSpace(basePath))
            return ResolveAgainst(basePath, trimmed);

        return ResolveAgainst(documentSource, trimmed);
    }

    public string LoadSource(string resolvedId)
    {
        if (TryResolveFilePath(resolvedId, out var filePath))
            return File.ReadAllText(filePath, Encoding.UTF8);

        using var request = new HttpRequestMessage(HttpMethod.Get, resolvedId);
        request.Headers.TryAddWithoutValidation("User-Agent", WorkerUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", WorkerAcceptHeader);
        request.Headers.TryAddWithoutValidation("Accept-Language", CreateAcceptLanguageHeader());
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "worker");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "same-origin");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

        using var response = HttpClient.Send(request);
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static string ResolveAgainst(string baseValue, string specifier)
    {
        if (Uri.TryCreate(baseValue, UriKind.Absolute, out var baseUri))
        {
            if (!baseUri.IsFile)
                return new Uri(baseUri, specifier).ToString();

            var basePath = Directory.Exists(baseUri.LocalPath)
                ? baseUri.LocalPath
                : Path.GetDirectoryName(baseUri.LocalPath) ?? Environment.CurrentDirectory;
            return Path.GetFullPath(Path.Combine(basePath, Uri.UnescapeDataString(specifier)));
        }

        var fullBase = Path.GetFullPath(baseValue);
        var directory = Directory.Exists(fullBase)
            ? fullBase
            : Path.GetDirectoryName(fullBase) ?? Environment.CurrentDirectory;
        return Path.GetFullPath(Path.Combine(directory, Uri.UnescapeDataString(specifier)));
    }

    private static bool TryResolveFilePath(string resolvedId, out string filePath)
    {
        if (Uri.TryCreate(resolvedId, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
            {
                filePath = uri.LocalPath;
                return true;
            }

            filePath = string.Empty;
            return false;
        }

        filePath = resolvedId;
        return true;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        return new HttpClient(handler);
    }

    private static string CreateAcceptLanguageHeader()
    {
        var culture = CultureInfo.CurrentUICulture;
        var language = string.IsNullOrWhiteSpace(culture.Name) ? "en-US" : culture.Name;
        var neutral = culture.TwoLetterISOLanguageName;
        if (string.IsNullOrWhiteSpace(neutral) || string.Equals(neutral, language, StringComparison.OrdinalIgnoreCase))
            return $"{language},en;q=0.8";

        return $"{language},{neutral};q=0.9,en;q=0.8";
    }
}
