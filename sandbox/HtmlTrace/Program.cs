using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;
using Enaga.Html;
using Enaga.Rendering;
using Enaga.Rendering.Skia;

TraceConfig config;
try
{
    config = TraceConfig.Parse(args);
}
catch (HelpRequestedException)
{
    PrintUsage();
    return;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}

var fixture = config.HtmlPath is { Length: > 0 } htmlPath
    ? HtmlTraceFixtures.CreateFromFile(htmlPath)
    : HtmlTraceFixtures.Create(config.Document);
var width = config.Width ?? fixture.Width;
var height = config.Height ?? fixture.Height;

Console.WriteLine("HTML trace harness");
Console.WriteLine(FormattableString.Invariant($"pid: {Environment.ProcessId}"));
Console.WriteLine(FormattableString.Invariant($"case: {config.Document}"));
Console.WriteLine(FormattableString.Invariant($"mode: {config.Mode}"));
Console.WriteLine(FormattableString.Invariant($"viewport: {width}x{height}"));
Console.WriteLine(FormattableString.Invariant($"seconds: {config.Seconds}"));
Console.WriteLine(FormattableString.Invariant($"warmup seconds: {config.WarmupSeconds}"));
Console.WriteLine(
    FormattableString.Invariant($"text backend: {(config.UseSkiaText ? "skia" : "dummy")}")
);
if (config.HtmlPath is { Length: > 0 })
    Console.WriteLine(FormattableString.Invariant($"html: {config.HtmlPath}"));
Console.WriteLine();
Console.WriteLine("Example:");
Console.WriteLine(
    FormattableString.Invariant(
        $"  dotnet-trace collect -p {Environment.ProcessId} --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x1C000080018:5,Enaga-HtmlTrace"
    )
);
Console.WriteLine();

if (config.WaitForAttach)
{
    Console.WriteLine("Waiting. Start dotnet-trace, then press Enter.");
    Console.ReadLine();
}

using var backend = config.UseSkiaText
    ? SkiaRuntimeBackendServices.Create()
    : DummyRuntimeBackendServices.Create();
var options = new Enaga.Html.HtmlOptions(BackendServices: backend, DefaultFontFamily: null);

if (config.WarmupSeconds > 0)
{
    Console.WriteLine("Warmup...");
    RunScenario(
        HtmlTraceMode.Resize,
        fixture.Document,
        options,
        width,
        height,
        TimeSpan.FromSeconds(config.WarmupSeconds),
        config.ReportEvery,
        printProgress: false
    );
}

if (config.DumpLayout)
{
    var source = new HtmlSceneFrameSource(fixture.Document, options);
    DumpLayout(source.RenderFrame(width, height, TimeSpan.Zero));
    return;
}

Console.WriteLine("Trace loop started.");
var totalChecksum = 0;
if (config.Mode == HtmlTraceMode.All)
{
    totalChecksum ^= RunScenario(
        HtmlTraceMode.Cold,
        fixture.Document,
        options,
        width,
        height,
        TimeSpan.FromSeconds(config.Seconds),
        config.ReportEvery,
        printProgress: true
    );
    totalChecksum ^= RunScenario(
        HtmlTraceMode.Resize,
        fixture.Document,
        options,
        width,
        height,
        TimeSpan.FromSeconds(config.Seconds),
        config.ReportEvery,
        printProgress: true
    );
    totalChecksum ^= RunScenario(
        HtmlTraceMode.Cached,
        fixture.Document,
        options,
        width,
        height,
        TimeSpan.FromSeconds(config.Seconds),
        config.ReportEvery,
        printProgress: true
    );
}
else
{
    totalChecksum = RunScenario(
        config.Mode,
        fixture.Document,
        options,
        width,
        height,
        TimeSpan.FromSeconds(config.Seconds),
        config.ReportEvery,
        printProgress: true
    );
}

Console.WriteLine(FormattableString.Invariant($"done checksum={totalChecksum}"));

