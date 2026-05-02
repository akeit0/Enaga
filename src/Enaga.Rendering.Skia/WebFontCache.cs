using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;

namespace Enaga.Rendering.Skia;


internal enum WebFontCacheState
{
    Pending,
    Ready,
    Failed
}

internal readonly record struct WebFontCacheResult(
    WebFontCacheState State,
    string? LocalPath = null,
    string? Error = null);

internal static class WebFontCache
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, RemoteFontEntry> RemoteEntries = new(StringComparer.Ordinal);
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Enaga",
        "font-cache");
    private static readonly TimeSpan MaxEntryAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan CleanupThrottle = TimeSpan.FromMinutes(1);
    private const int MaxCacheFileCount = 64;
    private static long lastCleanupTicksUtc;

    public static WebFontCacheResult Resolve(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return ResolveLocal(source);

        Directory.CreateDirectory(CacheDirectory);
        TryApplyEvictionPolicy();
        var cachePath = BuildCachePath(uri, source);
        var entry = RemoteEntries.GetOrAdd(source, _ => new RemoteFontEntry(cachePath));

        lock (entry.Sync)
        {
            if (entry.State == WebFontCacheState.Ready && File.Exists(cachePath))
                return new WebFontCacheResult(WebFontCacheState.Ready, cachePath);

            if (entry.State == WebFontCacheState.Ready && !File.Exists(cachePath))
                entry.State = WebFontCacheState.Pending;

            if (File.Exists(cachePath))
            {
                entry.State = WebFontCacheState.Ready;
                entry.Error = null;
                return new WebFontCacheResult(WebFontCacheState.Ready, cachePath);
            }

            if (entry.State == WebFontCacheState.Failed)
                return new WebFontCacheResult(WebFontCacheState.Failed, Error: entry.Error);

            StartDownloadIfNeeded(uri, entry);
            return new WebFontCacheResult(WebFontCacheState.Pending);
        }
    }

    private static WebFontCacheResult ResolveLocal(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new WebFontCacheResult(WebFontCacheState.Failed, Error: "Font source is empty.");

        return File.Exists(source)
            ? new WebFontCacheResult(WebFontCacheState.Ready, source)
            : new WebFontCacheResult(WebFontCacheState.Failed, Error: $"Font file was not found: {source}");
    }

    private static string BuildCachePath(Uri uri, string source)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".font";

        return Path.Combine(CacheDirectory, $"{ComputeHash(source)}{extension}");
    }

    private static void StartDownloadIfNeeded(Uri uri, RemoteFontEntry entry)
    {
        if (entry.DownloadTask is not null)
            return;

        entry.DownloadTask = Task.Run(async () =>
        {
            var tempPath = $"{entry.CachePath}.tmp";
            try
            {
                using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
                File.Move(tempPath, entry.CachePath, overwrite: true);
                File.SetLastWriteTimeUtc(entry.CachePath, DateTime.UtcNow);
                lock (entry.Sync)
                {
                    entry.State = WebFontCacheState.Ready;
                    entry.Error = null;
                    entry.DownloadTask = null;
                }
                TryApplyEvictionPolicy();
            }
            catch (HttpRequestException ex)
            {
                SetDownloadFailure(entry, ex.Message, tempPath);
            }
            catch (TaskCanceledException ex)
            {
                SetDownloadFailure(entry, ex.Message, tempPath);
            }
            catch (IOException ex)
            {
                SetDownloadFailure(entry, ex.Message, tempPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                SetDownloadFailure(entry, ex.Message, tempPath);
            }
        });
    }

    private static void SetDownloadFailure(RemoteFontEntry entry, string message, string tempPath)
    {
        if (File.Exists(tempPath))
            File.Delete(tempPath);
        lock (entry.Sync)
        {
            entry.State = WebFontCacheState.Failed;
            entry.Error = message;
            entry.DownloadTask = null;
        }
    }

    private static void TryApplyEvictionPolicy()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var previousTicks = Interlocked.Read(ref lastCleanupTicksUtc);
        if (previousTicks != 0 && nowTicks - previousTicks < CleanupThrottle.Ticks)
            return;
        Interlocked.Exchange(ref lastCleanupTicksUtc, nowTicks);

        if (!Directory.Exists(CacheDirectory))
            return;

        var files = new DirectoryInfo(CacheDirectory)
            .EnumerateFiles()
            .Where(file => !file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToArray();

        var cutoff = DateTime.UtcNow - MaxEntryAge;
        foreach (var file in files)
        {
            if (file.LastWriteTimeUtc >= cutoff)
                continue;
            TryDelete(file);
        }

        files = new DirectoryInfo(CacheDirectory)
            .EnumerateFiles()
            .Where(file => !file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToArray();

        for (var index = 0; index < files.Length - MaxCacheFileCount; index++)
            TryDelete(files[index]);
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[WebFontCache] Failed to delete {file.FullName}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[WebFontCache] Failed to delete {file.FullName}: {ex.Message}");
        }
    }

    private static string ComputeHash(string value)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class RemoteFontEntry
    {
        public RemoteFontEntry(string cachePath)
        {
            CachePath = cachePath;
        }

        public object Sync { get; } = new();

        public string CachePath { get; }

        public WebFontCacheState State { get; set; } = WebFontCacheState.Pending;

        public string? Error { get; set; }

        public Task? DownloadTask { get; set; }
    }
}
