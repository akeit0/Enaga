using System.Drawing;
using NativeInlineIme.Windows;
using Enaga.Rendering;
using Enaga.Input;
namespace Enaga.Platforms.Windows;

internal sealed class RenderRootTextInputClient : ITextInputClient
{
    private readonly ITextCompositionSink compositionSink;
    private readonly IInputSink inputSink;
    private bool compositionActive;

    public RenderRootTextInputClient(ITextCompositionSink compositionSink, IInputSink inputSink)
    {
        this.compositionSink = compositionSink;
        this.inputSink = inputSink;
    }

    public event EventHandler? CursorRectangleChanged;
    public event EventHandler? SurroundingTextChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ResetRequested;

    public bool SupportsPreedit => true;

    public bool SupportsSurroundingText => false;

    public string SurroundingText => string.Empty;

    public RectangleF CursorRectangle
    {
        get
        {
            if (!compositionSink.TryGetTextCompositionCursor(out var cursor))
                return RectangleF.Empty;

            return new RectangleF(
                cursor.X,
                cursor.Y,
                Math.Max(1, cursor.Width),
                Math.Max(1, cursor.Height));
        }
    }

    public TextSelection Selection { get; set; }

    public void SetPreeditText(string? preeditText, int? cursorPosition)
    {
        SetPreeditText(preeditText, cursorPosition, 0, preeditText?.Length ?? 0);
    }

    public void SetPreeditText(string? preeditText, int? cursorPosition, int selectionStart, int selectionLength)
    {
        if (string.IsNullOrEmpty(preeditText))
        {
            if (!compositionActive)
                return;

            compositionActive = false;
            compositionSink.EndTextComposition();
            return;
        }

        if (!compositionActive)
        {
            compositionActive = true;
            compositionSink.StartTextComposition();
        }

        compositionSink.UpdateTextComposition(
            preeditText,
            cursorPosition ?? preeditText.Length,
            selectionStart,
            selectionLength);
    }

    public void CommitText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        compositionSink.PrepareTextCompositionCommit();
        inputSink.TextInput(text, synthetic: false);
        SurroundingTextChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        CursorRectangleChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CommitDirectText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // WM_CHAR is a real native text path, but the runtime's printable-repeat
        // synthesis assumes non-synthetic text is only the first accepted native
        // character for a held key. Route direct Windows character input through
        // the synthetic flag so native WM_CHAR repeat stays authoritative.
        inputSink.TextInput(text, synthetic: true);
        SurroundingTextChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        CursorRectangleChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyCursorRectangleChanged()
    {
        CursorRectangleChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyResetRequested()
    {
        ResetRequested?.Invoke(this, EventArgs.Empty);
    }
}
