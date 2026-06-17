using Enaga.Hosting;
#if HOST_WINDOWS
using Enaga.Platforms.Windows;
#endif
#if HOST_UNIX
using Enaga.Platforms.Mac;
#endif

namespace Enaga.Platforms.Defaults;

public static class NativeWindowPlatformDefaults
{
    public static INativeWindowPlatformIntegration CreatePlatformIntegration()
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

    public static RenderGraphicsBackend GraphicsBackend => ResolveGraphicsBackend();

    public static RenderGraphicsBackend ResolveGraphicsBackend()
    {
        if (OperatingSystem.IsMacOS())
            return RenderGraphicsBackend.Metal;

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            return RenderGraphicsBackend.Vulkan;

        return RenderGraphicsBackend.OpenGl;
    }

    public static RenderGraphicsBackend NormalizeGraphicsBackend(RenderGraphicsBackend requested)
    {
        if (requested == RenderGraphicsBackend.Metal && !OperatingSystem.IsMacOS())
            return ResolveGraphicsBackend();

        if (requested == RenderGraphicsBackend.Vulkan && OperatingSystem.IsMacOS())
            return RenderGraphicsBackend.Metal;

        return requested;
    }
}
