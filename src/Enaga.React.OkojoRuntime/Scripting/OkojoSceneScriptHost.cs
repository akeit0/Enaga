using System.Collections.Concurrent;
using Enaga.Input;
using Enaga.Rendering;
using Enaga.Scene;
using Okojo;
using Okojo.Annotations;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.WebPlatform;

namespace Enaga.React.OkojoRuntime;

[GenerateJsGlobals]
public sealed partial class OkojoSceneScriptHost : ISceneFrameSource, IInputSink, IDisposable
{
    private static readonly HostTaskQueueKey[] SEventLoopQueueOrder =
    [
        WebTaskQueueKeys.Timers,
        WebTaskQueueKeys.Messages,
        WebTaskQueueKeys.Network,
        HostingTaskQueueKeys.Default,
        WebTaskQueueKeys.Rendering,
    ];
    private readonly FileSystemWatcher fileWatcher;
    private readonly RuntimeBackendServices backendServices;
    private readonly RenderInvalidatingHostTaskScheduler hostTaskScheduler;
    private readonly HostPump hostPump;
    private readonly JsRealm realm;
    private readonly JsRuntime runtime;
    private readonly SceneNodeIdentityMap<string> sceneNodeIds = new(
        "root",
        StringComparer.Ordinal
    );
    private readonly SceneStore sceneStore;
    private readonly ConcurrentQueue<HostInputEvent> inputEvents = new();
    private readonly string scriptFilePath;
    private JsFunction? pointerDownFunction;
    private JsFunction? pointerMoveFunction;
    private JsFunction? pointerUpFunction;
    private JsFunction? keyDownFunction;
    private JsFunction? keyUpFunction;
    private JsFunction? renderFunction;
    private JsFunction? setupFunction;
    private JsFunction? textInputFunction;
    private JsFunction? wheelFunction;
    private bool animationEnabled;
    private bool renderInvalidated = true;
    private SceneDamageReason pendingDamageReasons = SceneDamageReason.FullFrameFallback;
    private bool reloadRequested = true;
    private bool setupRan;

