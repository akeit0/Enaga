using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.React.OkojoRuntime;

internal sealed class HostTextInputController
{
    private readonly IRuntimeTextServices textServices;
    private readonly Action<NativeTextInputState> ensureVisible;
    private readonly Action<NativeTextInputState> updateLayout;
    private readonly Action<NativeTextInputState, string> notifyEvent;
    private readonly Func<bool, bool> moveFocus;
    private readonly Action<string?> setFocus;

    public HostTextInputController(
        IRuntimeTextServices textServices,
        Action<NativeTextInputState> ensureVisible,
        Action<NativeTextInputState> updateLayout,
        Action<NativeTextInputState, string> notifyEvent,
        Func<bool, bool> moveFocus,
        Action<string?> setFocus)
    {
        this.textServices = textServices ?? throw new ArgumentNullException(nameof(textServices));
        this.ensureVisible = ensureVisible ?? throw new ArgumentNullException(nameof(ensureVisible));
        this.updateLayout = updateLayout ?? throw new ArgumentNullException(nameof(updateLayout));
        this.notifyEvent = notifyEvent ?? throw new ArgumentNullException(nameof(notifyEvent));
        this.moveFocus = moveFocus ?? throw new ArgumentNullException(nameof(moveFocus));
        this.setFocus = setFocus ?? throw new ArgumentNullException(nameof(setFocus));
    }

    public void ApplyTextInput(NativeTextInputState state, string text)
    {
        ArgumentNullException.ThrowIfNull(state);
        TextInputStateLogic.ApplyTextInput(state, textServices, text);
        ensureVisible(state);
        updateLayout(state);
        notifyEvent(state, "change");
    }

    public void HandleKey(NativeTextInputState state, string key, int modifiers)
    {
        ArgumentNullException.ThrowIfNull(state);

        var hasShift = (modifiers & HostInputState.ShiftModifier) != 0;
        if (state.CompositionText.Length > 0 && key is not "Escape" and not "Tab")
            return;

        if ((modifiers & (HostInputState.ControlModifier | HostInputState.MetaModifier)) != 0)
        {
            if (string.Equals(key, "A", StringComparison.OrdinalIgnoreCase))
            {
                SelectAll(state);
                updateLayout(state);
                return;
            }

            if (string.Equals(key, "C", StringComparison.OrdinalIgnoreCase))
            {
                CopySelectionToClipboard(state);
                return;
            }

            if (string.Equals(key, "X", StringComparison.OrdinalIgnoreCase))
            {
                if (CopySelectionToClipboard(state) && DeleteSelection(state))
                {
                    updateLayout(state);
                    notifyEvent(state, "change");
                }
                return;
            }

            if (string.Equals(key, "V", StringComparison.OrdinalIgnoreCase))
            {
                if (TryInsertClipboardText(state))
                {
                    updateLayout(state);
                    notifyEvent(state, "change");
                }
                return;
            }
        }

        switch (key)
        {
            case "Backspace":
            case "BackSpace":
                {
                    var removedSelection = DeleteSelection(state);
                    if (!(removedSelection || state.CaretIndex > 0))
                        break;

                    if (!removedSelection)
                    {
                        var previousIndex = textServices.GetPreviousTextElementIndex(state.Text, state.CaretIndex);
                        state.Text = state.Text.Remove(previousIndex, state.CaretIndex - previousIndex);
                        state.CaretIndex = previousIndex;
                    }

                    state.CompositionText = string.Empty;
                    state.CompositionCursorOffset = 0;
                    state.PreferredCaretX = null;
                    ClearSelection(state);
                    ensureVisible(state);
                    updateLayout(state);
                    notifyEvent(state, "change");
                    break;
                }
            case "Delete":
                {
                    var removedSelection = DeleteSelection(state);
                    if (!(removedSelection || state.CaretIndex < state.Text.Length))
                        break;

                    if (!removedSelection)
                    {
                        var nextIndex = textServices.GetNextTextElementIndex(state.Text, state.CaretIndex);
                        state.Text = state.Text.Remove(state.CaretIndex, nextIndex - state.CaretIndex);
                    }

                    state.CompositionText = string.Empty;
                    state.CompositionCursorOffset = 0;
                    state.PreferredCaretX = null;
                    ClearSelection(state);
                    ensureVisible(state);
                    updateLayout(state);
                    notifyEvent(state, "change");
                    break;
                }
            case "Left":
            case "ArrowLeft":
                state.PreferredCaretX = null;
                MoveSelectionCaret(
                    state,
                    hasShift
                        ? textServices.GetPreviousTextElementIndex(state.Text, state.CaretIndex)
                        : HasSelection(state)
                            ? Math.Min(state.SelectionStart, state.SelectionEnd)
                            : textServices.GetPreviousTextElementIndex(state.Text, state.CaretIndex),
                    hasShift);
                ensureVisible(state);
                updateLayout(state);
                break;
            case "Right":
            case "ArrowRight":
                state.PreferredCaretX = null;
                MoveSelectionCaret(
                    state,
                    hasShift
                        ? textServices.GetNextTextElementIndex(state.Text, state.CaretIndex)
                        : HasSelection(state)
                            ? Math.Max(state.SelectionStart, state.SelectionEnd)
                            : textServices.GetNextTextElementIndex(state.Text, state.CaretIndex),
                    hasShift);
                ensureVisible(state);
                updateLayout(state);
                break;
            case "Up":
            case "ArrowUp":
                MoveCaretVertically(state, -1, hasShift);
                ensureVisible(state);
                updateLayout(state);
                break;
            case "Down":
            case "ArrowDown":
                MoveCaretVertically(state, 1, hasShift);
                ensureVisible(state);
                updateLayout(state);
                break;
            case "Home":
                state.PreferredCaretX = null;
                MoveSelectionCaret(
                    state,
                    state.Multiline ? MoveCaretToLineEdge(state, toEnd: false) : 0,
                    hasShift);
                ensureVisible(state);
                updateLayout(state);
                break;
            case "End":
                state.PreferredCaretX = null;
                MoveSelectionCaret(
                    state,
                    state.Multiline ? MoveCaretToLineEdge(state, toEnd: true) : state.Text.Length,
                    hasShift);
                ensureVisible(state);
                updateLayout(state);
                break;
            case "Tab":
                if (moveFocus(!hasShift))
                    return;
                break;
            case "Enter":
            case "KeypadEnter":
            case "NumpadEnter":
                if (state.Multiline)
                    ApplyTextInput(state, "\n");
                else
                    notifyEvent(state, "submit");
                break;
            case "Escape":
                setFocus(null);
                break;
        }
    }

