using Enaga.Browser;
using Enaga.Html;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlBrowserWorkerTests
{
    [Fact]
    public void Worker_LoadsRelativeScriptAndPostsMessageBackToDocument()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(Path.Combine(tempDirectory, "worker.js"), """
            onmessage = function (event) {
              postMessage("worker:" + event.data);
            };
            """);

        try
        {
            var document = new HtmlDocument("""
                <body>
                  <div id="status">idle</div>
                  <script>
                    const worker = new Worker("./worker.js");
                    worker.onmessage = function (event) {
                      document.getElementById("status").textContent = event.data;
                    };
                    worker.postMessage("ping");
                  </script>
                </body>
                """, BasePath: tempDirectory);

            using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, Path.Combine(tempDirectory, "index.html"));

            Assert.NotNull(runtime);
            PumpUntil(runtime, html => html.Contains("<div id=\"status\">worker:ping</div>", StringComparison.Ordinal));
            Assert.Contains("<div id=\"status\">worker:ping</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Worker_SupportsModuleImportsSharedArrayBufferAndAtomics()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(Path.Combine(tempDirectory, "dep.js"), "export const prefix = 'atomics';");
        File.WriteAllText(Path.Combine(tempDirectory, "worker.js"), """
            import { prefix } from "./dep.js";
            onmessage = function () {
              const buffer = new SharedArrayBuffer(4);
              const values = new Int32Array(buffer);
              Atomics.store(values, 0, 41);
              const previous = Atomics.add(values, 0, 1);
              postMessage(prefix + ":" + previous + ":" + Atomics.load(values, 0) + ":" + (self === globalThis));
            };
            """);

        try
        {
            var document = new HtmlDocument("""
                <body>
                  <div id="status">idle</div>
                  <script>
                    const worker = new Worker("./worker.js", { type: "module" });
                    worker.onmessage = function (event) {
                      document.getElementById("status").textContent = event.data;
                    };
                    worker.postMessage("go");
                  </script>
                </body>
                """, BasePath: tempDirectory);

            using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, Path.Combine(tempDirectory, "index.html"));

            Assert.NotNull(runtime);
            PumpUntil(runtime, html => html.Contains("<div id=\"status\">atomics:41:42:true</div>", StringComparison.Ordinal));
            Assert.Contains("<div id=\"status\">atomics:41:42:true</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Worker_RemoteModuleRequests_UseRequesterAwareHeaders()
    {
        const string customUserAgent = "Mozilla/5.0 (compatible; EnagaBrowserWorkerTest/1.0)";
        List<TestHttpRequest> requests = [];
        using var server = new TestHttpServer(request =>
        {
            lock (requests)
                requests.Add(request);

            return request.Path switch
            {
                "/page/worker.js" => TestHttpResponse.Ok("""
                    import { prefix } from "./dep.js";
                    onmessage = function (event) { postMessage(prefix + ":" + event.data); };
                    """),
                "/page/dep.js" => TestHttpResponse.Ok("""export const prefix = "remote";"""),
                _ => TestHttpResponse.NotFound()
            };
        });

        var documentUrl = server.Url("/page/index.html");
        var workerUrl = server.Url("/page/worker.js");
        var depUrl = server.Url("/page/dep.js");
        var document = new HtmlDocument("""
            <body>
              <div id="status">idle</div>
              <script>
                const worker = new Worker("./worker.js", { type: "module" });
                worker.onmessage = function (event) {
                  document.getElementById("status").textContent = event.data;
                };
                worker.postMessage("ping");
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(
            document,
            documentUrl,
            new HtmlBrowserScriptRuntimeOptions(customUserAgent));

        Assert.NotNull(runtime);
        PumpUntil(runtime, html => html.Contains("<div id=\"status\">remote:ping</div>", StringComparison.Ordinal));
        Assert.Contains("<div id=\"status\">remote:ping</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);

        TestHttpRequest workerRequest;
        TestHttpRequest depRequest;
        lock (requests)
        {
            workerRequest = Assert.Single(requests, req => req.Path == "/page/worker.js");
            depRequest = Assert.Single(requests, req => req.Path == "/page/dep.js");
        }

        Assert.Equal(customUserAgent, workerRequest.Headers["User-Agent"]);
        Assert.Equal("worker", workerRequest.Headers["Sec-Fetch-Dest"]);
        Assert.Equal("same-origin", workerRequest.Headers["Sec-Fetch-Mode"]);
        Assert.Equal("same-origin", workerRequest.Headers["Sec-Fetch-Site"]);
        Assert.Equal(documentUrl, workerRequest.Headers["Referer"]);

        Assert.Equal("worker", depRequest.Headers["Sec-Fetch-Dest"]);
        Assert.Equal("same-origin", depRequest.Headers["Sec-Fetch-Mode"]);
        Assert.Equal("same-origin", depRequest.Headers["Sec-Fetch-Site"]);
        Assert.Equal(workerUrl, depRequest.Headers["Referer"]);
        Assert.Equal(customUserAgent, depRequest.Headers["User-Agent"]);
        Assert.DoesNotContain(depUrl, workerRequest.Headers["Referer"], StringComparison.Ordinal);
    }

    private static void PumpUntil(HtmlBrowserScriptRuntime runtime, Func<string, bool> isDone)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline && !isDone(runtime.CurrentDocument.Html))
        {
            Thread.Sleep(10);
            runtime.PumpEventLoopUntilIdle();
        }
    }

    private sealed record TestHttpRequest(string Path, IReadOnlyDictionary<string, string> Headers);

    private sealed record TestHttpResponse(int StatusCode, string StatusText, string Body)
    {
        public static TestHttpResponse Ok(string body) => new(200, "OK", body);
        public static TestHttpResponse NotFound() => new(404, "Not Found", string.Empty);
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
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n" };
            await writer.WriteLineAsync($"HTTP/1.1 {response.StatusCode} {response.StatusText}").ConfigureAwait(false);
            await writer.WriteLineAsync($"Content-Length: {bodyBytes.Length}").ConfigureAwait(false);
            await writer.WriteLineAsync("Content-Type: text/javascript; charset=utf-8").ConfigureAwait(false);
            await writer.WriteLineAsync("Connection: close").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
        }
    }
}
