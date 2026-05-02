using Enaga.Hosting;
using Enaga.SampleApp;
using Enaga.Rendering.Skia;
using Silk.NET.Maths;
using Enaga.Platforms.Windows;

namespace Enaga.SampleApp.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var options = SampleAppOptions.Parse(args);
        var diagnostics = options.CreateDiagnosticsSink();
        var graphicsBackend = options.GraphicsBackend;
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
        return new WindowsNativeWindowPlatformIntegration();
    }
}
