using BenchmarkDotNet.Attributes;
using Enaga.Benchmarks.Support;

namespace Enaga.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class HostFlushBenchmarks
{
    private HostFlushBenchmarkState state = null!;
    private int cursor;

    [Params(256, 512)]
    public int NodeCount { get; set; }

    [Params(BenchmarkTreeShape.WideContainers, BenchmarkTreeShape.DeepNested)]
    public BenchmarkTreeShape TreeShape { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        state = HostFlushScenarioFactory.Create(
            Path.Combine(AppContext.BaseDirectory, "benchmark-entry.mjs"),
            NodeCount,
            TreeShape);
        state.Host.BenchmarkMarkFullSceneFlush();
        state.Host.BenchmarkResetAfterCommit(state.RootChildren);
        cursor = 0;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        state.Dispose();
    }

    [Benchmark]
    [BenchmarkCategory("flush")]
    public int FullFlush()
    {
        state.Host.BenchmarkMarkFullSceneFlush();
        state.Host.BenchmarkResetAfterCommit(state.RootChildren);
        return state.Host.BenchmarkSnapshot().Layout.Count;
    }

    [Benchmark]
    [BenchmarkCategory("flush")]
    public int DirtyLayoutFlush()
    {
        var index = cursor % state.MutableNodes.Length;
        var useSecondVariant = (cursor & 1) != 0;
        state.Host.BenchmarkCommitHostUpdate(
            state.MutableNodes[index],
            useSecondVariant ? state.PropsVariantB[index] : state.PropsVariantA[index],
            layoutAffected: true);
        state.Host.BenchmarkResetAfterCommit(state.RootChildren);
        cursor++;
        return state.Host.BenchmarkSnapshot().Layout.Count;
    }
}