static int RunScenario(
    HtmlTraceMode mode,
    Enaga.Html.HtmlDocument document,
    Enaga.Html.HtmlOptions options,
    int width,
    int height,
    TimeSpan duration,
    int reportEvery,
    bool printProgress
)
{
    HtmlTraceEventSource.Log.ScenarioStart(mode.ToString(), width, height);
    var stopwatch = Stopwatch.StartNew();
    var frameElapsed = TimeSpan.Zero;
    var iterations = 0;
    var checksum = 0;
    var lastAllocated = GC.GetTotalAllocatedBytes(precise: false);
    var source = mode == HtmlTraceMode.Cold ? null : new HtmlSceneFrameSource(document, options);

    source?.RenderFrame(width, height, TimeSpan.Zero);

    while (stopwatch.Elapsed < duration)
    {
        frameElapsed += TimeSpan.FromMilliseconds(16);
        var frame = mode switch
        {
            HtmlTraceMode.Cold => new HtmlSceneFrameSource(document, options).RenderFrame(
                width,
                height,
                TimeSpan.Zero
            ),
            HtmlTraceMode.Resize => source!.RenderFrame(
                ResolveResizeWidth(width, iterations),
                height,
                frameElapsed
            ),
            HtmlTraceMode.Cached => source!.RenderFrame(width, height, frameElapsed),
            _ => throw new InvalidOperationException($"Unsupported scenario mode: {mode}."),
        };

        checksum ^= Checksum(frame);
        iterations++;

        if (iterations % reportEvery != 0)
            continue;

        var allocated = GC.GetTotalAllocatedBytes(precise: false);
        var allocatedDelta = allocated - lastAllocated;
        lastAllocated = allocated;
        HtmlTraceEventSource.Log.IterationBatch(
            mode.ToString(),
            iterations,
            stopwatch.ElapsedMilliseconds,
            allocatedDelta,
            checksum
        );
        if (printProgress)
        {
            Console.WriteLine(
                FormattableString.Invariant(
                    $"{mode, -6} iter={iterations, 8} elapsed={stopwatch.Elapsed.TotalSeconds, 6:0.0}s allocDelta={allocatedDelta / 1024.0 / 1024.0, 8:0.0} MiB checksum={checksum}"
                )
            );
        }
    }

    HtmlTraceEventSource.Log.ScenarioStop(
        mode.ToString(),
        iterations,
        stopwatch.ElapsedMilliseconds,
        checksum
    );
    return checksum;
}

static int ResolveResizeWidth(int width, int iteration)
{
    var span = Math.Min(240, Math.Max(48, width / 3));
    return width - (iteration % 4) * (span / 3);
}

static int Checksum(SceneFrameResult frame)
{
    return HashCode.Combine(
        frame.Commit.Nodes.Count,
        frame.Commit.Layout.Count,
        frame.DirtyRects.Length,
        frame.DamageReasons,
        frame.Commit.Viewport.Width,
        frame.Commit.Viewport.Height
    );
}

