namespace Enaga.React.OkojoRuntime;

public sealed class FileReactAppEntrySource : IReactAppEntrySource
{
    private readonly string entryPath;
    private readonly string assetBasePath;
    private readonly string[] watchPaths;

    public FileReactAppEntrySource(
        string entryPath,
        IEnumerable<string>? watchPaths = null,
        string? assetBasePath = null
    )
    {
        if (string.IsNullOrWhiteSpace(entryPath))
            throw new InvalidOperationException("A valid React entry path is required.");

        this.entryPath = Path.GetFullPath(entryPath);
        this.assetBasePath = string.IsNullOrWhiteSpace(assetBasePath)
            ? this.entryPath
            : Path.GetFullPath(assetBasePath);
        this.watchPaths =
            watchPaths
                ?.Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            ?? [this.entryPath];
    }

    public string DisplayPath => entryPath;

    public string AssetBasePath => assetBasePath;

    public IEnumerable<string> EnumerateWatchPaths()
    {
        return watchPaths;
    }

    public string PrepareEntryPath() => entryPath;

    public void Dispose() { }
}
