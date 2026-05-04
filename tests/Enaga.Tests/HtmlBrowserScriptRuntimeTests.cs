using System.Net;
using System.Net.Sockets;
using System.Text;
using Enaga.Browser;
using Enaga.Html;
using Enaga.Html.Dom;
using Enaga.React.OkojoRuntime;
using Enaga.Rendering;
using Enaga.Scene;
using Okojo.Objects;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlBrowserScriptRuntimeTests
{
    [Fact]
    public void CreateAndRun_DrainsAwaitContinuation_FromInlineScript()
    {
        var document = new HtmlDocument("""
            <body>
              <div id="status">loading</div>
              <script>
                (async function () {
                  await Promise.resolve();
                  document.getElementById("status").textContent = "ready";
                })();
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");

        Assert.NotNull(runtime);
        Assert.Contains("ready", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteJavaScriptUrl_RunsAgainstCurrentDocument()
    {
        var document = new HtmlDocument("""
            <body>
              <div id="status">idle</div>
            </body>D
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");
        Assert.NotNull(runtime);

        runtime.ExecuteJavaScriptUrl("javascript:document.getElementById('status').textContent = 'clicked'");

        Assert.Contains("clicked", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void DispatchClick_DrainsAwaitContinuation_FromAsyncHandler()
    {
        var document = new HtmlDocument("""
            <body>
              <button id="btn">click</button>
              <div id="status">idle</div>
              <script>
                document.getElementById("btn").onclick = async function () {
                  await Promise.resolve();
                  document.getElementById("status").textContent = "clicked";
                };
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");
        Assert.NotNull(runtime);

        var parsed = new Enaga.Html.Dom.HtmlDocumentParser().Parse(document.Html, document.BasePath).ToDomDocument();
        var button = Assert.IsType<HtmlDomElement>(parsed.GetElementById("btn"));

        runtime.DispatchClick(button);

        Assert.Contains("clicked", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void EventLoopWake_PumpsQueuedScriptWork_WithoutRenderWakeUntilDocumentChanges()
    {
        var document = new HtmlDocument("""
            <body>
              <div id="status">idle</div>
              <script>
                setTimeout(function () {
                  document.getElementById("status").textContent = "timer";
                }, 10);
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");
        Assert.NotNull(runtime);

        var source = new HtmlSceneFrameSource(runtime.CurrentDocument);
        var renderWakeRequested = false;
        runtime.DocumentMutated += source.UpdateDocument;
        runtime.EventLoopWorkQueued += () => runtime.PumpEventLoopUntilIdle();
        source.RenderWakeRequested += () => renderWakeRequested = true;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < deadline && !renderWakeRequested)
            Thread.Sleep(10);

        Assert.True(renderWakeRequested);
        _ = source.RenderFrame(320, 200, TimeSpan.FromMilliseconds(16));
        Assert.Contains("timer", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueProperty_GetsAndSetsInputValue()
    {
        var document = new HtmlDocument("""
            <body>
              <input id="name" value="old">
              <div id="status"></div>
              <script>
                const inputField = document.getElementById("name");
                inputField.value = inputField.value + "-new";
                document.getElementById("status").textContent = inputField.value;
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");

        Assert.NotNull(runtime);
        Assert.Contains("value=\"old-new\"", runtime.CurrentDocument.Html, StringComparison.Ordinal);
        Assert.Contains("old-new", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueProperty_ReadsLiveRendererInputValue_WhenDomAttributeIsEmpty()
    {
        var document = new HtmlDocument("""
            <body>
              <input id="name">
              <button id="read">read</button>
              <div id="status"></div>
              <script>
                document.getElementById("read").onclick = function () {
                  document.getElementById("status").textContent = document.getElementById("name").value;
                };
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");
        Assert.NotNull(runtime);
        runtime.TextInputValueResolver = (string elementId, out string value) =>
        {
            value = elementId == "name" ? "typed" : string.Empty;
            return elementId == "name";
        };

        var parsed = new Enaga.Html.Dom.HtmlDocumentParser().Parse(document.Html, document.BasePath).ToDomDocument();
        var button = Assert.IsType<HtmlDomElement>(parsed.GetElementById("read"));

        runtime.DispatchClick(button);

        Assert.Contains("<div id=\"status\">typed</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueProperty_GetsAndSetsTextAreaValue()
    {
        var document = new HtmlDocument("""
            <body>
              <textarea id="message">old</textarea>
              <div id="status"></div>
              <script>
                const textarea = document.getElementById("message");
                textarea.value = textarea.value + "-new";
                document.getElementById("status").textContent = textarea.value;
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");

        Assert.NotNull(runtime);
        Assert.Contains("<textarea id=\"message\">old-new</textarea>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
        Assert.Contains("<div id=\"status\">old-new</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAndRun_LoadsLocalExternalScriptInDocumentOrder()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var scriptPath = Path.Combine(tempDirectory, "api.js.ダウンロード");
        File.WriteAllText(scriptPath, "window.externalValue = 'loaded';");

        try
        {
            var document = new HtmlDocument("""
                <body>
                  <div id="status"></div>
                  <script src="./api.js.ダウンロード" async="" defer=""></script>
                  <script>
                    document.getElementById("status").textContent = window.externalValue || "missing";
                  </script>
                </body>
                """, BasePath: tempDirectory);

            using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");

            Assert.NotNull(runtime);
            Assert.Contains("<div id=\"status\">loaded</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateAndRun_ProvidesNavigatorAndDataLayerGlobals()
    {
        var document = new HtmlDocument("""
            <body>
              <div id="status"></div>
              <script>
                dataLayer.push("ready");
                document.getElementById("status").textContent =
                  navigator.userAgent.indexOf("Enaga.Browser") >= 0 && window.dataLayer.length === 1
                    ? "available"
                    : "missing";
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");

        Assert.NotNull(runtime);
        Assert.Contains("<div id=\"status\">available</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAndRun_ExposesWindowAssignmentsAsGlobalsForLaterScripts()
    {
        var document = new HtmlDocument("""
            <body>
              <div id="status"></div>
              <script>
                window.libraryValue = "available";
              </script>
              <script>
                document.getElementById("status").textContent = libraryValue;
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");

        Assert.NotNull(runtime);
        Assert.Contains("<div id=\"status\">available</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAndRun_ProvidesMinimalDomProbeApis()
    {
        var document = new HtmlDocument("""
            <html>
              <head></head>
              <body>
                <div id="status"></div>
                <script>
                  const fieldset = document.createElement("fieldset");
                  fieldset.innerHTML = "<a href='#'></a>";
                  const inputHost = document.createElement("fieldset");
                  inputHost.innerHTML = "<input/>";
                  inputHost.firstChild.setAttribute("value", "");
                  inputHost.firstChild.setAttribute("checked", "checked");
                  const inputClone = inputHost.cloneNode(true).cloneNode(true);
                  const htmlDocBody = document.implementation.createHTMLDocument("").body;
                  htmlDocBody.innerHTML = "<form></form><form></form>";
                  const script = document.createElement("script");
                  const ok = document.documentElement.nodeType === 1 &&
                    document.head.appendChild(script).parentNode.removeChild(script) === script &&
                    document.querySelectorAll("fieldset").length === 0 &&
                    fieldset.nodeType === 1 &&
                    fieldset.nodeName === "FIELDSET" &&
                    fieldset.childNodes.length === 1 &&
                    fieldset.lastChild === fieldset.firstChild &&
                    inputClone.lastChild.checked === true &&
                    htmlDocBody.childNodes.length === 2 &&
                    fieldset.style.getPropertyValue("display") === "" &&
                    fieldset.firstChild.ownerDocument === document &&
                    fieldset.firstChild.getAttribute("href") === "#" &&
                    inputHost.firstChild.nodeName === "INPUT" &&
                    inputHost.firstChild.getAttributeNode("value").specified &&
                    inputHost.firstChild.getAttributeNode("value").value === "" &&
                    fieldset.compareDocumentPosition(document.createElement("fieldset")) === 1 &&
                    fieldset.parentNode === null &&
                    (function ce(e){var t=document.createElement("fieldset");try{return!!e(t)}catch(e){return false}finally{t.parentNode&&t.parentNode.removeChild(t),t=null}})(function(e){return 1&e.compareDocumentPosition(document.createElement("fieldset"))}) &&
                    (function ce(e){var t=document.createElement("fieldset");try{return!!e(t)}catch(e){return false}finally{t.parentNode&&t.parentNode.removeChild(t),t=null}})(function(e){return e.innerHTML="<a href='#'></a>","#"===e.firstChild.getAttribute("href")});
                  document.getElementById("status").textContent = ok ? "available" : "missing";
                </script>
              </body>
            </html>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");

        Assert.NotNull(runtime);
        Assert.Contains("<div id=\"status\">available</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAndRun_ClampsLongScriptErrorSourceLine()
    {
        var script = "const filler = '" + new string('x', 400) + "'; throw new Error('boom');";
        var document = new HtmlDocument("<body><script>" + script + "</script></body>");
        using var error = new StringWriter();
        var previousError = Console.Error;
        Console.SetError(error);

        try
        {
            using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");

            Assert.NotNull(runtime);
        }
        finally
        {
            Console.SetError(previousError);
        }

        var log = error.ToString();
        Assert.Contains("...", log, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 300), log, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAndRun_LoadsRemoteExternalScriptWithBrowserHeadersAndReferer()
    {
        string? serverReferer = null;
        using var server = new TestHttpServer(request =>
        {
            if (request.Path == "/static/_js/iana.js?version=1" &&
                request.Headers.TryGetValue("User-Agent", out var userAgent) &&
                userAgent.Contains("Mozilla/5.0", StringComparison.Ordinal) &&
                request.Headers.TryGetValue("Referer", out var referer) &&
                string.Equals(referer, serverReferer, StringComparison.Ordinal) &&
                request.Headers.TryGetValue("Sec-Fetch-Dest", out var fetchDest) &&
                string.Equals(fetchDest, "script", StringComparison.OrdinalIgnoreCase))
            {
                return TestHttpResponse.Ok("document.getElementById('status').textContent = 'remote-loaded';");
            }

            return TestHttpResponse.Forbidden();
        });
        serverReferer = server.Url("/domains");

        var document = new HtmlDocument("""
            <body>
              <div id="status"></div>
              <script type="text/javascript" src="./static/_js/iana.js?version=1"></script>
            </body>
            """, BasePath: server.Url("/"));

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, serverReferer);

        Assert.NotNull(runtime);
        Assert.Contains("<div id=\"status\">remote-loaded</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAndRun_PreservesHeadStylesAfterBodySerialization()
    {
        var document = new HtmlDocument("""
            <html>
              <head>
                <style>
                  #styled { background: #123456; }
                </style>
              </head>
              <body>
                <div id="styled">styled</div>
                <script>
                  document.getElementById("styled").textContent = "updated";
                </script>
              </body>
            </html>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "inline:test.html");
        Assert.NotNull(runtime);

        var source = new HtmlSceneFrameSource(runtime.CurrentDocument);
        var commit = source.BuildCommit(320, 200);
        var styled = Assert.Single(commit.Layout.Values, box => box.BackgroundColor == "#123456");

        Assert.Equal(SceneNodeKind.View, styled.NodeKind);
    }

    [Fact]
    public void Fetch_ResolvesRelativeUrlAgainstDocumentBasePath()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetDirectory = Path.Combine(tempDirectory, "login_files");
        Directory.CreateDirectory(assetDirectory);
        File.WriteAllText(Path.Combine(assetDirectory, "data.json"), """{"message":"relative"}""");

        try
        {
            var document = new HtmlDocument("""
                <body>
                  <div id="status"></div>
                  <script>
                    (async function () {
                      const response = await window.fetch("./login_files/data.json");
                      const payload = await response.json();
                      document.getElementById("status").textContent = payload.message;
                    })();
                  </script>
                </body>
                """, BasePath: tempDirectory);

            using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, Path.Combine(tempDirectory, "login.html"));

            Assert.NotNull(runtime);
            Assert.Contains("<div id=\"status\">relative</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void LocationReplace_RequestsNavigationWithResolvedRelativeUrl()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var document = new HtmlDocument("""
                <body>
                  <button id="go">go</button>
                  <script>
                    document.getElementById("go").onclick = function () {
                      window.location.replace("./next.html");
                    };
                  </script>
                </body>
                """, BasePath: tempDirectory);

            using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, Path.Combine(tempDirectory, "login.html"));
            Assert.NotNull(runtime);
            string? requestedUrl = null;
            runtime.NavigationRequested += url => requestedUrl = url;

            var parsed = new Enaga.Html.Dom.HtmlDocumentParser().Parse(document.Html, document.BasePath).ToDomDocument();
            var button = Assert.IsType<HtmlDomElement>(parsed.GetElementById("go"));
            runtime.DispatchClick(button);

            Assert.Equal(Path.GetFullPath(Path.Combine(tempDirectory, "next.html")), requestedUrl);
            Assert.True(runtime.PendingNavigationReplacesHistory);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ReactHost_BenchmarkPump_ResumesAwaitedTimeout()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var entryPath = Path.Combine(tempDirectory, "react-entry.mjs");
        File.WriteAllText(entryPath, "export {};");

        try
        {
            using var host = new OkojoNodeReactHost(entryPath, debugEnabled: false, backendServices: DummyRuntimeBackendServices.Create());
            host.InitializeBenchmarkRuntime();

            var realm = host.BenchmarkRealm;
            _ = realm.Eval("""
                globalThis.done = false;
                globalThis.start = async function () {
                  await new Promise(resolve => setTimeout(resolve, 10));
                  done = true;
                };
                start();
                """);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            while (DateTime.UtcNow < deadline && !realm.Global["done"].IsTrue)
            {
                host.BenchmarkPumpRuntimeJobs();
                Thread.Sleep(10);
            }

            Assert.True(realm.Global["done"].IsTrue);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
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
                .Append("\r\nContent-Type: text/javascript; charset=utf-8\r\nContent-Length: ")
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

        public static TestHttpResponse Forbidden()
            => new("403 Forbidden", string.Empty, new Dictionary<string, string>());
    }
}
