using Enaga.Rendering;
using Enaga.Scene;
using Xunit;

namespace Enaga.Html.Tests;

public sealed class CssSupportTests
{
    [Fact]
    public void RenderFrame_AppliesFlexOrderProperty()
    {
        var source = new HtmlSceneFrameSource(
            new HtmlDocument(
                """
                <body>
                  <div id="row">
                    <div id="a"></div>
                    <div id="b"></div>
                    <div id="c"></div>
                  </div>
                </body>
                """,
                """
                body { padding: 0; }
                #row { display: flex; flex-direction: row; width: 180px; }
                #row > div { width: 40px; height: 20px; }
                #a { order: 2; }
                #b { order: -1; }
                #c { order: 1; }
                """
            ),
            new HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create())
        );

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var a = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "a").Key];
        var b = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "b").Key];
        var c = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "c").Key];

        Assert.True(b.AbsLeft < c.AbsLeft, $"b={b.AbsLeft} c={c.AbsLeft}");
        Assert.True(c.AbsLeft < a.AbsLeft, $"c={c.AbsLeft} a={a.AbsLeft}");
    }

    [Fact]
    public void RenderFrame_AppliesAspectRatioToNonReplacedElements()
    {
        var source = new HtmlSceneFrameSource(
            new HtmlDocument(
                "<body><div id='card'></div></body>",
                "body { padding: 0; } #card { width: 120px; aspect-ratio: 2 / 1; background: #123456; }"
            ),
            new HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create())
        );

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var card = frame.Commit.Layout[
            frame.Commit.Nodes.Single(pair => pair.Value.Label == "card").Key
        ];

        Assert.Equal(120, card.Width);
        Assert.Equal(60, card.Height);
    }

    [Fact]
    public void RenderFrame_AppliesSpaceEvenlyAndPlaceAlignmentAliases()
    {
        var source = new HtmlSceneFrameSource(
            new HtmlDocument(
                """
                <body>
                  <div id="row">
                    <div id="first"></div>
                    <div id="second"></div>
                  </div>
                </body>
                """,
                """
                body { padding: 0; }
                #row { display: flex; flex-direction: row; width: 200px; height: 100px; justify-content: space-evenly; place-items: center; }
                #first, #second { width: 20px; height: 20px; }
                #second { place-self: end; }
                """
            ),
            new HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create())
        );

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var row = frame.Commit.Layout[
            frame.Commit.Nodes.Single(pair => pair.Value.Label == "row").Key
        ];
        var first = frame.Commit.Layout[
            frame.Commit.Nodes.Single(pair => pair.Value.Label == "first").Key
        ];
        var second = frame.Commit.Layout[
            frame.Commit.Nodes.Single(pair => pair.Value.Label == "second").Key
        ];

        Assert.Equal(row.AbsLeft + 160f / 3f, first.AbsLeft, precision: 0);
        Assert.Equal(row.AbsTop + 40, first.AbsTop, precision: 0);
        Assert.Equal(row.AbsTop + 80, second.AbsTop, precision: 0);
    }

    [Fact]
    public void RenderFrame_AppliesAttributeSelectors()
    {
        var source = new HtmlSceneFrameSource(
            new HtmlDocument(
                """
                <body>
                  <input id="email" type="email" data-role="primary login" />
                  <input id="search" type="search" data-role="secondary" />
                  <span id="download" data-file="REPORT.PDF">report</span>
                </body>
                """,
                """
                input[type="email"] { border-color: #112233; }
                [data-role~="login"] { background-color: #445566; }
                [data-file$=".pdf" i] { color: #778899; }
                """
            ),
            new HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create())
        );

        var frame = source.RenderFrame(360, 180, TimeSpan.Zero);
        var email = frame.Commit.Layout[
            frame.Commit.Nodes.Single(pair => pair.Value.Label == "email").Key
        ];
        var search = frame.Commit.Layout[
            frame.Commit.Nodes.Single(pair => pair.Value.Label == "search").Key
        ];
        var downloadText = frame.Commit.Layout.Values.Single(box => box.TextContent == "report");

        Assert.Equal("#112233", email.BorderColor);
        Assert.Equal("#445566", email.BackgroundColor);
        Assert.NotEqual("#112233", search.BorderColor);
        Assert.Equal("#778899", downloadText.TextStyle?.Color);
    }

    [Fact]
    public void RenderFrame_FlattensDisplayContents()
    {
        var source = new HtmlSceneFrameSource(
            new HtmlDocument(
                """
                <body>
                  <div id="row">
                    <span id="wrapper"><span id="child">A</span></span>
                    <span id="sibling">B</span>
                  </div>
                </body>
                """,
                """
                body { padding: 0; }
                #row { display: flex; flex-direction: row; gap: 8px; }
                #wrapper { display: contents; }
                #child, #sibling { display: block; width: 20px; height: 20px; }
                """
            ),
            new HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create())
        );

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);

        Assert.DoesNotContain(frame.Commit.Nodes, pair => pair.Value.Label == "wrapper");
        Assert.Contains(frame.Commit.Nodes, pair => pair.Value.Label == "child");
        Assert.Contains(frame.Commit.Nodes, pair => pair.Value.Label == "sibling");
    }
}
