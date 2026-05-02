namespace Enaga.Platforms.Windows;

public sealed class WindowsNativeWindowPlatformOptions
{
    public nint OwnerWindowHandle { get; init; }

    public bool MousePassthrough { get; init; }

    public bool HideFromTaskbarAndAltTab { get; init; }

    public bool NoActivate { get; init; }
}
