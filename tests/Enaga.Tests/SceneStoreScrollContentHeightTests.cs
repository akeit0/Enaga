using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneStoreScrollContentHeightTests
{
    private static readonly SceneNodeId Root = new(1);
    private static readonly SceneNodeId Scroll = new(2);
    private static readonly SceneNodeId Label = new(3);
    private static readonly SceneNodeId Container = new(4);
    private static readonly SceneNodeId OuterScroll = new(5);
    private static readonly SceneNodeId InnerScroll = new(6);
    private static readonly SceneNodeId InnerContent = new(7);
    private static readonly SceneNodeId OverflowingChild = new(8);

    [Fact]
    public void Snapshot_InfersScrollContentWidthFromDescendantBoundsAndPadding()
    {
        var store = new SceneStore(Root, new SceneViewport(1280, 800));
        store.UpsertNode(
            Scroll,
            SceneNodeKind.ScrollView,
            Root,
            "scroll",
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                20,
                40,
                200,
                100,
                PaddingLeft: 16,
                PaddingRight: 20,
                HorizontalScrollEnabled: true));
        store.UpsertNode(
            Label,
            SceneNodeKind.Text,
            Scroll,
            "label",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                36,
                56,
                260,
                30,
                TextContent: "Wide note"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue(Scroll, out var scrollBox));
        Assert.True(Math.Abs(296 - scrollBox.ContentWidth) < 0.001f);
    }

    [Fact]
    public void Snapshot_DoesNotInferScrollContentWidthWhenHorizontalScrollIsDisabled()
    {
        var store = new SceneStore(Root, new SceneViewport(1280, 800));
        store.UpsertNode(
            Scroll,
            SceneNodeKind.ScrollView,
            Root,
            "scroll",
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                20,
                40,
                200,
                100,
                PaddingLeft: 16,
                PaddingRight: 20));
        store.UpsertNode(
            Label,
            SceneNodeKind.Text,
            Scroll,
            "label",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                36,
                56,
                260,
                30,
                TextContent: "Wide note"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue(Scroll, out var scrollBox));
        Assert.True(Math.Abs(200 - scrollBox.ContentWidth) < 0.001f);
    }

    [Fact]
    public void Snapshot_InfersScrollContentHeightFromDescendantBoundsAndPadding()
    {
        var store = new SceneStore(Root, new SceneViewport(1280, 800));
        store.UpsertNode(
            Scroll,
            SceneNodeKind.ScrollView,
            Root,
            "scroll",
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                20,
                40,
                200,
                100,
                PaddingTop: 16,
                PaddingBottom: 20));
        store.UpsertNode(
            Container,
            SceneNodeKind.View,
            Scroll,
            "container",
            new SceneLayoutBox(
                SceneNodeKind.View,
                36,
                56,
                160,
                0));
        store.UpsertNode(
            Label,
            SceneNodeKind.Text,
            Container,
            "label",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                36,
                134,
                120,
                30,
                TextContent: "Note"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue(Scroll, out var scrollBox));
        Assert.True(Math.Abs(144 - scrollBox.ContentHeight) < 0.001f);
    }

    [Fact]
    public void Snapshot_DoesNotExpandOuterScrollFromNestedScrollDescendants()
    {
        var store = new SceneStore(Root, new SceneViewport(1280, 800));
        store.UpsertNode(
            OuterScroll,
            SceneNodeKind.ScrollView,
            Root,
            "outer-scroll",
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                20,
                40,
                220,
                60,
                PaddingTop: 10,
                PaddingBottom: 12));
        store.UpsertNode(
            InnerScroll,
            SceneNodeKind.ScrollView,
            OuterScroll,
            "inner-scroll",
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                28,
                70,
                180,
                40,
                ContentHeight: 200));
        store.UpsertNode(
            InnerContent,
            SceneNodeKind.Text,
            InnerScroll,
            "inner-content",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                42,
                190,
                120,
                24,
                TextContent: "Deep child"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue(OuterScroll, out var outerScrollBox));
        Assert.True(Math.Abs(82 - outerScrollBox.ContentHeight) < 0.001f);
    }

    [Fact]
    public void Snapshot_PrefersExplicitScrollContentHeightOverDescendantInference()
    {
        var store = new SceneStore(Root, new SceneViewport(1280, 800));
        store.UpsertNode(
            Scroll,
            SceneNodeKind.ScrollView,
            Root,
            "scroll",
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                20,
                40,
                220,
                120,
                ContentHeight: 180));
        store.UpsertNode(
            Container,
            SceneNodeKind.View,
            Scroll,
            "container",
            new SceneLayoutBox(
                SceneNodeKind.View,
                20,
                40,
                220,
                180));
        store.UpsertNode(
            OverflowingChild,
            SceneNodeKind.Text,
            Container,
            "overflowing-child",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                20,
                320,
                120,
                24,
                TextContent: "Should not expand explicit content height"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue(Scroll, out var scrollBox));
        Assert.True(Math.Abs(180 - scrollBox.ContentHeight) < 0.001f);
    }
}
