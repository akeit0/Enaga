using System.Collections.Concurrent;

namespace Enaga.React.OkojoRuntime;

internal sealed class RuntimeReloadCoordinator
{
    private readonly ConcurrentQueue<string> pendingChangedPaths = new();

    public RuntimeReloadCoordinator(ReactRuntimeReloadOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ReactRuntimeReloadOptions Options { get; }

    public bool ReloadRequested { get; private set; } = true;

    public DateTime ReloadRequestedAtUtc { get; private set; }

    public void RequestReload(string? changedPath = null)
    {
        if (!string.IsNullOrWhiteSpace(changedPath))
            pendingChangedPaths.Enqueue(Path.GetFullPath(changedPath));

        ReloadRequested = true;
        ReloadRequestedAtUtc = DateTime.UtcNow;
    }

    public bool ShouldWaitForStabilization(DateTime utcNow)
    {
        return ReloadRequestedAtUtc != default
            && utcNow - ReloadRequestedAtUtc < Options.StabilizationDelay;
    }

    public void MarkReloadStarted()
    {
        ReloadRequested = false;
    }

    public string[] ConsumePendingPaths()
    {
        if (pendingChangedPaths.IsEmpty)
            return [];

        var changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pendingChangedPaths.TryDequeue(out var changedPath))
        {
            if (!string.IsNullOrWhiteSpace(changedPath))
                changedPaths.Add(changedPath);
        }

        return [.. changedPaths];
    }
}
