namespace Enaga.React.OkojoRuntime;

public sealed class ReactRuntimeReloadOptions
{
    private static readonly string[] DefaultWatchPatterns =
    [
        "*.mjs",
        "*.js",
        "*.jsx",
        "*.ts",
        "*.tsx",
        "*.json"
    ];

    public static ReactRuntimeReloadOptions Production { get; } = new();

    public ReactRuntimeReloadMode Mode { get; init; } = ReactRuntimeReloadMode.RebuildRuntime;

    public bool EnableFileWatching { get; init; }

    public TimeSpan StabilizationDelay { get; init; } = TimeSpan.FromMilliseconds(150);

    public IReadOnlyList<string> WatchPaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> WatchPatterns { get; init; } = DefaultWatchPatterns;

    public static ReactRuntimeReloadOptions Development(
        ReactRuntimeReloadMode mode = ReactRuntimeReloadMode.FastRefresh,
        IEnumerable<string>? watchPaths = null,
        IEnumerable<string>? watchPatterns = null)
    {
        return new ReactRuntimeReloadOptions
        {
            Mode = mode,
            EnableFileWatching = true,
            WatchPaths = watchPaths?.ToArray() ?? Array.Empty<string>(),
            WatchPatterns = watchPatterns?.ToArray() ?? DefaultWatchPatterns
        };
    }
}
