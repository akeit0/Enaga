using System.Diagnostics;
using Enaga.Browser;
using Enaga.Html;
using Enaga.Html.Dom;
using Enaga.Html.Loader;

namespace SampleBrowser;

internal sealed class SampleBrowserDocumentController : IDisposable
{
    private const string BackCommandHref = "SampleBrowser:back";
    private const string ForwardCommandHref = "SampleBrowser:forward";
    private const string RefreshCommandHref = "SampleBrowser:refresh";
    private readonly HtmlSceneFrameSource source;
    private readonly SampleBrowserToolbarSource? toolbarSource;
    private readonly bool watchFiles;
    private readonly bool enableScripts;
    private readonly object sync = new();
    private readonly List<HistoryEntry> history = [];
    private HtmlDocumentFileWatcher? watcher;
    private HtmlBrowserScriptRuntime? scriptRuntime;
    private string currentDocumentSource;
    private string? currentStyleSheetSource;
    private int historyIndex;
    private int navigationVersion;
    private bool disposed;

    public SampleBrowserDocumentController(
        HtmlSceneFrameSource source,
        SampleBrowserToolbarSource? toolbarSource,
        string documentSource,
        string? styleSheetSource,
        bool watchFiles,
        bool enableScripts,
        HtmlBrowserScriptRuntime? scriptRuntime)
    {
        this.source = source;
        this.toolbarSource = toolbarSource;
        this.watchFiles = watchFiles;
        this.enableScripts = enableScripts;
        currentDocumentSource = documentSource;
        currentStyleSheetSource = styleSheetSource;
        history.Add(new HistoryEntry(documentSource, styleSheetSource));
        source.BeforeRenderFrame += PumpScriptRuntimeBeforeRender;
        ReplaceScriptRuntime(scriptRuntime);
        UpdateToolbarState();
        ResetWatcher();
    }

    public static SampleBrowserLoadedDocument LoadDocument(string documentSource, string? styleSheetSource, bool enableScripts)
        => ProcessLoadedDocument(HtmlDocumentLoader.Load(documentSource, styleSheetSource), documentSource, enableScripts);

    public void HandleActivatedLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#", StringComparison.Ordinal))
            return;

        if (string.Equals(href, BackCommandHref, StringComparison.OrdinalIgnoreCase))
        {
            _ = GoBackAsync();
            return;
        }

        if (string.Equals(href, ForwardCommandHref, StringComparison.OrdinalIgnoreCase))
        {
            _ = GoForwardAsync();
            return;
        }

        if (string.Equals(href, RefreshCommandHref, StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshAsync();
            return;
        }

        if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            OpenExternal(href);
            return;
        }

