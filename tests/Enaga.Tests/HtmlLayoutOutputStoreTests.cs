using Enaga.Html;
using Enaga.Html.Dom;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlLayoutOutputStoreTests
{
    [Fact]
    public void InvalidateNodes_SelfDirtyKeepsDescendantCache()
    {
        var store = CreateStore(out var parentKey, out var childKey, out var siblingKey);
        var dirty = new HtmlLayoutDirtySet();
        dirty.Add(HtmlLayoutVersion.ToLayoutNodeId(Id("parent")), HtmlLayoutDirtyBits.Self);

        store.InvalidateNodes(dirty);

        Assert.False(store.Outputs.TryGet(parentKey, out _));
        Assert.True(store.Outputs.TryGet(childKey, out _));
        Assert.True(store.Outputs.TryGet(siblingKey, out _));
    }

    [Fact]
    public void InvalidateNodes_SubtreeDirtyDropsDescendantCache()
    {
        var store = CreateStore(out var parentKey, out var childKey, out var siblingKey);
        var dirty = new HtmlLayoutDirtySet();
        dirty.Add(HtmlLayoutVersion.ToLayoutNodeId(Id("parent")), HtmlLayoutDirtyBits.Self | HtmlLayoutDirtyBits.Subtree);

        store.InvalidateNodes(dirty);

        Assert.False(store.Outputs.TryGet(parentKey, out _));
        Assert.False(store.Outputs.TryGet(childKey, out _));
        Assert.True(store.Outputs.TryGet(siblingKey, out _));
    }

    [Fact]
    public void InvalidateNodes_AncestorDirtyPropagatesToParent()
    {
        var store = CreateStore(out var parentKey, out var childKey, out var siblingKey);
        var dirty = new HtmlLayoutDirtySet();
        dirty.Add(HtmlLayoutVersion.ToLayoutNodeId(Id("child")), HtmlLayoutDirtyBits.Self | HtmlLayoutDirtyBits.Ancestors);

        store.InvalidateNodes(dirty);

        Assert.False(store.Outputs.TryGet(parentKey, out _));
        Assert.False(store.Outputs.TryGet(childKey, out _));
        Assert.True(store.Outputs.TryGet(siblingKey, out _));
    }

    [Fact]
    public void InvalidateNodes_ContainmentBoundaryStopsAncestorPropagation()
    {
        var store = new HtmlLayoutOutputStore();
        var normalStyle = Style();
        var containedStyle = Style("contain: size; width: 100px; height: 40px; overflow: hidden;");
        var child = Node("child", normalStyle, []);
        var boundary = Node("boundary", containedStyle, [child]);
        store.UpdateLayoutTree(Id("root"), [boundary]);
        var rootKey = Key("root");
        var boundaryKey = Key("boundary");
        var childKey = Key("child");
        store.Outputs.Store(rootKey, Output(200, 120));
        store.Outputs.Store(boundaryKey, Output(100, 40));
        store.Outputs.Store(childKey, Output(80, 20));
        var dirty = new HtmlLayoutDirtySet();
        dirty.Add(HtmlLayoutVersion.ToLayoutNodeId(Id("child")), HtmlLayoutDirtyBits.Self | HtmlLayoutDirtyBits.Ancestors);

        store.InvalidateNodes(dirty);

        Assert.True(store.Outputs.TryGet(rootKey, out _));
        Assert.False(store.Outputs.TryGet(boundaryKey, out _));
        Assert.False(store.Outputs.TryGet(childKey, out _));
    }

    [Fact]
    public void InvalidateNodes_ContainmentBoundarySelfDirtyCanPropagateToRoot()
    {
        var store = new HtmlLayoutOutputStore();
        var containedStyle = Style("contain: size; width: 100px; height: 40px; overflow: hidden;");
        var boundary = Node("boundary", containedStyle, []);
        store.UpdateLayoutTree(Id("root"), [boundary]);
        var rootKey = Key("root");
        var boundaryKey = Key("boundary");
        store.Outputs.Store(rootKey, Output(200, 120));
        store.Outputs.Store(boundaryKey, Output(100, 40));
        var dirty = new HtmlLayoutDirtySet();
        dirty.Add(HtmlLayoutVersion.ToLayoutNodeId(Id("boundary")), HtmlLayoutDirtyBits.Self | HtmlLayoutDirtyBits.Ancestors);

        store.InvalidateNodes(dirty);

        Assert.False(store.Outputs.TryGet(rootKey, out _));
        Assert.False(store.Outputs.TryGet(boundaryKey, out _));
    }

    [Fact]
    public void Build_ReusesUnchangedSceneLayoutBoxesAcrossEquivalentLayoutPasses()
    {
        var parser = new Enaga.Html.HtmlDocumentParser();
        var parsed = parser.Parse(new Enaga.Html.HtmlDocument(
            """
            <body>
              <table class="initial">
                <tbody>
                  <tr><td><a>A</a></td><td><a>B</a></td></tr>
                  <tr><td><a>C</a></td><td><a>D</a></td></tr>
                </tbody>
              </table>
            </body>
            """,
            """
            table.initial { width: 100%; }
            table.initial td { width: 50%; height: 30px; }
            table.initial td a { display: block; padding: 18px 8px; background: #eee; }
            """));
        var builder = new HtmlDocumentSceneBuilder(
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()),
            new SceneNodeIdAllocator());

        var first = builder.Build(parsed, 240, 200, viewportScale: 1);
        var second = builder.Build(parsed, 240, 200, viewportScale: 1);
        var firstTextId = first.Layout.Single(pair => pair.Value.TextContent == "A").Key;
        var firstCellId = FindAncestorId(first, firstTextId, "td-");

        Assert.NotSame(first, second);
        Assert.Same(first.Layout[firstTextId], second.Layout[firstTextId]);
        Assert.Same(first.Layout[firstCellId], second.Layout[firstCellId]);
    }

    private static HtmlLayoutOutputStore CreateStore(
        out LayoutCacheKey parentKey,
        out LayoutCacheKey childKey,
        out LayoutCacheKey siblingKey)
    {
        var store = new HtmlLayoutOutputStore();
        var style = HtmlComputedStyle.CreateDefault(new Enaga.Html.HtmlOptions(), LayoutEngineConfig.WebDefaults);
        var child = Node("child", style, []);
        var parent = Node("parent", style, [child]);
        var sibling = Node("sibling", style, []);
        store.UpdateLayoutTree(Id("root"), [parent, sibling]);

        parentKey = Key("parent");
        childKey = Key("child");
        siblingKey = Key("sibling");
        store.Outputs.Store(parentKey, Output(100, 40));
        store.Outputs.Store(childKey, Output(80, 20));
        store.Outputs.Store(siblingKey, Output(60, 20));
        return store;
    }

    private static HtmlSceneNode Node(string id, HtmlComputedStyle style, HtmlSceneNode[] children)
        => new(
            Id(id),
            SceneNodeKind.View,
            style,
            children,
            TextContent: null,
            PlaceholderText: null,
            ImageSource: null,
            LinkHref: null,
            Label: null);

    private static HtmlComputedStyle Style(string css = "")
    {
        var parser = new Enaga.Html.HtmlDocumentParser();
        var parsed = parser.Parse(new Enaga.Html.HtmlDocument("<body><div id='target'></div></body>", $"#target {{ {css} }}"));
        var traversal = new HtmlStyleTraversal(new Enaga.Html.HtmlOptions(), LayoutEngineConfig.WebDefaults);
        var styles = traversal.Resolve(parsed, 320, 180).Styles;
        return styles[FindElement(parsed.RootElement, "target").NodeId];
    }

    private static HtmlDomElement FindElement(HtmlDomElement root, string id)
    {
        if (string.Equals(root.Id, id, StringComparison.Ordinal))
            return root;

        foreach (var child in root.Children)
        {
            if (child is not HtmlDomElement childElement)
                continue;
            try
            {
                return FindElement(childElement, id);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException($"Element not found: {id}");
    }

    private static LayoutCacheKey Key(string id)
        => new(
            HtmlLayoutVersion.ToLayoutNodeId(Id(id)),
            StyleVersion: 1,
            LayoutVersion: 1,
            LayoutInput.Definite(100, 40),
            new LayoutContainerStyle());

    private static LayoutOutput Output(float width, float height)
        => new(
            new LayoutSize(width, height),
            new LayoutSize(width, height),
            new LayoutRect(0, 0, width, height));

    private static SceneNodeId FindAncestorId(SceneLayoutCommit commit, SceneNodeId startId, string prefix)
    {
        var currentId = startId;
        while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
        {
            if (commit.Layout.TryGetValue(parentId, out var box) && box.NodeKind == SceneNodeKind.View)
                return parentId;

            currentId = parentId;
        }

        throw new InvalidOperationException($"No ancestor with prefix {prefix} for {startId}.");
    }

    private static HtmlSceneNodeId Id(string id)
        => id switch
        {
            "root" => HtmlSceneNodeId.Root,
            "parent" => new HtmlSceneNodeId(2),
            "child" => new HtmlSceneNodeId(3),
            "sibling" => new HtmlSceneNodeId(4),
            "boundary" => new HtmlSceneNodeId(5),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };
}
