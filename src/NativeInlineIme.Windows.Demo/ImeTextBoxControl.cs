using System.ComponentModel;
using System.Drawing;
using NativeInlineIme.Windows;

namespace NativeInlineIme.Windows.Demo;

internal sealed class ImeTextBoxControl : Control, ITextInputClient
{
    private const int HorizontalPadding = 12;
    private const int VerticalPadding = 10;
    private readonly Imm32InputMethod inputMethod = new();
    private string textValue = string.Empty;
    private int caretIndex;
    private int selectionStart;
    private int selectionEnd;
    private string? preeditText;
    private int? preeditCursor;

    public ImeTextBoxControl()
    {
        DoubleBuffered = true;
        TabStop = true;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(15, 23, 42);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable
                | ControlStyles.UserPaint,
            true
        );
        Size = new Size(520, 48);
    }

    public event EventHandler? CursorRectangleChanged;
    public event EventHandler? SurroundingTextChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ResetRequested;

    public bool SupportsPreedit => true;
    public bool SupportsSurroundingText => true;
    public string SurroundingText => textValue;

    public RectangleF CursorRectangle
    {
        get
        {
            var display = BuildDisplayState();
            using var graphics = CreateGraphics();
            var x =
                HorizontalPadding
                + MeasureTextWidth(graphics, display.DisplayText[..display.CaretIndex]);
            return new RectangleF(x, VerticalPadding, 2, Font.Height + 4);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextSelection Selection
    {
        get => new(selectionStart, selectionEnd);
        set
        {
            var start = ClampTextIndex(value.Start);
            var end = ClampTextIndex(value.End);
            if (selectionStart == start && selectionEnd == end)
                return;

            selectionStart = start;
            selectionEnd = end;
            caretIndex = end;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            CursorRectangleChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        inputMethod.Attach(Handle);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        inputMethod.Dispose();
        base.OnHandleDestroyed(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        inputMethod.SetClient(this);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        inputMethod.SetClient(null);
        ClearPreedit();
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        var borderColor = Focused ? Color.FromArgb(37, 99, 235) : Color.FromArgb(148, 163, 184);
        using var borderPen = new Pen(borderColor, Focused ? 2 : 1);
        var borderRect = new Rectangle(0, 0, Width - 1, Height - 1);
        e.Graphics.DrawRectangle(borderPen, borderRect);

        var display = BuildDisplayState();
        var baselineY = VerticalPadding;

        if (display.PreeditLength > 0)
        {
            var preeditLeft =
                HorizontalPadding
                + MeasureTextWidth(e.Graphics, display.DisplayText[..display.PreeditStart]);
            var preeditWidth = MeasureTextWidth(
                e.Graphics,
                display.DisplayText.Substring(display.PreeditStart, display.PreeditLength)
            );
            using var preeditBrush = new SolidBrush(Color.FromArgb(220, 239, 255));
            e.Graphics.FillRectangle(
                preeditBrush,
                preeditLeft,
                baselineY + Font.Height + 1,
                Math.Max(2, preeditWidth),
                3
            );
        }

        if (display.SelectionLeft >= 0 && display.SelectionRight > display.SelectionLeft)
        {
            using var selectionBrush = new SolidBrush(Color.FromArgb(191, 219, 254));
            e.Graphics.FillRectangle(
                selectionBrush,
                display.SelectionLeft,
                baselineY,
                display.SelectionRight - display.SelectionLeft,
                Font.Height + 6
            );
        }

        using var textBrush = new SolidBrush(ForeColor);
        using var format = StringFormat.GenericTypographic;
        e.Graphics.DrawString(
            display.DisplayText,
            Font,
            textBrush,
            new PointF(HorizontalPadding, baselineY),
            format
        );

        if (Focused)
        {
            var caretRect = CursorRectangle;
            using var caretBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
            e.Graphics.FillRectangle(caretBrush, caretRect);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        caretIndex = HitTestTextIndex(e.Location);
        selectionStart = caretIndex;
        selectionEnd = caretIndex;
        ClearPreedit();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        CursorRectangleChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        var keyCode = keyData & Keys.KeyCode;
        return keyCode
                is Keys.Left
                    or Keys.Right
                    or Keys.Home
                    or Keys.End
                    or Keys.Delete
                    or Keys.Back
            || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.KeyCode)
        {
            case Keys.Left:
                MoveCaret(Math.Max(0, caretIndex - 1), e.Shift);
                e.Handled = true;
                break;
            case Keys.Right:
                MoveCaret(Math.Min(textValue.Length, caretIndex + 1), e.Shift);
                e.Handled = true;
                break;
            case Keys.Home:
                MoveCaret(0, e.Shift);
                e.Handled = true;
                break;
            case Keys.End:
                MoveCaret(textValue.Length, e.Shift);
                e.Handled = true;
                break;
            case Keys.Back:
                Delete(backspace: true);
                e.Handled = true;
                break;
            case Keys.Delete:
                Delete(backspace: false);
                e.Handled = true;
                break;
            case Keys.A when e.Control:
                selectionStart = 0;
                selectionEnd = textValue.Length;
                caretIndex = selectionEnd;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                CursorRectangleChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
                e.Handled = true;
                break;
            case Keys.Escape:
                ResetRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (inputMethod.ProcessMessage((uint)m.Msg, m.WParam, m.LParam))
            return;

        if (m.Msg is 0x0102 or 0x0286)
        {
            var value = (char)m.WParam.ToInt32();
            if (inputMethod.ShouldIgnoreChar(value))
                return;

            if (!char.IsControl(value))
            {
                CommitText(value.ToString());
                return;
            }
        }

        base.WndProc(ref m);
    }

    public void SetPreeditText(string? preeditText, int? cursorPosition)
    {
        this.preeditText = preeditText;
        preeditCursor = cursorPosition;
        CursorRectangleChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void CommitText(string text)
    {
        var start = Math.Min(selectionStart, selectionEnd);
        var end = Math.Max(selectionStart, selectionEnd);
        textValue = textValue.Remove(start, end - start).Insert(start, text);
        caretIndex = start + text.Length;
        selectionStart = caretIndex;
        selectionEnd = caretIndex;
        ClearPreedit();
        SurroundingTextChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        CursorRectangleChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void Delete(bool backspace)
    {
        if (selectionStart != selectionEnd)
        {
            CommitText(string.Empty);
            return;
        }

        if (backspace)
        {
            if (caretIndex == 0)
                return;

            selectionStart = caretIndex - 1;
            selectionEnd = caretIndex;
        }
        else
        {
            if (caretIndex >= textValue.Length)
                return;

            selectionStart = caretIndex;
            selectionEnd = caretIndex + 1;
        }

        CommitText(string.Empty);
    }

    private void MoveCaret(int newIndex, bool extendSelection)
    {
        newIndex = ClampTextIndex(newIndex);
        if (extendSelection)
        {
            selectionEnd = newIndex;
            caretIndex = newIndex;
        }
        else
        {
            caretIndex = newIndex;
            selectionStart = newIndex;
            selectionEnd = newIndex;
        }

        ClearPreedit();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        CursorRectangleChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void ClearPreedit()
    {
        preeditText = null;
        preeditCursor = null;
    }

    private int HitTestTextIndex(Point point)
    {
        var display = BuildDisplayState();
        using var graphics = CreateGraphics();
        for (var index = 0; index <= display.DisplayText.Length; index++)
        {
            var width =
                HorizontalPadding + MeasureTextWidth(graphics, display.DisplayText[..index]);
            if (point.X < width)
                return DisplayIndexToTextIndex(display, index);
        }

        return textValue.Length;
    }

    private int ClampTextIndex(int value) => Math.Clamp(value, 0, textValue.Length);

    private DisplayState BuildDisplayState()
    {
        var start = Math.Min(selectionStart, selectionEnd);
        var end = Math.Max(selectionStart, selectionEnd);
        if (string.IsNullOrEmpty(preeditText))
        {
            return new DisplayState(
                textValue,
                -1,
                0,
                caretIndex,
                GetSelectionLeft(),
                GetSelectionRight()
            );
        }

        var displayText = textValue.Remove(start, end - start).Insert(start, preeditText);
        var caretDisplayIndex =
            start + Math.Clamp(preeditCursor ?? preeditText.Length, 0, preeditText.Length);
        return new DisplayState(displayText, start, preeditText.Length, caretDisplayIndex, -1, -1);
    }

    private float GetSelectionLeft()
    {
        if (selectionStart == selectionEnd || !string.IsNullOrEmpty(preeditText))
            return -1;

        using var graphics = CreateGraphics();
        return HorizontalPadding
            + MeasureTextWidth(graphics, textValue[..Math.Min(selectionStart, selectionEnd)]);
    }

    private float GetSelectionRight()
    {
        if (selectionStart == selectionEnd || !string.IsNullOrEmpty(preeditText))
            return -1;

        using var graphics = CreateGraphics();
        return HorizontalPadding
            + MeasureTextWidth(graphics, textValue[..Math.Max(selectionStart, selectionEnd)]);
    }

    private int DisplayIndexToTextIndex(DisplayState display, int displayIndex)
    {
        if (display.PreeditLength == 0 || displayIndex <= display.PreeditStart)
            return ClampTextIndex(displayIndex);

        if (displayIndex <= display.PreeditStart + display.PreeditLength)
            return ClampTextIndex(display.PreeditStart);

        return ClampTextIndex(displayIndex - display.PreeditLength);
    }

    private float MeasureTextWidth(Graphics graphics, string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        using var format = StringFormat.GenericTypographic;
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return graphics.MeasureString(text, Font, int.MaxValue, format).Width;
    }

    private readonly record struct DisplayState(
        string DisplayText,
        int PreeditStart,
        int PreeditLength,
        int CaretIndex,
        float SelectionLeft,
        float SelectionRight
    );
}
