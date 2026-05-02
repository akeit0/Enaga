using Enaga.Scene;

namespace Enaga.Benchmarks.Support;

internal sealed record BenchmarkSceneStoreState(
    SceneStore Store,
    string[] MutableNodeIds,
    SceneLayoutBox[] LayoutVariantA,
    SceneLayoutBox[] LayoutVariantB);
