using Enaga.Rendering;
using Okojo.Annotations;
using Enaga.Input;
namespace Enaga.React.OkojoRuntime;

public sealed partial class OkojoNodeReactHost : ITextCompositionRangeSink
{
    public void StartTextComposition()
    {
        StartTextCompositionAt(startIndex: null);
    }

    public void StartTextComposition(int startIndex)
    {
        StartTextCompositionAt(startIndex);
    }

    private void StartTextCompositionAt(int? startIndex)
    {
        if (focusedTextInputId is null || !textInputs.TryGetValue(focusedTextInputId, out var state))
            return;

        TextInputStateLogic.StartComposition(state, startIndex);
        UpdateTextInputLayout(state);
    }

    public void UpdateTextComposition(string text, int cursorPosition)
    {
        UpdateTextComposition(text, cursorPosition, 0, text.Length);
    }

    public void UpdateTextComposition(string text, int cursorPosition, int selectionStart, int selectionLength)
    {
        if (focusedTextInputId is null || !textInputs.TryGetValue(focusedTextInputId, out var state))
            return;

        TextInputStateLogic.UpdateComposition(state, text, cursorPosition, selectionStart, selectionLength);
        UpdateTextInputLayout(state);
    }

    public void EndTextComposition()
    {
        if (focusedTextInputId is null || !textInputs.TryGetValue(focusedTextInputId, out var state))
            return;

        TextInputStateLogic.EndComposition(state, backendServices.Text);
        UpdateTextInputLayout(state);
    }

    public void PrepareTextCompositionCommit()
    {
        if (focusedTextInputId is null || !textInputs.TryGetValue(focusedTextInputId, out var state))
            return;

        TextInputStateLogic.PrepareCompositionCommit(state);
    }

    public void UpdateImeState(bool isOpen, string indicator)
    {
        if (focusedTextInputId is null || !textInputs.TryGetValue(focusedTextInputId, out var state))
            return;

        state.ImeOpen = isOpen;
        state.ImeIndicator = indicator ?? string.Empty;
        UpdateTextInputLayout(state);
    }

    public bool TryGetTextCompositionCursor(out TextCompositionCursor cursor)
    {
        cursor = default;
        if (focusedTextInputId is null || !textInputs.TryGetValue(focusedTextInputId, out var state))
            return false;

        var screenLeft = state.Left;
        var screenTop = state.Top;
        if (TryGetNodeScreenBounds(state.Id, out var bounds))
        {
            screenLeft = bounds.Left;
            screenTop = bounds.Top;
        }

        var textStyle = CreateTextInputTextStyle(state);
        var composedValue = state.CompositionText.Length > 0
            ? state.Text.Insert(Math.Clamp(state.CompositionStartIndex, 0, state.Text.Length), state.CompositionText)
            : state.Text;
        var caretIndex = state.CompositionText.Length > 0
            ? state.CompositionStartIndex + Math.Clamp(state.CompositionCursorOffset, 0, state.CompositionText.Length)
            : state.CaretIndex;
        var caret = backendServices.Text.GetCaretPosition(textStyle, composedValue, state.LineHeight, Math.Max(0, state.Width - state.PaddingLeft - state.PaddingRight), caretIndex);
        cursor = new TextCompositionCursor(
            screenLeft + state.PaddingLeft + caret.X,
            screenTop + state.PaddingTop + caret.Y,
            2,
            Math.Max(state.FontSize + 4, state.LineHeight));
        return true;
    }
}
