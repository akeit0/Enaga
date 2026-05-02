using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Enaga.Hosting;
using Enaga.React.OkojoRuntime;

namespace Enaga.SampleApp;

public enum SampleAppRuntimeProfile : byte
{
    Stable = 0,
    Development = 1,
    FastRefresh = 2
}

public sealed record SampleAppAssetSource(
    string? Alias,
    string? Path,
    string? ManifestResourcePrefix,
    string? AssemblyName);

public sealed partial record SampleAppOptions(
    string WindowTitle,
    int WindowWidth,
    int WindowHeight,
    string? ReactEntryPath,
    string? ReactAssetBasePath,
    SampleAppRuntimeProfile RuntimeProfile,
    bool ReactDebug,
    bool EnableFileWatching,
    IReadOnlyList<string> WatchPaths,
    IReadOnlyList<string> WatchPatterns,
    IReadOnlyList<RuntimeDiagnosticArea> DiagnosticAreas,
    string? DiagnosticLogPath,
    IReadOnlyList<SampleAppAssetSource> AssetSources,
    bool RenderStats,
    bool TraceViewCalls,
    RenderTraceLogFlags TraceLogFlags,
    double FramesPerSecond,
    RenderGraphicsBackend GraphicsBackend,
    string? ConfigPath)
{
    private const double DefaultFramesPerSecond = 60;

    public bool EnableDebugFeatures => ReactDebug || RuntimeProfile == SampleAppRuntimeProfile.Development;

    public ReactRuntimeReloadMode ReloadMode => RuntimeProfile == SampleAppRuntimeProfile.FastRefresh
        ? ReactRuntimeReloadMode.FastRefresh
        : ReactRuntimeReloadMode.ReloadModuleGraph;

    public static SampleAppOptions Parse(string[] args)
    {
        var configPath = TryGetConfigPath(args);
        var config = SampleAppConfig.Load(configPath);
        var configDirectory = string.IsNullOrWhiteSpace(configPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;

        var windowTitle = "Enaga sample";
        var windowWidth = NormalizeWindowDimension(config?.Window?.Width, 1280);
        var windowHeight = NormalizeWindowDimension(config?.Window?.Height, 800);
        var defaultReactEntryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../dist/react-entry.mjs"));
        var defaultFastRefreshReactEntryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../dist/fast-refresh/examples/SampleApp/src/fast-refresh-entry.mjs"));
        var reactEntryPath = ResolveOptionalPath(configDirectory, config?.React?.Entry);
        var reactAssetBasePath = ResolveOptionalPath(configDirectory, config?.React?.AssetBase);
        var runtimeProfile = ResolveRuntimeProfile(config);
        var reactDebug = false;
        var enableFileWatching = config?.React?.Development?.Watch ?? false;
        var watchPaths = ResolvePaths(configDirectory, config?.React?.Development?.WatchPaths);
        var watchPatterns = ResolvePatterns(config?.React?.Development?.WatchPatterns);
        var diagnosticAreas = ParseDiagnosticAreas(config?.React?.Diagnostics?.Areas);
        var diagnosticLogPath = ResolveOptionalPath(configDirectory, config?.React?.Diagnostics?.File);
        var assetSources = ParseAssetSources(configDirectory, config?.React?.Assets);
        var renderStats = false;
        var traceViewCalls = false;
        var traceLogFlags = RenderTraceLogFlags.None;
        var framesPerSecond = DefaultFramesPerSecond;
        var graphicsBackend = RenderGraphicsBackend.Vulkan;
        var configProvidedProfile = config?.React?.Development is not null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config":
                    i++;
                    break;
                case "--title" when i + 1 < args.Length:
                    windowTitle = args[++i];
                    break;
                case "--width" when i + 1 < args.Length &&
                                   int.TryParse(args[++i], out var parsedWindowWidth) &&
                                   parsedWindowWidth > 0:
                    windowWidth = parsedWindowWidth;
                    break;
                case "--height" when i + 1 < args.Length &&
                                    int.TryParse(args[++i], out var parsedWindowHeight) &&
                                    parsedWindowHeight > 0:
                    windowHeight = parsedWindowHeight;
                    break;
                case "--react-entry" when i + 1 < args.Length:
                    reactEntryPath = Path.GetFullPath(args[++i]);
                    break;
                case "--asset-base" when i + 1 < args.Length:
                    reactAssetBasePath = Path.GetFullPath(args[++i]);
                    break;
                case "--react-debug":
                    reactDebug = true;
                    break;
                case "--development":
                    runtimeProfile = SampleAppRuntimeProfile.Development;
                    break;
                case "--fast-refresh":
                    runtimeProfile = SampleAppRuntimeProfile.FastRefresh;
                    enableFileWatching = true;
                    break;
                case "--watch":
                    enableFileWatching = true;
                    break;
                case "--no-watch":
                    enableFileWatching = false;
                    break;
                case "--watch-path" when i + 1 < args.Length:
                    watchPaths = [.. watchPaths, Path.GetFullPath(args[++i])];
                    break;
                case "--host-diagnostics" when i + 1 < args.Length:
                    diagnosticAreas = ParseDiagnosticAreas(args[++i]);
                    break;
                case "--host-log-file" when i + 1 < args.Length:
                    diagnosticLogPath = Path.GetFullPath(args[++i]);
                    break;
                case "--render-stats":
                    renderStats = true;
                    traceLogFlags |= RenderTraceLogFlags.All;
                    break;
                case "--trace-log-flags" when i + 1 < args.Length:
                    traceLogFlags |= ParseTraceLogFlags(args[++i]);
                    break;
                case "--trace-view-calls":
                    traceViewCalls = true;
                    break;
                case "--fps" when i + 1 < args.Length &&
                                  double.TryParse(args[++i], out var parsedFramesPerSecond) &&
                                  parsedFramesPerSecond > 0:
                    framesPerSecond = parsedFramesPerSecond;
                    break;
                case "--vulkan":
                    graphicsBackend = RenderGraphicsBackend.Vulkan;
                    break;
                case "--opengl":
                    graphicsBackend = RenderGraphicsBackend.OpenGl;
                    break;
                case "--metal":
                    graphicsBackend = RenderGraphicsBackend.Metal;
                    break;
                case "--graphics-backend" when i + 1 < args.Length:
                    graphicsBackend = ParseGraphicsBackend(args[++i]);
                    break;
            }
        }

        reactEntryPath ??= runtimeProfile == SampleAppRuntimeProfile.FastRefresh && File.Exists(defaultFastRefreshReactEntryPath)
            ? defaultFastRefreshReactEntryPath
            : defaultReactEntryPath;
        reactAssetBasePath ??= ResolveDefaultAssetBasePath(reactEntryPath);
        if (!configProvidedProfile && runtimeProfile == SampleAppRuntimeProfile.Stable)
            enableFileWatching = false;
        if (enableFileWatching && watchPaths.Count == 0 && !string.IsNullOrWhiteSpace(reactEntryPath))
        {
            var defaultWatchPath = ResolveDefaultWatchPath(reactEntryPath, runtimeProfile);
            watchPaths = [Path.GetFullPath(string.IsNullOrWhiteSpace(defaultWatchPath) ? reactEntryPath : defaultWatchPath)];
        }
        if (enableFileWatching && !string.IsNullOrWhiteSpace(configPath))
            watchPaths = [.. watchPaths, Path.GetFullPath(configPath)];

        return new SampleAppOptions(
            windowTitle,
            windowWidth,
            windowHeight,
            reactEntryPath,
            reactAssetBasePath,
            runtimeProfile,
            reactDebug,
            enableFileWatching,
            watchPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            watchPatterns,
            diagnosticAreas,
            diagnosticLogPath,
            assetSources,
            renderStats,
            traceViewCalls,
            traceLogFlags,
            framesPerSecond,
            graphicsBackend,
            configPath);
    }

    public IReactAppEntrySource CreateReactEntrySource()
    {
        var entryPath = ReactEntryPath ?? throw new InvalidOperationException("React entry path is required.");
        return new FileReactAppEntrySource(entryPath, assetBasePath: ReactAssetBasePath);
    }

    public ReactRuntimeReloadOptions CreateReloadOptions()
    {
        return new ReactRuntimeReloadOptions
        {
            Mode = ReloadMode,
            EnableFileWatching = EnableFileWatching,
            WatchPaths = WatchPaths.ToArray(),
            WatchPatterns = WatchPatterns.Count == 0 ? ["*.mjs", "*.js", "*.jsx", "*.ts", "*.tsx", "*.json"] : WatchPatterns.ToArray()
        };
    }

    public IRuntimeAssetResolver CreateAssetResolver()
    {
        if (AssetSources.Count == 0)
            return RuntimeAssetResolver.FileSystemRelativeToEntry;

        List<IRuntimeAssetResolver> resolvers = [];
        foreach (var source in AssetSources)
        {
            if (!string.IsNullOrWhiteSpace(source.Path) && !string.IsNullOrWhiteSpace(source.Alias))
            {
                resolvers.Add(new PrefixedFileAssetResolver(source.Alias, source.Path));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(source.ManifestResourcePrefix))
            {
                var assembly = string.IsNullOrWhiteSpace(source.AssemblyName)
                    ? typeof(SampleAppRuntime).Assembly
                    : Assembly.Load(source.AssemblyName);
                resolvers.Add(new ManifestResourceAssetResolver(assembly, source.ManifestResourcePrefix, source.Alias));
            }
        }

        resolvers.Add(RuntimeAssetResolver.FileSystemRelativeToEntry);
        return new CompositeAssetResolver(resolvers);
    }

    public IRuntimeDiagnosticsSink CreateDiagnosticsSink()
    {
        HashSet<RuntimeDiagnosticArea> areas = [.. DiagnosticAreas];
        foreach (var area in GetImplicitDiagnosticAreas())
            areas.Add(area);

        if (areas.Count == 0)
            return RuntimeDiagnosticsSink.None;

        return string.IsNullOrWhiteSpace(DiagnosticLogPath)
            ? RuntimeDiagnosticsSink.Console([.. areas])
            : RuntimeDiagnosticsSink.File(DiagnosticLogPath, [.. areas]);
    }

    public string Describe()
    {
        return $"title={WindowTitle}, size={WindowWidth}x{WindowHeight}, entry={ReactEntryPath}, assetBase={ReactAssetBasePath}, profile={RuntimeProfile}, reactDebug={ReactDebug}, watch={EnableFileWatching}, diagnostics={string.Join(',', DiagnosticAreas)}, logFile={DiagnosticLogPath}, trace={TraceLogFlags}, backend={GraphicsBackend}, fps={FramesPerSecond}";
    }

    private static string? TryGetConfigPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--config")
                return Path.GetFullPath(args[i + 1]);
        }

        var defaultConfigPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "sample-appsettings.json"));
        return File.Exists(defaultConfigPath)
            ? defaultConfigPath
            : null;
    }

    private static SampleAppRuntimeProfile ResolveRuntimeProfile(SampleAppConfig? config)
    {
        var development = config?.React?.Development;
        if (development?.FastRefresh == true)
            return SampleAppRuntimeProfile.FastRefresh;
        if (development?.Watch == true ||
            (development?.WatchPaths?.Count ?? 0) > 0 ||
            (development?.WatchPatterns?.Count ?? 0) > 0)
            return SampleAppRuntimeProfile.Development;
        return SampleAppRuntimeProfile.Stable;
    }

    private IReadOnlyList<RuntimeDiagnosticArea> GetImplicitDiagnosticAreas()
    {
        HashSet<RuntimeDiagnosticArea> areas = [];
        if (RuntimeProfile == SampleAppRuntimeProfile.FastRefresh)
        {
            areas.Add(RuntimeDiagnosticArea.Configuration);
            areas.Add(RuntimeDiagnosticArea.Reload);
            areas.Add(RuntimeDiagnosticArea.ModuleInvalidation);
        }

        if (ReactDebug)
        {
            areas.Add(RuntimeDiagnosticArea.Configuration);
            areas.Add(RuntimeDiagnosticArea.RuntimeLifecycle);
        }

        if (TraceLogFlags.HasFlag(RenderTraceLogFlags.Paint) ||
            TraceLogFlags.HasFlag(RenderTraceLogFlags.Runtime) ||
            TraceLogFlags.HasFlag(RenderTraceLogFlags.ViewPerFrame))
        {
            areas.Add(RuntimeDiagnosticArea.Rendering);
        }

        if (TraceLogFlags.HasFlag(RenderTraceLogFlags.Damage))
            areas.Add(RuntimeDiagnosticArea.Damage);

        return [.. areas];
    }

    private static string? ResolveOptionalPath(string baseDirectory, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(baseDirectory, value));
    }

    private static int NormalizeWindowDimension(int? configuredValue, int fallback)
    {
        return configuredValue is > 0 ? configuredValue.Value : fallback;
    }

    private static string? ResolveDefaultAssetBasePath(string? reactEntryPath)
    {
        if (string.IsNullOrWhiteSpace(reactEntryPath))
            return reactEntryPath;

        var candidate = Path.GetDirectoryName(Path.GetFullPath(reactEntryPath));
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            if (Directory.Exists(Path.Combine(candidate, "assets")) &&
                Directory.Exists(Path.Combine(candidate, "dist")))
            {
                return candidate;
            }

            candidate = Path.GetDirectoryName(candidate);
        }

        return reactEntryPath;
    }

    private static string? ResolveDefaultWatchPath(string reactEntryPath, SampleAppRuntimeProfile runtimeProfile)
    {
        var entryDirectory = Path.GetDirectoryName(reactEntryPath);
        if (runtimeProfile != SampleAppRuntimeProfile.FastRefresh || string.IsNullOrWhiteSpace(entryDirectory))
            return entryDirectory;

        var marker = $"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}fast-refresh{Path.DirectorySeparatorChar}";
        var normalizedEntryPath = Path.GetFullPath(reactEntryPath);
        var markerIndex = normalizedEntryPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return entryDirectory;

        return normalizedEntryPath[..(markerIndex + marker.Length - 1)];
    }

    private static IReadOnlyList<string> ResolvePaths(string baseDirectory, IEnumerable<string>? values)
    {
        if (values is null)
            return Array.Empty<string>();

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolveOptionalPath(baseDirectory, value)!)
            .ToArray();
    }

    private static IReadOnlyList<string> ResolvePatterns(IEnumerable<string>? values)
    {
        return values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();
    }

    private static IReadOnlyList<SampleAppAssetSource> ParseAssetSources(string baseDirectory, IReadOnlyList<SampleAppAssetSourceConfig>? values)
    {
        if (values is null || values.Count == 0)
            return Array.Empty<SampleAppAssetSource>();

        return values
            .Select(value => new SampleAppAssetSource(
                value.Alias,
                ResolveOptionalPath(baseDirectory, value.Path),
                value.ManifestResourcePrefix,
                value.AssemblyName))
            .ToArray();
    }

    private static IReadOnlyList<RuntimeDiagnosticArea> ParseDiagnosticAreas(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<RuntimeDiagnosticArea>();

        return ParseDiagnosticAreas(value.Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static IReadOnlyList<RuntimeDiagnosticArea> ParseDiagnosticAreas(IEnumerable<string>? values)
    {
        if (values is null)
            return Array.Empty<RuntimeDiagnosticArea>();

        HashSet<RuntimeDiagnosticArea> areas = [];
        foreach (var token in values)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (token == "*" || token.Equals("all", StringComparison.OrdinalIgnoreCase))
                return Enum.GetValues<RuntimeDiagnosticArea>();

            if (token.Equals("lifecycle", StringComparison.OrdinalIgnoreCase) || token.Equals("runtime-lifecycle", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.RuntimeLifecycle);
            else if (token.Equals("reload", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.Reload);
            else if (token.Equals("module-invalidation", StringComparison.OrdinalIgnoreCase) || token.Equals("modules", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.ModuleInvalidation);
            else if (token.Equals("input", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.Input);
            else if (token.Equals("scene", StringComparison.OrdinalIgnoreCase) || token.Equals("scene-commit", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.SceneCommit);
            else if (token.Equals("assets", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.Assets);
            else if (token.Equals("render", StringComparison.OrdinalIgnoreCase) || token.Equals("rendering", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.Rendering);
            else if (token.Equals("damage", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.Damage);
            else if (token.Equals("window", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.Window);
            else if (token.Equals("shader-trace", StringComparison.OrdinalIgnoreCase) || token.Equals("shader", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.ShaderTrace);
            else if (token.Equals("config", StringComparison.OrdinalIgnoreCase) || token.Equals("configuration", StringComparison.OrdinalIgnoreCase))
                areas.Add(RuntimeDiagnosticArea.Configuration);
        }

        return [.. areas];
    }

    private static RenderTraceLogFlags ParseTraceLogFlags(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return RenderTraceLogFlags.None;

        RenderTraceLogFlags flags = RenderTraceLogFlags.None;
        foreach (var token in value.Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token == "*" || token.Equals("all", StringComparison.OrdinalIgnoreCase))
                return RenderTraceLogFlags.All;
            if (token.Equals("paint", StringComparison.OrdinalIgnoreCase))
                flags |= RenderTraceLogFlags.Paint;
            else if (token.Equals("view-per-frame", StringComparison.OrdinalIgnoreCase))
                flags |= RenderTraceLogFlags.ViewPerFrame;
            else if (token.Equals("damage", StringComparison.OrdinalIgnoreCase))
                flags |= RenderTraceLogFlags.Damage;
            else if (token.Equals("runtime", StringComparison.OrdinalIgnoreCase))
                flags |= RenderTraceLogFlags.Runtime;
        }

        return flags;
    }

    private static RenderGraphicsBackend ParseGraphicsBackend(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "gl" or "opengl" => RenderGraphicsBackend.OpenGl,
            "mtl" or "metal" => RenderGraphicsBackend.Metal,
            "vk" or "vulkan" => RenderGraphicsBackend.Vulkan,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Expected 'opengl', 'vulkan', or 'metal'."),
        };
    }

    private sealed class SampleAppConfig
    {
        [JsonPropertyName("react")]
        public SampleAppReactConfig? React { get; init; }

        [JsonPropertyName("window")]
        public SampleAppWindowConfig? Window { get; init; }

        public static SampleAppConfig? Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return JsonSerializer.Deserialize(
                File.ReadAllText(path),
                SampleAppConfigJsonContext.Default.SampleAppConfig)
                ?? throw new InvalidOperationException($"Failed to parse sample app config '{path}'.");
        }
    }

    private sealed class SampleAppReactConfig
    {
        [JsonPropertyName("entry")]
        public string? Entry { get; init; }

        [JsonPropertyName("assetBase")]
        public string? AssetBase { get; init; }

        [JsonPropertyName("assets")]
        public List<SampleAppAssetSourceConfig>? Assets { get; init; }

        [JsonPropertyName("development")]
        public SampleAppDevelopmentConfig? Development { get; init; }

        [JsonPropertyName("diagnostics")]
        public SampleAppDiagnosticsConfig? Diagnostics { get; init; }
    }

    private sealed class SampleAppDevelopmentConfig
    {
        [JsonPropertyName("fastRefresh")]
        public bool FastRefresh { get; init; }

        [JsonPropertyName("watch")]
        public bool Watch { get; init; }

        [JsonPropertyName("watchPaths")]
        public List<string>? WatchPaths { get; init; }

        [JsonPropertyName("watchPatterns")]
        public List<string>? WatchPatterns { get; init; }
    }

    private sealed class SampleAppDiagnosticsConfig
    {
        [JsonPropertyName("areas")]
        public List<string>? Areas { get; init; }

        [JsonPropertyName("file")]
        public string? File { get; init; }
    }

    private sealed class SampleAppWindowConfig
    {
        [JsonPropertyName("width")]
        public int? Width { get; init; }

        [JsonPropertyName("height")]
        public int? Height { get; init; }
    }

    private sealed class SampleAppAssetSourceConfig
    {
        [JsonPropertyName("alias")]
        public string? Alias { get; init; }

        [JsonPropertyName("path")]
        public string? Path { get; init; }

        [JsonPropertyName("manifestResourcePrefix")]
        public string? ManifestResourcePrefix { get; init; }

        [JsonPropertyName("assemblyName")]
        public string? AssemblyName { get; init; }
    }

    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [JsonSerializable(typeof(SampleAppConfig))]
    [JsonSerializable(typeof(SampleAppReactConfig))]
    [JsonSerializable(typeof(SampleAppWindowConfig))]
    [JsonSerializable(typeof(SampleAppDevelopmentConfig))]
    [JsonSerializable(typeof(SampleAppDiagnosticsConfig))]
    [JsonSerializable(typeof(SampleAppAssetSourceConfig))]
    [JsonSerializable(typeof(List<SampleAppAssetSourceConfig>))]
    [JsonSerializable(typeof(List<string>))]
    private sealed partial class SampleAppConfigJsonContext : JsonSerializerContext;
}
