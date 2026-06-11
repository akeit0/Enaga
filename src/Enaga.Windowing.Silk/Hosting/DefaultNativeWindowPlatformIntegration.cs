using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Silk.NET.Windowing;

namespace Enaga.Hosting;

public sealed class DefaultNativeWindowPlatformIntegration : INativeWindowPlatformIntegration
{
    public bool HandlesTextInput => false;

    public DefaultNativeWindowPlatformIntegration()
    {
        ConfigurePlatformFonts();
    }

    public void Attach(IWindow window, IRenderRoot renderRoot) { }

    public void OnRendered() { }

    public void Dispose() { }

    private static void ConfigurePlatformFonts()
    {
        if (OperatingSystem.IsWindows())
        {
            NativeTextConfiguration.ConfigureFonts(
                "Segoe UI",
                "Yu Gothic UI",
                "Yu Gothic",
                "Meiryo",
                "Segoe UI Emoji",
                "Segoe UI Symbol"
            );
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            NativeTextConfiguration.ConfigureFonts(
                "Helvetica Neue",
                "Arial Unicode MS",
                "Hiragino Sans",
                "Apple Color Emoji"
            );
            return;
        }

        NativeTextConfiguration.ConfigureFonts(
            "DejaVu Sans",
            "Noto Sans",
            "Noto Sans CJK JP",
            "Liberation Sans",
            "Noto Color Emoji"
        );
    }
}
