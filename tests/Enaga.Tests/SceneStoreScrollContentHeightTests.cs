using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneStoreScrollContentHeightTests
{
    [Fact]
    public void Snapshot_InfersScrollContentWidthFromDescendantBoundsAndPadding()
    {
        var store = new SceneStore("root", new SceneViewport(1280, 800));
        store.UpsertNode(
            "scroll",
            SceneNodeKind.ScrollView,
            "root",
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
            "label",
            SceneNodeKind.Text,
            "scroll",
            "label",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                36,
                56,
                260,
                30,
                TextContent: "Wide note"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue("scroll", out var scrollBox));
        Assert.True(Math.Abs(296 - scrollBox.ContentWidth) < 0.001f);
    }

    [Fact]
    public void Snapshot_DoesNotInferScrollContentWidthWhenHorizontalScrollIsDisabled()
    {
        var store = new SceneStore("root", new SceneViewport(1280, 800));
        store.UpsertNode(
            "scroll",
            SceneNodeKind.ScrollView,
            "root",
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
            "label",
            SceneNodeKind.Text,
            "scroll",
            "label",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                36,
                56,
                260,
                30,
                TextContent: "Wide note"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue("scroll", out var scrollBox));
        Assert.True(Math.Abs(200 - scrollBox.ContentWidth) < 0.001f);
    }

    [Fact]
    public void Snapshot_InfersScrollContentHeightFromDescendantBoundsAndPadding()
    {
        var store = new SceneStore("root", new SceneViewport(1280, 800));
        store.UpsertNode(
            "scroll",
            SceneNodeKind.ScrollView,
            "root",
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
            "container",
            SceneNodeKind.View,
            "scroll",
            "container",
            new SceneLayoutBox(
                SceneNodeKind.View,
                36,
                56,
                160,
                0));
        store.UpsertNode(
            "label",
            SceneNodeKind.Text,
            "container",
            "label",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                36,
                134,
                120,
                30,
                TextContent: "Note"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue("scroll", out var scrollBox));
        Assert.True(Math.Abs(144 - scrollBox.ContentHeight) < 0.001f);
    }

    [Fact]
    public void Snapshot_DoesNotExpandOuterScrollFromNestedScrollDescendants()
    {
        var store = new SceneStore("root", new SceneViewport(1280, 800));
        store.UpsertNode(
            "outer-scroll",
            SceneNodeKind.ScrollView,
            "root",
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
            "inner-scroll",
            SceneNodeKind.ScrollView,
            "outer-scroll",
            "inner-scroll",
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                28,
                70,
                180,
                40,
                ContentHeight: 200));
        store.UpsertNode(
            "inner-content",
            SceneNodeKind.Text,
            "inner-scroll",
            "inner-content",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                42,
                190,
                120,
                24,
                TextContent: "Deep child"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue("outer-scroll", out var outerScrollBox));
        Assert.True(Math.Abs(82 - outerScrollBox.ContentHeight) < 0.001f);
    }

    [Fact]
    public void Snapshot_PrefersExplicitScrollContentHeightOverDescendantInference()
    {
        var store = new SceneStore("root", new SceneViewport(1280, 800));
        store.UpsertNode(
            "scroll",
            SceneNodeKind.ScrollView,
            "root",
            "scroll",
            new SceneLayoutBox(
                SceneNodeKind.ScrollView,
                20,
                40,
                220,
                120,
                ContentHeight: 180));
        store.UpsertNode(
            "container",
            SceneNodeKind.View,
            "scroll",
            "container",
            new SceneLayoutBox(
                SceneNodeKind.View,
                20,
                40,
                220,
                180));
        store.UpsertNode(
            "overflowing-child",
            SceneNodeKind.Text,
            "container",
            "overflowing-child",
            new SceneLayoutBox(
                SceneNodeKind.Text,
                20,
                320,
                120,
                24,
                TextContent: "Should not expand explicit content height"));

        var commit = store.Snapshot();

        Assert.True(commit.Layout.TryGetValue("scroll", out var scrollBox));
        Assert.True(Math.Abs(180 - scrollBox.ContentHeight) < 0.001f);
    }
}
