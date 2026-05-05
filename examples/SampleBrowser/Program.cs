using Enaga.Browser;
using Enaga.Html;
using Enaga.Html.Loader;
using Enaga.Hosting;
using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Silk.NET.Maths;
using System.Globalization;


#if HOST_WINDOWS
using Enaga.Platforms.Windows;
#endif
#if HOST_UNIX
using Enaga.Platforms.Mac;
#endif

namespace SampleBrowser;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var options = ParseOptions(args);
        var timeProvider = TimeProvider.System;
        var backendServices = SkiaRuntimeBackendServices.Create();
        var loadedDocument = SampleBrowserDocumentController.LoadDocument(options.DocumentSource, options.StyleSheetSource, options.DocumentLoadOptions);
        var htmlSource = new HtmlSceneFrameSource(
            loadedDocument.Document,
            new Enaga.Html.HtmlOptions(
                BackendServices: backendServices,
                DefaultFontWeight: 400,
                DefaultFontFamily: OperatingSystem.IsWindows() && CultureInfo.CurrentCulture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "Noto Sans JP" : null,
                TimeProvider: timeProvider));
        ISceneFrameSource source = options.InputLog
            ? new SampleBrowserDiagnosticsFrameSource(htmlSource, timeProvider)
            : htmlSource;
        var toolbarSource = options.ShowUrlBar ? new SampleBrowserToolbarSource(options.DocumentSource, backendServices.Text, timeProvider) : null;
        using var documentController = new SampleBrowserDocumentController(
            htmlSource,
            toolbarSource,
            options.DocumentSource,
            options.StyleSheetSource,
            options.WatchFiles,
            options.DocumentLoadOptions,
            loadedDocument.ScriptRuntime);
        htmlSource.LinkActivated += documentController.HandleActivatedLink;
        htmlSource.ElementClicked += documentController.HandleElementClicked;
        if (toolbarSource is not null)
        {
            toolbarSource.BackRequested += documentController.HandleBackRequested;
            toolbarSource.ForwardRequested += documentController.HandleForwardRequested;
            toolbarSource.RefreshRequested += documentController.HandleRefreshRequested;
            toolbarSource.UrlSubmitted += documentController.HandleUrlSubmitted;
        }
        using var app = NativeWindowApp.Create(
            new SampleBrowserRenderRoot(source, toolbarSource),
            new NativeWindowOptions
            {
                Title = options.WindowTitle,
                InitialSize = new Vector2D<int>(options.WindowWidth, options.WindowHeight),
                PlatformIntegration = CreatePlatformIntegration(),
                FramesPerSecond = 60,
                GraphicsBackend = options.GraphicsBackend,
                TimeProvider = timeProvider
            });
        app.Run();
    }

    private static SampleBrowserOptions ParseOptions(string[] args)
    {
        var windowTitle = "SampleBrowser";
        var windowWidth = 980;
        var windowHeight = 720;
        var documentSource = ResolveDefaultDocumentPath("./navigation-demo.html");
        string? styleSheetSource = null;
        var watchFiles = true;
        var inputLog = false;
        var showUrlBar = true;
        var enableScripts = true;
        string? userAgent = null;
        string? acceptLanguage = null;
        var graphicsBackend = OperatingSystem.IsMacOS()
            ? RenderGraphicsBackend.Metal
            : RenderGraphicsBackend.Vulkan;
        var htmlPathSpecified = false;
        var cssPathSpecified = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--title" when index + 1 < args.Length:
                    windowTitle = args[++index];
                    break;
                case "--width" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedWidth):
                    windowWidth = parsedWidth;
                    index += 1;
                    break;
                case "--height" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedHeight):
                    windowHeight = parsedHeight;
                    index += 1;
                    break;
                case "--html" when index + 1 < args.Length:
                    documentSource = NormalizeDocumentSource(args[++index]);
                    htmlPathSpecified = true;
                    break;
                case "--url" when index + 1 < args.Length:
                    documentSource = NormalizeDocumentSource(args[++index]);
                    htmlPathSpecified = true;
                    break;
                case "--css" when index + 1 < args.Length:
                    styleSheetSource = NormalizeDocumentSource(args[++index]);
                    cssPathSpecified = true;
                    break;
                case "--no-css":
                    styleSheetSource = null;
                    cssPathSpecified = true;
                    break;
                case "--watch":
                    watchFiles = true;
                    break;
                case "--no-watch":
                    watchFiles = false;
                    break;
                case "--input-log":
                case "--log-input":
                    inputLog = true;
                    break;
                case "--url-bar":
                    showUrlBar = true;
                    break;
                case "--no-url-bar":
                    showUrlBar = false;
                    break;
                case "--no-js":
                    enableScripts = false;
                    break;
                case "--user-agent" when index + 1 < args.Length:
                    userAgent = args[++index];
                    break;
                case "--accept-language" when index + 1 < args.Length:
                    acceptLanguage = args[++index];
                    break;
                case "--opengl":
                    graphicsBackend = RenderGraphicsBackend.OpenGl;
                    break;
                case "--metal":
                    graphicsBackend = RenderGraphicsBackend.Metal;
                    break;
                case "--vulkan":
                    graphicsBackend = RenderGraphicsBackend.Vulkan;
                    break;
            }
        }

        if (htmlPathSpecified && !cssPathSpecified)
            styleSheetSource = null;

        if (OperatingSystem.IsMacOS() && graphicsBackend == RenderGraphicsBackend.Vulkan)
            graphicsBackend = RenderGraphicsBackend.Metal;

        var scriptRuntimeOptions = userAgent is null && acceptLanguage is null
            ? null
            : new HtmlBrowserScriptRuntimeOptions(new BrowserRequestProfile(
                userAgent ?? BrowserRequestProfile.Default.UserAgent,
                acceptLanguage ?? BrowserRequestProfile.Default.AcceptLanguage));
        var documentHttpClientOptions = userAgent is null && acceptLanguage is null
            ? null
            : HtmlDocumentHttpClientOptions.Default with
            {
                UserAgent = userAgent ?? HtmlDocumentHttpClientOptions.Default.UserAgent,
                AcceptLanguage = acceptLanguage ?? HtmlDocumentHttpClientOptions.Default.AcceptLanguage
            };
        var documentLoadOptions = new HtmlBrowserDocumentLoadOptions(
            EnableScripts: enableScripts,
            DocumentHttpClientOptions: documentHttpClientOptions,
            ScriptRuntimeOptions: scriptRuntimeOptions);
        var canWatch = HtmlDocumentLoader.IsLocalFileSource(documentSource) &&
                       (styleSheetSource is null || HtmlDocumentLoader.IsLocalFileSource(styleSheetSource));
        return new SampleBrowserOptions(windowTitle, windowWidth, windowHeight, documentSource, styleSheetSource, graphicsBackend, watchFiles && canWatch, inputLog, showUrlBar, documentLoadOptions);
    }

    private sealed record SampleBrowserOptions(
        string WindowTitle,
        int WindowWidth,
        int WindowHeight,
        string DocumentSource,
        string? StyleSheetSource,
        RenderGraphicsBackend GraphicsBackend,
        bool WatchFiles,
        bool InputLog,
        bool ShowUrlBar,
        HtmlBrowserDocumentLoadOptions DocumentLoadOptions);

    private static string NormalizeDocumentSource(string source)
        => HtmlBrowserDocumentLoader.NormalizeNavigationSource(source);

    private static string ResolveDefaultDocumentPath(string fileName)
    {
        var sourceCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", fileName));
        if (File.Exists(sourceCandidate))
            return sourceCandidate;

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, fileName));
    }

    private static INativeWindowPlatformIntegration CreatePlatformIntegration()
    {
#if HOST_WINDOWS
        return new WindowsNativeWindowPlatformIntegration();
#elif HOST_UNIX
        return OperatingSystem.IsMacOS()
            ? new MacNativeWindowPlatformIntegration()
            : new DefaultNativeWindowPlatformIntegration();
#else
        return new DefaultNativeWindowPlatformIntegration();
#endif
    }
}
