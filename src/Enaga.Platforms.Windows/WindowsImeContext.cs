using System.Collections.Generic;
using System.Runtime.InteropServices;
using Enaga.Input;
using Enaga.Rendering;
using NativeInlineIme.Windows;

namespace Enaga.Platforms.Windows;

internal sealed unsafe class WindowsImeContext : IDisposable
{
    private const int GwlWndProc = -4;
    private const uint WmChar = 0x0102;
    private const uint WmImeSetContext = 0x0281;
    private static readonly nint IscShowUiCompositionWindow = unchecked((nint)0x80000000u);
    private readonly nint hwnd;
    private readonly Imm32InputMethod inputMethod;
    private readonly Queue<char> pendingCharacters = new();
    private readonly RenderRootTextInputClient textInputClient;
    private nint previousWndProc;
    private bool pendingVisualUpdate;
    private bool disposed;
    static GCHandle selfHandle;

    public bool HasPendingTextInput => pendingVisualUpdate || pendingCharacters.Count > 0;

    public WindowsImeContext(nint hwnd, ITextCompositionSink compositionSink, IInputSink inputSink)
    {
        selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        this.hwnd = hwnd;
        textInputClient = new RenderRootTextInputClient(compositionSink, inputSink);
        inputMethod = new Imm32InputMethod();
        inputMethod.Attach(hwnd);

        previousWndProc = SetWindowLongPtr(
            hwnd,
            GwlWndProc,
            (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProcCallBack
        );
    }

    public void Update()
    {
        ThrowIfDisposed();
        var cursorRect = textInputClient.CursorRectangle;
        if (cursorRect.IsEmpty)
        {
            inputMethod.SetClient(null);
            return;
        }

        inputMethod.SetClient(textInputClient);
        textInputClient.NotifyCursorRectangleChanged();
        inputMethod.UpdateCursorRect();
    }

    public bool ShouldForwardTextInput(char character)
    {
        ThrowIfDisposed();
        return !inputMethod.IsComposing && !inputMethod.ShouldIgnoreChar(character);
    }

    public void CompleteComposition()
    {
        ThrowIfDisposed();
        if (inputMethod.IsComposing)
            inputMethod.Complete();
    }

    public void FlushPendingTextInput()
    {
        ThrowIfDisposed();
        while (pendingCharacters.Count > 0)
            textInputClient.CommitDirectText(pendingCharacters.Dequeue().ToString());
        pendingVisualUpdate = false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        inputMethod.SetClient(null);
        inputMethod.Dispose();

        if (previousWndProc != 0)
        {
            SetWindowLongPtr(hwnd, GwlWndProc, previousWndProc);
            previousWndProc = 0;
        }
        selfHandle.Free();
        selfHandle = default;
    }

    [UnmanagedCallersOnly]
    private static nint WndProcCallBack(nint windowHandle, uint message, nint wParam, nint lParam)
    {
        var self = (WindowsImeContext)selfHandle.Target!;
        return self.WndProc(windowHandle, message, wParam, lParam);
    }

    private nint WndProc(nint windowHandle, uint message, nint wParam, nint lParam)
    {
        if (message == WmImeSetContext)
        {
            if (wParam != 0)
                lParam &= ~IscShowUiCompositionWindow;

            return CallWindowProc(previousWndProc, windowHandle, message, wParam, lParam);
        }

        if (message == WmChar)
        {
            var character = (char)(ushort)wParam;
            if (
                !char.IsControl(character)
                && !inputMethod.IsComposing
                && !inputMethod.ShouldIgnoreChar(character)
            )
            {
                pendingCharacters.Enqueue(character);
                pendingVisualUpdate = true;
                return 0;
            }
        }

        if (inputMethod.ProcessMessage(message, wParam, lParam))
        {
            pendingVisualUpdate = true;
            return 0;
        }

        return CallWindowProc(previousWndProc, windowHandle, message, wParam, lParam);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private delegate nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(
        nint lpPrevWndFunc,
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam
    );

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
    }
}
