using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Okojo.Annotations;
using Enaga.React.OkojoRuntime;
using Enaga.React.OkojoRuntime.Skia;
using System.Runtime.InteropServices;
using Enaga.Overlay.Windows;
using Enaga.Rendering.Skia;
using Enaga.SampleApp;
namespace Enaga.MonoGameOverlay.Sample;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var game = new OverlaySampleGame(args);
        game.Run();
    }
}

public sealed class OverlaySampleGame : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly string[] args;
    private readonly OverlayGameBridge bridge = new();
    private WindowsDirectCompositionOverlayHost? overlay;
    private SpriteBatch? spriteBatch;
    private Texture2D? pixel;
    private double elapsedSeconds;
    private nint ownerHwnd;
    private Vector2 actorPosition = new(160, 520);
    private Vector2 clickTarget = new(160, 520);
    private PointerSnapshot previousPointer;
    private bool overlayPointerCaptured;

    public OverlaySampleGame(string[] args)
    {
        this.args = args;
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
            SynchronizeWithVerticalRetrace = true
        };
        IsFixedTimeStep = true;
        TargetElapsedTime = new TimeSpan(0, 0, 0, 0, 16, 666);
        Window.Title = "MonoGame + Enaga overlay";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        base.Initialize();
        spriteBatch = new SpriteBatch(GraphicsDevice);
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData([Color.White]);

        var options = SampleAppOptions.Parse(AddDefaultSamplePaths(args));
        var source = CreateOverlaySource(options, bridge);
        ownerHwnd = NativeWindowHandle.ResolveWin32WindowHandle(Window.Handle);
        overlay = new WindowsDirectCompositionOverlayHost(
            new SceneRenderRoot(source, options.RenderStats, requiresFullFramePresentation: true),
            new WindowsDirectCompositionOverlayOptions
            {
                TargetWindowHandle = ownerHwnd,
                Width = GraphicsDevice.Viewport.Width,
                Height = GraphicsDevice.Viewport.Height
            });
    }


    protected override void Update(GameTime gameTime)
    {
        if (KeyboardEscapePressed())
            Exit();

        SyncOverlayBounds();
        UpdateClickTarget(gameTime);
        elapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;
        bridge.UpdateGameState(actorPosition, clickTarget, elapsedSeconds);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(18, 23, 31));
        DrawBackground();
        overlay?.Tick(gameTime.TotalGameTime);
        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            overlay?.Dispose();
            pixel?.Dispose();
            spriteBatch?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawBackground()
    {
        if (spriteBatch is null || pixel is null)
            return;

        var viewport = GraphicsDevice.Viewport;
        spriteBatch.Begin();
        for (var y = 0; y < viewport.Height; y += 40)
        {
            var t = y / (float)Math.Max(1, viewport.Height);
            var color = Color.Lerp(new Color(25, 32, 44), new Color(44, 55, 66), t);
            spriteBatch.Draw(pixel, new Rectangle(0, y, viewport.Width, 40), color);
        }

        var pulse = (float)((Math.Sin(elapsedSeconds * 1.6) + 1) * 0.5);
        var block = new Rectangle(
            80 + (int)(pulse * 180),
            viewport.Height - 180 + (int)(pulse * 80),
            280,
            96);
        spriteBatch.Draw(pixel, block, ResolveAccentColor(bridge.CurrentAction, 180));
        spriteBatch.Draw(pixel, new Rectangle(viewport.Width - 360, 90, 260, 180), ResolvePanelColor(bridge.CurrentAction, 150));

        DrawActionEffect(spriteBatch, pixel, viewport);
        DrawClickTarget(spriteBatch, pixel);
        DrawActor(spriteBatch, pixel);
        spriteBatch.End();
    }

    private void UpdateClickTarget(GameTime gameTime)
    {
        if (!TryGetGamePointer(out var pointer))
        {
            previousPointer = default;
            return;
        }

        overlay?.PointerMove(pointer.X, pointer.Y, pointer.Buttons);

        var pressed = pointer.LeftDown && !previousPointer.LeftDown;
        if (pressed)
        {
            if (IsPointerOverOverlayUi(pointer.X, pointer.Y))
            {
                overlayPointerCaptured = true;
                overlay?.PointerDown(0, pointer.Buttons);
            }
            else
            {
                clickTarget = new Vector2(pointer.X, pointer.Y);
            }
        }

        var released = !pointer.LeftDown && previousPointer.LeftDown;
        if (released && overlayPointerCaptured)
        {
            overlay?.PointerUp(0, pointer.Buttons);
            overlayPointerCaptured = false;
        }

        var delta = clickTarget - actorPosition;
        if (delta.LengthSquared() > 1f)
        {
            var speed = bridge.CurrentAction switch
            {
                OverlayGameAction.BlinkStep => 14f,
                OverlayGameAction.GuardBreak => 5f,
                _ => 8f
            };
            var t = Math.Min(1f, (float)(gameTime.ElapsedGameTime.TotalSeconds * speed));
            actorPosition += delta * t;
        }

        previousPointer = pointer;
    }

    private bool IsPointerOverOverlayUi(int x, int y)
    {
        if (overlay is null)
            return false;

        return overlay.HitTestOverlayInput(x, y);
    }

    private bool TryGetGamePointer(out PointerSnapshot pointer)
    {
        pointer = default;
        var mouse = Mouse.GetState();
        var viewport = GraphicsDevice.Viewport;
        if (mouse.X < 0 || mouse.Y < 0 || mouse.X >= viewport.Width || mouse.Y >= viewport.Height)
            return false;

        pointer = new PointerSnapshot(mouse.X, mouse.Y, mouse.LeftButton == ButtonState.Pressed, GetMouseButtonsMask(mouse));
        return true;
    }

    private static int GetMouseButtonsMask(MouseState mouse)
    {
        var mask = 0;
        if (mouse.LeftButton == ButtonState.Pressed)
            mask |= 1;
        if (mouse.RightButton == ButtonState.Pressed)
            mask |= 1 << 1;
        if (mouse.MiddleButton == ButtonState.Pressed)
            mask |= 1 << 2;
        return mask;
    }

    private void DrawClickTarget(SpriteBatch batch, Texture2D texture)
    {
        var x = (int)MathF.Round(clickTarget.X);
        var y = (int)MathF.Round(clickTarget.Y);
        var color = ResolveAccentColor(bridge.CurrentAction, 220);
        batch.Draw(texture, new Rectangle(x - 18, y - 2, 36, 4), color);
        batch.Draw(texture, new Rectangle(x - 2, y - 18, 4, 36), color);
        batch.Draw(texture, new Rectangle(x - 24, y - 24, 48, 3), color * 0.55f);
        batch.Draw(texture, new Rectangle(x - 24, y + 21, 48, 3), color * 0.55f);
        batch.Draw(texture, new Rectangle(x - 24, y - 24, 3, 48), color * 0.55f);
        batch.Draw(texture, new Rectangle(x + 21, y - 24, 3, 48), color * 0.55f);
    }

    private void DrawActor(SpriteBatch batch, Texture2D texture)
    {
        var x = (int)MathF.Round(actorPosition.X);
        var y = (int)MathF.Round(actorPosition.Y);
        batch.Draw(texture, new Rectangle(x - 18, y - 18, 36, 36), ResolveAccentColor(bridge.CurrentAction, 230));
        batch.Draw(texture, new Rectangle(x - 14, y - 14, 28, 28), new Color(21, 42, 54, 210));
        batch.Draw(texture, new Rectangle(x - 8, y - 8, 16, 16), ResolveHighlightColor(bridge.CurrentAction, 240));
    }

    private void DrawActionEffect(SpriteBatch batch, Texture2D texture, Viewport viewport)
    {
        var intensity = bridge.EffectPulse;
        switch (bridge.CurrentAction)
        {
            case OverlayGameAction.Strike:
                DrawStrikeEffect(batch, texture, intensity);
                break;
            case OverlayGameAction.GuardBreak:
                DrawGuardBreakEffect(batch, texture, intensity);
                break;
            case OverlayGameAction.BlinkStep:
                DrawBlinkStepEffect(batch, texture, viewport, intensity);
                break;
        }
    }

    private void DrawStrikeEffect(SpriteBatch batch, Texture2D texture, float intensity)
    {
        var delta = clickTarget - actorPosition;
        if (delta.LengthSquared() < 4f)
            return;

        var length = delta.Length();
        var direction = delta / length;
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var color = ResolveAccentColor(OverlayGameAction.Strike, (byte)(120 + intensity * 90));
        for (var index = 1; index <= 4; index++)
        {
            var t = index / 5f;
            var center = actorPosition + direction * length * t;
            var width = 18 + (int)(intensity * 24) - index * 2;
            var height = 4 + index;
            var offset = perpendicular * ((index % 2 == 0 ? -1f : 1f) * intensity * 10f);
            batch.Draw(texture, new Rectangle(
                (int)MathF.Round(center.X + offset.X) - width / 2,
                (int)MathF.Round(center.Y + offset.Y) - height / 2,
                width,
                height), color);
        }
    }

    private void DrawGuardBreakEffect(SpriteBatch batch, Texture2D texture, float intensity)
    {
        var color = ResolveAccentColor(OverlayGameAction.GuardBreak, (byte)(96 + intensity * 120));
        var size = 64 + (int)(intensity * 46);
        var x = (int)MathF.Round(clickTarget.X) - size / 2;
        var y = (int)MathF.Round(clickTarget.Y) - size / 2;
        batch.Draw(texture, new Rectangle(x, y, size, 4), color);
        batch.Draw(texture, new Rectangle(x, y + size - 4, size, 4), color);
        batch.Draw(texture, new Rectangle(x, y, 4, size), color);
        batch.Draw(texture, new Rectangle(x + size - 4, y, 4, size), color);

        var innerSize = Math.Max(12, size - 22);
        var innerX = (int)MathF.Round(clickTarget.X) - innerSize / 2;
        var innerY = (int)MathF.Round(clickTarget.Y) - innerSize / 2;
        var innerColor = ResolveHighlightColor(OverlayGameAction.GuardBreak, (byte)(80 + intensity * 90));
        batch.Draw(texture, new Rectangle(innerX, innerY, innerSize, 2), innerColor);
        batch.Draw(texture, new Rectangle(innerX, innerY + innerSize - 2, innerSize, 2), innerColor);
    }

    private void DrawBlinkStepEffect(SpriteBatch batch, Texture2D texture, Viewport viewport, float intensity)
    {
        var movement = clickTarget - actorPosition;
        var direction = movement.LengthSquared() > 1f
            ? Vector2.Normalize(movement)
            : new Vector2(1f, 0f);
        var trailColor = ResolveAccentColor(OverlayGameAction.BlinkStep, (byte)(90 + intensity * 90));
        for (var index = 1; index <= 4; index++)
        {
            var offset = direction * (-index * (10 + intensity * 8));
            batch.Draw(texture, new Rectangle(
                (int)MathF.Round(actorPosition.X + offset.X) - 14,
                (int)MathF.Round(actorPosition.Y + offset.Y) - 14,
                28,
                28), trailColor);
        }

        var pulseWidth = 120 + (int)(intensity * 90);
        batch.Draw(texture, new Rectangle(
            Math.Max(0, (int)MathF.Round(actorPosition.X) - pulseWidth / 2),
            Math.Max(0, (int)MathF.Round(actorPosition.Y) - 2),
            Math.Min(viewport.Width, pulseWidth),
            4), ResolveHighlightColor(OverlayGameAction.BlinkStep, (byte)(100 + intensity * 100)));
    }

    private static bool KeyboardEscapePressed()
    {
        return Microsoft.Xna.Framework.Input.Keyboard.GetState()
            .IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape);
    }

    private void SyncOverlayBounds()
    {
        if (overlay is null || ownerHwnd == 0 || !NativeWindowHandle.TryGetClientSize(ownerHwnd, out var width, out var height))
            return;

        overlay.Resize(width, height);
    }

    private static string[] AddDefaultSamplePaths(string[] args)
    {
        if (args.Any(static arg => arg.Equals("--react-entry", StringComparison.OrdinalIgnoreCase)))
            return args;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var overlayRoot = Path.Combine(directory.FullName, "examples", "MonoGameOverlay");
            var overlayEntryPath = Path.Combine(overlayRoot, "dist", "react-entry.mjs");
            if (File.Exists(overlayEntryPath))
                return [.. args, "--react-entry", overlayEntryPath, "--asset-base", overlayRoot];

            overlayRoot = Path.Combine(directory.FullName, "MonoGameOverlay");
            overlayEntryPath = Path.Combine(overlayRoot, "dist", "react-entry.mjs");
            if (File.Exists(overlayEntryPath))
                return [.. args, "--react-entry", overlayEntryPath, "--asset-base", overlayRoot];

            var sampleRoot = Path.Combine(directory.FullName, "examples", "SampleApp");
            var entryPath = Path.Combine(sampleRoot, "dist", "react-entry.mjs");
            if (File.Exists(entryPath))
                return [.. args, "--react-entry", entryPath, "--asset-base", sampleRoot];

            sampleRoot = Path.Combine(directory.FullName, "SampleApp");
            entryPath = Path.Combine(sampleRoot, "dist", "react-entry.mjs");
            if (File.Exists(entryPath))
                return [.. args, "--react-entry", entryPath, "--asset-base", sampleRoot];

            directory = directory.Parent;
        }

        return args;
    }

    private static SkiaRuntimeSceneHost CreateOverlaySource(SampleAppOptions options, OverlayGameBridge bridge)
    {
        var host = new OkojoNodeReactHost(new OkojoReactHostOptions
        {
            EntrySource = options.CreateReactEntrySource(),
            BackendServices = SkiaRuntimeBackendServices.Create(),
            ConfigureAdditionalGlobals = bridge.InstallGeneratedGlobals,
            Reload = options.CreateReloadOptions(),
            AssetResolver = options.CreateAssetResolver(),
            Diagnostics = options.CreateDiagnosticsSink(),
            EnableDebugFeatures = options.EnableDebugFeatures
        });
        return new SkiaRuntimeSceneHost(host);
    }

    private static Color ResolveAccentColor(OverlayGameAction action, byte alpha)
    {
        return action switch
        {
            OverlayGameAction.GuardBreak => new Color((byte)240, (byte)181, (byte)68, alpha),
            OverlayGameAction.BlinkStep => new Color((byte)116, (byte)130, (byte)255, alpha),
            _ => new Color((byte)255, (byte)96, (byte)86, alpha)
        };
    }

    private static Color ResolveHighlightColor(OverlayGameAction action, byte alpha)
    {
        return action switch
        {
            OverlayGameAction.GuardBreak => new Color((byte)255, (byte)245, (byte)200, alpha),
            OverlayGameAction.BlinkStep => new Color((byte)205, (byte)214, (byte)255, alpha),
            _ => new Color((byte)255, (byte)224, (byte)180, alpha)
        };
    }

    private static Color ResolvePanelColor(OverlayGameAction action, byte alpha)
    {
        return action switch
        {
            OverlayGameAction.GuardBreak => new Color((byte)126, (byte)68, (byte)32, alpha),
            OverlayGameAction.BlinkStep => new Color((byte)72, (byte)68, (byte)138, alpha),
            _ => new Color((byte)170, (byte)82, (byte)85, alpha)
        };
    }
}

