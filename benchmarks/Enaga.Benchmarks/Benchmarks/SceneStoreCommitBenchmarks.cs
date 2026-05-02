using BenchmarkDotNet.Attributes;
using Enaga.Benchmarks.Support;
using Enaga.Scene;

namespace Enaga.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class SceneStoreCommitBenchmarks
{
    private BenchmarkSceneStoreState state = null!;
    private int cursor;

    [Params(256, 2048)]
    public int NodeCount { get; set; }

    [Params(BenchmarkTreeShape.WideContainers, BenchmarkTreeShape.DeepNested)]
    public BenchmarkTreeShape TreeShape { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        state = SceneStoreScenarioFactory.Create(NodeCount, TreeShape);
        _ = state.Store.Snapshot();
        cursor = 0;
    }

    [Benchmark]
    [BenchmarkCategory("commit")]
    public SceneLayoutCommit SnapshotAfterSingleLayoutUpdate()
    {
        var index = cursor % state.MutableNodeIds.Length;
        var useSecondVariant = (cursor & 1) != 0;
        state.Store.SetLayout(
            state.MutableNodeIds[index],
            useSecondVariant ? state.LayoutVariantB[index] : state.LayoutVariantA[index]);
        cursor++;
        return state.Store.Snapshot();
    }
}
