using System.Net;
using System.Net.Sockets;
using System.Text;
using Enaga.Html.Loader;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlDocumentLoaderTests
{
    [Fact]
    public async Task LoadAsyncReadsHtmlAndLinkedStyleSheetFromUrl()
    {
        var userAgents = new List<string>();
        using var server = new TestHttpServer(request =>
        {
            if (request.Headers.TryGetValue("User-Agent", out var userAgent))
                userAgents.Add(userAgent);

            return request.Path switch
            {
                "/index.html" => TestHttpResponse.Ok("<body><link rel=\"stylesheet\" href=\"style.css\"><img src=\"images/hero.png\"></body>"),
                "/style.css" => TestHttpResponse.Ok("img { display: block; }"),
                _ => TestHttpResponse.NotFound()
            };
        });

        var document = await HtmlDocumentLoader.LoadAsync(server.Url("/index.html"));

        Assert.Contains("<img", document.Html, StringComparison.Ordinal);
        Assert.Contains("display: block", document.StyleSheet, StringComparison.Ordinal);
        Assert.Equal(server.Url("/"), document.BasePath);
        Assert.Contains(userAgents, value => value.Contains("Mozilla/5.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsyncReadsLinkedStyleSheetFromLocalRelativePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "html-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var assetDirectory = Path.Combine(directory, "saved-page_files");
            Directory.CreateDirectory(assetDirectory);
            var htmlPath = Path.Combine(directory, "saved-page.html");
            var cssPath = Path.Combine(assetDirectory, "style.css");
            await File.WriteAllTextAsync(htmlPath, """<body><link rel="stylesheet" href="./saved-page_files/style.css"><main class="card">Local</main></body>""");
            await File.WriteAllTextAsync(cssPath, ".card { color: #123456; }");

            var document = await HtmlDocumentLoader.LoadAsync(htmlPath);

            Assert.Contains("#123456", document.StyleSheet, StringComparison.Ordinal);
            Assert.Equal(directory, document.BasePath);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsyncRetriesSameDocumentCookieGate()
    {
        var requestCount = 0;
        using var server = new TestHttpServer(request =>
        {
            requestCount++;
            if (request.Headers.TryGetValue("Cookie", out var cookie) &&
                cookie.Contains("PHPSESSID=test-session", StringComparison.Ordinal))
            {
                return TestHttpResponse.Ok("<body><h1>Real page</h1></body>");
            }

            return TestHttpResponse.Ok(
                "<a href=\"/word1580.html\">Please click here.</a>",
                new Dictionary<string, string> { ["Set-Cookie"] = "PHPSESSID=test-session; path=/" });
        });

        var document = await HtmlDocumentLoader.LoadAsync(server.Url("/word1580.html"));

        Assert.Equal(2, requestCount);
        Assert.Contains("Real page", document.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateHttpClientUsesGenericBrowserHeaders()
    {
        using var client = HtmlDocumentLoader.CreateHttpClient();

        var userAgent = client.DefaultRequestHeaders.UserAgent.ToString();
        var acceptLanguage = client.DefaultRequestHeaders.AcceptLanguage.ToString();

        Assert.Contains("Mozilla/5.0", userAgent, StringComparison.Ordinal);
        Assert.Contains("Enaga.Html.Loader/1.0", userAgent, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows NT", userAgent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SampleBrowser", userAgent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ja,en-US", acceptLanguage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text/html", client.DefaultRequestHeaders.Accept.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateHttpClientAcceptsCallerProvidedHeaders()
    {
        using var client = HtmlDocumentLoader.CreateHttpClient(new HtmlDocumentHttpClientOptions(
            "TestAgent/1.0",
            "text/plain",
            "fr-FR,fr;q=0.9"));

        Assert.Equal("TestAgent/1.0", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal("text/plain", client.DefaultRequestHeaders.Accept.ToString());
        Assert.Equal("fr-FR, fr; q=0.9", client.DefaultRequestHeaders.AcceptLanguage.ToString());
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
                .Append("\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: ")
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

    private sealed record TestHttpResponse(string Status, string Body, IReadOnlyDictionary<string, string> Headers)
    {
        public static TestHttpResponse Ok(string body, IReadOnlyDictionary<string, string>? headers = null)
            => new("200 OK", body, headers ?? new Dictionary<string, string>());

        public static TestHttpResponse NotFound()
            => new("404 Not Found", string.Empty, new Dictionary<string, string>());
    }
}
