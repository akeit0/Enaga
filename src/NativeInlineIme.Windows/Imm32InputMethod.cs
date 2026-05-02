using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

namespace NativeInlineIme.Windows;

public sealed class Imm32InputMethod : IDisposable
{
    private const int CaretMargin = 1;
    private const int LangJapanese = 0x11;
    private const int LangKorean = 0x12;
    private readonly Imm32CaretManager caretManager = new();
    private nint hwnd;
    private nint currentHimc;
    private ushort langId;
    private bool ignoreComposition;
    private readonly Queue<char> suppressedChars = new();

    public ITextInputClient? Client { get; private set; }

    public bool IsActive => Client is not null;

    public bool IsComposing { get; private set; }

    public string? Composition { get; private set; }

    public void Attach(nint hwnd)
    {
        this.hwnd = hwnd;
        UpdateLanguage(GetKeyboardLayout(0));
    }

    public void Dispose()
    {
        SetClient(null);
        hwnd = 0;
    }

    public void SetClient(ITextInputClient? client)
    {
        if (ReferenceEquals(Client, client))
            return;

        if (Client is not null)
        {
            Client.CursorRectangleChanged -= OnCursorRectangleChanged;
            Client.ResetRequested -= OnResetRequested;
            Client.SetPreeditText(null, null);
        }

        Client = client;

        if (Client is not null)
        {
            Client.CursorRectangleChanged += OnCursorRectangleChanged;
            Client.ResetRequested += OnResetRequested;
            EnableImm();
            UpdateCursorRect();
        }
        else
        {
            DisableImm();
        }
    }

    public void UpdateLanguage(nint hkl)
    {
        langId = PrimaryLangId(LowWord(hkl));
    }

    public void UpdateCursorRect()
    {
        if (hwnd == 0 || Client is null)
            return;

        var himc = ImmGetContext(hwnd);
        if (himc == 0)
            return;

        try
        {
            MoveImeWindow(Client.CursorRectangle, himc);
        }
        finally
        {
            ImmReleaseContext(hwnd, himc);
        }
    }

    public void Reset()
    {
        if (hwnd == 0)
            return;

        var himc = ImmGetContext(hwnd);
        if (himc == 0)
            return;

        try
        {
            if (IsComposing)
                ignoreComposition = true;

            ImmNotifyIME(himc, NiCompositionStr, CpsComplete, 0);
        }
        finally
        {
            ImmReleaseContext(hwnd, himc);
        }

        IsComposing = false;
        Composition = null;
        Client?.SetPreeditText(null, null);
    }

    public void Complete()
    {
        if (hwnd == 0 || !IsComposing)
            return;

        var himc = ImmGetContext(hwnd);
        if (himc == 0)
            return;

        try
        {
            ImmNotifyIME(himc, NiCompositionStr, CpsComplete, 0);
        }
        finally
        {
            ImmReleaseContext(hwnd, himc);
        }
    }

