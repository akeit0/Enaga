using Enaga.Html.Dom;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlDomDocumentTests
{
    [Fact]
    public void ToDomDocument_IndexesElementsForHostDomApi()
    {
        var parsed = new HtmlDocumentParser().Parse("""
            <body>
              <main id="app">
                <button id="run" class="primary action">Run</button>
              </main>
            </body>
            """, basePath: "site");

        var document = parsed.ToDomDocument();
        var button = document.GetElementById("run");

        Assert.NotNull(button);
        Assert.Equal("button", button.LocalName);
        Assert.Equal(button, document.QuerySelector("#run"));
        Assert.Equal(button, document.QuerySelector(".action"));
        Assert.Equal(button, document.GetElementsByTagName("button").Single());
        Assert.Equal(document.GetElementById("app")!.NodeId, document.GetParentNodeId(button.NodeId));
        Assert.Contains(button, document.EnumerateSelfAndAncestors(button.NodeId));
    }

    [Fact]
    public void Parse_ExposesExecutableInlineScriptsFromHtmlSide()
    {
        var parsed = new HtmlDocumentParser().Parse("""
            <head>
              <script>window.first = 1;</script>
              <script src="/app.js">ignored()</script>
              <script type="application/json">{"ignored":true}</script>
              <script type="text/javascript">window.second = 2;</script>
            </head>
            <body></body>
            """, basePath: null);

        Assert.Equal(["window.first = 1;", "window.second = 2;"], parsed.GetExecutableInlineScriptTexts());
        Assert.Equal(4, parsed.AuthorScripts.Count);
        Assert.True(parsed.AuthorScripts[1].HasSource);
    }

    [Fact]
    public void DomDocument_ExposesInnerTextAndCreateElement()
    {
        var parsed = new HtmlDocumentParser().Parse("""
            <body>
              <main id="app">Hello <span>world</span><script>ignored()</script><style>.x{}</style></main>
            </body>
            """, basePath: null);
        var document = parsed.ToDomDocument();

        Assert.Equal("Hello world", document.GetElementById("app")!.InnerText);

        var created = document.CreateElement("Button");

        Assert.Equal("button", created.LocalName);
        Assert.Equal(created, document.GetElementByNodeId(created.NodeId));
        Assert.Empty(created.InnerText);
    }

    [Fact]
    public void DomDocument_SetTextContentUpdatesTreeAndSerialization()
    {
        var parsed = new HtmlDocumentParser().Parse("""
            <body><main id="app"><span>old</span></main></body>
            """, basePath: null);
        var document = parsed.ToDomDocument();
        var app = document.GetElementById("app")!;

        var updated = document.SetTextContent(app.NodeId, "new <value>");

        Assert.NotNull(updated);
        Assert.Equal("new <value>", document.GetElementById("app")!.TextContent);
        Assert.Equal("new <value>", document.GetElementById("app")!.InnerText);
        Assert.Equal("<body><main id=\"app\">new &lt;value&gt;</main></body>", document.ToHtml());
    }

    [Fact]
    public void DomDocument_AppendChildAttachesCreatedElement()
    {
        var parsed = new HtmlDocumentParser().Parse("<body><main id=\"app\"></main></body>", basePath: null);
        var document = parsed.ToDomDocument();
        var app = document.GetElementById("app")!;
        var button = document.CreateElement("button");
        document.SetTextContent(button.NodeId, "Run");

        var updatedParent = document.AppendChild(app.NodeId, button.NodeId);

        Assert.NotNull(updatedParent);
        Assert.Equal("Run", document.GetElementById("app")!.TextContent);
        Assert.Equal("<body><main id=\"app\"><button>Run</button></main></body>", document.ToHtml());
    }

    [Fact]
    public void DomDocument_SetAndRemoveAttributeUpdatesIndexesAndSerialization()
    {
        var parsed = new HtmlDocumentParser().Parse("<body><main id=\"app\"></main></body>", basePath: null);
        var document = parsed.ToDomDocument();
        var app = document.GetElementById("app")!;

        var updated = document.SetAttribute(app.NodeId, "class", "active panel");
        updated = document.SetAttribute(updated!.NodeId, "id", "root");

        Assert.Null(document.GetElementById("app"));
        Assert.Equal(updated, document.GetElementById("root"));
        Assert.Equal(updated, document.QuerySelector(".panel"));
        Assert.Equal("<body><main id=\"root\" class=\"active panel\"></main></body>", document.ToHtml());

        updated = document.RemoveAttribute(updated!.NodeId, "class");

        Assert.NotNull(updated);
        Assert.Null(document.QuerySelector(".panel"));
        Assert.Equal("<body><main id=\"root\"></main></body>", document.ToHtml());
    }
}
