using Enaga.Rendering;
using Enaga.Rendering.Skia;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SkiaSharp;
using Xunit;

namespace Enaga.Tests;

public sealed class SkiaImageAssetLoaderTests
{
    [Fact]
    public void LoadFromPath_DecodesRasterImage()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        using (var bitmap = new SKBitmap(12, 8))
        using (var canvas = new SKCanvas(bitmap))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.OpenWrite(filePath))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            data.SaveTo(stream);
        }

        try
        {
            using var asset = SkiaImageAssetLoader.LoadFromPath(filePath);

            Assert.NotNull(asset);
            Assert.NotNull(asset!.RasterImage);
            Assert.Null(asset.VectorPicture);
            Assert.Equal(12, asset.SourceRect.Width);
            Assert.Equal(8, asset.SourceRect.Height);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void LoadFromPath_DecodesSvgImage()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.svg");
        File.WriteAllText(filePath, """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 20" fill="none">
              <rect width="32" height="20" rx="4" fill="#2563EB"/>
            </svg>
            """);

        try
        {
            using var asset = SkiaImageAssetLoader.LoadFromPath(filePath);

            Assert.NotNull(asset);
            Assert.Null(asset!.RasterImage);
            Assert.NotNull(asset.VectorPicture);
            Assert.Equal(32, asset.SourceRect.Width);
            Assert.Equal(20, asset.SourceRect.Height);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void LoadFromPath_InvalidSvg_DoesNotThrowAndReturnsNull()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.svg");
        File.WriteAllText(filePath, "<svg><g><invalid");

        try
        {
            var exception = Record.Exception(() => SkiaImageAssetLoader.LoadFromPath(filePath));
            var asset = SkiaImageAssetLoader.LoadFromPath(filePath);
            var loaded = SkiaImageAssetLoader.TryLoadFromPath(filePath, out var tryAsset, out var error);

            Assert.Null(exception);
            Assert.Null(asset);
            Assert.False(loaded);
            Assert.Null(tryAsset);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Resolve_FileUri_LocalImage_IsReady()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.svg");
        File.WriteAllText(filePath, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\" />");
        try
        {
            var result = WebImageCache.Resolve(new Uri(filePath).AbsoluteUri);

            Assert.Equal(WebImageCacheState.Ready, result.State);
            Assert.Equal(filePath, result.LocalPath);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Resolve_RemoteSvg_SendsBrowserHeadersAndDecodesImage()
    {
        var userAgents = new List<string>();
        using var server = new TestHttpServer(request =>
        {
            if (request.Headers.TryGetValue("User-Agent", out var userAgent))
                userAgents.Add(userAgent);

            if (userAgent?.Contains("Mozilla/5.0", StringComparison.Ordinal) != true)
                return TestHttpResponse.Forbidden("Status 403 Forbidden: User-Agent required.");

            return TestHttpResponse.Ok("""
                <svg xmlns="http://www.w3.org/2000/svg" width="234px" height="72px" viewBox="0 0 468 144">
                  <linearGradient id="g" x1="0" x2="1"><stop offset="0" stop-color="#0673BA"/><stop offset="1" stop-color="#11A14E"/></linearGradient>
                  <rect width="468" height="144" fill="url(#g)" />
                </svg>
                """);
        });

        var source = server.Url($"/iana-logo-header-{Guid.NewGuid():N}.svg");
        var first = WebImageCache.Resolve(source);
        Assert.Equal(WebImageCacheState.Pending, first.State);

        var downloaded = SpinWait.SpinUntil(
            () => WebImageCache.Resolve(source).State != WebImageCacheState.Pending,
            TimeSpan.FromSeconds(5));

        Assert.True(downloaded);
        var image = WebImageCache.Resolve(source);
        Assert.Equal(WebImageCacheState.Ready, image.State);
        Assert.NotNull(image.LocalPath);
        Assert.Contains(userAgents, value => value.Contains("Mozilla/5.0", StringComparison.Ordinal));

        var decoded = SpinWait.SpinUntil(
            () => SkiaImageAssetCache.Resolve(image.LocalPath).State != SkiaImageAssetState.Pending,
            TimeSpan.FromSeconds(5));

        Assert.True(decoded);
        var asset = SkiaImageAssetCache.Resolve(image.LocalPath);
        Assert.Equal(SkiaImageAssetState.Ready, asset.State);
        Assert.NotNull(asset.Asset?.VectorPicture);
    }

    [Fact]
    public void AssetCache_ResolvesRasterImageAsynchronously()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        using (var bitmap = new SKBitmap(10, 6))
        using (var canvas = new SKCanvas(bitmap))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.OpenWrite(filePath))
        {
            canvas.Clear(SKColors.Orange);
            data.SaveTo(stream);
        }

        try
        {
            var first = SkiaImageAssetCache.Resolve(filePath);
            Assert.Equal(SkiaImageAssetState.Pending, first.State);

            var completed = SpinWait.SpinUntil(
                () => SkiaImageAssetCache.Resolve(filePath).State != SkiaImageAssetState.Pending,
                TimeSpan.FromSeconds(5));

            Assert.True(completed);
            var resolved = SkiaImageAssetCache.Resolve(filePath);
            Assert.Equal(SkiaImageAssetState.Ready, resolved.State);
            Assert.NotNull(resolved.Asset?.RasterImage);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private sealed class TestHttpServer : IDisposable
    {
        private readonly Func<TestHttpRequest, TestHttpResponse> respond;
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task serverTask;

        public TestHttpServer(Func<TestHttpRequest, TestHttpResponse> respond)
        {
            this.respond = respond;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            serverTask = Task.Run(RunAsync);
        }

        public string Url(string path)
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            return $"http://127.0.0.1:{endpoint.Port}{path}";
        }

        public void Dispose()
        {
            cancellation.Cancel();
            listener.Stop();
            serverTask.Wait(TimeSpan.FromSeconds(2));
            cancellation.Dispose();
        }

        private async Task RunAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }

                _ = Task.Run(() => HandleClientAsync(client), cancellation.Token);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using var _ = client;
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
            if (requestLine is null)
                return;

            var requestParts = requestLine.Split(' ', 3);
            var path = requestParts[1];
            var headersByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0)
                    headersByName[line[..separator]] = line[(separator + 1)..].Trim();
            }

            var response = respond(new TestHttpRequest(path, headersByName));

            var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
            var headerBuilder = new StringBuilder()
                .Append("HTTP/1.1 ")
                .Append(response.Status)
                .Append("\r\nContent-Type: image/svg+xml; charset=utf-8\r\nContent-Length: ")
                .Append(bodyBytes.Length)
                .Append("\r\nConnection: close\r\n\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(headerBuilder.ToString())).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
        }
    }

    private sealed record TestHttpRequest(string Path, IReadOnlyDictionary<string, string> Headers);

    private sealed record TestHttpResponse(string Status, string Body)
    {
        public static TestHttpResponse Ok(string body)
            => new("200 OK", body);

        public static TestHttpResponse Forbidden(string body)
            => new("403 Forbidden", body);
    }
}
