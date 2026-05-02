using Enaga.Hosting;
using Enaga.Rendering.Skia;
using Silk.NET.Maths;
using System.Runtime.InteropServices;
#if HOST_WINDOWS
using Enaga.Platforms.Windows;
#endif
#if HOST_UNIX
using Enaga.Platforms.Mac;
#endif

namespace Enaga.SampleApp;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var options = SampleAppOptions.Parse(args);
        var diagnostics = options.CreateDiagnosticsSink();
        var graphicsBackend = options.GraphicsBackend;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && graphicsBackend == RenderGraphicsBackend.Vulkan)
        {
            Console.WriteLine("Vulkan is not supported on macOS. Falling back to Metal.");
            graphicsBackend = RenderGraphicsBackend.Metal;
        }
        using var source = SampleAppRuntime.CreateSource(options, diagnostics);
        using var app = NativeWindowApp.Create(
            new SceneRenderRoot(source, options.RenderStats || options.TraceLogFlags != RenderTraceLogFlags.None, viewCounter: options.TraceViewCalls),
            new NativeWindowOptions
            {
                Title = options.WindowTitle,
                InitialSize = new Vector2D<int>(options.WindowWidth, options.WindowHeight),
                PlatformIntegration = CreatePlatformIntegration(),
                FramesPerSecond = options.FramesPerSecond,
                TraceLogFlags = options.TraceLogFlags,
                GraphicsBackend = graphicsBackend,
                Diagnostics = diagnostics
            });
        app.Run();
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