        _ = NavigateAsync(href, styleSheetSource: null, HistoryUpdate.Push);
    }

    public void HandleBackRequested() => _ = GoBackAsync();

    public void HandleForwardRequested() => _ = GoForwardAsync();

    public void HandleRefreshRequested() => _ = RefreshAsync();

    public void HandleUrlSubmitted(string value) => _ = NavigateAsync(value, styleSheetSource: null, HistoryUpdate.Push);

    public void HandleElementClicked(HtmlDomElement element)
    {
        HtmlBrowserScriptRuntime? runtime;
        lock (sync)
            runtime = scriptRuntime;

        runtime?.DispatchClick(element);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;

            disposed = true;
            navigationVersion++;
            watcher?.Dispose();
            watcher = null;
            if (scriptRuntime is not null)
            {
                scriptRuntime.DocumentMutated -= HandleScriptDocumentMutated;
                scriptRuntime.EventLoopWorkQueued -= HandleScriptEventLoopWorkQueued;
                scriptRuntime.NavigationRequested -= HandleScriptNavigationRequested;
                scriptRuntime.TextInputValueResolver = null;
            }
            source.BeforeRenderFrame -= PumpScriptRuntimeBeforeRender;
            scriptRuntime?.Dispose();
            scriptRuntime = null;
        }
    }

    private Task GoBackAsync()
    {
        HistoryEntry entry;
        lock (sync)
        {
            if (disposed || historyIndex <= 0)
                return Task.CompletedTask;

            historyIndex--;
            entry = history[historyIndex];
        }

        return NavigateAsync(entry.DocumentSource, entry.StyleSheetSource, HistoryUpdate.Keep);
    }

    private Task GoForwardAsync()
    {
        HistoryEntry entry;
        lock (sync)
        {
            if (disposed || historyIndex >= history.Count - 1)
                return Task.CompletedTask;

            historyIndex++;
            entry = history[historyIndex];
        }

        return NavigateAsync(entry.DocumentSource, entry.StyleSheetSource, HistoryUpdate.Keep);
    }

    private Task RefreshAsync()
    {
        HistoryEntry entry;
        lock (sync)
        {
            if (disposed || history.Count == 0)
                return Task.CompletedTask;

            entry = history[historyIndex];
        }

        return NavigateAsync(entry.DocumentSource, entry.StyleSheetSource, HistoryUpdate.Keep);
    }

    private Task NavigateAsync(string documentSource, string? styleSheetSource, HistoryUpdate historyUpdate)
    {
        var normalizedSource = NormalizeNavigationSource(documentSource);
        if (string.IsNullOrWhiteSpace(normalizedSource))
            return Task.CompletedTask;

        int version;
        lock (sync)
        {
            if (disposed)
                return Task.CompletedTask;

            currentDocumentSource = normalizedSource;
            currentStyleSheetSource = styleSheetSource;
            if (historyUpdate == HistoryUpdate.Push)
            {
                if (historyIndex < history.Count - 1)
                    history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);

                history.Add(new HistoryEntry(normalizedSource, styleSheetSource));
                historyIndex = history.Count - 1;
            }
            else if (historyUpdate == HistoryUpdate.Replace)
            {
                if (historyIndex >= 0 && historyIndex < history.Count)
                {
                    history[historyIndex] = new HistoryEntry(normalizedSource, styleSheetSource);
                }
                else
                {
                    history.Add(new HistoryEntry(normalizedSource, styleSheetSource));
                    historyIndex = history.Count - 1;
                }
            }

            version = ++navigationVersion;
            watcher?.Dispose();
            watcher = null;
            ReplaceScriptRuntime(null);
            UpdateToolbarState("Loading...");
        }

        source.UpdateDocument(CreateStatusDocument("Loading..."));

        return Task.Run(() =>
        {
            try
            {
                var loaded = HtmlDocumentLoader.Load(normalizedSource, styleSheetSource);
                Enaga.Html.HtmlDocument wrapped;
                lock (sync)
                {
                    if (disposed || version != navigationVersion)
                        return;

                    var processed = ProcessLoadedDocument(loaded, normalizedSource, enableScripts);
                    ReplaceScriptRuntime(processed.ScriptRuntime);
                    wrapped = processed.Document;
                    UpdateToolbarState();
                }

                source.UpdateDocument(wrapped);
                ResetWatcher();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or NotSupportedException or ArgumentException)
            {
                lock (sync)
                {
                    if (disposed || version != navigationVersion)
                        return;
                }

                lock (sync)
                    UpdateToolbarState($"Failed to load: {ex.Message}");
                source.UpdateDocument(CreateStatusDocument($"Failed to load: {ex.Message}"));
            }
        });
    }

    private void ResetWatcher()
    {
        lock (sync)
        {
            watcher?.Dispose();
            watcher = null;
            if (!watchFiles ||
                !HtmlDocumentLoader.IsLocalFileSource(currentDocumentSource) ||
                (currentStyleSheetSource is not null && !HtmlDocumentLoader.IsLocalFileSource(currentStyleSheetSource)))
            {
                return;
            }

            watcher = new HtmlDocumentFileWatcher(
                source,
                currentDocumentSource,
                currentStyleSheetSource,
                () =>
                {
                    var processed = ProcessLoadedDocument(
                        HtmlDocumentLoader.Load(currentDocumentSource, currentStyleSheetSource),
                        currentDocumentSource,
                        enableScripts);
                    lock (sync)
                        ReplaceScriptRuntime(processed.ScriptRuntime);
                    return processed.Document;
                });
        }
    }

    private void ReplaceScriptRuntime(HtmlBrowserScriptRuntime? next)
    {
        if (ReferenceEquals(scriptRuntime, next))
            return;

        if (scriptRuntime is not null)
        {
            scriptRuntime.DocumentMutated -= HandleScriptDocumentMutated;
            scriptRuntime.EventLoopWorkQueued -= HandleScriptEventLoopWorkQueued;
            scriptRuntime.NavigationRequested -= HandleScriptNavigationRequested;
            scriptRuntime.TextInputValueResolver = null;
        }
        scriptRuntime?.Dispose();
        scriptRuntime = next;
        if (scriptRuntime is not null)
        {
            scriptRuntime.DocumentMutated += HandleScriptDocumentMutated;
            scriptRuntime.EventLoopWorkQueued += HandleScriptEventLoopWorkQueued;
            scriptRuntime.NavigationRequested += HandleScriptNavigationRequested;
            scriptRuntime.TextInputValueResolver = source.TryGetTextInputValueByElementId;
            if (scriptRuntime.PendingNavigationRequest is { } pendingNavigation)
                HandleScriptNavigationRequested(pendingNavigation);
        }
    }

    private void HandleScriptDocumentMutated(Enaga.Html.HtmlDocument document)
        => source.UpdateDocument(document);

    private void HandleScriptEventLoopWorkQueued()
        => source.RequestRenderWake();

    private void HandleScriptNavigationRequested(string url)
    {
        var historyUpdate = scriptRuntime?.PendingNavigationReplacesHistory == true
            ? HistoryUpdate.Replace
            : HistoryUpdate.Push;
        _ = NavigateAsync(url, styleSheetSource: null, historyUpdate);
    }

    private void PumpScriptRuntimeBeforeRender()
    {
        HtmlBrowserScriptRuntime? runtime;
        lock (sync)
            runtime = disposed ? null : scriptRuntime;

        runtime?.PumpEventLoopUntilIdle();
    }

    private void UpdateToolbarState(string? message = null)
        => toolbarSource?.SetState(currentDocumentSource, historyIndex > 0, historyIndex < history.Count - 1, message);

    private static SampleBrowserLoadedDocument ProcessLoadedDocument(
        Enaga.Html.HtmlDocument document,
        string documentSource,
        bool enableScripts)
    {
        var scriptRuntime = enableScripts
            ? HtmlBrowserScriptRuntime.CreateAndRun(document, documentSource)
            : null;

        return new SampleBrowserLoadedDocument(scriptRuntime?.CurrentDocument ?? document, scriptRuntime);
    }

    private static Enaga.Html.HtmlDocument CreateStatusDocument(string message)
    {
        var html = $"<body><main class=\"html-viewer-status\"><p>{EscapeHtml(message)}</p></main></body>";
        return new Enaga.Html.HtmlDocument(html);
    }

    private static string NormalizeNavigationSource(string source)
    {
        var trimmed = source.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.IsFile))
        {
            return trimmed;
        }

        if (File.Exists(trimmed))
            return Path.GetFullPath(trimmed);

        return trimmed.Contains('.') && !trimmed.Contains('\\') && !trimmed.Contains('/')
            ? "https://" + trimmed
            : Path.GetFullPath(trimmed);
    }

    private static void OpenExternal(string href)
    {
        try
        {
            Process.Start(new ProcessStartInfo(href) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to open link '{href}': {ex.Message}");
        }
    }

    private static string EscapeHtml(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private enum HistoryUpdate
    {
        Keep,
        Push,
        Replace
    }

    private sealed record HistoryEntry(string DocumentSource, string? StyleSheetSource);
}

internal sealed record SampleBrowserLoadedDocument(Enaga.Html.HtmlDocument Document, HtmlBrowserScriptRuntime? ScriptRuntime);
