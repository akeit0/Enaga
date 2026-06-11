using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

public sealed class HtmlTextInputController(
    IRuntimeTextServices textServices,
    Action requestUpdate,
    Func<bool, bool> moveFocus,
    Action<SceneNodeId?> setFocus
)
{
    private const int ShiftModifier = 1;
    private const int ControlModifier = 2;
    private const int AltModifier = 4;
    private const int MetaModifier = 8;

    public void ApplyTextInput(HtmlTextInputState state, string text)
    {
        HtmlTextInputStateLogic.ApplyTextInput(state, textServices, text);
        requestUpdate();
    }

    public void HandleKey(HtmlTextInputState state, string key, int modifiers)
    {
        var handled = false;
        var wordJump = (modifiers & AltModifier) != 0 && OperatingSystem.IsMacOS();
        var command = (modifiers & (ControlModifier | MetaModifier)) != 0;
        var extendSelection = (modifiers & ShiftModifier) != 0;

        switch (key)
        {
            case "A" when command:
            case "a" when command:
                SelectAll(state);
                handled = true;
                break;
            case "C" when command:
            case "c" when command:
                handled = CopySelectionToClipboard(state);
                break;
            case "X" when command:
            case "x" when command:
                if (CopySelectionToClipboard(state) && DeleteSelection(state))
                    handled = true;
                break;
            case "V" when command:
            case "v" when command:
                handled = TryInsertClipboardText(state);
                break;
            case "Tab":
                handled = moveFocus((modifiers & ShiftModifier) == 0);
                break;
            case "Backspace":
            case "BackSpace":
                handled = DeleteBeforeCaret(state, wordJump);
                break;
            case "Delete":
                handled = DeleteAfterCaret(state, wordJump);
                break;
            case "Left":
            case "ArrowLeft":
                MoveHorizontal(state, -1, extendSelection, wordJump);
                handled = true;
                break;
            case "Right":
            case "ArrowRight":
                MoveHorizontal(state, 1, extendSelection, wordJump);
                handled = true;
                break;
            case "Up" when state.Multiline:
            case "ArrowUp" when state.Multiline:
                MoveCaretVertically(state, -1, extendSelection);
                handled = true;
                break;
            case "Down" when state.Multiline:
            case "ArrowDown" when state.Multiline:
                MoveCaretVertically(state, 1, extendSelection);
                handled = true;
                break;
            case "Home":
                MoveSelectionCaret(state, MoveCaretToLineEdge(state, false), extendSelection);
                handled = true;
                break;
            case "End":
                MoveSelectionCaret(state, MoveCaretToLineEdge(state, true), extendSelection);
                handled = true;
                break;
            case "Enter" when state.Multiline:
                ApplyTextInput(state, "\n");
                return;
            case "Escape":
                setFocus(null);
                handled = true;
                break;
        }

        if (handled)
            requestUpdate();
    }

    public bool HasSelection(HtmlTextInputState state) =>
        HtmlTextInputStateLogic.HasSelection(state);

    public void MoveSelectionCaret(HtmlTextInputState state, int caretIndex, bool extendSelection)
    {
        if (extendSelection)
            SetSelection(state, state.SelectionAnchorIndex, caretIndex);
        else
        {
            state.CaretIndex = textServices.SnapCaretIndex(
                state.Text,
                Math.Clamp(caretIndex, 0, state.Text.Length)
            );
            state.PreferredCaretX = null;
            ClearSelection(state);
        }
    }

    public void ClearSelection(HtmlTextInputState state) =>
        HtmlTextInputStateLogic.ClearSelection(state);

    public void SetSelection(HtmlTextInputState state, int anchorIndex, int caretIndex) =>
        HtmlTextInputStateLogic.SetSelection(state, textServices, anchorIndex, caretIndex);

    public void SelectAll(HtmlTextInputState state)
    {
        state.SelectionAnchorIndex = 0;
        state.SelectionStart = 0;
        state.SelectionEnd = state.Text.Length;
        state.CaretIndex = state.Text.Length;
        state.PreferredCaretX = null;
    }

    public bool DeleteSelection(HtmlTextInputState state)
    {
        var deleted = HtmlTextInputStateLogic.DeleteSelection(state);
        if (deleted)
            state.PendingHostText = state.Text;
        return deleted;
    }

    public void SelectWordAt(HtmlTextInputState state, int caretIndex)
    {
        if (state.Text.Length == 0)
        {
            ClearSelection(state);
            return;
        }

        var snappedCaret = textServices.SnapCaretIndex(
            state.Text,
            Math.Clamp(caretIndex, 0, state.Text.Length)
        );
        var start = snappedCaret;
        var end = snappedCaret;

        if (start == state.Text.Length)
            start = end = textServices.SnapCaretIndex(
                state.Text,
                Math.Max(0, state.Text.Length - 1)
            );

        while (start > 0 && IsWordCharacter(state.Text[start - 1]))
            start = textServices.SnapCaretIndex(state.Text, start - 1);

        while (end < state.Text.Length && IsWordCharacter(state.Text[end]))
            end = textServices.SnapCaretIndex(state.Text, end + 1);

        SetSelection(state, start, end);
    }

    private void MoveHorizontal(
        HtmlTextInputState state,
        int direction,
        bool extendSelection,
        bool wordJump
    )
    {
        if (!extendSelection && HasSelection(state))
        {
            var collapsed =
                direction < 0
                    ? Math.Min(state.SelectionStart, state.SelectionEnd)
                    : Math.Max(state.SelectionStart, state.SelectionEnd);
            MoveSelectionCaret(state, collapsed, false);
            return;
        }

        var next = wordJump
            ? MoveCaretByWord(state.Text, state.CaretIndex, direction)
            : state.CaretIndex + direction;
        MoveSelectionCaret(state, next, extendSelection);
    }

    private bool DeleteBeforeCaret(HtmlTextInputState state, bool wordJump)
    {
        if (DeleteSelection(state))
            return true;
        if (state.CaretIndex == 0)
            return false;

        var start = wordJump
            ? MoveCaretByWord(state.Text, state.CaretIndex, -1)
            : textServices.GetPreviousTextElementIndex(state.Text, state.CaretIndex);
        var end = state.CaretIndex;
        state.Text = state.Text.Remove(start, end - start);
        state.CaretIndex = start;
        state.PreferredCaretX = null;
        ClearSelection(state);
        state.PendingHostText = state.Text;
        return true;
    }

    private bool DeleteAfterCaret(HtmlTextInputState state, bool wordJump)
    {
        if (DeleteSelection(state))
            return true;
        if (state.CaretIndex >= state.Text.Length)
            return false;

        var end = wordJump
            ? MoveCaretByWord(state.Text, state.CaretIndex, 1)
            : textServices.GetNextTextElementIndex(state.Text, state.CaretIndex);
        state.Text = state.Text.Remove(state.CaretIndex, end - state.CaretIndex);
        state.PreferredCaretX = null;
        ClearSelection(state);
        state.PendingHostText = state.Text;
        return true;
    }

    private void MoveCaretVertically(HtmlTextInputState state, int lineDelta, bool extendSelection)
    {
        var textStyle = CreateTextInputTextStyle(state);
        var textWidth = Math.Max(0, state.Width - state.PaddingLeft - state.PaddingRight);
        var preferredX =
            state.PreferredCaretX
            ?? textServices
                .GetCaretPosition(
                    textStyle,
                    state.Text,
                    state.LineHeight,
                    textWidth,
                    state.CaretIndex
                )
                .X;
        var caretIndex = textServices.MoveCaretVertical(
            textStyle,
            state.Text,
            state.LineHeight,
            textWidth,
            state.CaretIndex,
            lineDelta,
            preferredX
        );
        state.PreferredCaretX = preferredX;
        MoveSelectionCaret(state, caretIndex, extendSelection);
    }

    private int MoveCaretToLineEdge(HtmlTextInputState state, bool toEnd)
    {
        return textServices.MoveCaretToLineEdge(
            CreateTextInputTextStyle(state),
            state.Text,
            state.LineHeight,
            Math.Max(0, state.Width - state.PaddingLeft - state.PaddingRight),
            state.CaretIndex,
            toEnd
        );
    }

    private string? GetSelectedText(HtmlTextInputState state)
    {
        if (!HasSelection(state))
            return null;

        var selectionStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        var selectionLength = Math.Abs(state.SelectionEnd - state.SelectionStart);
        return state.Text.Substring(selectionStart, selectionLength);
    }

    private bool CopySelectionToClipboard(HtmlTextInputState state)
    {
        var selectedText = GetSelectedText(state);
        return !string.IsNullOrEmpty(selectedText) && HtmlClipboardService.SetText(selectedText);
    }

    private bool TryInsertClipboardText(HtmlTextInputState state)
    {
        var text = HtmlClipboardService.GetText();
        if (string.IsNullOrEmpty(text))
            return false;

        DeleteSelection(state);
        state.Text = state.Text.Insert(state.CaretIndex, text);
        state.CaretIndex = textServices.SnapCaretIndex(state.Text, state.CaretIndex + text.Length);
        state.CompositionText = string.Empty;
        state.CompositionCursorOffset = 0;
        state.PreferredCaretX = null;
        ClearSelection(state);
        state.PendingHostText = state.Text;
        return true;
    }

    private int MoveCaretByWord(string text, int caretIndex, int direction)
    {
        var index = textServices.SnapCaretIndex(text, Math.Clamp(caretIndex, 0, text.Length));
        if (direction < 0)
        {
            while (index > 0 && char.IsWhiteSpace(text[index - 1]))
                index = textServices.SnapCaretIndex(text, index - 1);
            while (index > 0 && IsWordCharacter(text[index - 1]))
                index = textServices.SnapCaretIndex(text, index - 1);
            return index;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index = textServices.SnapCaretIndex(text, index + 1);
        while (index < text.Length && IsWordCharacter(text[index]))
            index = textServices.SnapCaretIndex(text, index + 1);
        return index;
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static SceneTextStyle CreateTextInputTextStyle(HtmlTextInputState state) =>
        new(
            state.FontSize,
            state.Color,
            state.FontFamily,
            state.FontWeight,
            state.TextAlign,
            state.Multiline
        );
}
