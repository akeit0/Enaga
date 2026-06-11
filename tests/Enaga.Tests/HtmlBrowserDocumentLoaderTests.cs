using System.Net;
using System.Net.Sockets;
using System.Text;
using Enaga.Browser;
using Enaga.Html.Loader;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlBrowserDocumentLoaderTests
{
    [Fact]
    public void NormalizeNavigationSource_PromotesBareHostToHttps() =>
        Assert.Equal(
            "https://example.com",
            HtmlBrowserDocumentLoader.NormalizeNavigationSource("example.com")
        );

    [Fact]
    public void Load_UsesDocumentAndRuntimeOptions()
    {
        const string documentUserAgent = "DocAgent/1.0";
        const string documentAcceptLanguage = "fr-FR,fr;q=0.9";
        const string runtimeUserAgent = "RuntimeAgent/1.0";
        const string runtimeAcceptLanguage = "ja-JP,ja;q=0.9";

        var requests = new List<TestHttpRequest>();
        using var server = new TestHttpServer(request =>
        {
            lock (requests)
                requests.Add(request);

            return request.Path switch
            {
                "/index.html" => TestHttpResponse.Ok(
                    """
                    <body>
                      <div id="status">idle</div>
                      <script src="./app.js"></script>
                    </body>
                    """,
                    "text/html; charset=utf-8"
                ),
                "/app.js" => TestHttpResponse.Ok(
                    "document.getElementById('status').textContent = navigator.userAgent;",
                    "text/javascript; charset=utf-8"
                ),
                _ => TestHttpResponse.NotFound(),
            };
        });

        var loaded = HtmlBrowserDocumentLoader.Load(
            server.Url("/index.html"),
            options: new HtmlBrowserDocumentLoadOptions(
                EnableScripts: true,
                DocumentHttpClientOptions: new HtmlDocumentHttpClientOptions(
                    documentUserAgent,
                    "text/html",
                    documentAcceptLanguage
                ),
                ScriptRuntimeOptions: new HtmlBrowserScriptRuntimeOptions(
                    new BrowserRequestProfile(runtimeUserAgent, runtimeAcceptLanguage)
                )
            )
        );

        Assert.Contains(runtimeUserAgent, loaded.Document.Html, StringComparison.Ordinal);
        Assert.NotNull(loaded.ScriptRuntime);

        TestHttpRequest documentRequest;
        TestHttpRequest scriptRequest;
        lock (requests)
        {
            documentRequest = Assert.Single(requests, request => request.Path == "/index.html");
            scriptRequest = Assert.Single(requests, request => request.Path == "/app.js");
        }

        Assert.Equal(documentUserAgent, documentRequest.Headers["User-Agent"]);
        Assert.Equal(
            documentAcceptLanguage.Replace(" ", string.Empty, StringComparison.Ordinal),
            documentRequest
                .Headers["Accept-Language"]
                .Replace(" ", string.Empty, StringComparison.Ordinal)
        );
        Assert.Equal(runtimeUserAgent, scriptRequest.Headers["User-Agent"]);
        Assert.Equal(
            runtimeAcceptLanguage.Replace(" ", string.Empty, StringComparison.Ordinal),
            scriptRequest
                .Headers["Accept-Language"]
                .Replace(" ", string.Empty, StringComparison.Ordinal)
        );
    }

    private sealed class TestHttpServer : IDisposable
    {
        private readonly Func<TestHttpRequest, TestHttpResponse> respond;
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task serverTask;

        public TestHttpServer(Func<TestHttpRequest, TestHttpResponse> respond)
        {
            this.respond = respond;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            serverTask = Task.Run(RunAsync);
        }

        public string Url(string path)
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            return $"http://127.0.0.1:{endpoint.Port}{path}";
        }

        public void Dispose()
        {
            cancellation.Cancel();
            listener.Stop();
            serverTask.Wait(TimeSpan.FromSeconds(2));
            cancellation.Dispose();
        }

        private async Task RunAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }

                _ = Task.Run(() => HandleClientAsync(client), cancellation.Token);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using var _ = client;
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
            if (requestLine is null)
                return;

            var requestParts = requestLine.Split(' ', 3);
            var path = requestParts[1];
            var headersByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0)
                    headersByName[line[..separator]] = line[(separator + 1)..].Trim();
            }

            var response = respond(new TestHttpRequest(path, headersByName));
            var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
            var headerBuilder = new StringBuilder()
                .Append("HTTP/1.1 ")
                .Append(response.Status)
                .Append("\r\nContent-Type: ")
                .Append(response.ContentType)
                .Append("\r\nContent-Length: ")
                .Append(bodyBytes.Length)
                .Append("\r\nConnection: close\r\n");
            foreach (var header in response.Headers)
                headerBuilder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            headerBuilder.Append("\r\n");

            var headers = Encoding.ASCII.GetBytes(headerBuilder.ToString());
            await stream.WriteAsync(headers).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
        }
    }

    private sealed record TestHttpRequest(string Path, IReadOnlyDictionary<string, string> Headers);

    private sealed record TestHttpResponse(
        string Status,
        string Body,
        string ContentType,
        IReadOnlyDictionary<string, string> Headers
    )
    {
        public static TestHttpResponse Ok(string body, string contentType) =>
            new("200 OK", body, contentType, new Dictionary<string, string>());

        public static TestHttpResponse NotFound() =>
            new(
                "404 Not Found",
                string.Empty,
                "text/plain; charset=utf-8",
                new Dictionary<string, string>()
            );
    }
}
