namespace Enaga.React.OkojoRuntime;

internal enum HostInputEventType
{
    Move,
    Down,
    Up,
    Wheel,
    KeyDown,
    KeyUp,
    TextInput,
}

internal readonly record struct HostInputEvent(
    HostInputEventType Type,
    float X,
    float Y,
    int Button,
    int Buttons,
    float DeltaX,
    float DeltaY,
    bool Synthetic,
    string? Key = null,
    string? Text = null,
    int Modifiers = 0,
    bool Repeat = false
);
