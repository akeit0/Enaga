using System.Collections.Concurrent;
using System.Text;
using Okojo.Runtime;

namespace Enaga.Browser;

internal sealed class BrowserWorkerModuleLoader : IModuleSourceLoader
{
    private const string WorkerAcceptHeader = "text/javascript, application/javascript, application/ecmascript, */*;q=0.8";

    private readonly string documentSource;
    private readonly string? basePath;
    private readonly BrowserNetworkSession networkSession;
    // NOTE: Okojo's module loader calls LoadSource with only the resolved id, so Enaga keeps a
    // best-effort requester map from ResolveSpecifier. This lets worker fetches send a sensible
    // Referer and Sec-Fetch-* shape even though there is no full browser navigation/origin policy yet.
    private readonly ConcurrentDictionary<string, string?> requestContextByResolvedId = new(StringComparer.Ordinal);

    public BrowserWorkerModuleLoader(string documentSource, string? basePath, BrowserNetworkSession networkSession)
    {
        this.documentSource = documentSource;
        this.basePath = basePath;
        this.networkSession = networkSession;
    }

    public string ResolveSpecifier(string specifier, string? referrer)
    {
        var trimmed = specifier.Trim();
        string resolved;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            resolved = absoluteUri.IsFile ? absoluteUri.LocalPath : absoluteUri.ToString();
        else if (!string.IsNullOrWhiteSpace(referrer))
            resolved = ResolveAgainst(referrer, trimmed);
        else if (!string.IsNullOrWhiteSpace(basePath))
            resolved = ResolveAgainst(basePath, trimmed);
        else
            resolved = ResolveAgainst(documentSource, trimmed);

        requestContextByResolvedId[NormalizeResolvedId(resolved)] = referrer ?? GetInitialRequestContext();
        return resolved;
    }

    public string LoadSource(string resolvedId)
    {
        if (TryResolveFilePath(resolvedId, out var filePath))
            return File.ReadAllText(filePath, Encoding.UTF8);

        var targetUri = new Uri(resolvedId);
        var requestContext = requestContextByResolvedId.TryGetValue(NormalizeResolvedId(resolvedId), out var context)
            ? context
            : GetInitialRequestContext();

        using var request = CreateHttpRequest(targetUri, requestContext);

        using var response = networkSession.HttpClient.Send(request);
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private string? GetInitialRequestContext()
        => !string.IsNullOrWhiteSpace(basePath) ? basePath : documentSource;

    private static string NormalizeResolvedId(string resolvedId)
    {
        if (Uri.TryCreate(resolvedId, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.IsFile ? absoluteUri.LocalPath : absoluteUri.ToString();

        return Path.GetFullPath(resolvedId);
    }

    private HttpRequestMessage CreateHttpRequest(Uri targetUri, string? requestContext)
    {
        var referer = TryResolveHttpReferer(requestContext);
        var (fetchMode, fetchSite) = GetFetchContext(targetUri, referer);
        return networkSession.CreateRequest(
            HttpMethod.Get,
            targetUri,
            new BrowserHttpRequestOptions(
                WorkerAcceptHeader,
                referer,
                FetchDestination: "worker",
                FetchMode: fetchMode,
                FetchSite: fetchSite));
    }

    private static Uri? TryResolveHttpReferer(string? requestContext)
    {
        if (!Uri.TryCreate(requestContext, UriKind.Absolute, out var uri))
            return null;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? uri : null;
    }

    private static (string Mode, string Site) GetFetchContext(Uri targetUri, Uri? referer)
    {
        // NOTE: This is intentionally heuristic. Enaga does not yet implement full browser
        // worker/CORS/referrer policy, but many servers behave better if these headers roughly
        // match the requester relationship instead of always claiming same-origin.
        if (referer is null)
            return ("no-cors", "none");

        if (Uri.Compare(targetUri, referer, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
            return ("same-origin", "same-origin");

        return ("cors", "cross-site");
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

}