[GenerateJsGlobals]
public sealed partial class OverlayGameBridge
{
    [JsGlobalProperty("overlaySelectedAction")]
    public string SelectedAction => CurrentAction switch
    {
        OverlayGameAction.GuardBreak => "Guard Break",
        OverlayGameAction.BlinkStep => "Blink Step",
        _ => "Strike"
    };

    [JsGlobalProperty("overlayUiCommandCount")]
    public int UiCommandCount { get; private set; }

    [JsGlobalProperty("overlayActionSummary")]
    public string ActionSummary { get; private set; } = "Select a command in the overlay to drive the MonoGame scene.";

    [JsGlobalProperty("overlayActorX")]
    public int ActorX { get; private set; }

    [JsGlobalProperty("overlayActorY")]
    public int ActorY { get; private set; }

    [JsGlobalProperty("overlayTargetX")]
    public int TargetX { get; private set; }

    [JsGlobalProperty("overlayTargetY")]
    public int TargetY { get; private set; }

    [JsGlobalProperty("overlayDistanceToTarget")]
    public float DistanceToTarget { get; private set; }

    [JsGlobalProperty("overlayEffectPulse")]
    public float EffectPulse { get; private set; }

    public OverlayGameAction CurrentAction { get; private set; } = OverlayGameAction.Strike;

