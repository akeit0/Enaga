using System.Drawing;

namespace NativeInlineIme.Windows;

public interface ITextInputClient
{
    event EventHandler? CursorRectangleChanged;
    event EventHandler? SurroundingTextChanged;
    event EventHandler? SelectionChanged;
    event EventHandler? ResetRequested;

    bool SupportsPreedit { get; }
    bool SupportsSurroundingText { get; }
    string SurroundingText { get; }
    RectangleF CursorRectangle { get; }
    TextSelection Selection { get; set; }

    void SetPreeditText(string? preeditText, int? cursorPosition);
    void SetPreeditText(string? preeditText, int? cursorPosition, int selectionStart, int selectionLength)
    {
        SetPreeditText(preeditText, cursorPosition);
    }

    void CommitText(string text);
}
