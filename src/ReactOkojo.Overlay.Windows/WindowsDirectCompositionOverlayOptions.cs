namespace Enaga.Overlay.Windows;

public sealed class WindowsDirectCompositionOverlayOptions
{
    public required nint TargetWindowHandle { get; init; }

    public int Width { get; init; } = 1;

    public int Height { get; init; } = 1;

    public bool IsTopmostInTarget { get; init; } = true;
}
