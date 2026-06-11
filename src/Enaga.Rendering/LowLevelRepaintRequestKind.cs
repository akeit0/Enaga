namespace Enaga.Rendering;

public enum LowLevelRepaintRequestKind
{
    NoRepaint = 0,
    NextFrame = 1,
    NextPointerMove = 2,
    NextPointerDown = 3,
    NextPointerUp = 4,
    NextWheel = 5,
    NextKeyDown = 6,
    NextKeyUp = 7,
    NextTextInput = 8,
    NextInput = 9,
}

[Flags]
public enum LowLevelRepaintEventKind
{
    None = 0,
    Frame = 1 << 0,
    PointerMove = 1 << 1,
    PointerDown = 1 << 2,
    PointerUp = 1 << 3,
    Wheel = 1 << 4,
    KeyDown = 1 << 5,
    KeyUp = 1 << 6,
    TextInput = 1 << 7,
    AnyInput = PointerMove | PointerDown | PointerUp | Wheel | KeyDown | KeyUp | TextInput,
}

public readonly record struct LowLevelRepaintRequest(
    LowLevelRepaintRequestKind Kind,
    SceneDamageRect RepaintRect,
    SceneDamageRect? SensitiveRect = null
);

public readonly record struct LowLevelRepaintEvent(
    LowLevelRepaintEventKind Kind,
    float X = float.NaN,
    float Y = float.NaN
)
{
    public bool HasPointer => !float.IsNaN(X) && !float.IsNaN(Y);
}

public static class LowLevelRepaintMatcher
{
    public static bool IsMatch(
        LowLevelRepaintRequest request,
        ReadOnlySpan<LowLevelRepaintEvent> events
    )
    {
        if (request.Kind == LowLevelRepaintRequestKind.NoRepaint)
            return false;

        for (var index = 0; index < events.Length; index++)
        {
            if (IsMatch(request, events[index]))
                return true;
        }

        return false;
    }

    public static bool IsMatch(LowLevelRepaintRequest request, LowLevelRepaintEvent repaintEvent)
    {
        if (!MatchesKind(request.Kind, repaintEvent.Kind))
            return false;

        if (request.SensitiveRect is not { } sensitiveRect)
            return true;

        return repaintEvent.HasPointer
            && repaintEvent.X >= sensitiveRect.X
            && repaintEvent.Y >= sensitiveRect.Y
            && repaintEvent.X <= sensitiveRect.X + sensitiveRect.Width
            && repaintEvent.Y <= sensitiveRect.Y + sensitiveRect.Height;
    }

    private static bool MatchesKind(
        LowLevelRepaintRequestKind requestKind,
        LowLevelRepaintEventKind eventKind
    )
    {
        return requestKind switch
        {
            LowLevelRepaintRequestKind.NoRepaint => false,
            LowLevelRepaintRequestKind.NextFrame => eventKind.HasFlag(
                LowLevelRepaintEventKind.Frame
            ),
            LowLevelRepaintRequestKind.NextPointerMove => eventKind.HasFlag(
                LowLevelRepaintEventKind.PointerMove
            ),
            LowLevelRepaintRequestKind.NextPointerDown => eventKind.HasFlag(
                LowLevelRepaintEventKind.PointerDown
            ),
            LowLevelRepaintRequestKind.NextPointerUp => eventKind.HasFlag(
                LowLevelRepaintEventKind.PointerUp
            ),
            LowLevelRepaintRequestKind.NextWheel => eventKind.HasFlag(
                LowLevelRepaintEventKind.Wheel
            ),
            LowLevelRepaintRequestKind.NextKeyDown => eventKind.HasFlag(
                LowLevelRepaintEventKind.KeyDown
            ),
            LowLevelRepaintRequestKind.NextKeyUp => eventKind.HasFlag(
                LowLevelRepaintEventKind.KeyUp
            ),
            LowLevelRepaintRequestKind.NextTextInput => eventKind.HasFlag(
                LowLevelRepaintEventKind.TextInput
            ),
            LowLevelRepaintRequestKind.NextInput => (eventKind & LowLevelRepaintEventKind.AnyInput)
                != 0,
            _ => false,
        };
    }
}
