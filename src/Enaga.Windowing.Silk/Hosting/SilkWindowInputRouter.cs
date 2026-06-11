using Enaga.Input;
using Enaga.Rendering.Skia;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Enaga.Hosting;

internal sealed class SilkWindowInputRouter : IDisposable
{
    private const int ShiftModifier = 1;
    private const int ControlModifier = 2;
    private const int AltModifier = 4;
    private const int MetaModifier = 8;
    private static readonly MouseButton[] MouseButtons = Enum.GetValues<MouseButton>();

    private readonly Action requestFrame;
    private readonly INativeWindowPlatformIntegration? platformIntegration;
    private readonly IRenderRoot renderRoot;
    private readonly HashSet<string> heldKeyboardKeys = new(StringComparer.Ordinal);
    private IInputContext? inputContext;
    private IKeyboard? keyboard;
    private IMouse? mouse;
    private PointerCursorKind appliedCursor = PointerCursorKind.Default;

    public SilkWindowInputRouter(
        IRenderRoot renderRoot,
        INativeWindowPlatformIntegration? platformIntegration,
        Action requestFrame
    )
    {
        this.renderRoot = renderRoot ?? throw new ArgumentNullException(nameof(renderRoot));
        this.platformIntegration = platformIntegration;
        this.requestFrame = requestFrame ?? throw new ArgumentNullException(nameof(requestFrame));
    }

    public bool HasHeldKeyboardKeys => heldKeyboardKeys.Count > 0;

    public void Attach(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        inputContext = window.CreateInput();
        mouse = inputContext.Mice.Count > 0 ? inputContext.Mice[0] : null;
        keyboard = inputContext.Keyboards.Count > 0 ? inputContext.Keyboards[0] : null;
        AttachMouse(mouse);
        AttachKeyboard(keyboard);
    }

    public void Dispose()
    {
        inputContext?.Dispose();
        inputContext = null;
        keyboard = null;
        mouse = null;
        heldKeyboardKeys.Clear();
    }

    private void AttachMouse(IMouse? nextMouse)
    {
        if (nextMouse is null || renderRoot is not IInputSink inputSink)
            return;

        nextMouse.MouseMove += (_, position) =>
        {
            inputSink.PointerMove(
                position.X,
                position.Y,
                GetButtonsMask(nextMouse),
                synthetic: false
            );
            ApplyPointerCursor(nextMouse);
            requestFrame();
        };
        nextMouse.MouseDown += (_, button) =>
        {
            inputSink.PointerMove(
                nextMouse.Position.X,
                nextMouse.Position.Y,
                GetButtonsMask(nextMouse),
                synthetic: false
            );
            ApplyPointerCursor(nextMouse);
            platformIntegration?.OnPointerDown((int)button);
            inputSink.PointerDown((int)button, GetButtonsMask(nextMouse), synthetic: false);
            requestFrame();
        };
        nextMouse.MouseUp += (_, button) =>
        {
            inputSink.PointerMove(
                nextMouse.Position.X,
                nextMouse.Position.Y,
                GetButtonsMask(nextMouse),
                synthetic: false
            );
            ApplyPointerCursor(nextMouse);
            inputSink.PointerUp((int)button, GetButtonsMask(nextMouse), synthetic: false);
            requestFrame();
        };
        nextMouse.Scroll += (_, wheel) =>
        {
            inputSink.Wheel(
                wheel.X,
                wheel.Y,
                synthetic: false,
                keyboard is not null ? GetModifiersMask(keyboard) : 0
            );
            requestFrame();
        };
    }

    private void ApplyPointerCursor(IMouse nextMouse)
    {
        if (renderRoot is not IPointerCursorSource cursorSource)
            return;

        var nextCursor = cursorSource.CurrentCursor;
        if (nextCursor == appliedCursor)
            return;

        var standardCursor = nextCursor switch
        {
            PointerCursorKind.Pointer => StandardCursor.Hand,
            PointerCursorKind.Text => StandardCursor.IBeam,
            _ => StandardCursor.Arrow,
        };
        if (nextMouse.Cursor.IsSupported(standardCursor))
        {
            nextMouse.Cursor.Type = CursorType.Standard;
            nextMouse.Cursor.StandardCursor = standardCursor;
            appliedCursor = nextCursor;
        }
    }

    private void AttachKeyboard(IKeyboard? nextKeyboard)
    {
        if (nextKeyboard is null || renderRoot is not IInputSink inputSink)
            return;

        nextKeyboard.KeyDown += (keyboardDevice, key, _) =>
        {
            heldKeyboardKeys.Add(key.ToString());
            inputSink.KeyDown(
                key.ToString(),
                GetModifiersMask(keyboardDevice),
                repeat: false,
                synthetic: false
            );
            requestFrame();
        };
        nextKeyboard.KeyUp += (keyboardDevice, key, _) =>
        {
            heldKeyboardKeys.Remove(key.ToString());
            inputSink.KeyUp(key.ToString(), GetModifiersMask(keyboardDevice), synthetic: false);
            requestFrame();
        };
        nextKeyboard.KeyChar += (_, character) =>
        {
            if (
                !char.IsControl(character)
                && platformIntegration?.HandlesTextInput != true
                && platformIntegration?.ShouldForwardTextInput(character) != false
            )
            {
                inputSink.TextInput(character.ToString(), synthetic: false);
                requestFrame();
            }
        };
    }

    private static int GetButtonsMask(IMouse nextMouse)
    {
        var mask = 0;
        foreach (var value in MouseButtons)
        {
            if (!nextMouse.IsButtonPressed(value))
                continue;
            mask |= 1 << (int)value;
        }

        return mask;
    }

    private static int GetModifiersMask(IKeyboard keyboardDevice)
    {
        var mask = 0;
        if (
            keyboardDevice.IsKeyPressed(Key.ShiftLeft)
            || keyboardDevice.IsKeyPressed(Key.ShiftRight)
        )
            mask |= ShiftModifier;
        if (
            keyboardDevice.IsKeyPressed(Key.ControlLeft)
            || keyboardDevice.IsKeyPressed(Key.ControlRight)
        )
            mask |= ControlModifier;
        if (keyboardDevice.IsKeyPressed(Key.AltLeft) || keyboardDevice.IsKeyPressed(Key.AltRight))
            mask |= AltModifier;
        if (
            keyboardDevice.IsKeyPressed(Key.SuperLeft)
            || keyboardDevice.IsKeyPressed(Key.SuperRight)
        )
            mask |= MetaModifier;
        return mask;
    }
}
