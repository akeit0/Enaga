namespace Enaga.React.OkojoRuntime;

internal sealed class RuntimeFileWatchService : IDisposable
{
    private readonly List<FileSystemWatcher> watchers = [];

    public RuntimeFileWatchService(IEnumerable<string> watchPaths, IEnumerable<string>? watchPatterns = null)
    {
        ArgumentNullException.ThrowIfNull(watchPaths);

        var patterns = watchPatterns?.Where(static pattern => !string.IsNullOrWhiteSpace(pattern)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (patterns is null || patterns.Length == 0)
            patterns = ["*.mjs"];

        var seenWatchers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in watchPaths.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            var fullPath = Path.GetFullPath(rawPath);
            if (Directory.Exists(fullPath))
            {
                foreach (var pattern in patterns)
                    AddDirectoryWatcher(fullPath, pattern, seenWatchers);
                continue;
            }

            AddFileWatcher(fullPath, seenWatchers);
        }
    }

    public event EventHandler<string>? Changed;

    public void Dispose()
    {
        foreach (var watcher in watchers)
            watcher.Dispose();
    }

    private void AddDirectoryWatcher(string directory, string filter, HashSet<string> seenWatchers)
    {
        var key = $"{directory}|{filter}";
        if (!seenWatchers.Add(key))
            return;

        var watcher = new FileSystemWatcher(directory, filter)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
        };
        AttachHandlers(watcher);
    }

    private void AddFileWatcher(string fullPath, HashSet<string> seenWatchers)
    {
        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException($"A valid watch path is required: '{fullPath}'.");

        var key = $"{directory}|{fileName}";
        if (!seenWatchers.Add(key))
            return;

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
        };
        AttachHandlers(watcher);
    }

    private void AttachHandlers(FileSystemWatcher watcher)
    {
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.EnableRaisingEvents = true;
        watchers.Add(watcher);
    }

    private void OnChanged(object? _, FileSystemEventArgs args)
    {
        Changed?.Invoke(this, Path.GetFullPath(args.FullPath));
    }
}
