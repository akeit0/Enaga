using System.Collections.Concurrent;

namespace Enaga.Rendering.Skia;


internal enum SkiaImageAssetState
{
    Pending,
    Ready,
    Failed
}

internal readonly record struct SkiaImageAssetResolveResult(
    SkiaImageAssetState State,
    SkiaImageAsset? Asset = null,
    string? Error = null);

internal static class SkiaImageAssetCache
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Entries = new(StringComparer.Ordinal);

    public static event Action? AssetChanged;

    public static SkiaImageAssetResolveResult Resolve(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
            return new SkiaImageAssetResolveResult(SkiaImageAssetState.Failed, Error: "Image path is empty.");

        if (!File.Exists(localPath))
            return new SkiaImageAssetResolveResult(SkiaImageAssetState.Failed, Error: $"Image file was not found: {localPath}");

        var lastWriteTicksUtc = File.GetLastWriteTimeUtc(localPath).Ticks;
        var entry = Entries.GetOrAdd(localPath, _ => new CacheEntry(localPath));
        lock (entry.Sync)
        {
            if (entry.LastWriteTicksUtc != lastWriteTicksUtc)
            {
                entry.Asset?.Dispose();
                entry.Asset = null;
                entry.Error = null;
                entry.LoadTask = null;
                entry.State = SkiaImageAssetState.Pending;
                entry.LastWriteTicksUtc = lastWriteTicksUtc;
            }

            if (entry.State == SkiaImageAssetState.Ready && entry.Asset is not null)
                return new SkiaImageAssetResolveResult(SkiaImageAssetState.Ready, entry.Asset);

            if (entry.State == SkiaImageAssetState.Failed)
                return new SkiaImageAssetResolveResult(SkiaImageAssetState.Failed, Error: entry.Error);

            StartLoadIfNeeded(entry, lastWriteTicksUtc);
            return new SkiaImageAssetResolveResult(SkiaImageAssetState.Pending);
        }
    }

    private static void StartLoadIfNeeded(CacheEntry entry, long expectedWriteTicksUtc)
    {
        if (entry.LoadTask is not null)
            return;

        entry.LoadTask = Task.Run(() =>
        {
            var loaded = SkiaImageAssetLoader.TryLoadFromPath(entry.LocalPath, out var asset, out var error);
            var notify = false;

            lock (entry.Sync)
            {
                entry.LoadTask = null;
                if (entry.LastWriteTicksUtc != expectedWriteTicksUtc)
                {
                    asset?.Dispose();
                }
                else if (!loaded || asset is null)
                {
                    entry.Asset?.Dispose();
                    entry.Asset = null;
                    entry.Error = error ?? "Image decode failed.";
                    entry.State = SkiaImageAssetState.Failed;
                    notify = true;
                }
                else
                {
                    entry.Asset?.Dispose();
                    entry.Asset = asset;
                    entry.Error = null;
                    entry.State = SkiaImageAssetState.Ready;
                    notify = true;
                }
            }

            if (notify)
                AssetChanged?.Invoke();
        });
    }

    private sealed class CacheEntry
    {
        public CacheEntry(string localPath)
        {
            LocalPath = localPath;
        }

        public object Sync { get; } = new();
        public string LocalPath { get; }
        public long LastWriteTicksUtc { get; set; }
        public SkiaImageAssetState State { get; set; } = SkiaImageAssetState.Pending;
        public string? Error { get; set; }
        public SkiaImageAsset? Asset { get; set; }
        public Task? LoadTask { get; set; }
    }
}
