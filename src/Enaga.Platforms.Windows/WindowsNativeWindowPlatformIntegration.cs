using Enaga.Hosting;
using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Silk.NET.Windowing;
using Enaga.Input;

namespace Enaga.Platforms.Windows;

public sealed class WindowsNativeWindowPlatformIntegration : INativeWindowPlatformIntegration
{
    private readonly WindowsNativeWindowPlatformOptions options;
    private WindowsImeContext? imeContext;
    private WindowsOverlayWindowStyles? overlayWindowStyles;

    public bool HandlesTextInput => true;
    public bool HasPendingInput => imeContext?.HasPendingTextInput ?? false;

    public bool ShouldForwardTextInput(char character) => imeContext?.ShouldForwardTextInput(character) ?? true;

    public WindowsNativeWindowPlatformIntegration()
        : this(new WindowsNativeWindowPlatformOptions())
    {
    }

    public WindowsNativeWindowPlatformIntegration(WindowsNativeWindowPlatformOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        NativeTextConfiguration.ConfigureFonts("Segoe UI", "Yu Gothic UI", "Yu Gothic", "Meiryo", "Segoe UI Emoji", "Segoe UI Symbol");
    }

    public void Attach(IWindow window, IRenderRoot renderRoot)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var hwnd = TryGetWin32WindowHandle(window);
        if (hwnd == 0)
            return;

        if (ShouldApplyOverlayStyles(options))
            overlayWindowStyles = new WindowsOverlayWindowStyles(hwnd, options);

        if (renderRoot is ITextCompositionSink sink && renderRoot is IInputSink inputSink)
            imeContext = new WindowsImeContext(hwnd, sink, inputSink);
    }

    public void OnRendered()
    {
        // The Windows IME bridge currently assumes one Update() call for each presented frame.
        // Attempts to dedupe or suppress presents in the native host caused inline IME jitter
        // even when cursor updates still ran, so preserve the existing frame cadence here.
        imeContext?.Update();
    }

    public void OnBeforeRender()
    {
        imeContext?.FlushPendingTextInput();
    }

    public void OnPointerDown(int button)
    {
        if (button == 0)
            imeContext?.CompleteComposition();
    }

    public void Dispose()
    {
        overlayWindowStyles?.Dispose();
        overlayWindowStyles = null;
        imeContext?.Dispose();
        imeContext = null;
    }

    private static bool ShouldApplyOverlayStyles(WindowsNativeWindowPlatformOptions options)
    {
        return options.OwnerWindowHandle != 0 ||
               options.MousePassthrough ||
               options.HideFromTaskbarAndAltTab ||
               options.NoActivate;
    }

    private nint TryGetWin32WindowHandle(IWindow nextWindow)
    {
        var native = nextWindow.Native;
        var nativeHwnd = native?.Win32?.Hwnd ?? 0;
        if (nativeHwnd != 0)
            return nativeHwnd;

        return 0;
    }
}
