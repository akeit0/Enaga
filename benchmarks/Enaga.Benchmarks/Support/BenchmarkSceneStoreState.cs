using Enaga.Scene;

namespace Enaga.Benchmarks.Support;

internal sealed record BenchmarkSceneStoreState(
    SceneStore Store,
    SceneNodeId[] MutableNodeIds,
    SceneLayoutBox[] LayoutVariantA,
    SceneLayoutBox[] LayoutVariantB);
