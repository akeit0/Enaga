using Enaga.Browser;
using Enaga.Html;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlBrowserStorageTests
{
    [Fact]
    public void CreateAndRun_ProvidesLocalStorageAndSessionStorageApis()
    {
        var document = new HtmlDocument("""
            <body>
              <div id="status"></div>
              <script>
                localStorage.clear();
                sessionStorage.clear();
                localStorage.setItem("name", "local");
                sessionStorage.setItem("name", "session");
                localStorage.setItem("remove-me", "old");
                localStorage.removeItem("remove-me");
                document.getElementById("status").textContent =
                  localStorage.length + ":" +
                  localStorage.key(0) + ":" +
                  localStorage.getItem("name") + ":" +
                  localStorage.getItem("remove-me") + ":" +
                  sessionStorage.getItem("name");
              </script>
            </body>
            """);

        using var runtime = HtmlBrowserScriptRuntime.CreateAndRun(document, "https://storage.example/page");

        Assert.NotNull(runtime);
        Assert.Contains("<div id=\"status\">1:name:local:null:session</div>", runtime.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalStorage_IsSharedForSameOriginRuntimeInstances()
    {
        var writeDocument = new HtmlDocument("""
            <body>
              <script>
                localStorage.clear();
                localStorage.setItem("shared", "yes");
              </script>
            </body>
            """);
        using var writer = HtmlBrowserScriptRuntime.CreateAndRun(writeDocument, "https://storage-shared.example/one");
        Assert.NotNull(writer);

        var readDocument = new HtmlDocument("""
            <body>
              <div id="status"></div>
              <script>
                document.getElementById("status").textContent = localStorage.getItem("shared");
              </script>
            </body>
            """);
        using var reader = HtmlBrowserScriptRuntime.CreateAndRun(readDocument, "https://storage-shared.example/two");

        Assert.NotNull(reader);
        Assert.Contains("<div id=\"status\">yes</div>", reader.CurrentDocument.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionStorage_IsIsolatedPerRuntimeInstance()
    {
        var writeDocument = new HtmlDocument("""
            <body>
              <script>
                sessionStorage.setItem("shared", "no");
              </script>
            </body>
            """);
        using var writer = HtmlBrowserScriptRuntime.CreateAndRun(writeDocument, "https://storage-session.example/one");
        Assert.NotNull(writer);

        var readDocument = new HtmlDocument("""
            <body>
              <div id="status"></div>
              <script>
                document.getElementById("status").textContent = "value:" + sessionStorage.getItem("shared");
              </script>
            </body>
            """);
        using var reader = HtmlBrowserScriptRuntime.CreateAndRun(readDocument, "https://storage-session.example/two");

        Assert.NotNull(reader);
        Assert.Contains("<div id=\"status\">value:null</div>", reader.CurrentDocument.Html, StringComparison.Ordinal);
    }
}
