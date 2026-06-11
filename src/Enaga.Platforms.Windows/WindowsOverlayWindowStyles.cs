using System.Runtime.InteropServices;

namespace Enaga.Platforms.Windows;

internal sealed class WindowsOverlayWindowStyles : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_WNDPROC = -4;
    private const int GWLP_HWNDPARENT = -8;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const nint MA_NOACTIVATE = 3;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private readonly nint hwnd;
    private readonly nint previousStyle;
    private readonly nint previousOwner;
    private readonly WndProc? wndProc;
    private nint previousWndProc;
    private bool disposed;

    public WindowsOverlayWindowStyles(nint hwnd, WindowsNativeWindowPlatformOptions options)
    {
        this.hwnd = hwnd;
        previousStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        previousOwner = GetWindowLongPtr(hwnd, GWLP_HWNDPARENT);

        if (options.OwnerWindowHandle != 0)
            _ = SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, options.OwnerWindowHandle);

        var style = previousStyle;
        if (options.MousePassthrough)
            style |= WS_EX_TRANSPARENT;
        if (options.HideFromTaskbarAndAltTab)
        {
            style |= WS_EX_TOOLWINDOW;
            style &= ~WS_EX_APPWINDOW;
        }
        if (options.NoActivate)
            style |= WS_EX_NOACTIVATE;

        if (style != previousStyle)
        {
            _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, style);
            ApplyFrameChanged(hwnd);
        }

        if (options.NoActivate)
        {
            wndProc = WndProcCallback;
            previousWndProc = SetWindowLongPtr(
                hwnd,
                GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(wndProc)
            );
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (hwnd == 0)
            return;

        if (previousWndProc != 0)
        {
            _ = SetWindowLongPtr(hwnd, GWLP_WNDPROC, previousWndProc);
            previousWndProc = 0;
        }

        _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, previousStyle);
        _ = SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, previousOwner);
        ApplyFrameChanged(hwnd);
    }

    private nint WndProcCallback(nint windowHandle, uint message, nint wParam, nint lParam)
    {
        if (message == WM_MOUSEACTIVATE)
            return MA_NOACTIVATE;

        return CallWindowProc(previousWndProc, windowHandle, message, wParam, lParam);
    }

    private static void ApplyFrameChanged(nint hwnd)
    {
        _ = SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED
        );
    }

    private static nint GetWindowLongPtr(nint hWnd, int index)
    {
        return Environment.Is64BitProcess
            ? GetWindowLongPtr64(hWnd, index)
            : GetWindowLong32(hWnd, index);
    }

    private static nint SetWindowLongPtr(nint hWnd, int index, nint value)
    {
        return Environment.Is64BitProcess
            ? SetWindowLongPtr64(hWnd, index, value)
            : SetWindowLong32(hWnd, index, value.ToInt32());
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags
    );

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(
        nint previousWndProc,
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam
    );

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);
}
