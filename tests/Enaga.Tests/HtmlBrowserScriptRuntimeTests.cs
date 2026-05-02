using Enaga.Browser;
using Enaga.Html;
using Enaga.Html.Dom;
using Enaga.React.OkojoRuntime;
using Enaga.Rendering;
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
}
