using System.Runtime.InteropServices;

namespace NativeInlineIme.Windows;

internal struct Imm32CaretManager
{
    private bool isCaretCreated;

    public void TryCreate(nint hwnd)
    {
        if (!isCaretCreated)
            isCaretCreated = CreateCaret(hwnd, 0, 2, 2);
    }

    public void TryMove(int x, int y)
    {
        if (isCaretCreated)
            SetCaretPos(x, y);
    }

    public void TryDestroy()
    {
        if (!isCaretCreated)
            return;

        DestroyCaret();
        isCaretCreated = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateCaret(nint hWnd, nint hBitmap, int nWidth, int nHeight);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCaretPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyCaret();
}
