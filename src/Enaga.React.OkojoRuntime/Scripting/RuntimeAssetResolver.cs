using System.Reflection;

namespace Enaga.React.OkojoRuntime;

public enum RuntimeAssetKind : byte
{
    None = 0,
    LocalPath = 1,
    Uri = 2,
    Stream = 3,
}

public readonly record struct RuntimeAssetRequest(
    string Source,
    string? AssetBasePath,
    string? ReferrerPath = null
);

public sealed record RuntimeAssetReference
{
    public static RuntimeAssetReference Unresolved(string source) =>
        new(source, RuntimeAssetKind.None);

    public RuntimeAssetReference(
        string source,
        RuntimeAssetKind kind,
        string? localPath = null,
        Uri? uri = null,
        Func<Stream>? openStream = null,
        string? contentType = null
    )
    {
        Source = source;
        Kind = kind;
        LocalPath = localPath;
        Uri = uri;
        OpenStream = openStream;
        ContentType = contentType;
    }

    public string Source { get; }

    public RuntimeAssetKind Kind { get; }

    public string? LocalPath { get; }

    public Uri? Uri { get; }

    public Func<Stream>? OpenStream { get; }

    public string? ContentType { get; }

    public bool IsResolved => Kind != RuntimeAssetKind.None;
}

public interface IRuntimeAssetResolver
{
    RuntimeAssetReference Resolve(RuntimeAssetRequest request);
}

public static class RuntimeAssetResolver
{
    public static IRuntimeAssetResolver FileSystemRelativeToEntry { get; } =
        FileSystemAssetResolver.Instance;
}

public sealed class FileSystemAssetResolver : IRuntimeAssetResolver
{
    public static FileSystemAssetResolver Instance { get; } = new();

    public RuntimeAssetReference Resolve(RuntimeAssetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Source))
            return RuntimeAssetReference.Unresolved(request.Source);

        if (Uri.TryCreate(request.Source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                return new RuntimeAssetReference(request.Source, RuntimeAssetKind.Uri, uri: uri);

            if (uri.Scheme == Uri.UriSchemeFile)
                return new RuntimeAssetReference(
                    request.Source,
                    RuntimeAssetKind.LocalPath,
                    localPath: uri.LocalPath
                );

            return RuntimeAssetReference.Unresolved(request.Source);
        }

        if (Path.IsPathRooted(request.Source))
            return new RuntimeAssetReference(
                request.Source,
                RuntimeAssetKind.LocalPath,
                localPath: request.Source
            );

        var assetBasePath = request.ReferrerPath ?? request.AssetBasePath;
        var assetBaseDirectory = Directory.Exists(assetBasePath)
            ? assetBasePath
            : Path.GetDirectoryName(assetBasePath);
        var resolvedPath = Path.GetFullPath(
            request.Source,
            string.IsNullOrWhiteSpace(assetBaseDirectory)
                ? Environment.CurrentDirectory
                : assetBaseDirectory
        );
        return new RuntimeAssetReference(
            request.Source,
            RuntimeAssetKind.LocalPath,
            localPath: resolvedPath
        );
    }
}

public sealed class PrefixedFileAssetResolver : IRuntimeAssetResolver
{
    private readonly string aliasPrefix;
    private readonly string rootPath;

    public PrefixedFileAssetResolver(string aliasPrefix, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(aliasPrefix))
            throw new ArgumentException("An alias prefix is required.", nameof(aliasPrefix));
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("A root path is required.", nameof(rootPath));

        this.aliasPrefix = aliasPrefix;
        this.rootPath = Path.GetFullPath(rootPath);
    }

    public RuntimeAssetReference Resolve(RuntimeAssetRequest request)
    {
        if (!request.Source.StartsWith(aliasPrefix, StringComparison.OrdinalIgnoreCase))
            return RuntimeAssetReference.Unresolved(request.Source);

        var relativePath = request
            .Source[aliasPrefix.Length..]
            .TrimStart('/', '\\')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var combinedPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        return new RuntimeAssetReference(
            request.Source,
            RuntimeAssetKind.LocalPath,
            localPath: combinedPath
        );
    }
}

