namespace Enaga.Browser;

internal readonly record struct BrowserHttpRequestOptions(
    string Accept,
    Uri? Referer = null,
    string? FetchDestination = null,
    string FetchMode = "no-cors",
    string FetchSite = "same-origin");
