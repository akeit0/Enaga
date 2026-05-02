using Enaga.Rendering;

namespace Enaga.React.OkojoRuntime;

internal static class TextInputStateLogic
{
    public static void ApplyExternalValue(NativeTextInputState state, IRuntimeTextServices textServices, string value)
    {
        value ??= string.Empty;

        if (state.PendingHostText is { } pendingHostText)
        {
            if (string.Equals(value, pendingHostText, StringComparison.Ordinal))
            {
                state.PendingHostText = null;
                state.LastKnownExternalText = value;
                return;
            }

            if (string.Equals(value, state.LastKnownExternalText, StringComparison.Ordinal))
                return;

            state.PendingHostText = null;
        }

        if (string.Equals(state.Text, value, StringComparison.Ordinal))
        {
            state.LastKnownExternalText = value;
            return;
        }

        state.Text = value;
        state.CaretIndex = textServices.SnapCaretIndex(state.Text, Math.Min(state.CaretIndex, state.Text.Length));
        state.SelectionStart = textServices.SnapCaretIndex(state.Text, Math.Min(state.SelectionStart, state.Text.Length));
        state.SelectionEnd = textServices.SnapCaretIndex(state.Text, Math.Min(state.SelectionEnd, state.Text.Length));
        state.PreferredCaretX = null;
        EndComposition(state, textServices);
        if (!state.IsFocused)
        {
            state.CaretIndex = state.Text.Length;
            ClearSelection(state);
        }

        state.LastKnownExternalText = value;
    }

    public static void StartComposition(NativeTextInputState state)
    {
        StartComposition(state, null);
    }

    public static void StartComposition(NativeTextInputState state, int? startIndex)
    {
        state.IsTextCompositionActive = true;
        state.PendingCompositionCommit = false;
        state.CompositionReplacedSelection = false;
        state.CompositionRestoreText = null;
        state.CompositionStartIndex = HasSelection(state)
            ? Math.Min(state.SelectionStart, state.SelectionEnd)
            : Math.Clamp(startIndex ?? state.CaretIndex, 0, state.Text.Length);
        state.CompositionCursorOffset = 0;
        state.CompositionText = string.Empty;
    }

    public static void UpdateComposition(NativeTextInputState state, string? text, int cursorPosition)
    {
        UpdateComposition(state, text, cursorPosition, 0, text?.Length ?? 0);
    }

    public static void UpdateComposition(NativeTextInputState state, string? text, int cursorPosition, int selectionStart, int selectionLength)
    {
        state.IsTextCompositionActive = true;
        if (state.CompositionText.Length == 0 && !string.IsNullOrEmpty(text) && HasSelection(state))
        {
            state.CompositionRestoreText ??= state.Text;
            state.CompositionStartIndex = Math.Min(state.SelectionStart, state.SelectionEnd);
            DeleteSelection(state);
            state.CompositionReplacedSelection = true;
            state.PendingHostText = state.Text;
        }

        state.CompositionText = text ?? string.Empty;
        state.CompositionCursorOffset = Math.Clamp(cursorPosition, 0, state.CompositionText.Length);
        state.CompositionSelectionStart = Math.Clamp(selectionStart, 0, state.CompositionText.Length);
        state.CompositionSelectionLength = Math.Clamp(selectionLength, 0, state.CompositionText.Length - state.CompositionSelectionStart);
    }

    public static void PrepareCompositionCommit(NativeTextInputState state)
    {
        state.PendingCompositionCommit = true;
    }

    public static void EndComposition(NativeTextInputState state, IRuntimeTextServices textServices)
    {
        if (!state.PendingCompositionCommit &&
            state.CompositionReplacedSelection &&
            state.CompositionRestoreText is { } restoreText)
        {
            state.Text = restoreText;
            state.CaretIndex = textServices.SnapCaretIndex(restoreText, Math.Clamp(state.CompositionStartIndex, 0, restoreText.Length));
            state.PreferredCaretX = null;
            ClearSelection(state);
            state.PendingHostText = null;
        }

        state.IsTextCompositionActive = false;
        state.PendingCompositionCommit = false;
        state.CompositionReplacedSelection = false;
        state.CompositionRestoreText = null;
        state.CompositionText = string.Empty;
        state.CompositionCursorOffset = 0;
        state.CompositionSelectionStart = 0;
        state.CompositionSelectionLength = 0;
    }

    public static void ApplyTextInput(NativeTextInputState state, IRuntimeTextServices textServices, string text)
    {
        var useCompositionAnchor = state.PendingCompositionCommit;
        if (useCompositionAnchor && !state.CompositionReplacedSelection && HasSelection(state))
        {
            DeleteSelection(state);
            state.CompositionReplacedSelection = true;
        }

        if (!useCompositionAnchor)
            DeleteSelection(state);
        var insertIndex = useCompositionAnchor
            ? Math.Clamp(state.CompositionStartIndex, 0, state.Text.Length)
            : state.CaretIndex;
        var caretIndex = useCompositionAnchor
            ? AdjustCaretIndexForInsertedText(state.CaretIndex, insertIndex, text.Length)
            : insertIndex + text.Length;

        EndComposition(state, textServices);
        state.Text = state.Text.Insert(insertIndex, text);
        state.CaretIndex = textServices.SnapCaretIndex(state.Text, caretIndex);
        state.PreferredCaretX = null;
        ClearSelection(state);
        state.PendingHostText = state.Text;
    }

    public static bool HasSelection(NativeTextInputState state)
    {
        return state.SelectionStart != state.SelectionEnd;
    }

    public static void ClearSelection(NativeTextInputState state)
    {
        state.SelectionAnchorIndex = state.CaretIndex;
        state.SelectionStart = state.CaretIndex;
        state.SelectionEnd = state.CaretIndex;
    }

    public static void SetSelection(NativeTextInputState state, IRuntimeTextServices textServices, int anchorIndex, int caretIndex)
    {
        var clampedAnchor = textServices.SnapCaretIndex(state.Text, Math.Clamp(anchorIndex, 0, state.Text.Length));
        var clampedCaret = textServices.SnapCaretIndex(state.Text, Math.Clamp(caretIndex, 0, state.Text.Length));
        state.SelectionAnchorIndex = clampedAnchor;
        state.CaretIndex = clampedCaret;
        state.SelectionStart = Math.Min(clampedAnchor, clampedCaret);
        state.SelectionEnd = Math.Max(clampedAnchor, clampedCaret);
    }

    public static bool DeleteSelection(NativeTextInputState state)
    {
        if (!HasSelection(state))
            return false;

        var selectionStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        var selectionLength = Math.Abs(state.SelectionEnd - state.SelectionStart);
        state.Text = state.Text.Remove(selectionStart, selectionLength);
        state.CaretIndex = selectionStart;
        state.CompositionText = string.Empty;
        state.CompositionCursorOffset = 0;
        state.CompositionSelectionStart = 0;
        state.CompositionSelectionLength = 0;
        ClearSelection(state);
        return true;
    }

    private static int AdjustCaretIndexForInsertedText(int caretIndex, int insertIndex, int insertedLength)
    {
        return caretIndex >= insertIndex
            ? caretIndex + insertedLength
            : caretIndex;
    }
}
