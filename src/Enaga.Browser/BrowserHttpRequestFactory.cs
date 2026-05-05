using System.Linq;
using System.Net.Http;

namespace Enaga.Browser;

internal sealed class BrowserHttpRequestFactory(BrowserRequestProfile requestProfile)
{
    public BrowserRequestProfile RequestProfile { get; } = requestProfile;

    public HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, BrowserHttpRequestOptions options)
    {
        var request = new HttpRequestMessage(method, uri);
        ApplyDefaultHeaders(request, options);
        return request;
    }

    public void ApplyDefaultHeaders(HttpRequestMessage request, BrowserHttpRequestOptions options)
    {
        if (!request.Headers.UserAgent.Any())
            request.Headers.UserAgent.ParseAdd(RequestProfile.UserAgent);
        if (!request.Headers.Accept.Any())
            request.Headers.Accept.ParseAdd(options.Accept);
        if (!request.Headers.AcceptLanguage.Any())
            request.Headers.AcceptLanguage.ParseAdd(RequestProfile.AcceptLanguage);
        if (options.Referer is not null)
            request.Headers.Referrer = options.Referer;
        if (!string.IsNullOrWhiteSpace(options.FetchDestination))
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", options.FetchDestination);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", options.FetchMode);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", options.FetchSite);
    }
}