    [JsGlobalFunction("overlaySelectAction")]
    public void SelectAction(string action)
    {
        CurrentAction = NormalizeAction(action);
        UiCommandCount++;
        ActionSummary = CurrentAction switch
        {
            OverlayGameAction.GuardBreak => "Guard Break slows the actor and slams a shock frame onto the target.",
            OverlayGameAction.BlinkStep => "Blink Step accelerates movement and leaves a violet after-image trail.",
            _ => "Strike sends fast orange slash bursts between actor and target."
        };
    }

    public void UpdateGameState(Vector2 actorPosition, Vector2 targetPosition, double elapsedSeconds)
    {
        ActorX = (int)MathF.Round(actorPosition.X);
        ActorY = (int)MathF.Round(actorPosition.Y);
        TargetX = (int)MathF.Round(targetPosition.X);
        TargetY = (int)MathF.Round(targetPosition.Y);
        DistanceToTarget = Vector2.Distance(actorPosition, targetPosition);
        var speed = CurrentAction switch
        {
            OverlayGameAction.GuardBreak => 2.2f,
            OverlayGameAction.BlinkStep => 5.2f,
            _ => 3.8f
        };
        EffectPulse = 0.5f + MathF.Sin((float)elapsedSeconds * speed) * 0.5f;
    }

