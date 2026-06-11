using System.Net;
using System.Net.Http;

namespace Enaga.Browser;

internal sealed class BrowserNetworkSession : IDisposable
{
    private readonly BrowserHttpRequestFactory requestFactory;

    public BrowserNetworkSession(BrowserRequestProfile requestProfile)
    {
        requestFactory = new BrowserHttpRequestFactory(requestProfile);
        HttpClient = CreateHttpClient();
    }

    public HttpClient HttpClient { get; }

    public BrowserRequestProfile RequestProfile => requestFactory.RequestProfile;

    public HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri uri,
        BrowserHttpRequestOptions options
    ) => requestFactory.CreateRequest(method, uri, options);

    public void ApplyDefaultHeaders(
        HttpRequestMessage request,
        BrowserHttpRequestOptions options
    ) => requestFactory.ApplyDefaultHeaders(request, options);

    public void Dispose() => HttpClient.Dispose();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression =
                DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        };
        return new HttpClient(handler);
    }
}
