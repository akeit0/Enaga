using Enaga.React.OkojoRuntime;
using Enaga.Rendering;
using Okojo.Objects;
using Okojo.Runtime;
using Xunit;

namespace Enaga.Tests;

public sealed class HostFlowDirectionLayoutTests
{
    [Fact]
    public void ResetAfterCommit_ResolvesRowReverseFlowLayout()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 200, height: 80);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(
                    realm,
                    ("width", 200),
                    ("height", 40),
                    ("flexDirection", "row-reverse")
                )
            );
            var first = host.BenchmarkCreateHostNode(
                "View",
                "first",
                CreateStyleProps(realm, ("width", 40), ("height", 20))
            );
            var second = host.BenchmarkCreateHostNode(
                "View",
                "second",
                CreateStyleProps(realm, ("width", 60), ("height", 20))
            );
            host.BenchmarkAppendChild(root, first);
            host.BenchmarkAppendChild(root, second);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(160 - snapshot.Layout["first"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(100 - snapshot.Layout["second"].AbsLeft) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_ResolvesRtlColumnStartAlignmentFromRightEdge()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 200, height: 80);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(
                    realm,
                    ("width", 200),
                    ("height", 80),
                    ("flexDirection", "column"),
                    ("direction", "rtl"),
                    ("alignItems", "start")
                )
            );
            var child = host.BenchmarkCreateHostNode(
                "View",
                "child",
                CreateStyleProps(realm, ("width", 40), ("height", 20))
            );
            host.BenchmarkAppendChild(root, child);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(160 - snapshot.Layout["child"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(0 - snapshot.Layout["child"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_ResolvesRowWrapFlowLayout()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 160, height: 220);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(
                    realm,
                    ("width", 140),
                    ("height", 220),
                    ("flexDirection", "row"),
                    ("flexWrap", "wrap"),
                    ("gap", 10)
                )
            );
            var first = host.BenchmarkCreateHostNode(
                "View",
                "first",
                CreateStyleProps(realm, ("width", 50), ("height", 50))
            );
            var second = host.BenchmarkCreateHostNode(
                "View",
                "second",
                CreateStyleProps(realm, ("width", 50), ("height", 50))
            );
            var third = host.BenchmarkCreateHostNode(
                "View",
                "third",
                CreateStyleProps(realm, ("width", 50), ("height", 50))
            );
            var fourth = host.BenchmarkCreateHostNode(
                "View",
                "fourth",
                CreateStyleProps(realm, ("width", 50), ("height", 50))
            );
            var fifth = host.BenchmarkCreateHostNode(
                "View",
                "fifth",
                CreateStyleProps(realm, ("width", 50), ("height", 50))
            );
            host.BenchmarkAppendChild(root, first);
            host.BenchmarkAppendChild(root, second);
            host.BenchmarkAppendChild(root, third);
            host.BenchmarkAppendChild(root, fourth);
            host.BenchmarkAppendChild(root, fifth);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(0 - snapshot.Layout["first"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(0 - snapshot.Layout["first"].AbsTop) < 0.001f);
            Assert.True(Math.Abs(60 - snapshot.Layout["second"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(0 - snapshot.Layout["second"].AbsTop) < 0.001f);
            Assert.True(Math.Abs(0 - snapshot.Layout["third"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(60 - snapshot.Layout["third"].AbsTop) < 0.001f);
            Assert.True(Math.Abs(60 - snapshot.Layout["fourth"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(60 - snapshot.Layout["fourth"].AbsTop) < 0.001f);
            Assert.True(Math.Abs(0 - snapshot.Layout["fifth"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(120 - snapshot.Layout["fifth"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_ImplicitColumnFlowCentersChildAlignSelf()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 200, height: 120);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(realm, ("width", 50), ("height", 50))
            );
            var child = host.BenchmarkCreateHostNode(
                "View",
                "child",
                CreateStyleProps(realm, ("width", 20), ("height", 25), ("alignSelf", "center"))
            );
            host.BenchmarkAppendChild(root, child);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(15 - snapshot.Layout["child"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(0 - snapshot.Layout["child"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_ImplicitColumnFlowStacksChildrenAndHonorsRtl()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 240, height: 240);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(
                    realm,
                    ("width", 200),
                    ("height", 200),
                    ("padding", 10),
                    ("direction", "rtl")
                )
            );
            var first = host.BenchmarkCreateHostNode(
                "View",
                "first",
                CreateStyleProps(realm, ("margin", 5), ("width", 50), ("height", 50))
            );
            var second = host.BenchmarkCreateHostNode(
                "View",
                "second",
                CreateStyleProps(realm, ("margin", 5), ("width", 50), ("height", 50))
            );
            host.BenchmarkAppendChild(root, first);
            host.BenchmarkAppendChild(root, second);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(135 - snapshot.Layout["first"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(15 - snapshot.Layout["first"].AbsTop) < 0.001f);
            Assert.True(Math.Abs(135 - snapshot.Layout["second"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(75 - snapshot.Layout["second"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_DefaultRelativePositionAppliesOffsets()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 200, height: 120);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(realm, ("width", 100), ("height", 100))
            );
            var child = host.BenchmarkCreateHostNode(
                "View",
                "child",
                CreateStyleProps(realm, ("width", 20), ("height", 20), ("left", 10), ("top", 6))
            );
            host.BenchmarkAppendChild(root, child);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(10 - snapshot.Layout["child"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(6 - snapshot.Layout["child"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_StaticDefaultIgnoresOmittedPositionOffsets()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create(),
                defaultPositionMode: DefaultPositionMode.Static
            );
            host.InitializeBenchmarkRuntime(width: 200, height: 120);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(realm, ("width", 100), ("height", 100))
            );
            var child = host.BenchmarkCreateHostNode(
                "View",
                "child",
                CreateStyleProps(realm, ("width", 20), ("height", 20), ("left", 10), ("top", 6))
            );
            host.BenchmarkAppendChild(root, child);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(0 - snapshot.Layout["child"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(0 - snapshot.Layout["child"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_AbsoluteChildDoesNotConsumeFlowSpace()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 200, height: 160);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(realm, ("width", 100), ("height", 100))
            );
            var absolute = host.BenchmarkCreateHostNode(
                "View",
                "absolute",
                CreateStyleProps(
                    realm,
                    ("position", "absolute"),
                    ("width", 20),
                    ("height", 20),
                    ("left", 12),
                    ("top", 8)
                )
            );
            var sibling = host.BenchmarkCreateHostNode(
                "View",
                "sibling",
                CreateStyleProps(realm, ("width", 20), ("height", 20))
            );
            host.BenchmarkAppendChild(root, absolute);
            host.BenchmarkAppendChild(root, sibling);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(12 - snapshot.Layout["absolute"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(8 - snapshot.Layout["absolute"].AbsTop) < 0.001f);
            Assert.True(Math.Abs(0 - snapshot.Layout["sibling"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(0 - snapshot.Layout["sibling"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_BorderActsLikePaddingForChildOffset()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 200, height: 140);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(
                    realm,
                    ("width", 60),
                    ("height", 60),
                    ("borderWidth", 4),
                    ("padding", 6)
                )
            );
            var child = host.BenchmarkCreateHostNode(
                "View",
                "child",
                CreateStyleProps(realm, ("width", 20), ("height", 20))
            );
            host.BenchmarkAppendChild(root, child);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(10 - snapshot.Layout["child"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(10 - snapshot.Layout["child"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_AutoSizedParentIncludesPaddingAndBorder()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 200, height: 140);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(realm, ("borderWidth", 4), ("padding", 6))
            );
            var child = host.BenchmarkCreateHostNode(
                "View",
                "child",
                CreateStyleProps(realm, ("width", 20), ("height", 10))
            );
            host.BenchmarkAppendChild(root, child);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(40 - snapshot.Layout["root"].Width) < 0.001f);
            Assert.True(Math.Abs(30 - snapshot.Layout["root"].Height) < 0.001f);
            Assert.True(Math.Abs(10 - snapshot.Layout["child"].AbsLeft) < 0.001f);
            Assert.True(Math.Abs(10 - snapshot.Layout["child"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_ContentBoxExplicitHeightUsesInsetFloor()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 240, height: 240);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(realm, ("width", 200), ("height", 200), ("padding", 10))
            );
            var first = host.BenchmarkCreateHostNode(
                "View",
                "first",
                CreateStyleProps(
                    realm,
                    ("margin", 5),
                    ("padding", 20),
                    ("borderWidth", 10),
                    ("height", 50),
                    ("boxSizing", "content-box")
                )
            );
            var second = host.BenchmarkCreateHostNode(
                "View",
                "second",
                CreateStyleProps(realm, ("height", 50))
            );
            host.BenchmarkAppendChild(root, first);
            host.BenchmarkAppendChild(root, second);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(15 - snapshot.Layout["first"].AbsTop) < 0.001f);
            Assert.True(Math.Abs(70 - snapshot.Layout["first"].Height) < 0.001f);
            Assert.True(Math.Abs(90 - snapshot.Layout["second"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_BorderBoxExplicitHeightKeepsOuterLayoutFixed()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 240, height: 240);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(realm, ("width", 200), ("height", 200), ("padding", 10))
            );
            var first = host.BenchmarkCreateHostNode(
                "View",
                "first",
                CreateStyleProps(
                    realm,
                    ("margin", 5),
                    ("padding", 20),
                    ("borderWidth", 10),
                    ("height", 50),
                    ("boxSizing", "border-box")
                )
            );
            var second = host.BenchmarkCreateHostNode(
                "View",
                "second",
                CreateStyleProps(realm, ("height", 50))
            );
            host.BenchmarkAppendChild(root, first);
            host.BenchmarkAppendChild(root, second);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(15 - snapshot.Layout["first"].AbsTop) < 0.001f);
            Assert.True(Math.Abs(50 - snapshot.Layout["first"].Height) < 0.001f);
            Assert.True(Math.Abs(70 - snapshot.Layout["second"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_ContentBoxPaddingMovesNextSibling()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 240, height: 240);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(realm, ("width", 200), ("height", 200), ("padding", 10))
            );
            var first = host.BenchmarkCreateHostNode(
                "View",
                "first",
                CreateStyleProps(
                    realm,
                    ("margin", 5),
                    ("padding", 60),
                    ("height", 30),
                    ("boxSizing", "content-box")
                )
            );
            var second = host.BenchmarkCreateHostNode(
                "View",
                "second",
                CreateStyleProps(realm, ("height", 50))
            );
            host.BenchmarkAppendChild(root, first);
            host.BenchmarkAppendChild(root, second);

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(15 - snapshot.Layout["first"].AbsTop) < 0.001f);
            Assert.True(Math.Abs(120 - snapshot.Layout["first"].Height) < 0.001f);
            Assert.True(Math.Abs(140 - snapshot.Layout["second"].AbsTop) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResetAfterCommit_BorderBoxLeafPaddingContributesToAutoSize()
    {
        var tempDirectory = CreateTempEntryDirectory(out var entryPath);

        try
        {
            using var host = new OkojoNodeReactHost(
                entryPath,
                debugEnabled: false,
                backendServices: DummyRuntimeBackendServices.Create()
            );
            host.InitializeBenchmarkRuntime(width: 200, height: 200);

            var realm = host.BenchmarkRealm;
            var root = host.BenchmarkCreateHostNode(
                "View",
                "root",
                CreateStyleProps(realm, ("padding", 12))
            );

            var rootChildren = new JsArray(realm);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(root));

            host.BenchmarkResetAfterCommit(rootChildren);

            var snapshot = host.BenchmarkSnapshot();
            Assert.True(Math.Abs(24 - snapshot.Layout["root"].Width) < 0.001f);
            Assert.True(Math.Abs(24 - snapshot.Layout["root"].Height) < 0.001f);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempEntryDirectory(out string entryPath)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        entryPath = Path.Combine(tempDirectory, "react-entry.mjs");
        File.WriteAllText(entryPath, "export {};");
        return tempDirectory;
    }

    private static JsPlainObject CreateStyleProps(
        JsRealm realm,
        params (string Name, object Value)[] styleEntries
    )
    {
        var style = new JsPlainObject(realm);
        foreach (var (name, value) in styleEntries)
        {
            style.SetProperty(
                name,
                value switch
                {
                    string text => JsValue.FromString(text),
                    int number => new JsValue(number),
                    double number => new JsValue(number),
                    float number => new JsValue(number),
                    _ => throw new InvalidOperationException(
                        $"Unsupported style value for '{name}'."
                    ),
                }
            );
        }

        var props = new JsPlainObject(realm);
        props.SetProperty("style", JsValue.FromObject(style));
        return props;
    }
}
