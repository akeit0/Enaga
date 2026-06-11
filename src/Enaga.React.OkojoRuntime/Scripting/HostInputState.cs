using System.Collections.Concurrent;

namespace Enaga.React.OkojoRuntime;

internal sealed class HostInputState
{
    public const int ShiftModifier = 1;
    public const int ControlModifier = 2;
    public const int AltModifier = 4;
    public const int MetaModifier = 8;
    public const int LeftMouseButtonMask = 1;
    public const double DoubleClickThresholdMs = 320;
    public const float DoubleClickThresholdPx = 8;
    public const double InitialKeyRepeatDelayMs = 420;
    public const double InitialKeyRepeatIntervalMs = 120;
    public const double MinimumKeyRepeatIntervalMs = 28;
    public const double KeyRepeatAccelerationFactor = 0.82;

    private readonly TimeProvider timeProvider;
    private long startTimestamp;

    public HostInputState(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        startTimestamp = this.timeProvider.GetTimestamp();
    }

    public ConcurrentQueue<HostInputEvent> Events { get; } = new();

    public Dictionary<string, HostKeyRepeatState> HeldKeys { get; } = new(StringComparer.Ordinal);

    public HostPrintableRepeatState? ActivePrintableRepeat { get; set; }

    public float MouseX { get; private set; }

    public float MouseY { get; private set; }

    public int MouseButtons { get; private set; }

    public float LastWheelDeltaX { get; private set; }

    public float LastWheelDeltaY { get; private set; }

    public bool LastInputSynthetic { get; private set; }

    public string LastKey { get; private set; } = string.Empty;

    public int KeyModifiers { get; private set; }

    public bool KeyRepeat { get; private set; }

    public string LastTextInput { get; private set; } = string.Empty;

    public string? LastPrimaryClickTextInputId { get; private set; }

    public double LastPrimaryClickElapsedMs { get; private set; } = double.NegativeInfinity;

    public float LastPrimaryClickX { get; private set; }

    public float LastPrimaryClickY { get; private set; }

    public double ElapsedMs => timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;

    public void RestartClock()
    {
        startTimestamp = timeProvider.GetTimestamp();
    }

    public void RecordPointerMove(float x, float y, int buttons, bool synthetic)
    {
        MouseX = x;
        MouseY = y;
        MouseButtons = buttons;
        LastInputSynthetic = synthetic;
    }

    public void RecordPointerButtons(int buttons, bool synthetic)
    {
        MouseButtons = buttons;
        LastInputSynthetic = synthetic;
    }

    public void RecordWheel(float deltaX, float deltaY, bool synthetic)
    {
        LastWheelDeltaX = deltaX;
        LastWheelDeltaY = deltaY;
        LastInputSynthetic = synthetic;
    }

    public void RecordKey(string key, int modifiers, bool repeat, bool synthetic)
    {
        LastKey = key;
        KeyModifiers = modifiers;
        KeyRepeat = repeat;
        LastInputSynthetic = synthetic;
    }

    public void RecordTextInput(string text, bool synthetic)
    {
        LastTextInput = text;
        LastInputSynthetic = synthetic;
    }

    public void RememberPrimaryClick(string id, float x, float y, double elapsedMs)
    {
        LastPrimaryClickTextInputId = id;
        LastPrimaryClickElapsedMs = elapsedMs;
        LastPrimaryClickX = x;
        LastPrimaryClickY = y;
    }

    public bool IsDoubleClick(string id, float x, float y, double elapsedMs)
    {
        return string.Equals(LastPrimaryClickTextInputId, id, StringComparison.Ordinal)
            && elapsedMs - LastPrimaryClickElapsedMs <= DoubleClickThresholdMs
            && Math.Abs(x - LastPrimaryClickX) <= DoubleClickThresholdPx
            && Math.Abs(y - LastPrimaryClickY) <= DoubleClickThresholdPx;
    }

    public bool TryDequeueCoalescedEvent(out HostInputEvent inputEvent)
    {
        if (!Events.TryDequeue(out inputEvent))
            return false;

        if (inputEvent.Type != HostInputEventType.Move)
            return true;

        var coalesced = inputEvent;
        while (Events.TryPeek(out var nextEvent) && nextEvent.Type == HostInputEventType.Move)
        {
            if (!Events.TryDequeue(out coalesced))
                break;
        }

        inputEvent = coalesced;
        return true;
    }

    public void ResetForReload()
    {
        Events.Clear();
        HeldKeys.Clear();
        ActivePrintableRepeat = null;
        LastPrimaryClickTextInputId = null;
        LastPrimaryClickElapsedMs = double.NegativeInfinity;
        LastPrimaryClickX = 0;
        LastPrimaryClickY = 0;
        KeyRepeat = false;
        LastTextInput = string.Empty;
    }
}

internal readonly record struct HostKeyRepeatState(
    double NextRepeatAtMs,
    int Modifiers,
    double IntervalMs
);

internal readonly record struct HostPrintableRepeatState(
    string Key,
    string Text,
    bool NativeInputAccepted
);
