using Enaga.Html;
using Enaga.Html.Loader;

namespace Enaga.Browser;

public static class HtmlBrowserDocumentLoader
{
    public static HtmlBrowserLoadedDocument Load(
        string documentSource,
        string? styleSheetSource = null,
        HtmlBrowserDocumentLoadOptions? options = null
    ) => LoadAsync(documentSource, styleSheetSource, options).GetAwaiter().GetResult();

    public static async Task<HtmlBrowserLoadedDocument> LoadAsync(
        string documentSource,
        string? styleSheetSource = null,
        HtmlBrowserDocumentLoadOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= HtmlBrowserDocumentLoadOptions.Default;
        var normalizedSource = NormalizeNavigationSource(documentSource);
        var document = await HtmlDocumentLoader
            .LoadAsync(
                normalizedSource,
                styleSheetSource,
                options.DocumentHttpClientOptions,
                cancellationToken
            )
            .ConfigureAwait(false);
        return ProcessLoadedDocument(document, normalizedSource, options);
    }

    public static bool TryLoad(
        string documentSource,
        string? styleSheetSource,
        HtmlBrowserDocumentLoadOptions? options,
        out HtmlBrowserLoadedDocument loadedDocument
    )
    {
        loadedDocument = default!;
        try
        {
            loadedDocument = Load(documentSource, styleSheetSource, options);
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

    public static string NormalizeNavigationSource(string source)
    {
        var trimmed = source.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        if (
            Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.IsFile)
        )
        {
            return trimmed;
        }

        if (File.Exists(trimmed))
            return Path.GetFullPath(trimmed);

        return
            trimmed.Contains('.', StringComparison.Ordinal)
            && !trimmed.Contains('\\')
            && !trimmed.Contains('/')
            ? "https://" + trimmed
            : Path.GetFullPath(trimmed);
    }

    private static HtmlBrowserLoadedDocument ProcessLoadedDocument(
        HtmlDocument document,
        string documentSource,
        HtmlBrowserDocumentLoadOptions options
    )
    {
        var scriptRuntime = options.EnableScripts
            ? HtmlBrowserScriptRuntime.CreateAndRun(
                document,
                documentSource,
                options.ScriptRuntimeOptions
            )
            : null;

        return new HtmlBrowserLoadedDocument(
            scriptRuntime?.CurrentDocument ?? document,
            scriptRuntime
        );
    }
}