    private static OverlayGameAction NormalizeAction(string? action)
    {
        return action?.Trim() switch
        {
            "Guard Break" => OverlayGameAction.GuardBreak,
            "Blink Step" => OverlayGameAction.BlinkStep,
            _ => OverlayGameAction.Strike
        };
    }
}

public enum OverlayGameAction : byte
{
    Strike = 0,
    GuardBreak = 1,
    BlinkStep = 2
}

internal static class NativeWindowHandle
{
    public static nint ResolveWin32WindowHandle(nint monoGameWindowHandle)
    {
        if (monoGameWindowHandle == 0)
            throw new InvalidOperationException("MonoGame returned an empty window handle.");

        if (IsWindow(monoGameWindowHandle))
            return monoGameWindowHandle;

        var info = new SdlSysWmInfo();
        Sdl.SDL_GetVersion(out info.Version);
        if (!Sdl.GetWindowWMInfo(monoGameWindowHandle, ref info))
            throw new InvalidOperationException($"SDL_GetWindowWMInfo failed for MonoGame window handle {monoGameWindowHandle}.");

        if (info.Subsystem != Sdl.SysWmWindows)
            throw new PlatformNotSupportedException($"Expected SDL Windows subsystem, got {info.Subsystem}.");

        if (info.WinWindow == 0 || !IsWindow(info.WinWindow))
            throw new InvalidOperationException($"SDL returned an invalid HWND: {info.WinWindow}.");

        return info.WinWindow;
    }