    public bool ProcessMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmInputLangChange:
                UpdateLanguage(lParam);
                return false;
            case WmImeStartComposition:
                HandleCompositionStart();
                return true;
            case WmImeEndComposition:
                HandleCompositionEnd();
                return true;
            case WmImeComposition:
                HandleComposition(lParam);
                return true;
            default:
                return false;
        }
    }

    public bool ShouldIgnoreChar(char value)
    {
        if (suppressedChars.Count == 0)
            return false;

        if (suppressedChars.Peek() != value)
        {
            suppressedChars.Clear();
            return false;
        }

        suppressedChars.Dequeue();
        return true;
    }

    private void HandleCompositionStart()
    {
        IsComposing = true;
        Composition = null;
        Client?.SetPreeditText(null, null);
        UpdateCursorRect();
    }

    private void HandleCompositionEnd()
    {
        IsComposing = false;
        Composition = null;
        Client?.SetPreeditText(null, null);
    }

    private void HandleComposition(nint lParam)
    {
        if (ignoreComposition)
        {
            ignoreComposition = false;
            return;
        }

        var flags = LowWord(lParam);
        if (flags == 0)
        {
            Composition = string.Empty;
            Client?.SetPreeditText(string.Empty, 0);
            return;
        }

        var himc = ImmGetContext(hwnd);
        if (himc == 0)
            return;

        try
        {
            if ((flags & GcsResultStr) != 0)
            {
                var result = GetCompositionString(himc, GcsResultStr);
                if (!string.IsNullOrEmpty(result))
                {
                    Composition = null;
                    Client?.CommitText(result);
                    Client?.SetPreeditText(null, null);
                    suppressedChars.Clear();
                    foreach (var character in result)
                        suppressedChars.Enqueue(character);
                }
            }

            if ((flags & GcsCompStr) != 0)
            {
                var composition = GetCompositionString(himc, GcsCompStr) ?? string.Empty;
                var cursor = Math.Max(0, ImmGetCompositionStringW(himc, GcsCursorPos, null, 0));
                var selection = GetTargetRange(himc, composition.Length, cursor);
                Composition = composition;
                Client?.SetPreeditText(composition, cursor, selection.Start, selection.End - selection.Start);
                MoveImeWindow(Client?.CursorRectangle ?? RectangleF.Empty, himc);
            }
        }
        finally
        {
            ImmReleaseContext(hwnd, himc);
        }
    }

    private void EnableImm()
    {
        if (hwnd == 0)
            return;

        var himc = ImmGetContext(hwnd);
        if (himc == 0)
            himc = ImmCreateContext();

        if (himc == 0)
            return;

        if (himc != currentHimc)
        {
            if (currentHimc != 0)
                DisableImm();

            ImmAssociateContext(hwnd, himc);
            ImmReleaseContext(hwnd, himc);
            currentHimc = himc;
            caretManager.TryCreate(hwnd);
        }
    }

    private void DisableImm()
    {
        caretManager.TryDestroy();
        Reset();
        if (hwnd != 0)
            ImmAssociateContext(hwnd, 0);
        caretManager.TryDestroy();
        currentHimc = 0;
        suppressedChars.Clear();
    }

    private void MoveImeWindow(RectangleF rect, nint himc)
    {
        if (rect.IsEmpty)
            return;

        var x1 = (int)Math.Floor(rect.Left);
        var y1 = (int)Math.Floor(rect.Top);
        var x2 = (int)Math.Ceiling(rect.Right);
        var y2 = (int)Math.Ceiling(rect.Bottom);

        caretManager.TryCreate(hwnd);
        caretManager.TryMove(x2, y2);

        if (langId == LangKorean)
            y2 += CaretMargin;

        var form = new CandidateForm
        {
            Index = 0,
            Style = CfsExclude,
            CurrentPosition = new PointL { X = x1, Y = y1 },
            Area = new RectL
            {
                Left = x1,
                Top = y1,
                Right = x2,
                Bottom = y2 + CaretMargin
            }
        };

        ImmSetCandidateWindow(himc, ref form);
    }

    private static string? GetCompositionString(nint himc, int flag)
    {
        var byteCount = ImmGetCompositionStringW(himc, flag, null, 0);
        if (byteCount <= 0)
            return null;

        var buffer = new byte[byteCount];
        var copied = ImmGetCompositionStringW(himc, flag, buffer, buffer.Length);
        if (copied <= 0)
            return null;

        return Encoding.Unicode.GetString(buffer, 0, copied);
    }

    private static TextSelection GetTargetRange(nint himc, int compositionLength, int cursor)
    {
        var attributes = GetCompositionBytes(himc, GcsCompAttr);
        if (attributes is not null && attributes.Length > 0)
        {
            var count = Math.Min(attributes.Length, compositionLength);
            var targetStart = -1;
            for (var index = 0; index < count; index++)
            {
                if (IsTargetAttribute(attributes[index]))
                {
                    targetStart = index;
                    break;
                }
            }

            if (targetStart >= 0)
            {
                var targetEnd = targetStart + 1;
                while (targetEnd < count && IsTargetAttribute(attributes[targetEnd]))
                    targetEnd++;

                return new TextSelection(targetStart, targetEnd);
            }
        }

        var caret = Math.Clamp(cursor, 0, compositionLength);
        return new TextSelection(caret, caret);
    }

    private static byte[]? GetCompositionBytes(nint himc, int flag)
    {
        var byteCount = ImmGetCompositionStringW(himc, flag, null, 0);
        if (byteCount <= 0)
            return null;

        var buffer = new byte[byteCount];
        var copied = ImmGetCompositionStringW(himc, flag, buffer, buffer.Length);
        if (copied <= 0)
            return null;

        if (copied == buffer.Length)
            return buffer;

        Array.Resize(ref buffer, copied);
        return buffer;
    }

    private static bool IsTargetAttribute(byte attribute)
    {
        return attribute is AttrTargetConverted or AttrTargetNotConverted;
    }

    private void OnCursorRectangleChanged(object? sender, EventArgs e)
    {
        if (sender == Client)
            UpdateCursorRect();
    }

    private void OnResetRequested(object? sender, EventArgs e)
    {
        if (sender == Client)
            Reset();
    }

    private static ushort LowWord(nint value) => unchecked((ushort)(value.ToInt64() & 0xffff));

    private static ushort PrimaryLangId(ushort value) => (ushort)(value & 0x3ff);

    private const uint WmInputLangChange = 0x0051;
    private const uint WmImeStartComposition = 0x010D;
    private const uint WmImeEndComposition = 0x010E;
    private const uint WmImeComposition = 0x010F;
    private const int GcsCompStr = 0x0008;
    private const int GcsCompAttr = 0x0010;
    private const int GcsResultStr = 0x0800;
    private const int GcsCursorPos = 0x0080;
    private const int CfsExclude = 0x0080;
    private const int NiCompositionStr = 0x0015;
    private const int CpsComplete = 0x0001;
    private const byte AttrTargetConverted = 0x01;
    private const byte AttrTargetNotConverted = 0x03;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectL
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CandidateForm
    {
        public int Index;
        public int Style;
        public PointL CurrentPosition;
        public RectL Area;
    }

    [DllImport("imm32.dll")]
    private static extern nint ImmGetContext(nint hWnd);

    [DllImport("imm32.dll")]
    private static extern nint ImmCreateContext();

    [DllImport("imm32.dll")]
    private static extern nint ImmAssociateContext(nint hWnd, nint hImc);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(nint hWnd, nint hImc);

    [DllImport("imm32.dll")]
    private static extern int ImmGetCompositionStringW(nint hImc, int dwIndex, byte[]? lpBuf, int dwBufLen);

    [DllImport("imm32.dll")]
    private static extern int ImmSetCandidateWindow(nint hImc, ref CandidateForm candidate);

    [DllImport("imm32.dll")]
    private static extern bool ImmNotifyIME(nint hImc, int dwAction, int dwIndex, int dwValue);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint idThread);
}
