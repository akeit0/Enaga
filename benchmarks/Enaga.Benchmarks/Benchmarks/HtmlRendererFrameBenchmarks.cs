using BenchmarkDotNet.Attributes;
using Enaga.Html;
using Enaga.Benchmarks.Support;
using Enaga.Rendering;
using Enaga.Rendering.Skia;

namespace Enaga.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class HtmlFrameBenchmarks
{
    private Enaga.Html.HtmlDocument document = null!;
    private Enaga.Html.HtmlOptions options = null!;
    private HtmlSceneFrameSource source = null!;
    private int width;
    private int height;
    private int resizeStep;
    private TimeSpan elapsed;

    [Params(
        HtmlBenchmarkDocument.TextWrapStress)]
    public HtmlBenchmarkDocument Document { get; set; }

    [Params(false, true)]
    public bool UseSkiaText { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        document = HtmlBenchmarkFixtures.Create(Document);
        (width, height) = HtmlBenchmarkFixtures.GetViewport(Document);
        options = new Enaga.Html.HtmlOptions(
            BackendServices: CreateBackendServices(),
            DefaultFontFamily: null);
        source = new HtmlSceneFrameSource(document, options);
        source.RenderFrame(width, height, TimeSpan.Zero);
    }

    [IterationSetup(Target = nameof(ResizeRenderFrame))]
    public void ResetResizeState()
    {
        source = new HtmlSceneFrameSource(document, options);
        source.RenderFrame(width, height, TimeSpan.Zero);
        resizeStep = 0;
        elapsed = TimeSpan.Zero;
    }

    [IterationSetup(Target = nameof(CachedRenderFrame))]
    public void ResetCachedState()
    {
        source = new HtmlSceneFrameSource(document, options);
        source.RenderFrame(width, height, TimeSpan.Zero);
        elapsed = TimeSpan.Zero;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        options.BackendServices?.Dispose();
    }

    [Benchmark]
    [BenchmarkCategory("html")]
    public int ColdInitialRenderFrame()
    {
        var localSource = new HtmlSceneFrameSource(document, options);
        var frame = localSource.RenderFrame(width, height, TimeSpan.Zero);
        return Checksum(frame);
    }

    [Benchmark]
    [BenchmarkCategory("html")]
    public int ResizeRenderFrame()
    {
        resizeStep++;
        elapsed += TimeSpan.FromMilliseconds(16);
        var nextWidth = width + ((resizeStep & 1) == 0 ? 0 : -Math.Min(180, width / 4));
        var frame = source.RenderFrame(nextWidth, height, elapsed);
        return Checksum(frame);
    }

    [Benchmark]
    [BenchmarkCategory("html")]
    public int CachedRenderFrame()
    {
        elapsed += TimeSpan.FromMilliseconds(16);
        var frame = source.RenderFrame(width, height, elapsed);
        return Checksum(frame);
    }

    private RuntimeBackendServices CreateBackendServices()
    {
        return UseSkiaText
            ? SkiaRuntimeBackendServices.Create()
            : DummyRuntimeBackendServices.Create();
    }

    private static int Checksum(SceneFrameResult frame)
    {
        return HashCode.Combine(
            frame.Commit.Nodes.Count,
            frame.Commit.Layout.Count,
            frame.DirtyRects.Length,
            frame.DamageReasons,
            frame.Commit.Viewport.Width,
            frame.Commit.Viewport.Height);
    }
}