static void DumpLayout(SceneFrameResult frame)
{
    foreach (
        var pair in frame
            .Commit.Layout.OrderBy(pair => pair.Value.AbsTop)
            .ThenBy(pair => pair.Value.AbsLeft)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
    )
    {
        frame.Commit.Nodes.TryGetValue(pair.Key, out var node);
        var box = pair.Value;
        var text = box.TextContent is { Length: > 0 }
            ? box.TextContent.ReplaceLineEndings(" ")
            : string.Empty;
        if (text.Length > 48)
            text = text[..48];

        var textInfo = box.TextStyle is { } textStyle
            ? FormattableString.Invariant(
                $" font={textStyle.Font.Size:0.##}/{textStyle.Font.Weight} wrap={textStyle.WrapText}"
            )
            : string.Empty;
        Console.WriteLine(
            FormattableString.Invariant(
                $"{pair.Key, -24} {node?.NodeKind.ToString() ?? "?", -10} label={node?.Label ?? "", -16} x={box.AbsLeft, 7:0.##} y={box.AbsTop, 7:0.##} w={box.Width, 7:0.##} h={box.Height, 7:0.##}{textInfo} text='{text}'"
            )
        );
    }
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Usage:
          dnrelay run sandbox\HtmlTrace\HtmlTrace.csproj -c Release -- [options]

        Options:
          --case iana|legacy|stress
          --mode cold|resize|cached|all
          --seconds N
          --warmup N
          --dummy-text | --skia-text
          --width N --height N
          --html PATH
          --dump-layout
          --wait
          --report-every N
        """
    );
}

internal enum HtmlTraceDocument
{
    TextWrapStress,
}

internal enum HtmlTraceMode
{
    Cold,
    Resize,
    Cached,
    All,
}

internal sealed record TraceConfig(
    HtmlTraceDocument Document,
    HtmlTraceMode Mode,
    int Seconds,
    int WarmupSeconds,
    bool UseSkiaText,
    bool WaitForAttach,
    int ReportEvery,
    int? Width,
    int? Height,
    string? HtmlPath,
    bool DumpLayout
)
{
    public static TraceConfig Parse(string[] args)
    {
        var config = new TraceConfig(
            HtmlTraceDocument.TextWrapStress,
            HtmlTraceMode.Resize,
            Seconds: 30,
            WarmupSeconds: 3,
            UseSkiaText: true,
            WaitForAttach: false,
            ReportEvery: 100,
            Width: null,
            Height: null,
            HtmlPath: null,
            DumpLayout: false
        );

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            string NextValue()
            {
                if (index + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {arg}.");
                return args[++index];
            }

            config = arg switch
            {
                "--case" => config with { Document = ParseDocument(NextValue()) },
                "--mode" => config with { Mode = ParseMode(NextValue()) },
                "--seconds" => config with { Seconds = ParsePositiveInt(NextValue(), arg) },
                "--warmup" => config with { WarmupSeconds = ParseNonNegativeInt(NextValue(), arg) },
                "--dummy-text" => config with { UseSkiaText = false },
                "--skia-text" => config with { UseSkiaText = true },
                "--wait" => config with { WaitForAttach = true },
                "--report-every" => config with
                {
                    ReportEvery = ParsePositiveInt(NextValue(), arg),
                },
                "--width" => config with { Width = ParsePositiveInt(NextValue(), arg) },
                "--height" => config with { Height = ParsePositiveInt(NextValue(), arg) },
                "--html" => config with { HtmlPath = NextValue() },
                "--dump-layout" => config with { DumpLayout = true, WarmupSeconds = 0 },
                "--help" or "-h" => throw new HelpRequestedException(),
                _ => throw new ArgumentException($"Unknown argument: {arg}."),
            };
        }

        return config;
    }

    private static HtmlTraceDocument ParseDocument(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "stress" or "text" or "text-wrap" => HtmlTraceDocument.TextWrapStress,
            _ => throw new ArgumentException(
                $"Unknown case: {value}. Expected stress, text, or text-wrap."
            ),
        };
    }

    private static HtmlTraceMode ParseMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "cold" => HtmlTraceMode.Cold,
            "resize" => HtmlTraceMode.Resize,
            "cached" => HtmlTraceMode.Cached,
            "all" => HtmlTraceMode.All,
            _ => throw new ArgumentException(
                $"Unknown mode: {value}. Expected cold, resize, cached, or all."
            ),
        };
    }

    private static int ParsePositiveInt(string value, string option)
    {
        if (
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0
        )
            return parsed;

        throw new ArgumentException($"{option} requires a positive integer.");
    }

    private static int ParseNonNegativeInt(string value, string option)
    {
        if (
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0
        )
            return parsed;

        throw new ArgumentException($"{option} requires a non-negative integer.");
    }
}

internal sealed class HelpRequestedException : Exception;

[EventSource(Name = "Enaga-HtmlTrace")]
internal sealed class HtmlTraceEventSource : EventSource
{
    public static readonly HtmlTraceEventSource Log = new();

    [Event(1, Level = EventLevel.Informational)]
    public void ScenarioStart(string mode, int width, int height) =>
        WriteEvent(1, mode, width, height);

    [Event(2, Level = EventLevel.Informational)]
    public void IterationBatch(
        string mode,
        int iterations,
        long elapsedMilliseconds,
        long allocatedBytes,
        int checksum
    ) => WriteEvent(2, mode, iterations, elapsedMilliseconds, allocatedBytes, checksum);

    [Event(3, Level = EventLevel.Informational)]
    public void ScenarioStop(string mode, int iterations, long elapsedMilliseconds, int checksum) =>
        WriteEvent(3, mode, iterations, elapsedMilliseconds, checksum);
}

internal sealed record HtmlTraceFixture(Enaga.Html.HtmlDocument Document, int Width, int Height);

internal static class HtmlTraceFixtures
{
    public static HtmlTraceFixture CreateFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var html = File.ReadAllText(fullPath);
        return new HtmlTraceFixture(
            new Enaga.Html.HtmlDocument(html, BasePath: Path.GetDirectoryName(fullPath)),
            1024,
            768
        );
    }

    public static HtmlTraceFixture Create(HtmlTraceDocument document)
    {
        return document switch
        {
            HtmlTraceDocument.TextWrapStress => new HtmlTraceFixture(
                CreateTextWrapStress(),
                960,
                720
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(document), document, null),
        };
    }

    private static Enaga.Html.HtmlDocument CreateTextWrapStress()
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html><body><main>");
        for (var index = 0; index < 160; index++)
        {
            html.Append("<section class='card'><h2>Receiving lane ");
            html.Append(index.ToString(CultureInfo.InvariantCulture));
            html.Append(
                "</h2><p>Review the <a href='docs.html'>receiving playbook</a> before assigning a carrier window. Confirm supplier holds, dock schedules, customs notes, and customer promises before the next handoff.</p>"
            );
            html.Append(
                "<ul><li>Confirm receiving capacity before assigning a carrier window.</li><li>Check exceptions against the <a href='policy.html'>account note policy</a>.</li><li>Flag temperature-sensitive freight for the morning shift lead.</li></ul></section>"
            );
        }

        html.Append("</main></body></html>");

        const string css = """
            body { margin: 0; font: 16px/1.45 Arial, sans-serif; color: #dbeafe; background: #0f172a; }
            main { padding: 24px; display: block; }
            .card { display: block; width: 48%; margin: 0 2% 18px 0; padding: 14px; border-width: 1px; border-style: solid; border-color: #36547b; border-radius: 8px; background: #13243b; }
            h2 { font-size: 22px; margin: 0 0 10px; line-height: 1.2; }
            p { margin: 0 0 12px; }
            ul { margin: 0 0 0 22px; padding: 0; }
            li { margin: 0 0 6px; }
            a { color: #7dd3fc; font-style: italic; text-decoration: underline; }
            """;

        return new Enaga.Html.HtmlDocument(html.ToString(), css);
    }
}
