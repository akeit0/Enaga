using System.Security.Cryptography;

namespace Enaga.React.OkojoRuntime;

internal sealed class RuntimeAssetService : IDisposable
{
    private readonly IRuntimeAssetResolver assetResolver;
    private readonly string materializationRoot;
    private readonly Dictionary<string, string> materializedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;

    public RuntimeAssetService(IRuntimeAssetResolver assetResolver)
    {
        this.assetResolver = assetResolver ?? throw new ArgumentNullException(nameof(assetResolver));
        materializationRoot = Path.Combine(Path.GetTempPath(), "Enaga.RuntimeAssets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(materializationRoot);
    }

    public RuntimeAssetReference Resolve(RuntimeAssetRequest request)
    {
        return assetResolver.Resolve(request);
    }

    public string ResolvePath(RuntimeAssetRequest request)
    {
        var resolved = Resolve(request);
        return Materialize(resolved);
    }

    public string Materialize(RuntimeAssetReference assetReference)
    {
        return assetReference.Kind switch
        {
            RuntimeAssetKind.LocalPath => assetReference.LocalPath ?? assetReference.Source,
            RuntimeAssetKind.Uri => assetReference.Uri?.IsFile == true
                ? assetReference.Uri.LocalPath
                : assetReference.Uri?.AbsoluteUri ?? assetReference.Source,
            RuntimeAssetKind.Stream => MaterializeStream(assetReference),
            _ => assetReference.Source
        };
    }

    private string MaterializeStream(RuntimeAssetReference assetReference)
    {
        if (assetReference.OpenStream is null)
            throw new InvalidOperationException($"Asset '{assetReference.Source}' cannot be materialized because no stream factory was provided.");

        if (materializedPaths.TryGetValue(assetReference.Source, out var existingPath) && File.Exists(existingPath))
            return existingPath;

        using var stream = assetReference.OpenStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var fileName = $"{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}{ResolveExtension(assetReference)}";
        var path = Path.Combine(materializationRoot, fileName);
        File.WriteAllBytes(path, bytes);
        materializedPaths[assetReference.Source] = path;
        return path;
    }

    private static string ResolveExtension(RuntimeAssetReference assetReference)
    {
        var extension = Path.GetExtension(assetReference.Source);
        if (!string.IsNullOrWhiteSpace(extension))
            return extension;

        return assetReference.ContentType?.ToLowerInvariant() switch
        {
            "image/svg+xml" => ".svg",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "application/json" => ".json",
            "text/plain; charset=utf-8" => ".txt",
            _ => ".bin"
        };
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (Directory.Exists(materializationRoot))
            Directory.Delete(materializationRoot, recursive: true);
    }
}
