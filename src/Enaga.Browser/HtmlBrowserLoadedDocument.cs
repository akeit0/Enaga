using Enaga.Html;

namespace Enaga.Browser;

public sealed record HtmlBrowserLoadedDocument(
    HtmlDocument Document,
    HtmlBrowserScriptRuntime? ScriptRuntime
);
