using Enaga.Rendering;
using Enaga.Scene;
using Xunit;

namespace Enaga.Html.Tests;

public sealed class CssSelectorSupportTests
{
    [Fact]
    public void RenderFrame_KeepsAttributeSelectorValuesQuotedWhileScanning()
    {
        var source = new HtmlSceneFrameSource(
            new HtmlDocument(
                """
                <body>
                  <span id="target" data-token="alpha ] beta + gamma ~ delta">match</span>
                  <span id="other" data-token="alpha beta">miss</span>
                </body>
                """,
                """
                [data-token="alpha ] beta + gamma ~ delta"] { color: #123456; }
                span + span { color: #abcdef; }
                """
            ),
            new HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create())
        );

        var frame = source.RenderFrame(360, 180, TimeSpan.Zero);
        var target = frame.Commit.Layout.Values.Single(box => box.TextContent == "match");
        var other = frame.Commit.Layout.Values.Single(box => box.TextContent == "miss");

        Assert.Equal("#123456", target.TextStyle?.Color);
        Assert.NotEqual("#abcdef", other.TextStyle?.Color);
    }

    [Fact]
    public void RenderFrame_AppliesMultipleAttributeSelectorsOnCompoundSelector()
    {
        var source = new HtmlSceneFrameSource(
            new HtmlDocument(
                """
                <body>
                  <button id="target" data-kind="primary" aria-disabled="false">ok</button>
                  <button id="other" data-kind="primary" aria-disabled="true">skip</button>
                </body>
                """,
                """button[data-kind="primary"][aria-disabled="false"] { background-color: #334455; }"""
            ),
            new HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create())
        );

        var frame = source.RenderFrame(360, 180, TimeSpan.Zero);
        var target = frame.Commit.Layout[
            frame.Commit.Nodes.Single(pair => pair.Value.Label == "target").Key
        ];
        var other = frame.Commit.Layout[
            frame.Commit.Nodes.Single(pair => pair.Value.Label == "other").Key
        ];

        Assert.Equal("#334455", target.BackgroundColor);
        Assert.NotEqual("#334455", other.BackgroundColor);
    }
}
