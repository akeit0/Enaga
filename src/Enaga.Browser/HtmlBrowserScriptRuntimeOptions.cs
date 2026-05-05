namespace Enaga.Browser;

public sealed record HtmlBrowserScriptRuntimeOptions(BrowserRequestProfile RequestProfile)
{
    public static HtmlBrowserScriptRuntimeOptions Default { get; } = new(BrowserRequestProfile.Default);

    public HtmlBrowserScriptRuntimeOptions(string userAgent)
        : this(BrowserRequestProfile.Default with { UserAgent = userAgent })
    {
    }

    public string UserAgent => RequestProfile.UserAgent;

    public string AcceptLanguage => RequestProfile.AcceptLanguage;
}
