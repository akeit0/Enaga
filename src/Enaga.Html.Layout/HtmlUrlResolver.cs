namespace Enaga.Html;

internal static class HtmlUrlResolver
{
    public static string? Resolve(string? value, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
            trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (string.IsNullOrWhiteSpace(basePath))
            return trimmed;

        if (Uri.TryCreate(basePath, UriKind.Absolute, out var baseUri) &&
            (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps) &&
            Uri.TryCreate(baseUri, trimmed, out var resolvedUri))
        {
            return resolvedUri.ToString();
        }

        return Path.GetFullPath(Path.Combine(basePath, trimmed));
    }
}
