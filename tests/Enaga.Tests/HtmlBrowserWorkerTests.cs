using Enaga.Browser;
using Enaga.Html;
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

    private static void PumpUntil(HtmlBrowserScriptRuntime runtime, Func<string, bool> isDone)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline && !isDone(runtime.CurrentDocument.Html))
        {
            Thread.Sleep(10);
            runtime.PumpEventLoopUntilIdle();
        }
    }
}
