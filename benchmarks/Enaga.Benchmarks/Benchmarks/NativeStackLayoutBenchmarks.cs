using BenchmarkDotNet.Attributes;
using Enaga.Benchmarks.Support;
using Enaga.Layout;
using Enaga.Rendering;

namespace Enaga.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class NativeStackLayoutBenchmarks
{
    private static readonly DeterministicRuntimeTextServices TextServices = new();
    private readonly LayoutCalculator calculator = new(TextServices);
    private LayoutChildRequest[] children = [];
    private LayoutFrameData?[] frames = [];

    [Params(128, 1024)]
    public int ChildCount { get; set; }

    [Params(false, true)]
    public bool UseRowAxis { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        children = StackLayoutScenarioFactory.CreateChildren(ChildCount, ResolveAxis());
        frames = new LayoutFrameData?[children.Length];
    }

    [Benchmark]
    [BenchmarkCategory("layout")]
    public float CalculateFrames()
    {
        calculator.ComputeFlexLayout(
            LayoutInput.Definite(1280, 800),
            CreateStyle(),
            children,
            frames);

        var sum = 0f;
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames[index] is { } frame)
                sum += frame.Left + frame.Top + frame.Width + frame.Height;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("layout")]
    public float MeasureIntrinsic()
    {
        var measurement = calculator.ComputeFlexLayout(
            LayoutInput.Definite(1280, 800, LayoutRunMode.ComputeSize),
            CreateStyle(),
            children,
            []);

        return measurement.ContentSize.Width + measurement.ContentSize.Height;
    }

    private LayoutAxis ResolveAxis() => UseRowAxis ? LayoutAxis.Row : LayoutAxis.Column;

    private LayoutContainerStyle CreateStyle()
        => new(
            UseRowAxis ? FlexDirection.Row : FlexDirection.Column,
            LayoutDirection.Ltr,
            FlexWrap.NoWrap,
            RowGap: 8,
            ColumnGap: 8,
            CrossAlignment.Stretch,
            MainAxisJustification.Start,
            new LayoutBoxEdges(12, 10, 12, 10));
}
