using System.Threading;
using Enaga.Html;
using Enaga.Html.Loader;

namespace SampleBrowser;

internal sealed class HtmlDocumentFileWatcher : IDisposable
{
    private readonly HtmlSceneFrameSource source;
    private readonly string htmlPath;
    private readonly string? cssPath;
    private readonly Func<Enaga.Html.HtmlDocument> loadDocument;
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly Timer reloadTimer;
    private readonly object reloadSync = new();
    private bool disposed;

    public HtmlDocumentFileWatcher(HtmlSceneFrameSource source, string htmlPath, string? cssPath, Func<Enaga.Html.HtmlDocument>? loadDocument = null)
    {
        this.source = source;
        this.htmlPath = HtmlDocumentLoader.GetLocalPath(htmlPath);
        this.cssPath = string.IsNullOrWhiteSpace(cssPath) ? null : HtmlDocumentLoader.GetLocalPath(cssPath);
        this.loadDocument = loadDocument ?? (() => HtmlDocumentLoader.Load(this.htmlPath, this.cssPath));
        reloadTimer = new Timer(OnReloadTimerTick);

        AddWatcher(this.htmlPath);
        if (this.cssPath is not null)
            AddWatcher(this.cssPath);
    }

    public void Dispose()
    {
        lock (reloadSync)
        {
            if (disposed)
                return;

            disposed = true;
        }

        reloadTimer.Dispose();
        foreach (var watcher in watchers)
            watcher.Dispose();
    }

    private void AddWatcher(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
            return;

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
        };
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Deleted += OnFileChanged;
        watcher.Renamed += OnFileRenamed;
        watcher.EnableRaisingEvents = true;
        watchers.Add(watcher);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleReload();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        ScheduleReload();
    }

    private void ScheduleReload()
    {
        lock (reloadSync)
        {
            if (disposed)
                return;

            reloadTimer.Change(TimeSpan.FromMilliseconds(120), Timeout.InfiniteTimeSpan);
        }
    }

    private void OnReloadTimerTick(object? state)
    {
        lock (reloadSync)
        {
            if (disposed)
                return;
        }

        if (!TryLoadDocument(out var document))
            return;

        source.UpdateDocument(document);
    }

    private bool TryLoadDocument(out Enaga.Html.HtmlDocument document)
    {
        document = default!;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (!File.Exists(htmlPath))
                    return false;

                document = loadDocument();
                return true;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(40);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }
}
