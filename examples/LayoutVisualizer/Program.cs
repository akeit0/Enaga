using Enaga.Rendering.Skia;
using Enaga.Hosting;
using Enaga.Rendering;
using Enaga.React.OkojoRuntime;

#if HOST_WINDOWS
using Enaga.Platforms.Windows;
#endif
#if HOST_UNIX
using Enaga.Platforms.Mac;
#endif

namespace Enaga.LayoutVisualizer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var (windowTitle, reactEntryPath, layoutSourcePath, reactDebug, debugVisuals, graphicsBackend) = ParseOptions(args);
        using var source = CreateSource(reactEntryPath, layoutSourcePath, reactDebug, debugVisuals);
        using var app = NativeWindowApp.Create(
            new SceneRenderRoot(source),
            windowTitle,
            CreatePlatformIntegration(),
            60,
            RenderTraceLogFlags.None,
            graphicsBackend);
        app.Run();
    }

    private static SkiaRuntimeSceneHost CreateSource(string? reactEntryPath, string layoutSourcePath, bool reactDebug, bool debugVisuals)
    {
        var backendServices = SkiaRuntimeBackendServices.Create();
        IReactAppEntrySource entrySource = string.IsNullOrWhiteSpace(reactEntryPath)
            ? new LayoutVisualizerEntrySource(
                layoutSourcePath,
                Path.Combine(AppContext.BaseDirectory, "../../../../../lib/Enaga.React/src/react-okojo.ts"),
                Path.Combine(AppContext.BaseDirectory, "../../../"),
                debugVisuals)
            : new FileReactAppEntrySource(reactEntryPath);
        var host = new OkojoNodeReactHost(
            entrySource,
            reactDebug,
            false,
            null,
            backendServices,
            enableFileWatching: true,
            reloadMode: ReactRuntimeReloadMode.ReloadModuleGraph);
        return new SkiaRuntimeSceneHost(host);
    }

    private static (string WindowTitle, string? ReactEntryPath, string LayoutSourcePath, bool ReactDebug, bool DebugVisuals, RenderGraphicsBackend GraphicsBackend) ParseOptions(string[] args)
    {
        var windowTitle = "Enaga layout visualizer";
        string? reactEntryPath = null;
        var layoutSourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../layout.jsx"));
        var reactDebug = false;
        var debugVisuals = false;
        var graphicsBackend = OperatingSystem.IsMacOS()
            ? RenderGraphicsBackend.Metal
            : RenderGraphicsBackend.Vulkan;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--title" when index + 1 < args.Length:
                    windowTitle = args[++index];
                    break;
                case "--react-entry" when index + 1 < args.Length:
                    reactEntryPath = Path.GetFullPath(args[++index]);
                    break;
                case "--layout-source" when index + 1 < args.Length:
                    layoutSourcePath = Path.GetFullPath(args[++index]);
                    break;
                case "--react-debug":
                    reactDebug = true;
                    break;
                case "--debug":
                case "--debug-visuals":
                    debugVisuals = true;
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

        return (windowTitle, reactEntryPath, layoutSourcePath, reactDebug, debugVisuals, graphicsBackend);
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
