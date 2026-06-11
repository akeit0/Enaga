namespace Enaga.Input;

public interface IInputSink
{
    void PointerMove(float x, float y, int buttons, bool synthetic);
    void PointerDown(int button, int buttons, bool synthetic);
    void PointerUp(int button, int buttons, bool synthetic);
    void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0);
    void KeyDown(string key, int modifiers, bool repeat, bool synthetic);
    void KeyUp(string key, int modifiers, bool synthetic);
    void TextInput(string text, bool synthetic);
}

public enum PointerCursorKind
{
    Default,
    Pointer,
    Text,
}

public interface IPointerCursorSource
{
    PointerCursorKind CurrentCursor { get; }
}

public readonly record struct TextCompositionCursor(float X, float Y, float Width, float Height);

public interface ITextCompositionSink
{
    void StartTextComposition();
    void UpdateTextComposition(string text, int cursorPosition);
    void UpdateTextComposition(
        string text,
        int cursorPosition,
        int selectionStart,
        int selectionLength
    )
    {
        UpdateTextComposition(text, cursorPosition);
    }
    void EndTextComposition();
    void PrepareTextCompositionCommit();
    void UpdateImeState(bool isOpen, string indicator);
    bool TryGetTextCompositionCursor(out TextCompositionCursor cursor);
}

public interface ITextCompositionRangeSink : ITextCompositionSink
{
    void StartTextComposition(int startIndex);
}