    public static bool TryGetClientScreenBounds(nint hwnd, out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        if (!IsWindow(hwnd) || !GetClientRect(hwnd, out var rect))
            return false;

        var point = new Point(0, 0);
        if (!ClientToScreen(hwnd, ref point))
            return false;

        x = point.X;
        y = point.Y;
        width = Math.Max(1, rect.Right - rect.Left);
        height = Math.Max(1, rect.Bottom - rect.Top);
        return true;
    }

    public static bool TryGetClientSize(nint hwnd, out int width, out int height)
    {
        width = height = 0;
        if (!IsWindow(hwnd) || !GetClientRect(hwnd, out var rect))
            return false;

        width = Math.Max(1, rect.Right - rect.Left);
        height = Math.Max(1, rect.Bottom - rect.Top);
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hwnd, ref Point point);

    private static class Sdl
    {
        public const int SysWmWindows = 1;

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_GetVersion(out SdlVersion version);

        [DllImport("SDL2.dll", EntryPoint = "SDL_GetWindowWMInfo", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowWMInfo(nint window, ref SdlSysWmInfo info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlVersion
    {
        public byte Major;
        public byte Minor;
        public byte Patch;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct SdlSysWmInfo
    {
        [FieldOffset(0)]
        public SdlVersion Version;

        [FieldOffset(4)]
        public int Subsystem;

        [FieldOffset(8)]
        public nint WinWindow;
    }
}

internal readonly record struct PointerSnapshot(int X, int Y, bool LeftDown, int Buttons);