public sealed class InMemoryAssetResolver : IRuntimeAssetResolver
{
    private readonly Dictionary<string, InMemoryRuntimeAsset> assets = new(
        StringComparer.OrdinalIgnoreCase
    );

    public void AddBytes(string source, byte[] content, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(content);
        assets[source] = new InMemoryRuntimeAsset(content, contentType);
    }

    public void AddText(string source, string content, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(content);
        assets[source] = new InMemoryRuntimeAsset(
            System.Text.Encoding.UTF8.GetBytes(content),
            contentType ?? "text/plain; charset=utf-8"
        );
    }

    public RuntimeAssetReference Resolve(RuntimeAssetRequest request)
    {
        if (!assets.TryGetValue(request.Source, out var asset))
            return RuntimeAssetReference.Unresolved(request.Source);

        return new RuntimeAssetReference(
            request.Source,
            RuntimeAssetKind.Stream,
            openStream: () => new MemoryStream(asset.Content, writable: false),
            contentType: asset.ContentType
        );
    }

    private sealed record InMemoryRuntimeAsset(byte[] Content, string? ContentType);
}

public sealed class ManifestResourceAssetResolver : IRuntimeAssetResolver
{
    private readonly Assembly assembly;
    private readonly string manifestResourcePrefix;
    private readonly string? sourcePrefix;

    public ManifestResourceAssetResolver(
        Assembly assembly,
        string manifestResourcePrefix,
        string? sourcePrefix = null
    )
    {
        this.assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        this.manifestResourcePrefix = string.IsNullOrWhiteSpace(manifestResourcePrefix)
            ? throw new ArgumentException(
                "A manifest resource prefix is required.",
                nameof(manifestResourcePrefix)
            )
            : manifestResourcePrefix;
        this.sourcePrefix = sourcePrefix;
    }

    public RuntimeAssetReference Resolve(RuntimeAssetRequest request)
    {
        var source = request.Source;
        if (string.IsNullOrWhiteSpace(source))
            return RuntimeAssetReference.Unresolved(source);

        string resourceSuffix;
        if (string.IsNullOrWhiteSpace(sourcePrefix))
        {
            resourceSuffix = source;
        }
        else
        {
            if (!source.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                return RuntimeAssetReference.Unresolved(source);

            resourceSuffix = source[sourcePrefix.Length..];
        }

        resourceSuffix = resourceSuffix.TrimStart('/', '\\').Replace('/', '.').Replace('\\', '.');
        var resourceName = manifestResourcePrefix + resourceSuffix;
        if (!assembly.GetManifestResourceNames().Contains(resourceName, StringComparer.Ordinal))
            return RuntimeAssetReference.Unresolved(source);

        return new RuntimeAssetReference(
            source,
            RuntimeAssetKind.Stream,
            openStream: () =>
                assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Manifest resource '{resourceName}' could not be opened."
                ),
            contentType: GuessContentType(source)
        );
    }

    private static string? GuessContentType(string source)
    {
        var extension = Path.GetExtension(source);
        return extension.ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".json" => "application/json",
            ".txt" => "text/plain; charset=utf-8",
            _ => null,
        };
    }
}

public sealed class CompositeAssetResolver : IRuntimeAssetResolver
{
    private readonly IReadOnlyList<IRuntimeAssetResolver> resolvers;

    public CompositeAssetResolver(params IRuntimeAssetResolver[] resolvers)
        : this((IEnumerable<IRuntimeAssetResolver>)resolvers) { }

    public CompositeAssetResolver(IEnumerable<IRuntimeAssetResolver> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        this.resolvers = resolvers.Where(static resolver => resolver is not null).ToArray();
    }

    public RuntimeAssetReference Resolve(RuntimeAssetRequest request)
    {
        foreach (var resolver in resolvers)
        {
            var resolved = resolver.Resolve(request);
            if (resolved.IsResolved)
                return resolved;
        }

        return RuntimeAssetReference.Unresolved(request.Source);
    }
}
