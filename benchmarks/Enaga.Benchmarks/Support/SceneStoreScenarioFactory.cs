using Enaga.Scene;

namespace Enaga.Benchmarks.Support;

internal static class SceneStoreScenarioFactory
{
    public static BenchmarkSceneStoreState Create(int nodeCount, BenchmarkTreeShape treeShape)
    {
        return treeShape switch
        {
            BenchmarkTreeShape.DeepNested => CreateDeepNested(nodeCount),
            _ => CreateWideContainers(nodeCount)
        };
    }

    private static BenchmarkSceneStoreState CreateWideContainers(int nodeCount)
    {
        var idAllocator = new SceneNodeIdAllocator();
        var rootId = idAllocator.Allocate();
        var store = new SceneStore(rootId, new SceneViewport(1280, 800));
        var rootChildren = new List<SceneNodeId>();
        var mutableNodeIds = new List<SceneNodeId>();
        var layoutVariantA = new List<SceneLayoutBox>();
        var layoutVariantB = new List<SceneLayoutBox>();

        var containerCount = Math.Max(1, nodeCount / 16);
        var createdNodes = 0;
        for (var containerIndex = 0; containerIndex < containerCount && createdNodes < nodeCount; containerIndex++)
        {
            var containerId = idAllocator.Allocate();
            var containerLabel = $"container-{containerIndex}";
            rootChildren.Add(containerId);
            store.UpsertNode(
                containerId,
                SceneNodeKind.View,
                rootId,
                containerLabel,
                new SceneLayoutBox(
                    SceneNodeKind.View,
                    AbsLeft: 12 + (containerIndex % 4) * 300,
                    AbsTop: 12 + containerIndex * 72,
                    Width: 280,
                    Height: 60,
                    PaddingLeft: 8,
                    PaddingTop: 6,
                    PaddingRight: 8,
                    PaddingBottom: 6,
                    BackgroundColor: containerIndex % 2 == 0 ? "#1b2533" : "#223044"));
            createdNodes++;

            var childIds = new List<SceneNodeId>();
            for (var leafIndex = 0; leafIndex < 15 && createdNodes < nodeCount; leafIndex++)
            {
                var nodeId = idAllocator.Allocate();
                var nodeLabel = $"node-{containerIndex}-{leafIndex}";
                childIds.Add(nodeId);
                var kind = ResolveKind(leafIndex);
                var top = 18 + leafIndex * 26;
                var width = 180 + (leafIndex % 4) * 18;
                var height = kind == SceneNodeKind.Text ? 22 : kind == SceneNodeKind.ScrollView ? 96 : 28;
                var box = new SceneLayoutBox(
                    kind,
                    AbsLeft: 24 + (containerIndex % 4) * 300,
                    AbsTop: top + containerIndex * 72,
                    Width: width,
                    Height: height,
                    TextContent: kind == SceneNodeKind.Text ? $"Item {containerIndex}-{leafIndex}" : null,
                    TextStyle: kind == SceneNodeKind.Text ? new SceneTextStyle(16, WrapText: leafIndex % 3 == 0) : null,
                    IsScrollContainer: kind == SceneNodeKind.ScrollView,
                    ContentHeight: kind == SceneNodeKind.ScrollView ? height + 80 : 0,
                    PaddingTop: kind == SceneNodeKind.ScrollView ? 6 : 0,
                    PaddingBottom: kind == SceneNodeKind.ScrollView ? 6 : 0);

                store.UpsertNode(nodeId, kind, containerId, nodeLabel, box);
                mutableNodeIds.Add(nodeId);
                layoutVariantA.Add(box);
                layoutVariantB.Add(box with
                {
                    Width = box.Width + 7,
                    Height = box.Height + (kind == SceneNodeKind.Text ? 4 : 2),
                    ContentHeight = kind == SceneNodeKind.ScrollView ? box.ContentHeight + 24 : box.ContentHeight
                });
                createdNodes++;
            }

            store.SetChildren(containerId, childIds);
        }

        store.SetChildren(rootId, rootChildren);
        return new BenchmarkSceneStoreState(
            store,
            [.. mutableNodeIds],
            [.. layoutVariantA],
            [.. layoutVariantB]);
    }

    private static BenchmarkSceneStoreState CreateDeepNested(int nodeCount)
    {
        var idAllocator = new SceneNodeIdAllocator();
        var rootId = idAllocator.Allocate();
        var store = new SceneStore(rootId, new SceneViewport(1280, 800));
        var mutableNodeIds = new List<SceneNodeId>(nodeCount);
        var layoutVariantA = new List<SceneLayoutBox>(nodeCount);
        var layoutVariantB = new List<SceneLayoutBox>(nodeCount);

        var parentId = rootId;
        for (var index = 0; index < nodeCount; index++)
        {
            var id = idAllocator.Allocate();
            var label = $"chain-{index}";
            var kind = ResolveKind(index);
            var box = new SceneLayoutBox(
                kind,
                AbsLeft: 12 + index * 2,
                AbsTop: 12 + index * 10,
                Width: Math.Max(72, 320 - index % 9 * 12),
                Height: kind == SceneNodeKind.Text ? 22 : kind == SceneNodeKind.ScrollView ? 92 : 28,
                TextContent: kind == SceneNodeKind.Text ? $"Nested item {index}" : null,
                TextStyle: kind == SceneNodeKind.Text ? new SceneTextStyle(16, WrapText: index % 4 == 0) : null,
                IsScrollContainer: kind == SceneNodeKind.ScrollView,
                ContentHeight: kind == SceneNodeKind.ScrollView ? 148 : 0,
                PaddingTop: kind == SceneNodeKind.ScrollView ? 6 : 0,
                PaddingBottom: kind == SceneNodeKind.ScrollView ? 6 : 0);

            store.UpsertNode(id, kind, parentId, label, box);
            store.SetChildren(parentId, [id]);

            mutableNodeIds.Add(id);
            layoutVariantA.Add(box);
            layoutVariantB.Add(box with
            {
                Width = box.Width + 5,
                Height = box.Height + (kind == SceneNodeKind.Text ? 3 : 2),
                ContentHeight = kind == SceneNodeKind.ScrollView ? box.ContentHeight + 18 : box.ContentHeight
            });

            parentId = id;
        }

        return new BenchmarkSceneStoreState(
            store,
            [.. mutableNodeIds],
            [.. layoutVariantA],
            [.. layoutVariantB]);
    }

    private static SceneNodeKind ResolveKind(int index)
    {
        return (index % 6) switch
        {
            0 => SceneNodeKind.Text,
            1 => SceneNodeKind.View,
            2 => SceneNodeKind.Image,
            3 => SceneNodeKind.TextInput,
            4 => SceneNodeKind.ScrollView,
            _ => SceneNodeKind.View
        };
    }
}
