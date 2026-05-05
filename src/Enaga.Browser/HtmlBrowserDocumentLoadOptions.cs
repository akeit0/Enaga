using Enaga.Html.Loader;

namespace Enaga.Browser;

public sealed record HtmlBrowserDocumentLoadOptions(
    bool EnableScripts,
    HtmlDocumentHttpClientOptions? DocumentHttpClientOptions,
    HtmlBrowserScriptRuntimeOptions? ScriptRuntimeOptions)
{
    public static HtmlBrowserDocumentLoadOptions Default { get; } = new(
        EnableScripts: true,
        DocumentHttpClientOptions: null,
        ScriptRuntimeOptions: null);
}
