using Okojo.Objects;
using Enaga.Rendering;
using Enaga.React.OkojoRuntime;
using Okojo.Runtime;
using Xunit;

namespace Enaga.Tests;

public sealed class HostPercentLayoutTests
{
    [Fact]
    public void ResetAfterCommit_ResolvesPercentWidthAgainstParentWidth()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var entryPath = Path.Combine(tempDirectory, "react-entry.mjs");
        File.WriteAllText(entryPath, "export {};");

        try
        {
            using var host = new OkojoNodeReactHost(entryPath, debugEnabled: false, backendServices: DummyRuntimeBackendServices.Create());
            host.InitializeBenchmarkRuntime(width: 200, height: 100);

            var realm = host.BenchmarkRealm;
            var style = new JsPlainObject(realm);
            style.SetProperty("width", JsValue.FromString("50%"));
            style.SetProperty("height", new JsValue(40));

            var props = new JsPlainObject(realm);
            props.SetProperty("style", JsValue.FromObject(style));

            var child = host.BenchmarkCreateHostNode("View", "child", props);
            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(child));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            var box = snapshot.Layout["child"];
            Assert.True(Math.Abs(100 - box.Width) < 0.001f);
            Assert.True(Math.Abs(40 - box.Height) < 0.001f);
            Assert.True(Math.Abs(0 - box.AbsLeft) < 0.001f);
            Assert.True(Math.Abs(0 - box.AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