    public bool HasSelection(NativeTextInputState state) => TextInputStateLogic.HasSelection(state);

    public void MoveSelectionCaret(NativeTextInputState state, int caretIndex, bool extendSelection)
    {
        if (extendSelection)
        {
            var anchor = HasSelection(state) ? state.SelectionAnchorIndex : state.CaretIndex;
            SetSelection(state, anchor, caretIndex);
            return;
        }

        state.CaretIndex = textServices.SnapCaretIndex(state.Text, Math.Clamp(caretIndex, 0, state.Text.Length));
        ClearSelection(state);
    }

    public void ClearSelection(NativeTextInputState state) => TextInputStateLogic.ClearSelection(state);

    public void SetSelection(NativeTextInputState state, int anchorIndex, int caretIndex)
    {
        TextInputStateLogic.SetSelection(state, textServices, anchorIndex, caretIndex);
    }

    public void SelectAll(NativeTextInputState state)
    {
        state.SelectionAnchorIndex = 0;
        state.SelectionStart = 0;
        state.SelectionEnd = state.Text.Length;
        state.CaretIndex = state.Text.Length;
    }

    public bool DeleteSelection(NativeTextInputState state) => TextInputStateLogic.DeleteSelection(state);

    public void SelectWordAt(NativeTextInputState state, int caretIndex)
    {
        var adjustedIndex = caretIndex >= state.Text.Length && state.Text.Length > 0
            ? state.Text.Length - 1
            : caretIndex;
        var (start, end) = SelectWordRange(state.Text, adjustedIndex);
        state.SelectionAnchorIndex = start;
        state.SelectionStart = start;
        state.SelectionEnd = end;
        state.CaretIndex = end;
    }

    private void MoveCaretVertically(NativeTextInputState state, int lineDelta, bool extendSelection)
    {
        var textStyle = CreateTextInputTextStyle(state);
        var textWidth = Math.Max(0, state.Width - state.PaddingLeft - state.PaddingRight);
        var preferredX = state.PreferredCaretX ?? textServices.GetCaretPosition(textStyle, state.Text, state.LineHeight, textWidth, state.CaretIndex).X;
        var caretIndex = textServices.MoveCaretVertical(textStyle, state.Text, state.LineHeight, textWidth, state.CaretIndex, lineDelta, preferredX);
        state.PreferredCaretX = preferredX;
        MoveSelectionCaret(state, caretIndex, extendSelection);
    }

    private int MoveCaretToLineEdge(NativeTextInputState state, bool toEnd)
    {
        return textServices.MoveCaretToLineEdge(
            CreateTextInputTextStyle(state),
            state.Text,
            state.LineHeight,
            Math.Max(0, state.Width - state.PaddingLeft - state.PaddingRight),
            state.CaretIndex,
            toEnd);
    }

    private static (int Start, int End) SelectWordRange(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text))
            return (0, 0);

        var index = Math.Clamp(caretIndex, 0, text.Length - 1);
        if (!IsWordCharacter(text[index]))
            return (index, Math.Min(text.Length, index + 1));

        var start = index;
        while (start > 0 && IsWordCharacter(text[start - 1]))
            start--;

        var end = index + 1;
        while (end < text.Length && IsWordCharacter(text[end]))
            end++;

        return (start, end);
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private string? GetSelectedText(NativeTextInputState state)
    {
        if (!HasSelection(state))
            return null;

        var selectionStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        var selectionLength = Math.Abs(state.SelectionEnd - state.SelectionStart);
        return state.Text.Substring(selectionStart, selectionLength);
    }

    private bool CopySelectionToClipboard(NativeTextInputState state)
    {
        var selectedText = GetSelectedText(state);
        return !string.IsNullOrEmpty(selectedText) && NativeClipboardService.SetText(selectedText);
    }

    private bool TryInsertClipboardText(NativeTextInputState state)
    {
        var text = NativeClipboardService.GetText();
        if (string.IsNullOrEmpty(text))
            return false;

        DeleteSelection(state);
        state.Text = state.Text.Insert(state.CaretIndex, text);
        state.CaretIndex = textServices.SnapCaretIndex(state.Text, state.CaretIndex + text.Length);
        state.CompositionText = string.Empty;
        state.CompositionCursorOffset = 0;
        state.PreferredCaretX = null;
        ClearSelection(state);
        return true;
    }

    private static SceneTextStyle CreateTextInputTextStyle(NativeTextInputState state)
    {
        return new SceneTextStyle(
            state.FontSize,
            state.Color,
            TextAlign: state.TextAlign,
            WrapText: state.Multiline,
            Font: new SceneFont(state.FontSize, state.FontFamily, state.FontWeight));
    }
}