    public OkojoSceneScriptHost(
        string scriptFilePath,
        RuntimeBackendServices? backendServices = null
    )
    {
        sceneStore = new SceneStore(sceneNodeIds.RootId, new SceneViewport(1280, 800));
        this.scriptFilePath = scriptFilePath;
        this.backendServices = backendServices ?? RuntimeBackendServices.Missing;
        hostTaskScheduler = new RenderInvalidatingHostTaskScheduler(() =>
            InvalidateRender(SceneDamageReason.FullFrameFallback)
        );
        runtime = JsRuntime
            .CreateBuilder()
            .UseLowLevelHost(host => host.UseTaskScheduler(hostTaskScheduler))
            .UseWebDelayScheduler(hostTaskScheduler)
            .UseWebTimerQueue(WebTaskQueueKeys.Timers)
            .UseFetchCompletionQueue(WebTaskQueueKeys.Network)
            .UseWebRuntimeGlobals()
            .UseFetch()
            .UseGlobals(InstallGeneratedGlobals)
            .Build();
        hostPump = new HostPump(runtime.MainAgent);
        realm = runtime.MainRealm;

        var directory = Path.GetDirectoryName(scriptFilePath);
        var fileName = Path.GetFileName(scriptFilePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("A valid script file path is required.");

        fileWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter =
                NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.FileName
                | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        fileWatcher.Changed += OnScriptChanged;
        fileWatcher.Created += OnScriptChanged;
        fileWatcher.Renamed += OnScriptChanged;
    }

    [JsGlobalProperty("width")]
    public int Width { get; private set; } = 1280;

    [JsGlobalProperty("height")]
    public int Height { get; private set; } = 800;

    [JsGlobalProperty("frameCount")]
    public int FrameCount { get; private set; }

    [JsGlobalProperty("mouseX")]
    public float MouseX { get; private set; }

    [JsGlobalProperty("mouseY")]
    public float MouseY { get; private set; }

    [JsGlobalProperty("mouseButtons")]
    public int MouseButtons { get; private set; }

    [JsGlobalProperty("lastWheelDeltaX")]
    public float LastWheelDeltaX { get; private set; }

    [JsGlobalProperty("lastWheelDeltaY")]
    public float LastWheelDeltaY { get; private set; }

    [JsGlobalProperty("lastInputSynthetic")]
    public bool LastInputSynthetic { get; private set; }

    [JsGlobalProperty("lastKey")]
    public string LastKey { get; private set; } = string.Empty;

    [JsGlobalProperty("keyModifiers")]
    public int KeyModifiers { get; private set; }

    [JsGlobalProperty("keyRepeat")]
    public bool KeyRepeat { get; private set; }

    [JsGlobalProperty("lastTextInput")]
    public string LastTextInput { get; private set; } = string.Empty;

    public string? LastError { get; private set; }

    public void Dispose()
    {
        fileWatcher.Dispose();
        hostTaskScheduler.Dispose();
        runtime.Dispose();
        if (!ReferenceEquals(backendServices, RuntimeBackendServices.Missing))
            backendServices.Dispose();
    }

    public SceneFrameResult RenderFrame(int width, int height, TimeSpan elapsed)
    {
        var nextWidth = Math.Max(1, width);
        var nextHeight = Math.Max(1, height);
        if (nextWidth != Width || nextHeight != Height)
            InvalidateRender(SceneDamageReason.Resize);
        Width = nextWidth;
        Height = nextHeight;

        if (reloadRequested)
            ReloadScript();
        DrainInputQueue();

        if (!renderInvalidated && !animationEnabled)
            return SceneFrameResult.NoDamage(sceneStore.Snapshot());

        var damageReasons = ConsumeFrameDamageReasons();

        sceneStore.Reset(sceneNodeIds.RootId, new SceneViewport(Width, Height));
        ApplyRootLayout("#0b1020");

        try
        {
            if (!setupRan)
            {
                setupRan = true;
                Invoke(setupFunction);
                PumpRuntimeJobs();
            }

            Invoke(renderFunction, elapsed.TotalMilliseconds, JsValue.FromInt32(FrameCount));
            PumpRuntimeJobs();
            LastError = null;
            renderInvalidated = false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        FrameCount++;
        return SceneFrameResult.FullFrame(sceneStore.Snapshot(), Width, Height, damageReasons);
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic)
    {
        MouseX = x;
        MouseY = y;
        MouseButtons = buttons;
        LastInputSynthetic = synthetic;
        inputEvents.Enqueue(
            new HostInputEvent(HostInputEventType.Move, x, y, 0, buttons, 0, 0, synthetic)
        );
    }

    public void PointerDown(int button, int buttons, bool synthetic)
    {
        MouseButtons = buttons;
        LastInputSynthetic = synthetic;
        inputEvents.Enqueue(
            new HostInputEvent(
                HostInputEventType.Down,
                MouseX,
                MouseY,
                button,
                buttons,
                0,
                0,
                synthetic
            )
        );
    }

    public void PointerUp(int button, int buttons, bool synthetic)
    {
        MouseButtons = buttons;
        LastInputSynthetic = synthetic;
        inputEvents.Enqueue(
            new HostInputEvent(
                HostInputEventType.Up,
                MouseX,
                MouseY,
                button,
                buttons,
                0,
                0,
                synthetic
            )
        );
    }

    public void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0)
    {
        LastWheelDeltaX = deltaX;
        LastWheelDeltaY = deltaY;
        LastInputSynthetic = synthetic;
        inputEvents.Enqueue(
            new HostInputEvent(
                HostInputEventType.Wheel,
                MouseX,
                MouseY,
                0,
                MouseButtons,
                deltaX,
                deltaY,
                synthetic
            )
        );
    }

    public void KeyDown(string key, int modifiers, bool repeat, bool synthetic)
    {
        LastKey = key;
        KeyModifiers = modifiers;
        KeyRepeat = repeat;
        LastInputSynthetic = synthetic;
        inputEvents.Enqueue(
            new HostInputEvent(
                HostInputEventType.KeyDown,
                MouseX,
                MouseY,
                0,
                MouseButtons,
                0,
                0,
                synthetic,
                Key: key,
                Modifiers: modifiers,
                Repeat: repeat
            )
        );
    }

    public void KeyUp(string key, int modifiers, bool synthetic)
    {
        LastKey = key;
        KeyModifiers = modifiers;
        KeyRepeat = false;
        LastInputSynthetic = synthetic;
        inputEvents.Enqueue(
            new HostInputEvent(
                HostInputEventType.KeyUp,
                MouseX,
                MouseY,
                0,
                MouseButtons,
                0,
                0,
                synthetic,
                Key: key,
                Modifiers: modifiers
            )
        );
    }

    public void TextInput(string text, bool synthetic)
    {
        if (string.IsNullOrEmpty(text))
            return;

        LastTextInput = text;
        LastInputSynthetic = synthetic;
        inputEvents.Enqueue(
            new HostInputEvent(
                HostInputEventType.TextInput,
                MouseX,
                MouseY,
                0,
                MouseButtons,
                0,
                0,
                synthetic,
                Text: text
            )
        );
    }

    [JsGlobalFunction("setRootBackground")]
    private void SetRootBackground(string color)
    {
        ApplyRootLayout(color);
    }

    [JsGlobalFunction("configureFonts")]
    private void ConfigureFonts(string? defaultFamily = null, string[]? fallbackFamilies = null)
    {
        backendServices.Text.ConfigureFonts(defaultFamily, fallbackFamilies);
    }

    [JsGlobalFunction("registerFont")]
    private void RegisterFont(string family, string source)
    {
        backendServices.Text.RegisterFont(family, ResolveAssetPath(source));
    }

    [JsGlobalFunction("setAnimationEnabled")]
    private void SetAnimationEnabled(bool enabled)
    {
        if (animationEnabled == enabled)
            return;

        animationEnabled = enabled;
        InvalidateRender(SceneDamageReason.Animation);
    }

    private void ApplyRootLayout(string color)
    {
        sceneStore.SetLayout(
            sceneNodeIds.RootId,
            new SceneLayoutBox(SceneNodeKind.View, 0, 0, Width, Height, color)
        );
    }

    private void DrainInputQueue()
    {
        while (inputEvents.TryDequeue(out var inputEvent))
        {
            try
            {
                switch (inputEvent.Type)
                {
                    case HostInputEventType.Move:
                        Invoke(
                            pointerMoveFunction,
                            (double)inputEvent.X,
                            (double)inputEvent.Y,
                            JsValue.FromInt32(inputEvent.Buttons),
                            inputEvent.Synthetic
                        );
                        break;
                    case HostInputEventType.Down:
                        Invoke(
                            pointerDownFunction,
                            JsValue.FromInt32(inputEvent.Button),
                            JsValue.FromInt32(inputEvent.Buttons),
                            inputEvent.Synthetic
                        );
                        break;
                    case HostInputEventType.Up:
                        Invoke(
                            pointerUpFunction,
                            JsValue.FromInt32(inputEvent.Button),
                            JsValue.FromInt32(inputEvent.Buttons),
                            inputEvent.Synthetic
                        );
                        break;
                    case HostInputEventType.Wheel:
                        Invoke(
                            wheelFunction,
                            (double)inputEvent.DeltaX,
                            (double)inputEvent.DeltaY,
                            inputEvent.Synthetic
                        );
                        break;
                    case HostInputEventType.KeyDown:
                        Invoke(
                            keyDownFunction,
                            inputEvent.Key ?? string.Empty,
                            JsValue.FromInt32(inputEvent.Modifiers),
                            inputEvent.Repeat,
                            inputEvent.Synthetic
                        );
                        break;
                    case HostInputEventType.KeyUp:
                        Invoke(
                            keyUpFunction,
                            inputEvent.Key ?? string.Empty,
                            JsValue.FromInt32(inputEvent.Modifiers),
                            inputEvent.Synthetic
                        );
                        break;
                    case HostInputEventType.TextInput:
                        Invoke(
                            textInputFunction,
                            inputEvent.Text ?? string.Empty,
                            inputEvent.Synthetic
                        );
                        break;
                }

                PumpRuntimeJobs();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }

    private void Invoke(JsFunction? function, params ReadOnlySpan<JsValue> args)
    {
        if (function is null)
            return;

        _ = realm.Call(function, JsValue.Undefined, args);
    }

    private void OnScriptChanged(object? _, FileSystemEventArgs __)
    {
        reloadRequested = true;
        InvalidateRender(SceneDamageReason.RuntimeReload);
    }

    private void ReloadScript()
    {
        reloadRequested = false;
        InvalidateRender(SceneDamageReason.RuntimeReload);
        setupRan = false;
        FrameCount = 0;
        LastError = null;
        sceneStore.Reset(sceneNodeIds.RootId, new SceneViewport(Width, Height));

        var source = File.ReadAllText(scriptFilePath);
        _ = realm.Eval(source);
        PumpRuntimeJobs();
        setupFunction = TryGetGlobalFunction("setup");
        renderFunction = TryGetGlobalFunction("render");
        pointerMoveFunction = TryGetGlobalFunction("pointerMove");
        pointerDownFunction = TryGetGlobalFunction("pointerDown");
        pointerUpFunction = TryGetGlobalFunction("pointerUp");
        wheelFunction = TryGetGlobalFunction("wheel");
        keyDownFunction = TryGetGlobalFunction("keyDown");
        keyUpFunction = TryGetGlobalFunction("keyUp");
        textInputFunction = TryGetGlobalFunction("textInput");
    }

    private JsFunction? TryGetGlobalFunction(string name)
    {
        return
            realm.Global.TryGetValue(name, out var value)
            && value.TryGetObject(out var obj)
            && obj is JsFunction function
            ? function
            : null;
    }

    private void InvalidateRender(SceneDamageReason reason)
    {
        renderInvalidated = true;
        pendingDamageReasons |= reason;
    }

    private SceneDamageReason ConsumeFrameDamageReasons() =>
        SceneDamageReasonState.Consume(ref pendingDamageReasons, animationEnabled);

    private void PumpRuntimeJobs()
    {
        for (var turn = 0; turn < 256; turn++)
        {
            if (!HostTurnRunner.RunTurn(hostTaskScheduler, hostPump, SEventLoopQueueOrder))
                return;
        }
    }

    private static SceneTextAlign ParseTextAlign(string? value)
    {
        if (string.Equals(value, "center", StringComparison.OrdinalIgnoreCase))
            return SceneTextAlign.Center;
        if (string.Equals(value, "right", StringComparison.OrdinalIgnoreCase))
            return SceneTextAlign.Right;
        return SceneTextAlign.Left;
    }

    private string ResolveAssetPath(string source)
    {
        return RuntimeAssetPathResolver.Resolve(source, scriptFilePath);
    }
}
