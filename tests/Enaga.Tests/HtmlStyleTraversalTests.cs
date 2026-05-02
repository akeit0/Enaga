using Enaga.Html;
using Enaga.Html.Dom;
using Enaga.Layout;
using Enaga.Rendering;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlStyleTraversalTests
{
    [Fact]
    public void Resolve_builds_dom_keyed_computed_styles_before_formatting()
    {
        var parsed = Parse(
            """
            <body>
              <div id="card" class="card"><span id="label">Hello</span></div>
            </body>
            """,
            """
            body { color: #112233; }
            .card { width: 120px; background: #445566; }
            .card > span { color: #abcdef; }
            """);
        var traversal = new HtmlStyleTraversal(
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()),
            LayoutEngineConfig.WebDefaults);

        var tree = traversal.Resolve(parsed, 320, 180);
        var card = FindElement(parsed.RootElement, "card");
        var label = FindElement(parsed.RootElement, "label");

        Assert.True(tree.Styles.ContainsKey(parsed.RootElement.NodeId));
        Assert.Equal(120, tree.Styles[card.NodeId].Width);
        Assert.Equal("#445566", tree.Styles[card.NodeId].BackgroundColor);
        Assert.Equal("#abcdef", tree.Styles[label.NodeId].Color);
    }

    [Fact]
    public void Resolve_accepts_pseudo_state_predicates()
    {
        var parsed = Parse(
            "<body><div id='cta'>Hover</div></body>",
            "div { background: #112233; } div:hover { background: #445566; }");
        var traversal = new HtmlStyleTraversal(
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()),
            LayoutEngineConfig.WebDefaults);

        var tree = traversal.Resolve(
            parsed,
            320,
            180,
            isHovered: static element => string.Equals(element.Id, "cta", StringComparison.Ordinal));
        var button = FindElement(parsed.RootElement, "cta");

        Assert.Equal("#445566", tree.Styles[button.NodeId].BackgroundColor);
    }

    [Fact]
    public void Scene_builder_exposes_initial_computed_style_tree()
    {
        var parser = new Enaga.Html.HtmlDocumentParser();
        var parsed = parser.Parse(new Enaga.Html.HtmlDocument(
            "<body><main id='app'>Hello</main></body>",
            "main { width: 200px; }"));
        var builder = new HtmlDocumentSceneBuilder(
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        builder.Build(parsed, 320, 180, viewportScale: 1);
        var app = FindElement(parsed.RootElement, "app");

        Assert.NotNull(builder.LastComputedStyleTree);
        Assert.True(builder.LastComputedStyleTree.Styles.ContainsKey(app.NodeId));
        Assert.Equal(200, builder.LastComputedStyleTree.Styles[app.NodeId].Width);
    }

    [Fact]
    public void Resolve_applies_descendant_styles_from_hovered_table_row()
    {
        var parsed = Parse(
            """
            <body>
              <table class="iana-table">
                <tbody>
                  <tr id="row"><td id="file">root-anchors.xml</td><td id="description">Updated 2024-11-05</td></tr>
                </tbody>
              </table>
            </body>
            """,
            ".iana-table td { background: #fafafc; } .iana-table tr:hover td { background: #f0f0f8; }");
        var traversal = new HtmlStyleTraversal(
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()),
            LayoutEngineConfig.WebDefaults);
        var row = FindElement(parsed.RootElement, "row");
        var file = FindElement(parsed.RootElement, "file");
        var description = FindElement(parsed.RootElement, "description");

        var tree = traversal.Resolve(
            parsed,
            520,
            120,
            isHovered: element => element.NodeId == row.NodeId);

        Assert.Equal("#f0f0f8", tree.Styles[file.NodeId].BackgroundColor);
        Assert.Equal("#f0f0f8", tree.Styles[description.NodeId].BackgroundColor);
    }

    [Fact]
    public void Resolve_treats_hovered_descendants_as_hovered_ancestors()
    {
        var parsed = Parse(
            """
            <body>
              <table class="iana-table">
                <tbody>
                  <tr id="row"><td id="file">root-anchors.xml</td><td id="description"><div id="note">Updated 2024-11-05</div></td></tr>
                </tbody>
              </table>
            </body>
            """,
            ".iana-table td { background: #fafafc; } .iana-table tr:hover td { background: #f0f0f8; }");
        var traversal = new HtmlStyleTraversal(
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()),
            LayoutEngineConfig.WebDefaults);
        var note = FindElement(parsed.RootElement, "note");
        var file = FindElement(parsed.RootElement, "file");
        var description = FindElement(parsed.RootElement, "description");

        var tree = traversal.Resolve(
            parsed,
            520,
            120,
            isHovered: element => element.NodeId == note.NodeId);

        Assert.Equal("#f0f0f8", tree.Styles[file.NodeId].BackgroundColor);
        Assert.Equal("#f0f0f8", tree.Styles[description.NodeId].BackgroundColor);
    }

    private static HtmlParsedDocument Parse(string html, string css)
    {
        var parser = new Enaga.Html.HtmlDocumentParser();
        return parser.Parse(new Enaga.Html.HtmlDocument(html, css));
    }

    private static HtmlDomElement FindElement(HtmlDomElement root, string id)
    {
        if (string.Equals(root.Id, id, StringComparison.Ordinal))
            return root;

        foreach (var child in root.Children)
        {
            if (child is HtmlDomElement childElement)
            {
                try
                {
                    return FindElement(childElement, id);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        throw new InvalidOperationException($"Element not found: {id}");
    }
}
