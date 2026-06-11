namespace Enaga.Hosting;

public enum RuntimeDiagnosticArea
{
    RuntimeLifecycle = 0,
    Reload = 1,
    ModuleInvalidation = 2,
    Input = 3,
    SceneCommit = 4,
    Assets = 5,
    Rendering = 6,
    Damage = 7,
    Window = 8,
    ShaderTrace = 9,
    Configuration = 10,
}

public readonly record struct RuntimeDiagnosticEvent(
    RuntimeDiagnosticArea Area,
    string Source,
    string Message
);

public interface IRuntimeDiagnosticsSink
{
    bool IsEnabled(RuntimeDiagnosticArea area);

    void Write(RuntimeDiagnosticEvent diagnosticEvent);
}

public static class RuntimeDiagnosticsSink
{
    public static IRuntimeDiagnosticsSink None { get; } = new NullRuntimeDiagnosticsSink();

    public static IRuntimeDiagnosticsSink Console(params RuntimeDiagnosticArea[] areas)
    {
        if (areas is null || areas.Length == 0)
            return None;

        return new ConsoleRuntimeDiagnosticsSink(areas);
    }

    public static IRuntimeDiagnosticsSink File(string path, params RuntimeDiagnosticArea[] areas)
    {
        if (areas is null || areas.Length == 0)
            return None;
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A diagnostics file path is required.", nameof(path));

        return new FileRuntimeDiagnosticsSink(path, areas);
    }
}

public sealed class ConsoleRuntimeDiagnosticsSink : IRuntimeDiagnosticsSink
{
    private readonly HashSet<RuntimeDiagnosticArea> enabledAreas;

    public ConsoleRuntimeDiagnosticsSink(IEnumerable<RuntimeDiagnosticArea> enabledAreas)
    {
        ArgumentNullException.ThrowIfNull(enabledAreas);
        this.enabledAreas = new HashSet<RuntimeDiagnosticArea>(enabledAreas);
    }

    public bool IsEnabled(RuntimeDiagnosticArea area)
    {
        return enabledAreas.Contains(area);
    }

    public void Write(RuntimeDiagnosticEvent diagnosticEvent)
    {
        if (!IsEnabled(diagnosticEvent.Area))
            return;

        Console.WriteLine(RuntimeDiagnosticsTextFormatter.Format(diagnosticEvent));
    }
}

public sealed class FileRuntimeDiagnosticsSink : IRuntimeDiagnosticsSink
{
    private readonly string path;
    private readonly HashSet<RuntimeDiagnosticArea> enabledAreas;
    private readonly object sync = new();

    public FileRuntimeDiagnosticsSink(string path, IEnumerable<RuntimeDiagnosticArea> enabledAreas)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A diagnostics file path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(enabledAreas);

        this.path = Path.GetFullPath(path);
        this.enabledAreas = new HashSet<RuntimeDiagnosticArea>(enabledAreas);
        var directory = Path.GetDirectoryName(this.path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public bool IsEnabled(RuntimeDiagnosticArea area)
    {
        return enabledAreas.Contains(area);
    }

    public void Write(RuntimeDiagnosticEvent diagnosticEvent)
    {
        if (!IsEnabled(diagnosticEvent.Area))
            return;

        lock (sync)
        {
            File.AppendAllText(
                path,
                $"{RuntimeDiagnosticsTextFormatter.Format(diagnosticEvent)}{Environment.NewLine}"
            );
        }
    }
}

public sealed class FilteredRuntimeDiagnosticsSink : IRuntimeDiagnosticsSink
{
    private readonly IRuntimeDiagnosticsSink inner;
    private readonly HashSet<RuntimeDiagnosticArea> enabledAreas;

    public FilteredRuntimeDiagnosticsSink(
        IRuntimeDiagnosticsSink inner,
        IEnumerable<RuntimeDiagnosticArea> enabledAreas
    )
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(enabledAreas);
        this.enabledAreas = new HashSet<RuntimeDiagnosticArea>(enabledAreas);
    }

    public bool IsEnabled(RuntimeDiagnosticArea area)
    {
        return enabledAreas.Contains(area) && inner.IsEnabled(area);
    }

    public void Write(RuntimeDiagnosticEvent diagnosticEvent)
    {
        if (enabledAreas.Contains(diagnosticEvent.Area))
            inner.Write(diagnosticEvent);
    }
}

internal sealed class NullRuntimeDiagnosticsSink : IRuntimeDiagnosticsSink
{
    public bool IsEnabled(RuntimeDiagnosticArea area) => false;

    public void Write(RuntimeDiagnosticEvent diagnosticEvent) { }
}

internal static class RuntimeDiagnosticsTextFormatter
{
    public static string Format(RuntimeDiagnosticEvent diagnosticEvent)
    {
        return $"[{diagnosticEvent.Source}:{FormatArea(diagnosticEvent.Area)}] {diagnosticEvent.Message}";
    }

    private static string FormatArea(RuntimeDiagnosticArea area)
    {
        return area switch
        {
            RuntimeDiagnosticArea.RuntimeLifecycle => "lifecycle",
            RuntimeDiagnosticArea.ModuleInvalidation => "module-invalidation",
            RuntimeDiagnosticArea.SceneCommit => "scene-commit",
            RuntimeDiagnosticArea.ShaderTrace => "shader-trace",
            _ => area.ToString().ToLowerInvariant(),
        };
    }
}
