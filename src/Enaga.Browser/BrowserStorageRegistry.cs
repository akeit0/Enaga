namespace Enaga.Browser;

internal static class BrowserStorageRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, BrowserStorageArea> LocalStorageByOrigin = new(StringComparer.Ordinal);

    public static BrowserStorageArea GetLocalStorageArea(string documentSource, string? basePath)
    {
        var origin = ResolveStorageOrigin(documentSource, basePath);
        lock (Gate)
        {
            if (!LocalStorageByOrigin.TryGetValue(origin, out var area))
            {
                area = new BrowserStorageArea();
                LocalStorageByOrigin[origin] = area;
            }

            return area;
        }
    }

    private static string ResolveStorageOrigin(string documentSource, string? basePath)
    {
        if (Uri.TryCreate(documentSource, UriKind.Absolute, out var documentUri))
            return ResolveStorageOrigin(documentUri, documentSource);

        if (Uri.TryCreate(basePath, UriKind.Absolute, out var baseUri))
            return ResolveStorageOrigin(baseUri, basePath!);

        var fullPath = Path.GetFullPath(documentSource);
        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        return "file:" + Path.GetFullPath(directory ?? Environment.CurrentDirectory);
    }

    private static string ResolveStorageOrigin(Uri uri, string fallback)
    {
        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();

        if (uri.IsFile)
        {
            var localPath = uri.LocalPath;
            var directory = Directory.Exists(localPath) ? localPath : Path.GetDirectoryName(localPath);
            return "file:" + Path.GetFullPath(directory ?? Environment.CurrentDirectory);
        }

        return uri.IsAbsoluteUri ? uri.GetLeftPart(UriPartial.Path) : fallback;
    }
}
