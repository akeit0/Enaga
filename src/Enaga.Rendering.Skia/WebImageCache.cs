using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Enaga.Rendering.Skia;

internal enum WebImageCacheState
{
    Pending,
    Ready,
    Failed,
}

internal readonly record struct WebImageCacheResult(
    WebImageCacheState State,
    string? LocalPath = null,
    string? Error = null
);

internal static class WebImageCache
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, RemoteImageEntry> RemoteEntries = new(
        StringComparer.Ordinal
    );
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Enaga",
        "image-cache"
    );
    private static readonly TimeSpan MaxEntryAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan CleanupThrottle = TimeSpan.FromMinutes(1);
    private const int MaxCacheFileCount = 128;
    private static long lastCleanupTicksUtc;

    public static event Action? ImageChanged;

    public static WebImageCacheResult Resolve(string source)
    {
        if (
            !Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        )
            return ResolveLocal(source);

        Directory.CreateDirectory(CacheDirectory);
        TryApplyEvictionPolicy();
        var cachePath = BuildCachePath(uri, source);
        var entry = RemoteEntries.GetOrAdd(source, _ => new RemoteImageEntry(cachePath));

        lock (entry.Sync)
        {
            if (entry.State == WebImageCacheState.Ready && File.Exists(cachePath))
                return new WebImageCacheResult(WebImageCacheState.Ready, cachePath);

            if (entry.State == WebImageCacheState.Ready && !File.Exists(cachePath))
                entry.State = WebImageCacheState.Pending;

            if (File.Exists(cachePath))
            {
                entry.State = WebImageCacheState.Ready;
                entry.Error = null;
                return new WebImageCacheResult(WebImageCacheState.Ready, cachePath);
            }

            if (entry.State == WebImageCacheState.Failed)
                return new WebImageCacheResult(WebImageCacheState.Failed, Error: entry.Error);

            StartDownloadIfNeeded(source, uri, entry);
            return new WebImageCacheResult(WebImageCacheState.Pending);
        }
    }

    private static WebImageCacheResult ResolveLocal(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new WebImageCacheResult(
                WebImageCacheState.Failed,
                Error: "Image source is empty."
            );

        var resolvedPath = source;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeFile)
            {
                resolvedPath = uri.LocalPath;
            }
            else
            {
                return new WebImageCacheResult(
                    WebImageCacheState.Failed,
                    Error: $"Unsupported image URI scheme: {uri.Scheme}"
                );
            }
        }

        return File.Exists(resolvedPath)
            ? new WebImageCacheResult(WebImageCacheState.Ready, resolvedPath)
            : new WebImageCacheResult(
                WebImageCacheState.Failed,
                Error: $"Image file was not found: {resolvedPath}"
            );
    }

    private static string BuildCachePath(Uri uri, string source)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".img";

        return Path.Combine(CacheDirectory, $"{ComputeHash(source)}{extension}");
    }

    private static void StartDownloadIfNeeded(string source, Uri uri, RemoteImageEntry entry)
    {
        if (entry.DownloadTask is not null)
            return;

        entry.DownloadTask = Task.Run(async () =>
        {
            var tempPath = $"{entry.CachePath}.tmp";
            try
            {
                using var response = await HttpClient
                    .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
                File.Move(tempPath, entry.CachePath, overwrite: true);
                File.SetLastWriteTimeUtc(entry.CachePath, DateTime.UtcNow);
                lock (entry.Sync)
                {
                    entry.State = WebImageCacheState.Ready;
                    entry.Error = null;
                    entry.DownloadTask = null;
                }
                ImageChanged?.Invoke();
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

    private static void SetDownloadFailure(RemoteImageEntry entry, string message, string tempPath)
    {
        if (File.Exists(tempPath))
            File.Delete(tempPath);
        lock (entry.Sync)
        {
            entry.State = WebImageCacheState.Failed;
            entry.Error = message;
            entry.DownloadTask = null;
        }
        ImageChanged?.Invoke();
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
            Console.Error.WriteLine(
                $"[WebImageCache] Failed to delete {file.FullName}: {ex.Message}"
            );
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine(
                $"[WebImageCache] Failed to delete {file.FullName}: {ex.Message}"
            );
        }
    }

    private static string ComputeHash(string value)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
        };
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko) Enaga.Rendering.Skia/1.0 Safari/537.36"
        );
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/avif"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("image/svg+xml")
        );
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("image/*", 0.8)
        );
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
        return client;
    }

    private sealed class RemoteImageEntry
    {
        public RemoteImageEntry(string cachePath)
        {
            CachePath = cachePath;
        }

        public object Sync { get; } = new();

        public string CachePath { get; }

        public WebImageCacheState State { get; set; } = WebImageCacheState.Pending;

        public string? Error { get; set; }

        public Task? DownloadTask { get; set; }
    }
}
