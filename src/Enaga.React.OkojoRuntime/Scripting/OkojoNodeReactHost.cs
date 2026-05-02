using System.Collections.Concurrent;
using System.Linq;
using Enaga.Rendering;
using Okojo;
using Okojo.Annotations;
using Okojo.Hosting;
using Okojo.Node;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.WebPlatform;
using Enaga.Hosting;
using Enaga.Layout;
using Enaga.Scene;
using Enaga.Input;
namespace Enaga.React.OkojoRuntime;

[GenerateJsGlobals]
public sealed partial class OkojoNodeReactHost : ISceneFrameSource, IInputSink, IRenderRuntimeStateSource, IRenderWakeSource, IOverlayInputHitTestSource, IRuntimeBackendServicesSource, IDisposable
{
    private const string DiagnosticSourceName = nameof(OkojoNodeReactHost);
    private const double WheelTargetLatchTimeoutMs = 200;
    private static readonly HostTaskQueueKey[] SEventLoopQueueOrder =
    [
        WebTaskQueueKeys.Timers,
        WebTaskQueueKeys.Messages,
        WebTaskQueueKeys.Network,
        HostingTaskQueueKeys.Default,
        WebTaskQueueKeys.Rendering
    ];
    private readonly bool debugFeaturesEnabled;
    private readonly ShaderTraceLogger shaderTrace;
    private readonly IRuntimeDiagnosticsSink diagnostics;
    private readonly RuntimeAssetService assetService;
    private readonly HostTextInputController textInputController;
    private readonly TimeProvider timeProvider;
    private readonly Action<JsGlobalInstaller>? configureAdditionalGlobals;
    private readonly Action<JsRuntimeBuilder>? configureRuntime;
    private readonly Action<NodeTerminalOptions>? configureTerminal;
    private readonly RuntimeBackendServices backendServices;
    private readonly HostInputState inputState;
    private readonly IReactAppEntrySource entrySource;
    private readonly DefaultPositionMode defaultPositionMode;
    private readonly RuntimeReloadCoordinator reloadCoordinator;
    private readonly RuntimeFileWatchService? fileWatchService;
    private readonly SceneStore sceneStore = new("root", new SceneViewport(1280, 800));
    private readonly object renderInvalidationGate = new();
    private readonly Dictionary<string, NativeHoverTargetState> hoverTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeImageState> images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeScrollViewState> scrollViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeTextInputState> textInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsValue[]> memoizedGlobalFunctionArguments = new(StringComparer.Ordinal);
    private LayoutCalculator? stackLayoutCalculator;
    private ReactAppPropertyAtoms? propertyAtoms;
    private readonly List<LowLevelRepaintEvent> pendingLowLevelRepaintEvents = [];
    private int loggedRenderFrames;
    private int loggedResets;
    private int loggedTexts;
    private int loggedViews;
    private int nextHoverTargetZOrder;
    private int nextScrollViewZOrder;
    private int nextTextInputZOrder;
    private int hoverTargetGeneration;
    private int imageGeneration;
    private int scrollViewGeneration;
    private int textInputGeneration;
    private double currentElapsedMs;
    private bool animationEnabled;
    private bool shaderAnimationEnabled;
    private bool renderInvalidated = true;
    private SceneDamageReason pendingDamageReasons = SceneDamageReason.FullFrameFallback;
    private long renderInvalidationVersion;
    private JsFunction? keyDownFunction;
    private JsFunction? keyUpFunction;
    private JsFunction? pointerDownFunction;
    private JsFunction? pointerMoveFunction;
    private JsFunction? pointerUpFunction;
    private JsFunction? renderFrameFunction;
    private JsFunction? imageEventFunction;
    private JsFunction? textInputEventFunction;
    private JsFunction? textInputFunction;
    private JsFunction? wheelFunction;
    private NodeRuntime? runtime;
    private HostPump? hostPump;
    private RenderInvalidatingHostTaskScheduler? hostTaskScheduler;
    private string? focusedTextInputId;
    private readonly SceneScrollBarDragState activeScrollBarDrag = new();
    private readonly SceneWheelScrollTargetLatch<string> wheelScrollTargetLatch = new(WheelTargetLatchTimeoutMs);
    private double? previousScrollAnimationElapsedMs;
    private int currentFrameViewCallCount;

    public OkojoNodeReactHost(
        string entryPath,
        bool debugEnabled,
        bool shaderTraceEnabled = false,
        Action<JsGlobalInstaller>? configureAdditionalGlobals = null,
        RuntimeBackendServices? backendServices = null,
        bool enableFileWatching = false,
        DefaultPositionMode defaultPositionMode = DefaultPositionMode.Relative,
        ReactRuntimeReloadMode reloadMode = ReactRuntimeReloadMode.RebuildRuntime)
        : this(CreateCompatibilityOptions(
            new FileReactAppEntrySource(entryPath),
            debugEnabled,
            shaderTraceEnabled,
            configureAdditionalGlobals,
            backendServices,
            enableFileWatching,
            defaultPositionMode,
            reloadMode))
    {
    }

    public OkojoNodeReactHost(
        IReactAppEntrySource entrySource,
        bool debugEnabled,
        bool shaderTraceEnabled = false,
        Action<JsGlobalInstaller>? configureAdditionalGlobals = null,
        RuntimeBackendServices? backendServices = null,
        bool enableFileWatching = false,
        DefaultPositionMode defaultPositionMode = DefaultPositionMode.Relative,
        ReactRuntimeReloadMode reloadMode = ReactRuntimeReloadMode.RebuildRuntime)
        : this(CreateCompatibilityOptions(
            entrySource,
            debugEnabled,
            shaderTraceEnabled,
            configureAdditionalGlobals,
            backendServices,
            enableFileWatching,
            defaultPositionMode,
            reloadMode))
    {
    }

    public OkojoNodeReactHost(OkojoReactHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        entrySource = options.EntrySource ?? throw new ArgumentNullException(nameof(options.EntrySource));
        debugFeaturesEnabled = options.EnableDebugFeatures;
        timeProvider = options.TimeProvider ?? throw new ArgumentNullException(nameof(options.TimeProvider));
        diagnostics = options.Diagnostics ?? RuntimeDiagnosticsSink.None;
        assetService = new RuntimeAssetService(options.AssetResolver ?? RuntimeAssetResolver.FileSystemRelativeToEntry);
        configureAdditionalGlobals = options.ConfigureAdditionalGlobals;
        backendServices = options.BackendServices ?? RuntimeBackendServices.Missing;
        defaultPositionMode = options.DefaultPositionMode;
        reloadCoordinator = new RuntimeReloadCoordinator(options.Reload ?? ReactRuntimeReloadOptions.Production);
        shaderTrace = new ShaderTraceLogger(diagnostics);
        inputState = new HostInputState(timeProvider);
        textInputController = new HostTextInputController(
            backendServices.Text,
            EnsureTextInputVisible,
            UpdateTextInputLayout,
            NotifyTextInputEvent,
            MoveFocus,
            SetFocusedTextInput);
        var watchPaths = ResolveWatchPaths(entrySource, reloadCoordinator.Options);
        if (reloadCoordinator.Options.EnableFileWatching && watchPaths.Length > 0)
        {
            fileWatchService = new RuntimeFileWatchService(watchPaths, reloadCoordinator.Options.WatchPatterns);
            fileWatchService.Changed += OnWatchedFileChanged;
        }
        configureRuntime = options.ConfigureRuntime;
        configureTerminal = options.ConfigureTerminal;
    }

    public RuntimeBackendServices BackendServices => backendServices;

    private static OkojoReactHostOptions CreateCompatibilityOptions(
        IReactAppEntrySource entrySource,
        bool debugEnabled,
        bool shaderTraceEnabled,
        Action<JsGlobalInstaller>? configureAdditionalGlobals,
        RuntimeBackendServices? backendServices,
        bool enableFileWatching,
        DefaultPositionMode defaultPositionMode,
        ReactRuntimeReloadMode reloadMode)
    {
        return new OkojoReactHostOptions
        {
            EntrySource = entrySource,
            EnableDebugFeatures = debugEnabled,
            ConfigureAdditionalGlobals = configureAdditionalGlobals,
            BackendServices = backendServices ?? RuntimeBackendServices.Missing,
            DefaultPositionMode = defaultPositionMode,
            Reload = new ReactRuntimeReloadOptions
            {
                Mode = reloadMode,
                EnableFileWatching = enableFileWatching
            },
            Diagnostics = CreateCompatibilityDiagnostics(debugEnabled, shaderTraceEnabled)
        };
    }

    private static IRuntimeDiagnosticsSink CreateCompatibilityDiagnostics(bool debugEnabled, bool shaderTraceEnabled)
    {
        List<RuntimeDiagnosticArea> areas = [];
        if (debugEnabled)
        {
            areas.Add(RuntimeDiagnosticArea.RuntimeLifecycle);
            areas.Add(RuntimeDiagnosticArea.Reload);
            areas.Add(RuntimeDiagnosticArea.ModuleInvalidation);
            areas.Add(RuntimeDiagnosticArea.SceneCommit);
            areas.Add(RuntimeDiagnosticArea.Rendering);
            areas.Add(RuntimeDiagnosticArea.Assets);
        }

        if (shaderTraceEnabled)
            areas.Add(RuntimeDiagnosticArea.ShaderTrace);

        return areas.Count == 0
            ? RuntimeDiagnosticsSink.None
            : RuntimeDiagnosticsSink.Console([.. areas]);
    }

    private static string[] ResolveWatchPaths(IReactAppEntrySource entrySource, ReactRuntimeReloadOptions reloadOptions)
    {
        var configuredPaths = reloadOptions.WatchPaths.Where(static path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (configuredPaths.Length > 0)
            return configuredPaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return entrySource.EnumerateWatchPaths()
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }


    [JsMember]
    public int Width { get; private set; } = 1280;

    [JsMember]
    public int Height { get; private set; } = 800;

    [JsMember]
    public int FrameCount { get; private set; }

    [JsMember]
    public float MouseX => inputState.MouseX;

    [JsMember]
    public float MouseY => inputState.MouseY;

    [JsMember]
    public int MouseButtons => inputState.MouseButtons;

    [JsMember]
    public float LastWheelDeltaX => inputState.LastWheelDeltaX;

    [JsMember]
    public float LastWheelDeltaY => inputState.LastWheelDeltaY;

    [JsMember]
    public bool LastInputSynthetic => inputState.LastInputSynthetic;

    [JsMember]
    public string LastKey => inputState.LastKey;

    [JsMember]
    public int KeyModifiers => inputState.KeyModifiers;

    [JsMember]
    public bool KeyRepeat => inputState.KeyRepeat;

    [JsMember]
    public string LastTextInput => inputState.LastTextInput;

    [JsMember]
    public bool NativeDebugEnabled => debugFeaturesEnabled;

    [JsMember]
    public string HoveredId { get; private set; } = string.Empty;

    [JsMember]
    public float HoverTargetLeft { get; private set; }

    [JsMember]
    public float HoverTargetTop { get; private set; }

    [JsMember]
    public float HoverTargetWidth { get; private set; }

    [JsMember]
    public float HoverTargetHeight { get; private set; }

    public string? LastError { get; private set; }

    public event Action? RenderWakeRequested;

    public RenderRuntimeStateSnapshot GetRenderRuntimeStateSnapshot()
    {
        if (focusedTextInputId is { } focusedId &&
            textInputs.TryGetValue(focusedId, out var focusedState))
        {
            return new RenderRuntimeStateSnapshot(
                focusedState.ImeOpen,
                focusedState.CompositionText.Length > 0,
                animationEnabled,
                shaderAnimationEnabled,
                IsRenderInvalidated(),
                currentFrameViewCallCount);
        }

        return new RenderRuntimeStateSnapshot(
            false,
            false,
            animationEnabled,
            shaderAnimationEnabled,
            IsRenderInvalidated(),
            currentFrameViewCallCount);
    }

    public void Dispose()
    {
        Log(RuntimeDiagnosticArea.RuntimeLifecycle, "disposing runtime");
        hostTaskScheduler?.Dispose();
        runtime?.Dispose();
        fileWatchService?.Dispose();
        assetService.Dispose();
        entrySource.Dispose();
        if (!ReferenceEquals(backendServices, RuntimeBackendServices.Missing))
            backendServices.Dispose();
    }

    public void RequestRuntimeReload(string? changedPath = null)
    {
        reloadCoordinator.RequestReload(changedPath);
        InvalidateRender(SceneDamageReason.RuntimeReload);
    }

    public void RequestRender(SceneDamageReason reason = SceneDamageReason.FullFrameFallback)
    {
        InvalidateRender(reason);
        RenderWakeRequested?.Invoke();
    }

    public bool TryInvokeGlobalFunction(string name, SceneDamageReason reason, params ReadOnlySpan<JsValue> args)
    {
        var function = TryGetGlobalFunction(name);
        if (function is null || runtime is null)
            return false;

        Invoke(function, args);
        PumpRuntimeJobs();
        RequestRender(reason);
        return true;
    }

    public bool TryInvokeGlobalFunctionWhenChanged(string name, SceneDamageReason reason, params ReadOnlySpan<JsValue> args)
    {
        if (memoizedGlobalFunctionArguments.TryGetValue(name, out var previousArgs) &&
            JsValueArgumentMemo.AreSame(previousArgs, args))
        {
            return false;
        }

        if (!TryInvokeGlobalFunction(name, reason, args))
            return false;

        memoizedGlobalFunctionArguments[name] = args.ToArray();
        return true;
    }

    private static class JsValueArgumentMemo
    {
        public static bool AreSame(ReadOnlySpan<JsValue> previous, ReadOnlySpan<JsValue> next)
        {
            if (previous.Length != next.Length)
                return false;

            for (var index = 0; index < next.Length; index++)
            {
                if (!JsValue.SameValue(previous[index], next[index]))
                    return false;
            }

            return true;
        }
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic)
    {
        inputState.RecordPointerMove(x, y, buttons, synthetic);
        if ((buttons & HostInputState.LeftMouseButtonMask) != 0 && UpdateActiveScrollBarDrag(x, y))
        {
            pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.PointerMove, x, y));
            return;
        }

        UpdateHoverState(sceneStore.Snapshot(), x, y);
        if ((buttons & HostInputState.LeftMouseButtonMask) != 0 &&
            focusedTextInputId is { } focusedId &&
            textInputs.TryGetValue(focusedId, out var focusedState) &&
            focusedState.IsSelectingWithMouse)
        {
            var caretIndex = HitTestCaretIndex(focusedState, x, y);
            SetSelection(focusedState, focusedState.SelectionAnchorIndex, caretIndex);
            UpdateTextInputLayout(focusedState);
        }

        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.PointerMove, x, y));
        inputState.Events.Enqueue(new HostInputEvent(HostInputEventType.Move, x, y, 0, buttons, 0, 0, synthetic));
    }

    public bool HitTestOverlayInput(float x, float y)
    {
        if (activeScrollBarDrag.IsActive)
            return true;

        return TryHitHoverTarget(sceneStore.Snapshot(), x, y);
    }

    public void PointerDown(int button, int buttons, bool synthetic)
    {
        inputState.RecordPointerButtons(buttons, synthetic);
        ClearWheelScrollTarget();
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.PointerDown, MouseX, MouseY));
        if (button == 0 && TryBeginScrollBarDrag(MouseX, MouseY))
            return;

        var focusedState = FocusTextInputAt(MouseX, MouseY);
        if (button == 0 && focusedState is not null)
        {
            var caretIndex = HitTestCaretIndex(focusedState, MouseX, MouseY);
            if (IsDoubleClick(focusedState.Id, MouseX, MouseY))
            {
                focusedState.IsSelectingWithMouse = false;
                SelectWordAt(focusedState, caretIndex);
            }
            else
            {
                focusedState.IsSelectingWithMouse = true;
                SetSelection(focusedState, caretIndex, caretIndex);
            }

            RememberPrimaryClick(focusedState.Id, MouseX, MouseY);
            UpdateTextInputLayout(focusedState);
        }

        inputState.Events.Enqueue(new HostInputEvent(HostInputEventType.Down, MouseX, MouseY, button, buttons, 0, 0, synthetic));
    }

    public void PointerUp(int button, int buttons, bool synthetic)
    {
        inputState.RecordPointerButtons(buttons, synthetic);
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.PointerUp, MouseX, MouseY));
        if (button == 0 && EndActiveScrollBarDrag())
            return;

        if (button == 0 &&
            focusedTextInputId is { } focusedId &&
            textInputs.TryGetValue(focusedId, out var focusedState))
        {
            if (focusedState.IsSelectingWithMouse)
            {
                var caretIndex = HitTestCaretIndex(focusedState, MouseX, MouseY);
                SetSelection(focusedState, focusedState.SelectionAnchorIndex, caretIndex);
                UpdateTextInputLayout(focusedState);
            }

            focusedState.IsSelectingWithMouse = false;
        }

        inputState.Events.Enqueue(new HostInputEvent(HostInputEventType.Up, MouseX, MouseY, button, buttons, 0, 0, synthetic));
    }

    public void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0)
    {
        inputState.RecordWheel(deltaX, deltaY, synthetic);
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.Wheel, MouseX, MouseY));
        ApplyScrollWheel(deltaX, deltaY, inputState.ElapsedMs);
        inputState.Events.Enqueue(new HostInputEvent(HostInputEventType.Wheel, MouseX, MouseY, 0, MouseButtons, deltaX, deltaY, synthetic));
    }

    public void KeyDown(string key, int modifiers, bool repeat, bool synthetic)
    {
        inputState.RecordKey(key, modifiers, repeat, synthetic);
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.KeyDown));
        if (!repeat)
        {
            inputState.HeldKeys[key] = new HostKeyRepeatState(GetInputElapsedMs() + HostInputState.InitialKeyRepeatDelayMs, modifiers, HostInputState.InitialKeyRepeatIntervalMs);
            inputState.ActivePrintableRepeat = TryCreatePrintableRepeatState(key, modifiers);
        }
        HandleFocusedTextInputKey(key, modifiers);
        inputState.Events.Enqueue(new HostInputEvent(HostInputEventType.KeyDown, MouseX, MouseY, 0, MouseButtons, 0, 0, synthetic, Key: key, Modifiers: modifiers, Repeat: repeat));
    }

    public void KeyUp(string key, int modifiers, bool synthetic)
    {
        inputState.RecordKey(key, modifiers, repeat: false, synthetic);
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.KeyUp));
        inputState.HeldKeys.Remove(key);
        if (inputState.ActivePrintableRepeat is { Key: var repeatKey } && string.Equals(repeatKey, key, StringComparison.Ordinal))
            inputState.ActivePrintableRepeat = null;
        inputState.Events.Enqueue(new HostInputEvent(HostInputEventType.KeyUp, MouseX, MouseY, 0, MouseButtons, 0, 0, synthetic, Key: key, Modifiers: modifiers));
    }

    public void TextInput(string text, bool synthetic)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (!synthetic &&
            inputState.ActivePrintableRepeat is { } printableRepeat &&
            string.Equals(printableRepeat.Text, text, StringComparison.Ordinal))
        {
            if (!printableRepeat.NativeInputAccepted)
                inputState.ActivePrintableRepeat = printableRepeat with { NativeInputAccepted = true };
            else
                return;
        }

        inputState.RecordTextInput(text, synthetic);
        pendingLowLevelRepaintEvents.Add(new LowLevelRepaintEvent(LowLevelRepaintEventKind.TextInput));
        ApplyFocusedTextInputText(text);
        inputState.Events.Enqueue(new HostInputEvent(HostInputEventType.TextInput, MouseX, MouseY, 0, MouseButtons, 0, 0, synthetic, Text: text));
    }

    public SceneFrameResult RenderFrame(int width, int height, TimeSpan elapsed)
    {
        currentFrameViewCallCount = 0;
        var nextWidth = Math.Max(1, width);
        var nextHeight = Math.Max(1, height);
        if (nextWidth != Width || nextHeight != Height)
            InvalidateRender(SceneDamageReason.Resize);
        Width = nextWidth;
        Height = nextHeight;
        currentElapsedMs = elapsed.TotalMilliseconds;
        var scrollAnimationDeltaSeconds = ResolveScrollAnimationDeltaSeconds(currentElapsedMs);

        if (reloadCoordinator.ReloadRequested || runtime is null)
        {
            if (runtime is null || !reloadCoordinator.ShouldWaitForStabilization(DateTime.UtcNow))
            {
                if (runtime is null || reloadCoordinator.Options.Mode == ReactRuntimeReloadMode.RebuildRuntime)
                    ReloadRuntime();
                else if (reloadCoordinator.Options.Mode == ReactRuntimeReloadMode.FastRefresh)
                    ReloadFastRefresh();
                else
                    ReloadModuleGraph();
            }
        }

        if (runtime is null)
        {
            Log(RuntimeDiagnosticArea.RuntimeLifecycle, "render skipped because runtime is null");
            return SceneFrameResult.FullFrame(
                sceneStore.Snapshot(),
                Width,
                Height,
                ConsumeFrameDamageReasons(out _));
        }

        PumpHeldKeys(GetInputElapsedMs());
        DrainInputQueue();
        UpdateTrackedImages();
        PumpRuntimeJobs();
        if (AdvanceScrollAnimations(scrollAnimationDeltaSeconds))
            InvalidateRender(SceneDamageReason.Scroll);

        var currentCommit = sceneStore.Snapshot();
        shaderTrace.ObserveFrame(shaderAnimationEnabled);
        if (!IsRenderInvalidated())
        {
            if (shaderAnimationEnabled)
            {
                var shaderDirtyRects = BuildHostAnimatedShaderDirtyRects(currentCommit);
                shaderTrace.RecordShaderOnly(shaderDirtyRects, shaderAnimationEnabled);
                if (shaderDirtyRects.Length > 0)
                {
                    shaderTrace.FlushIfDue(currentElapsedMs, FrameCount, shaderAnimationEnabled, IsRenderInvalidated());
                    return new SceneFrameResult(currentCommit, shaderDirtyRects, SceneDamageReason.Animation);
                }
            }

            if (!animationEnabled)
            {
                shaderTrace.RecordNoDamage(shaderAnimationEnabled);
                shaderTrace.FlushIfDue(currentElapsedMs, FrameCount, shaderAnimationEnabled, IsRenderInvalidated());
                return SceneFrameResult.NoDamage(currentCommit);
            }
        }

        var damageReasons = ConsumeFrameDamageReasons(out var consumedInvalidationVersion);
        shaderTrace.RecordFullRender(damageReasons, shaderAnimationEnabled);

        try
        {
            if (loggedRenderFrames < 5)
                Log(RuntimeDiagnosticArea.Rendering, $"render frame #{FrameCount} elapsed={elapsed.TotalMilliseconds:F1}ms callback={(renderFrameFunction is null ? "missing" : "ok")}");
            Invoke(renderFrameFunction,
                elapsed.TotalMilliseconds,
                JsValue.FromInt32(FrameCount));
            PumpRuntimeJobs();
            PruneInactiveTextInputs();
            PruneInactiveScrollViews();
            PruneInactiveImages();
            PruneInactiveHoverTargets();
            RefreshHoverState();
            LastError = null;
            ClearRenderInvalidation(consumedInvalidationVersion);
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            Log(RuntimeDiagnosticArea.Rendering, $"render failed: {LastError}");
            InvalidateRender(SceneDamageReason.ErrorOverlay);
        }

        loggedRenderFrames++;
        FrameCount++;
        shaderTrace.FlushIfDue(currentElapsedMs, FrameCount, shaderAnimationEnabled, IsRenderInvalidated());
        return new SceneFrameResult(
            SyncScrollViewLayoutFromCommit(sceneStore.Snapshot()),
            [],
            damageReasons);
    }

    private void ResetScene(string backgroundColor = "#08111f")
    {
        if (loggedResets < 5)
            Log(RuntimeDiagnosticArea.SceneCommit, $"resetScene background={backgroundColor}");
        sceneStore.Reset("root", new SceneViewport(Width, Height));
        sceneStore.SetLayout("root", new SceneLayoutBox(SceneNodeKind.View, 0, 0, Width, Height, backgroundColor));
        hoverTargetGeneration++;
        imageGeneration++;
        scrollViewGeneration++;
        textInputGeneration++;
        loggedResets++;
    }

    [JsGlobalFunction("setAnimationEnabled")]
    private void SetAnimationEnabled(bool enabled)
    {
        if (animationEnabled == enabled)
            return;

        animationEnabled = enabled;
        InvalidateRender(SceneDamageReason.Animation);
    }

    [JsGlobalFunction("setShaderAnimationEnabled")]
    private void SetShaderAnimationEnabled(bool enabled)
    {
        shaderAnimationEnabled = enabled;
    }

    private void View(
        string parentId,
        string id,
        float left,
        float top,
        float width,
        float height,
        JsObject? style)
    {
        currentFrameViewCallCount++;
        var hostStyle = ReadStyle(style);
        var layoutPadding = ResolveLayoutPadding(hostStyle, 0);
        UpdateHoverTarget(id, hostStyle);
        if (loggedViews < 8)
            Log(RuntimeDiagnosticArea.SceneCommit, $"view id={id} parent={parentId} x={left} y={top} w={width} h={height}");
        sceneStore.UpsertNode(
            id,
            SceneNodeKind.View,
            parentId,
            id,
            new SceneLayoutBox(
                SceneNodeKind.View,
                left,
                top,
                width,
                height,
                hostStyle.BackgroundColor,
                hostStyle.BorderColor,
                hostStyle.BorderWidth,
                hostStyle.BorderRadius,
                hostStyle.BoxSizing,
                ClipContent: hostStyle.ClipContent,
                PaddingLeft: layoutPadding.Left,
                PaddingTop: layoutPadding.Top,
                PaddingRight: layoutPadding.Right,
                PaddingBottom: layoutPadding.Bottom,
                BackgroundGradient: hostStyle.BackgroundGradient,
                BackgroundShader: hostStyle.BackgroundShader,
                BackgroundShadows: hostStyle.BackgroundShadows));
        loggedViews++;
    }

    private void ScrollView(
        string parentId,
        string id,
        float left,
        float top,
        float width,
        float height,
        JsObject? style,
        float contentWidth,
        float contentHeight)
    {
        var hostStyle = ReadStyle(style);
        var layoutPadding = ResolveLayoutPadding(hostStyle, 0);
        UpdateHoverTarget(id, hostStyle);
        var state = GetOrCreateScrollViewState(id);
        state.ParentId = parentId;
        state.Left = left;
        state.Top = top;
        state.Width = width;
        state.Height = height;
        state.HorizontalScrollEnabled = hostStyle.ContentWidth.HasValue;
        state.ContentWidth = state.HorizontalScrollEnabled
            ? Math.Max(width, hostStyle.ContentWidth!.Value)
            : width;
        state.ContentHeight = Math.Max(height, contentHeight);
        state.BackgroundColor = hostStyle.BackgroundColor;
        state.BackgroundGradient = hostStyle.BackgroundGradient;
        state.BackgroundShader = hostStyle.BackgroundShader;
        state.BackgroundShadows = hostStyle.BackgroundShadows;
        state.BorderColor = hostStyle.BorderColor;
        state.BorderWidth = hostStyle.BorderWidth;
        state.BorderRadius = hostStyle.BorderRadius;
        state.BoxSizing = hostStyle.BoxSizing;
        state.ClipContent = hostStyle.ClipContent;
        state.PaddingLeft = layoutPadding.Left;
        state.PaddingTop = layoutPadding.Top;
        state.PaddingRight = layoutPadding.Right;
        state.PaddingBottom = layoutPadding.Bottom;
        state.Generation = scrollViewGeneration;
        if (Math.Abs(state.ScrollX) < 0.001f && hostStyle.ScrollX > 0)
        {
            state.ScrollX = hostStyle.ScrollX;
            state.TargetScrollX = hostStyle.ScrollX;
        }
        if (Math.Abs(state.ScrollY) < 0.001f && hostStyle.ScrollY > 0)
        {
            state.ScrollY = hostStyle.ScrollY;
            state.TargetScrollY = hostStyle.ScrollY;
        }
        UpdateScrollViewLayout(state);
    }

    private void Text(
        string parentId,
        string id,
        float left,
        float top,
        float width,
        float height,
        string content,
        JsObject? style)
    {
        var hasExplicitHeight = GetNullableFloatProperty(style, propertyAtoms!.Height).HasValue;
        var hostStyle = ReadStyle(style);
        UpdateHoverTarget(id, hostStyle);
        if (loggedTexts < 8)
            Log(RuntimeDiagnosticArea.SceneCommit, $"text id={id} parent={parentId} content={content}");
        var textStyle = CreateSceneTextStyle(hostStyle);
        var resolvedHeight = hasExplicitHeight
            ? height
            : backendServices.Text.MeasureTextHeight(content, width, textStyle);
        sceneStore.UpsertNode(
            id,
            SceneNodeKind.Text,
            parentId,
            id,
            new SceneLayoutBox(
                SceneNodeKind.Text,
                left,
                top,
                width,
                resolvedHeight,
                TextContent: content,
                TextStyle: textStyle));
        loggedTexts++;
    }

    [JsGlobalFunction("nativeMeasureTextHeight")]
    private float NativeMeasureTextHeight(string content, float width, JsValue styleValue)
    {
        var style = ReadStyle(styleValue.TryGetObject(out var styleObject) ? styleObject : null);
        var textStyle = CreateSceneTextStyle(style);
        return backendServices.Text.MeasureTextHeight(content, width, textStyle);
    }

    [JsGlobalFunction("nativeMeasureTextWidth")]
    private float NativeMeasureTextWidth(string content, JsValue styleValue)
    {
        var style = ReadStyle(styleValue.TryGetObject(out var styleObject) ? styleObject : null);
        var textStyle = CreateSceneTextStyle(style);
        return backendServices.Text.MeasureTextWidth(content, textStyle);
    }

    private void Image(
        string parentId,
        string id,
        float left,
        float top,
        float width,
        float height,
        string source,
        string? placeholderSource,
        JsObject? style)
    {
        var hostStyle = ReadStyle(style);
        UpdateHoverTarget(id, hostStyle);
        var resolvedSource = ResolveAssetPath(source);
        var resolvedPlaceholderSource = string.IsNullOrWhiteSpace(placeholderSource) ? null : ResolveAssetPath(placeholderSource);
        sceneStore.UpsertNode(
            id,
            SceneNodeKind.Image,
            parentId,
            id,
            new SceneLayoutBox(
                SceneNodeKind.Image,
                left,
                top,
                width,
                height,
                hostStyle.BackgroundColor,
                hostStyle.BorderColor,
                hostStyle.BorderWidth,
                hostStyle.BorderRadius,
                hostStyle.BoxSizing,
                ImageSource: resolvedSource,
                ImagePlaceholderSource: resolvedPlaceholderSource,
                ImageFit: hostStyle.ImageFit));

        var state = GetOrCreateImageState(id);
        state.RequestedSource = source;
        state.RequestedPlaceholderSource = placeholderSource ?? string.Empty;
        state.Generation = imageGeneration;
        if (!string.Equals(state.Source, resolvedSource, StringComparison.Ordinal))
        {
            state.Source = resolvedSource;
            state.LoadState = NativeImageLoadState.None;
        }
        var nextPlaceholderSource = resolvedPlaceholderSource ?? string.Empty;
        if (!string.Equals(state.PlaceholderSource, nextPlaceholderSource, StringComparison.Ordinal))
        {
            state.PlaceholderSource = nextPlaceholderSource;
            state.PlaceholderLoadState = string.IsNullOrEmpty(nextPlaceholderSource)
                ? NativeImageLoadState.None
                : NativeImageLoadState.Pending;
        }
    }

    private void TextInputNode(
        string parentId,
        string id,
        float left,
        float top,
        float width,
        float height,
        string value,
        string placeholder,
        JsObject? style)
    {
        var hostStyle = ReadStyle(style);
        UpdateHoverTarget(id, hostStyle);
        var layoutPadding = ResolveLayoutPadding(hostStyle, 12, 10);
        var state = GetOrCreateTextInputState(id);
        state.ParentId = parentId;
        state.Left = left;
        state.Top = top;
        state.Width = width;
        state.Height = height;
        state.PlaceholderText = placeholder;
        state.FontSize = hostStyle.FontSize;
        state.Color = hostStyle.Color;
        state.FontFamily = hostStyle.FontFamily;
        state.FontWeight = hostStyle.FontWeight;
        state.TextAlign = ParseTextAlign(hostStyle.TextAlign);
        state.PaddingLeft = layoutPadding.Left;
        state.PaddingTop = layoutPadding.Top;
        state.PaddingRight = layoutPadding.Right;
        state.PaddingBottom = layoutPadding.Bottom;
        state.Multiline = hostStyle.Multiline;
        state.LineHeight = hostStyle.LineHeight > 0 ? hostStyle.LineHeight : Math.Max(hostStyle.FontSize + 4, hostStyle.FontSize * 1.35f);
        state.BackgroundColor = hostStyle.BackgroundColor;
        state.BackgroundGradient = hostStyle.BackgroundGradient;
        state.BackgroundShader = hostStyle.BackgroundShader;
        state.BackgroundShadows = hostStyle.BackgroundShadows;
        state.BorderColor = hostStyle.BorderColor;
        state.ActiveBorderColor = hostStyle.ActiveBorderColor;
        state.PlaceholderColor = hostStyle.PlaceholderColor ?? state.PlaceholderColor ?? "#475569";
        state.CompositionUnderlineColor = hostStyle.CompositionUnderlineColor;
        state.CompositionSelectionUnderlineColor = hostStyle.CompositionSelectionUnderlineColor;
        state.BorderRadius = hostStyle.BorderRadius > 0 ? hostStyle.BorderRadius : 12;
        state.BoxSizing = hostStyle.BoxSizing;
        state.Generation = textInputGeneration;
        TextInputStateLogic.ApplyExternalValue(state, backendServices.Text, value);

        UpdateTextInputLayout(state);
    }

    private HostStyle ReadStyle(JsObject? style)
    {
        if (style is null)
            return HostStyle.Default;

        var padding = GetNullableStyleFloatProperty(style, propertyAtoms!.Padding);
        var paddingX = GetNullableStyleFloatProperty(style, propertyAtoms.PaddingHorizontal)
            ?? padding;
        var paddingY = GetNullableStyleFloatProperty(style, propertyAtoms.PaddingVertical)
            ?? padding;
        var paddingLeft = GetNullableStyleFloatProperty(style, propertyAtoms.PaddingLeft) ?? paddingX;
        var paddingTop = GetNullableStyleFloatProperty(style, propertyAtoms.PaddingTop) ?? paddingY;
        var paddingRight = GetNullableStyleFloatProperty(style, propertyAtoms.PaddingRight) ?? paddingX;
        var paddingBottom = GetNullableStyleFloatProperty(style, propertyAtoms.PaddingBottom) ?? paddingY;

        return new HostStyle(
            BackgroundColor: GetStyleStringProperty(style, propertyAtoms!.BackgroundColor),
            BackgroundGradient: ReadGradient(TryGetStyleObjectProperty(style, propertyAtoms.BackgroundGradient)),
            BackgroundShader: ReadRuntimeShader(TryGetStyleObjectProperty(style, propertyAtoms.BackgroundShader)),
            BackgroundShadows: ReadShadows(GetStylePropertyOrUndefined(style, propertyAtoms.Shadow)),
            BorderColor: GetStyleStringProperty(style, propertyAtoms.BorderColor),
            BorderWidth: GetStyleFloatProperty(style, propertyAtoms.BorderWidth),
            BorderRadius: GetStyleFloatProperty(style, propertyAtoms.BorderRadius),
            BoxSizing: ParseSceneBoxSizing(GetStyleStringProperty(style, propertyAtoms.BoxSizing)),
            ClipContent: string.Equals(GetStyleStringProperty(style, propertyAtoms.Overflow), "hidden", StringComparison.Ordinal),
            FontSize: GetStyleFloatProperty(style, propertyAtoms.FontSize, 16),
            Color: GetStyleStringProperty(style, propertyAtoms.Color),
            FontFamily: GetStyleStringProperty(style, propertyAtoms.FontFamily),
            FontWeight: GetStyleIntProperty(style, propertyAtoms.FontWeight, 400),
            TextAlign: GetStyleStringProperty(style, propertyAtoms.TextAlign),
            WrapText: GetStyleBoolProperty(style, propertyAtoms.Wrap),
            PaddingLeft: paddingLeft,
            PaddingTop: paddingTop,
            PaddingRight: paddingRight,
            PaddingBottom: paddingBottom,
            Multiline: GetStyleBoolProperty(style, propertyAtoms.Multiline),
            LineHeight: GetStyleFloatProperty(style, propertyAtoms.LineHeight),
            ActiveBorderColor: GetStyleStringProperty(style, propertyAtoms.ActiveBorderColor),
            PlaceholderColor: GetStyleStringProperty(style, propertyAtoms.PlaceholderColor),
            CompositionUnderlineColor: GetStyleStringProperty(style, propertyAtoms.CompositionUnderlineColor),
            CompositionSelectionUnderlineColor: GetStyleStringProperty(style, propertyAtoms.CompositionSelectionUnderlineColor),
            ImageFit: GetStyleStringProperty(style, propertyAtoms.Fit),
            Hoverable: GetStyleBoolProperty(style, propertyAtoms.Hoverable),
            Tooltip: NormalizeTooltip(GetStyleStringProperty(style, propertyAtoms.Tooltip)),
            ContentWidth: GetNullableStyleFloatProperty(style, propertyAtoms.ContentWidth),
            ContentHeight: GetNullableStyleFloatProperty(style, propertyAtoms.ContentHeight),
            ScrollX: GetStyleFloatProperty(style, propertyAtoms.ScrollX),
            ScrollY: GetStyleFloatProperty(style, propertyAtoms.ScrollY));
    }

    private static ResolvedPadding ResolveLayoutPadding(HostStyle hostStyle, float defaultX, float? defaultY = null)
    {
        var resolvedDefaultY = defaultY ?? defaultX;
        var borderWidth = Math.Max(0, hostStyle.BorderWidth);
        var left = (hostStyle.PaddingLeft ?? defaultX) + borderWidth;
        var top = (hostStyle.PaddingTop ?? resolvedDefaultY) + borderWidth;
        var right = (hostStyle.PaddingRight ?? (hostStyle.PaddingLeft ?? defaultX)) + borderWidth;
        var bottom = (hostStyle.PaddingBottom ?? (hostStyle.PaddingTop ?? resolvedDefaultY)) + borderWidth;
        return new ResolvedPadding(left, top, right, bottom);
    }

    private static string? NormalizeTooltip(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static SceneBoxSizing ParseSceneBoxSizing(string? value)
    {
        return string.Equals(value, "border-box", StringComparison.Ordinal)
            ? SceneBoxSizing.BorderBox
            : SceneBoxSizing.ContentBox;
    }

    private readonly record struct ResolvedPadding(float Left, float Top, float Right, float Bottom);

    private static JsValue GetProperty(JsObject obj, int atom)
    {
        return obj.TryGetPropertyByAtom(atom, out var value) ? value : JsValue.Undefined;
    }

    private static JsObject? TryGetObjectProperty(JsObject? obj, int atom)
    {
        if (obj is null || !obj.TryGetPropertyByAtom(atom, out var value) || !value.TryGetObject(out var nested))
            return null;

        return nested;
    }

    private static string? GetStringProperty(JsObject? obj, int atom)
    {
        if (obj is null || !obj.TryGetPropertyByAtom(atom, out var value) || value.IsNullOrUndefined)
            return null;

        return value.TryGetString(out var result) ? result : null;
    }

    private static float GetFloatProperty(JsObject? obj, int atom, float fallback = 0)
    {
        return GetNullableFloatProperty(obj, atom) ?? fallback;
    }

    private static float? GetNullableFloatProperty(JsObject? obj, int atom)
    {
        if (obj is null || !obj.TryGetPropertyByAtom(atom, out var value) || !value.IsNumber)
            return null;

        return (float)value.NumberValue;
    }

    private static int GetIntProperty(JsObject? obj, int atom, int fallback = 0)
    {
        if (obj is null || !obj.TryGetPropertyByAtom(atom, out var value) || !value.IsNumber)
            return fallback;

        return value.IsInt32 ? value.Int32Value : (int)value.NumberValue;
    }

    private static bool GetBoolProperty(JsObject? obj, int atom)
    {
        if (obj is null || !obj.TryGetPropertyByAtom(atom, out var value) || !value.IsBool)
            return false;

        return value.IsTrue;
    }

    private static JsValue GetStylePropertyOrUndefined(JsObject? style, int atom)
    {
        return TryGetStyleProperty(style, atom, out var value) ? value : JsValue.Undefined;
    }

    private static bool TryGetStyleProperty(JsObject? style, int atom, out JsValue value)
    {
        value = JsValue.Undefined;
        if (style is null)
            return false;

        if (style is JsArray denseStyle)
        {
            var values = denseStyle.AsReadOnlySpan();
            for (var index = values.Length - 1; index >= 0; index--)
            {
                if (values[index].TryGetObject(out var entry) && TryGetStyleProperty(entry, atom, out value))
                    return true;
            }

            return false;
        }

        return style.TryGetPropertyByAtom(atom, out value);
    }

    private static JsObject? TryGetStyleObjectProperty(JsObject? style, int atom)
    {
        return GetStylePropertyOrUndefined(style, atom).TryGetObject(out var nested) ? nested : null;
    }

    private static string? GetStyleStringProperty(JsObject? style, int atom)
    {
        var value = GetStylePropertyOrUndefined(style, atom);
        return value.IsNullOrUndefined ? null : value.TryGetString(out var result) ? result : null;
    }

    private static float? GetNullableStyleFloatProperty(JsObject? style, int atom)
    {
        var value = GetStylePropertyOrUndefined(style, atom);
        return value.IsNumber ? (float)value.NumberValue : null;
    }

    private static float GetStyleFloatProperty(JsObject? style, int atom, float fallback = 0)
    {
        return GetNullableStyleFloatProperty(style, atom) ?? fallback;
    }

    private static int GetStyleIntProperty(JsObject? style, int atom, int fallback = 0)
    {
        var value = GetStylePropertyOrUndefined(style, atom);
        if (!value.IsNumber)
            return fallback;

        return value.IsInt32 ? value.Int32Value : (int)value.NumberValue;
    }

    private static bool GetStyleBoolProperty(JsObject? style, int atom)
    {
        var value = GetStylePropertyOrUndefined(style, atom);
        return value.IsTrue;
    }

    private SceneGradient? ReadGradient(JsObject? gradient)
    {
        if (gradient is null)
            return null;

        var colors = ReadStringArray(gradient, propertyAtoms!.Colors);
        if (colors is not { Length: >= 2 })
            return null;

        return new SceneGradient(
            string.Equals(GetStringProperty(gradient, propertyAtoms.Type), "radial", StringComparison.Ordinal)
                ? SceneGradientKind.Radial
                : SceneGradientKind.Linear,
            colors,
            ReadFloatArray(gradient, propertyAtoms.Stops),
            GetFloatProperty(gradient, propertyAtoms.StartX),
            GetFloatProperty(gradient, propertyAtoms.StartY),
            GetFloatProperty(gradient, propertyAtoms.EndX, 1),
            GetFloatProperty(gradient, propertyAtoms.EndY, 1),
            GetFloatProperty(gradient, propertyAtoms.CenterX, 0.5f),
            GetFloatProperty(gradient, propertyAtoms.CenterY, 0.5f),
            GetFloatProperty(gradient, propertyAtoms.Radius, 0.5f));
    }

    private static string[]? ReadStringArray(JsObject? obj, int atom)
    {
        if (obj is null || !obj.TryGetPropertyByAtom(atom, out var value) || !value.TryGetObject(out var array))
            return null;

        if (!TryGetArrayLength(array, out var length) || length < 2)
            return null;

        var values = new string[length];
        for (var index = 0; index < length; index++)
        {
            if (!array.TryGetElement((uint)index, out var item) || !item.TryGetString(out var element))
                return null;

            values[index] = element;
        }

        return values;
    }

    private static float[]? ReadFloatArray(JsObject? obj, int atom)
    {
        if (obj is null || !obj.TryGetPropertyByAtom(atom, out var value) || !value.TryGetObject(out var array))
            return null;

        return ReadFloatArray(array);
    }

    private static float[]? ReadFloatArray(JsObject array)
    {
        if (!TryGetArrayLength(array, out var length) || length <= 0)
            return null;

        var values = new float[length];
        for (var index = 0; index < length; index++)
        {
            if (!array.TryGetElement((uint)index, out var item) || !item.IsNumber)
                return null;

            values[index] = (float)item.NumberValue;
        }

        return values;
    }

    private static bool TryGetArrayLength(JsObject array, out int length)
    {
        length = 0;
        if (!array.TryGetPropertyByAtom(AtomTable.IdLength, out var lengthValue) || !lengthValue.IsNumber)
            return false;

        length = lengthValue.IsInt32 ? lengthValue.Int32Value : (int)lengthValue.NumberValue;
        return length >= 0;
    }

    private SceneRuntimeShader? ReadRuntimeShader(JsObject? shader)
    {
        var source = GetStringProperty(shader, propertyAtoms!.Source);
        if (string.IsNullOrWhiteSpace(source))
            return null;

        return new SceneRuntimeShader(
            GetStringProperty(shader, propertyAtoms.SourceId),
            source,
            GetBoolProperty(shader, propertyAtoms.HostTime),
            ReadUniforms(TryGetObjectProperty(shader, propertyAtoms.Uniforms)));
    }

    private SceneRuntimeShaderUniform[]? ReadUniforms(JsObject? uniforms)
    {
        if (uniforms is null)
            return null;

        var names = uniforms.GetEnumerableOwnPropertyNames();
        if (names.Count == 0)
            return null;

        var result = new List<SceneRuntimeShaderUniform>(names.Count);
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (!TryGetDynamicProperty(uniforms, name, out var value))
                continue;

            var uniform = ReadUniform(name, value);
            if (uniform is not null)
                result.Add(uniform);
        }

        return result.Count == 0 ? null : [.. result];
    }

    private static SceneRuntimeShaderUniform? ReadUniform(string name, JsValue value)
    {
        if (value.IsNullOrUndefined)
            return null;
        if (value.TryGetString(out var str))
            return new SceneRuntimeShaderUniform(name, SceneRuntimeShaderUniformKind.Color, ColorValue: str);
        if (value.IsNumber)
            return value.IsInt32
                ? new SceneRuntimeShaderUniform(name, SceneRuntimeShaderUniformKind.Int, IntValue: value.Int32Value)
                : new SceneRuntimeShaderUniform(name, SceneRuntimeShaderUniformKind.Float, FloatValue: (float)value.NumberValue);
        if (value.TryGetObject(out var array) && TryGetArrayLength(array, out _))
            return new SceneRuntimeShaderUniform(name, SceneRuntimeShaderUniformKind.FloatArray, FloatArrayValue: ReadFloatArray(array));
        return null;
    }

    private SceneBoxShadow[]? ReadShadows(JsValue shadowValue)
    {
        if (shadowValue.IsNullOrUndefined)
            return null;

        if (!shadowValue.TryGetObject(out var shadowObject))
            return null;

        var shadows = new List<SceneBoxShadow>();
        if (TryGetArrayLength(shadowObject, out var length))
        {
            for (var index = 0; index < length; index++)
            {
                if (!shadowObject.TryGetElement((uint)index, out var item) || !item.TryGetObject(out var entry))
                    continue;

                shadows.Add(ReadShadow(entry));
            }
        }
        else
        {
            shadows.Add(ReadShadow(shadowObject));
        }

        return shadows.Count == 0 ? null : [.. shadows];
    }

    private SceneBoxShadow ReadShadow(JsObject shadow)
    {
        return new SceneBoxShadow(
            GetStringProperty(shadow, propertyAtoms!.Color),
            GetFloatProperty(shadow, propertyAtoms.OffsetX),
            GetFloatProperty(shadow, propertyAtoms.OffsetY, 8),
            GetFloatProperty(shadow, propertyAtoms.Blur, 18),
            GetFloatProperty(shadow, propertyAtoms.Spread));
    }

    private bool TryGetDynamicProperty(JsObject? obj, string name, out JsValue value)
    {
        value = JsValue.Undefined;
        if (obj is null)
            return false;
        if (AtomTable.TryGetArrayIndexFromCanonicalString(name, out var index))
            return obj.TryGetElement(index, out value);

        return obj.TryGetPropertyByAtom(runtime!.MainRealm.Agent.Atoms.InternNoCheck(name), out value);
    }

    [JsGlobalFunction("nativeHostLog")]
    private void NativeHostLog(string message)
    {
        if (!debugFeaturesEnabled)
            return;
        Log(RuntimeDiagnosticArea.RuntimeLifecycle, $"js: {message}");
    }

    [JsGlobalFunction("nativeCreateHostNode")]
    private JsObject NativeCreateHostNode(string type, string runtimeId, string? publicId, JsValue propsValue)
    {
        if (runtime is null || propertyAtoms is null)
            throw new InvalidOperationException("Native runtime is not initialized.");

        return CreateHostInstanceObject(
            ResolveHostNodeKind(type),
            type,
            runtimeId,
            publicId,
            propsValue,
            text: null);
    }

    [JsGlobalFunction("nativeCreateTextNode")]
    private JsObject NativeCreateTextNode(string runtimeId, string text)
    {
        if (runtime is null || propertyAtoms is null)
            throw new InvalidOperationException("Native runtime is not initialized.");

        return CreateHostInstanceObject(
            HostNodeKind.RawText,
            "__text__",
            runtimeId,
            publicId: null,
            JsValue.Undefined,
            text);
    }

    [JsGlobalFunction("nativeMarkFullSceneFlush")]
    private void NativeMarkFullSceneFlush()
    {
        MarkFullSceneFlush();
    }

    [JsGlobalFunction("nativeResetAfterCommit")]
    private void NativeResetAfterCommit(JsValue rootChildrenValue, string backgroundColor = "#08111f")
    {
        if (runtime is null ||
            propertyAtoms is null ||
            stackLayoutCalculator is null ||
            !rootChildrenValue.TryGetObject(out var rootChildren))
        {
            MarkFullSceneFlush();
            return;
        }

        ResetAfterCommit(rootChildren, backgroundColor);
    }

    [JsGlobalFunction("nativeCommitHostUpdate")]
    private void NativeCommitHostUpdate(JsValue instanceValue, JsValue propsValue, string? publicId, bool layoutAffected)
    {
        if (runtime is null ||
            propertyAtoms is null ||
            stackLayoutCalculator is null ||
            !instanceValue.TryGetObject(out var instance))
        {
            MarkFullSceneFlush();
            return;
        }

        CommitHostUpdate(instance, propsValue, publicId, layoutAffected);
    }

    [JsGlobalFunction("nativeHasLayoutAffectingHostPropChange")]
    private bool NativeHasLayoutAffectingHostPropChange(JsValue oldPropsValue, JsValue newPropsValue)
    {
        if (runtime is null || propertyAtoms is null)
            return true;

        return HasLayoutAffectingHostPropChange(oldPropsValue, newPropsValue);
    }

    [JsGlobalFunction("nativeCommitTextUpdate")]
    private void NativeCommitTextUpdate(JsValue textInstanceValue, string oldText, string newText)
    {
        if (runtime is null ||
            propertyAtoms is null ||
            stackLayoutCalculator is null ||
            !textInstanceValue.TryGetObject(out var textInstance))
        {
            MarkFullSceneFlush();
            return;
        }

        CommitTextUpdate(textInstance, oldText, newText);
    }

    [JsGlobalFunction("nativeSetNodeHidden")]
    private void NativeSetNodeHidden(JsValue instanceValue, bool hidden)
    {
        if (runtime is null ||
            propertyAtoms is null ||
            !instanceValue.TryGetObject(out var instance))
        {
            MarkFullSceneFlush();
            return;
        }

        SetNodeHidden(instance, hidden);
    }

    [JsGlobalFunction("nativeAppendChild")]
    private bool NativeAppendChild(JsValue parentValue, JsValue childValue)
    {
        if (runtime is null ||
            propertyAtoms is null ||
            !parentValue.TryGetObject(out var parent) ||
            !childValue.TryGetObject(out var child))
        {
            return false;
        }

        return AppendChildNode(parent, child);
    }

    [JsGlobalFunction("nativeInsertChildBefore")]
    private bool NativeInsertChildBefore(JsValue parentValue, JsValue childValue, JsValue beforeChildValue)
    {
        if (runtime is null ||
            propertyAtoms is null ||
            !parentValue.TryGetObject(out var parent) ||
            !childValue.TryGetObject(out var child) ||
            !beforeChildValue.TryGetObject(out var beforeChild))
        {
            return false;
        }

        return InsertChildBeforeNode(parent, child, beforeChild);
    }

    [JsGlobalFunction("nativeRemoveChild")]
    private bool NativeRemoveChild(JsValue parentValue, JsValue childValue)
    {
        if (runtime is null ||
            propertyAtoms is null ||
            !parentValue.TryGetObject(out var parent) ||
            !childValue.TryGetObject(out var child))
        {
            return false;
        }

        return RemoveChildNode(parent, child);
    }

    [JsGlobalFunction("nativeClearChildren")]
    private void NativeClearChildren(JsValue parentValue)
    {
        if (runtime is null ||
            propertyAtoms is null ||
            !parentValue.TryGetObject(out var parent))
        {
            MarkFullSceneFlush();
            return;
        }

        ClearChildNodes(parent);
    }

    [JsGlobalFunction("nativeGetParentRuntimeId")]
    private string? NativeGetParentRuntimeId(string runtimeId)
    {
        if (runtime is null || propertyAtoms is null)
            return null;

        return GetParentRuntimeId(runtimeId);
    }

    [JsGlobalFunction("nativeResolveContainerLayout")]
    private NativeResolvedContainerLayout NativeResolveContainerLayout(
        JsValue styleValue,
        float parentWidth,
        float parentHeight,
        float parentOffsetLeft,
        float parentOffsetTop)
    {
        if (propertyAtoms is null)
        {
            return new NativeResolvedContainerLayout(
                new NativeLayoutInsets(0, 0, 0, 0),
                new NativeLayoutSize(0, 0),
                new NativeLayoutOffset(parentOffsetLeft, parentOffsetTop));
        }

        var style = styleValue.TryGetObject(out var styleObject) ? styleObject : null;
        var resolved = ResolveContainerLayout(style, Math.Max(0, parentWidth), Math.Max(0, parentHeight), parentOffsetLeft, parentOffsetTop);
        return new NativeResolvedContainerLayout(
            new NativeLayoutInsets(
                resolved.PaddingLeft,
                resolved.PaddingTop,
                resolved.PaddingRight,
                resolved.PaddingBottom),
            new NativeLayoutSize(
                resolved.ContentWidth,
                resolved.ContentHeight),
            new NativeLayoutOffset(
                resolved.ContentLeft,
                resolved.ContentTop));
    }

    private ResolvedContainerLayoutData ResolveContainerLayout(
        JsObject? style,
        float parentWidth,
        float parentHeight,
        float parentOffsetLeft,
        float parentOffsetTop)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var padding = ResolveContextPaddingInsets(style, atoms);
        var margin = ResolveMarginInsets(style, atoms);
        var frame = ResolveContextFrameMetrics(style, atoms, parentWidth, parentHeight, margin);
        return new ResolvedContainerLayoutData(
            frame.Left,
            frame.Top,
            frame.Width,
            frame.Height,
            padding.Left,
            padding.Top,
            padding.Right,
            padding.Bottom,
            parentOffsetLeft + frame.Left + padding.Left,
            parentOffsetTop + frame.Top + padding.Top,
            Math.Max(0, frame.Width - padding.Left - padding.Right),
            Math.Max(0, frame.Height - padding.Top - padding.Bottom));
    }

    private static EdgeInsets ResolveContextPaddingInsets(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        var resolvedPaddingX = GetNullableStyleFloatProperty(style, atoms.PaddingHorizontal)
            ?? GetNullableStyleFloatProperty(style, atoms.Padding)
            ?? 0;
        var resolvedPaddingY = GetNullableStyleFloatProperty(style, atoms.PaddingVertical)
            ?? GetNullableStyleFloatProperty(style, atoms.Padding)
            ?? 0;
        return new EdgeInsets(
            GetNullableStyleFloatProperty(style, atoms.PaddingLeft) ?? resolvedPaddingX,
            GetNullableStyleFloatProperty(style, atoms.PaddingTop) ?? resolvedPaddingY,
            GetNullableStyleFloatProperty(style, atoms.PaddingRight) ?? resolvedPaddingX,
            GetNullableStyleFloatProperty(style, atoms.PaddingBottom) ?? resolvedPaddingY);
    }

    private static LayoutFrameData ResolveContextFrameMetrics(
        JsObject? style,
        ReactAppPropertyAtoms atoms,
        float parentWidth,
        float parentHeight,
        EdgeInsets margin)
    {
        return ResolveFrameMetrics(
            new HostFrameProps(
                GetNullableStyleFloatProperty(style, atoms.Left) ?? LayoutValue.Unset,
                GetNullableStyleFloatProperty(style, atoms.Top) ?? LayoutValue.Unset,
                GetNullableStyleFloatProperty(style, atoms.Right) ?? LayoutValue.Unset,
                GetNullableStyleFloatProperty(style, atoms.Bottom) ?? LayoutValue.Unset,
                GetNullableStyleFloatProperty(style, atoms.Width) ?? LayoutValue.Unset,
                GetNullableStyleFloatProperty(style, atoms.Height) ?? LayoutValue.Unset,
                LayoutValue.Unset,
                LayoutValue.Unset,
                LayoutValue.Unset,
                LayoutValue.Unset,
                PositionMode.Relative,
                CrossAlignment.Auto,
                LayoutValueUnitFlags.None),
            parentWidth,
            parentHeight,
            fallbackWidth: 0,
            fallbackHeight: 0,
            margin);
    }

    private void DrainInputQueue()
    {
        while (inputState.TryDequeueCoalescedEvent(out var inputEvent))
        {
            try
            {
                switch (inputEvent.Type)
                {
                    case HostInputEventType.Move:
                        Invoke(pointerMoveFunction,
                            (double)inputEvent.X,
                            (double)inputEvent.Y,
                            JsValue.FromInt32(inputEvent.Buttons),
                            inputEvent.Synthetic);
                        break;
                    case HostInputEventType.Down:
                        Invoke(pointerDownFunction,
                            JsValue.FromInt32(inputEvent.Button),
                            JsValue.FromInt32(inputEvent.Buttons),
                            inputEvent.Synthetic);
                        break;
                    case HostInputEventType.Up:
                        Invoke(pointerUpFunction,
                            JsValue.FromInt32(inputEvent.Button),
                            JsValue.FromInt32(inputEvent.Buttons),
                            inputEvent.Synthetic);
                        break;
                    case HostInputEventType.Wheel:
                        Invoke(wheelFunction,
                            (double)inputEvent.DeltaX,
                            (double)inputEvent.DeltaY,
                            inputEvent.Synthetic);
                        break;
                    case HostInputEventType.KeyDown:
                        Invoke(keyDownFunction,
                            inputEvent.Key ?? string.Empty,
                            JsValue.FromInt32(inputEvent.Modifiers),
                            inputEvent.Repeat,
                            inputEvent.Synthetic);
                        break;
                    case HostInputEventType.KeyUp:
                        Invoke(keyUpFunction,
                            inputEvent.Key ?? string.Empty,
                            JsValue.FromInt32(inputEvent.Modifiers),
                            inputEvent.Synthetic);
                        break;
                    case HostInputEventType.TextInput:
                        Invoke(textInputFunction,
                            inputEvent.Text ?? string.Empty,
                            inputEvent.Synthetic);
                        break;
                }

                PumpRuntimeJobs();
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                Log(RuntimeDiagnosticArea.Input, $"input dispatch failed: {LastError}");
            }
        }
    }

    private void PumpHeldKeys(double elapsedMs)
    {
        foreach (var (key, state) in inputState.HeldKeys.ToArray())
        {
            if (!inputState.HeldKeys.ContainsKey(key))
                continue;

            if (elapsedMs < state.NextRepeatAtMs)
                continue;

            var nextInterval = Math.Max(HostInputState.MinimumKeyRepeatIntervalMs, state.IntervalMs * HostInputState.KeyRepeatAccelerationFactor);
            inputState.HeldKeys[key] = state with
            {
                NextRepeatAtMs = elapsedMs + nextInterval,
                IntervalMs = nextInterval
            };

            if (IsRepeatableKey(key))
            {
                InvalidateRender(SceneDamageReason.TextInput);
                KeyDown(key, state.Modifiers, repeat: true, synthetic: false);
                continue;
            }

            if (inputState.ActivePrintableRepeat is { Key: var printableKey, Text: var repeatedText, NativeInputAccepted: true } &&
                string.Equals(printableKey, key, StringComparison.Ordinal))
                TextInput(repeatedText, synthetic: true);
        }
    }

    private double GetInputElapsedMs()
    {
        return inputState.ElapsedMs;
    }

    private HostPrintableRepeatState? TryCreatePrintableRepeatState(string key, int modifiers)
    {
        return TryGetRepeatableTextInput(key, modifiers, out var text)
            ? new HostPrintableRepeatState(key, text, NativeInputAccepted: false)
            : null;
    }

    private bool TryGetRepeatableTextInput(string key, int modifiers, out string text)
    {
        text = string.Empty;
        if (focusedTextInputId is null || !textInputs.TryGetValue(focusedTextInputId, out var state))
            return false;

        if (state.ImeOpen || state.CompositionText.Length > 0)
            return false;

        if ((modifiers & (HostInputState.ControlModifier | HostInputState.AltModifier | HostInputState.MetaModifier)) != 0)
            return false;

        var shifted = (modifiers & HostInputState.ShiftModifier) != 0;
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
        {
            text = shifted ? key : key.ToLowerInvariant();
            return true;
        }

        if (TryMapRepeatableDigitKey(key, shifted, out text) ||
            TryMapRepeatablePunctuationKey(key, shifted, out text))
            return true;

        return false;
    }

    private static bool TryMapRepeatableDigitKey(string key, bool shifted, out string text)
    {
        text = key switch
        {
            "Number0" => shifted ? ")" : "0",
            "Number1" => shifted ? "!" : "1",
            "Number2" => shifted ? "@" : "2",
            "Number3" => shifted ? "#" : "3",
            "Number4" => shifted ? "$" : "4",
            "Number5" => shifted ? "%" : "5",
            "Number6" => shifted ? "^" : "6",
            "Number7" => shifted ? "&" : "7",
            "Number8" => shifted ? "*" : "8",
            "Number9" => shifted ? "(" : "9",
            "Keypad0" => "0",
            "Keypad1" => "1",
            "Keypad2" => "2",
            "Keypad3" => "3",
            "Keypad4" => "4",
            "Keypad5" => "5",
            "Keypad6" => "6",
            "Keypad7" => "7",
            "Keypad8" => "8",
            "Keypad9" => "9",
            _ => string.Empty
        };

        return text.Length > 0;
    }

    private static bool TryMapRepeatablePunctuationKey(string key, bool shifted, out string text)
    {
        text = key switch
        {
            "Space" => " ",
            "Minus" => shifted ? "_" : "-",
            "Equal" => shifted ? "+" : "=",
            "LeftBracket" => shifted ? "{" : "[",
            "RightBracket" => shifted ? "}" : "]",
            "BackSlash" => shifted ? "|" : "\\",
            "Backslash" => shifted ? "|" : "\\",
            "Semicolon" => shifted ? ":" : ";",
            "Apostrophe" => shifted ? "\"" : "'",
            "Comma" => shifted ? "<" : ",",
            "Period" => shifted ? ">" : ".",
            "Slash" => shifted ? "?" : "/",
            "GraveAccent" => shifted ? "~" : "`",
            "Grave" => shifted ? "~" : "`",
            "KeypadDecimal" => ".",
            "Decimal" => ".",
            "KeypadAdd" => "+",
            "KeypadSubtract" => "-",
            "KeypadMultiply" => "*",
            "KeypadDivide" => "/",
            _ => string.Empty
        };

        return text.Length > 0;
    }

    private void PruneInactiveTextInputs()
    {
        foreach (var (id, state) in textInputs)
        {
            if (state.Generation == textInputGeneration)
                continue;

            if (focusedTextInputId == id)
                focusedTextInputId = null;
            textInputs.Remove(id);
        }
    }

    private void PruneInactiveScrollViews()
    {
        // Keep inactive scroll states so views that remount with the same id
        // can restore their prior ScrollY instead of resetting on page switches.
    }

    private void PruneInactiveImages()
    {
        foreach (var (id, state) in images.ToArray())
        {
            if (state.Generation == imageGeneration)
                continue;

            images.Remove(id);
        }
    }

    private void PruneInactiveHoverTargets()
    {
        foreach (var (id, state) in hoverTargets.ToArray())
        {
            if (state.Generation == hoverTargetGeneration)
                continue;

            if (string.Equals(HoveredId, id, StringComparison.Ordinal))
                ClearHoverState();
            hoverTargets.Remove(id);
        }
    }

    private NativeTextInputState GetOrCreateTextInputState(string id)
    {
        if (textInputs.TryGetValue(id, out var existing))
            return existing;

        var created = new NativeTextInputState(id)
        {
            ZOrder = ++nextTextInputZOrder
        };
        textInputs[id] = created;
        return created;
    }

    private NativeScrollViewState GetOrCreateScrollViewState(string id)
    {
        if (scrollViews.TryGetValue(id, out var existing))
            return existing;

        var created = new NativeScrollViewState(id)
        {
            ZOrder = ++nextScrollViewZOrder
        };
        scrollViews[id] = created;
        return created;
    }

    private NativeImageState GetOrCreateImageState(string id)
    {
        if (images.TryGetValue(id, out var existing))
            return existing;

        var created = new NativeImageState(id);
        images[id] = created;
        return created;
    }

    private NativeHoverTargetState GetOrCreateHoverTarget(string id)
    {
        if (hoverTargets.TryGetValue(id, out var existing))
            return existing;

        var created = new NativeHoverTargetState(id)
        {
            ZOrder = ++nextHoverTargetZOrder
        };
        hoverTargets[id] = created;
        return created;
    }

    private void UpdateHoverTarget(string id, HostStyle style)
    {
        if (!style.Hoverable && style.Tooltip is null)
        {
            if (hoverTargets.Remove(id) && string.Equals(HoveredId, id, StringComparison.Ordinal))
                ClearHoverState();
            return;
        }

        var target = GetOrCreateHoverTarget(id);
        target.Generation = hoverTargetGeneration;
        target.Tooltip = style.Tooltip;
    }

    private NativeTextInputState? FocusTextInputAt(float x, float y)
    {
        NativeTextInputState? target = null;
        var targetDepth = -1;
        foreach (var state in textInputs.Values)
        {
            if (!TryGetNodeVisibleScreenBounds(state.Id, out var bounds) ||
                x < bounds.Left ||
                y < bounds.Top ||
                x > bounds.Right ||
                y > bounds.Bottom)
                continue;

            if (target is null || bounds.Depth > targetDepth || (bounds.Depth == targetDepth && state.ZOrder > target.ZOrder))
            {
                target = state;
                targetDepth = bounds.Depth;
            }
        }

        SetFocusedTextInput(target?.Id);
        return target;
    }

    private NativeScrollViewState? FindScrollViewAt(float x, float y, float deltaX, float deltaY)
    {
        NativeScrollViewState? bestMatch = null;
        NativeScrollViewState? bestScrollableMatch = null;
        SceneScreenBounds bestMatchBounds = default;
        SceneScreenBounds bestScrollableBounds = default;
        foreach (var state in scrollViews.Values)
        {
            if (!TryGetNodeVisibleScreenBounds(state.Id, out var bounds) ||
                x < bounds.Left ||
                y < bounds.Top ||
                x > bounds.Right ||
                y > bounds.Bottom)
                continue;

            if (bestMatch is null || SceneScreenBounds.IsHigherPriority(bounds, state.ZOrder, bestMatchBounds, bestMatch.ZOrder))
            {
                bestMatch = state;
                bestMatchBounds = bounds;
            }

            if (!CanScrollBy(state, deltaX, deltaY))
                continue;

            if (bestScrollableMatch is null || SceneScreenBounds.IsHigherPriority(bounds, state.ZOrder, bestScrollableBounds, bestScrollableMatch.ZOrder))
            {
                bestScrollableMatch = state;
                bestScrollableBounds = bounds;
            }
        }

        return bestScrollableMatch ?? bestMatch;
    }

    private bool TryBeginScrollBarDrag(float x, float y)
    {
        var commit = sceneStore.Snapshot();
        NativeScrollViewState? target = null;
        SceneScreenBounds targetBounds = default;
        var targetAxis = SceneScrollBarDragAxis.None;
        float targetGrabOffset = 0;
        foreach (var state in scrollViews.Values)
        {
            if (!TryGetScrollViewScreenBox(commit, state, out var screenBox, out var bounds) ||
                !TryGetNodeVisibleScreenBounds(commit, state.Id, out var visibleBounds) ||
                x < visibleBounds.Left ||
                y < visibleBounds.Top ||
                x > visibleBounds.Right ||
                y > visibleBounds.Bottom)
            {
                continue;
            }

            if (!SceneScrollBarDragController.TryHitThumb(screenBox, x, y, out var hitAxis, out var grabOffset))
                continue;

            if (target is null || SceneScreenBounds.IsHigherPriority(bounds, state.ZOrder, targetBounds, target.ZOrder))
            {
                target = state;
                targetBounds = bounds;
                targetAxis = hitAxis;
                targetGrabOffset = grabOffset;
            }
        }

        if (target is null)
            return false;

        activeScrollBarDrag.Begin(target.Id, targetAxis, targetGrabOffset);
        return true;
    }

    private bool UpdateActiveScrollBarDrag(float pointerX, float pointerY)
    {
        if (activeScrollBarDrag.ScrollViewId is not { } scrollViewId ||
            !scrollViews.TryGetValue(scrollViewId, out var state))
        {
            return false;
        }

        var commit = sceneStore.Snapshot();
        if (!TryGetScrollViewScreenBox(commit, state, out var screenBox, out _))
        {
            ClearActiveScrollBarDrag();
            return false;
        }

        if (!SceneScrollBarDragController.TryUpdate(activeScrollBarDrag, screenBox, state, pointerX, pointerY))
        {
            ClearActiveScrollBarDrag();
            return false;
        }

        UpdateScrollViewLayout(state);
        return true;
    }

    private bool EndActiveScrollBarDrag()
    {
        return activeScrollBarDrag.Clear();
    }

    private void ClearActiveScrollBarDrag()
    {
        activeScrollBarDrag.Clear();
    }

    private void SetFocusedTextInput(string? id)
    {
        if (focusedTextInputId == id)
            return;

        if (focusedTextInputId is { } previousId && textInputs.TryGetValue(previousId, out var previous))
        {
            previous.IsFocused = false;
            previous.IsSelectingWithMouse = false;
            previous.IsTextCompositionActive = false;
            previous.PendingCompositionCommit = false;
            previous.CompositionReplacedSelection = false;
            previous.PendingHostText = null;
            previous.CompositionText = string.Empty;
            previous.CompositionCursorOffset = 0;
            previous.ImeOpen = false;
            previous.ImeIndicator = string.Empty;
            ClearSelection(previous);
            UpdateTextInputLayout(previous);
            NotifyTextInputEvent(previous, "blur");
        }

        focusedTextInputId = id;

        if (id is { } nextId && textInputs.TryGetValue(nextId, out var next))
        {
            next.IsFocused = true;
            next.CaretIndex = Math.Min(next.CaretIndex, next.Text.Length);
            next.PreferredCaretX = null;
            next.IsTextCompositionActive = false;
            next.PendingCompositionCommit = false;
            next.CompositionReplacedSelection = false;
            next.PendingHostText = null;
            next.CompositionText = string.Empty;
            next.CompositionStartIndex = next.CaretIndex;
            next.CompositionCursorOffset = 0;
            next.ImeOpen = false;
            next.ImeIndicator = string.Empty;
            ClearSelection(next);
            EnsureTextInputVisible(next);
            UpdateTextInputLayout(next);
            NotifyTextInputEvent(next, "focus");
        }
    }

    private void RefreshHoverState()
    {
        UpdateHoverState(sceneStore.Snapshot(), MouseX, MouseY);
    }

    private void UpdateHoverState(SceneLayoutCommit commit, float x, float y)
    {
        var target = TryResolveHoverTarget(commit, x, y, out var targetBounds);

        if (target is null)
        {
            ClearHoverState();
            return;
        }

        SetHoverState(target, targetBounds);
    }

    private bool TryHitHoverTarget(SceneLayoutCommit commit, float x, float y)
    {
        return TryResolveHoverTarget(commit, x, y, out _) is not null;
    }

    private NativeHoverTargetState? TryResolveHoverTarget(SceneLayoutCommit commit, float x, float y, out SceneScreenBounds targetBounds)
    {
        NativeHoverTargetState? target = null;
        targetBounds = default;
        foreach (var state in hoverTargets.Values)
        {
            if (!TryGetNodeVisibleScreenBounds(commit, state.Id, out var bounds) ||
                x < bounds.Left ||
                y < bounds.Top ||
                x > bounds.Right ||
                y > bounds.Bottom)
                continue;

            if (target is null || SceneScreenBounds.IsHigherPriority(bounds, state.ZOrder, targetBounds, target.ZOrder))
            {
                target = state;
                targetBounds = bounds;
            }
        }

        return target;
    }

    private void SetHoverState(NativeHoverTargetState target, SceneScreenBounds bounds)
    {
        HoveredId = target.Id;
        HoverTargetLeft = bounds.Left;
        HoverTargetTop = bounds.Top;
        HoverTargetWidth = Math.Max(0, bounds.Right - bounds.Left);
        HoverTargetHeight = Math.Max(0, bounds.Bottom - bounds.Top);
    }

    private void ClearHoverState()
    {
        HoveredId = string.Empty;
        HoverTargetLeft = 0;
        HoverTargetTop = 0;
        HoverTargetWidth = 0;
        HoverTargetHeight = 0;
    }

    private (float Left, float Top, float Width, float Height) MeasureTooltip(string text, SceneScreenBounds bounds)
    {
        const float fontSize = 13;
        const float paddingX = 10;
        const float paddingY = 7;
        const float edgeMargin = 8;
        const float pointerGap = 10;

        var textWidth = backendServices.Text.MeasureTextWidth(text, new SceneTextStyle(fontSize, Font: new SceneFont(fontSize, Weight: 500)));
        var width = MathF.Min(MathF.Max(32f, textWidth + paddingX * 2), MathF.Max(32f, Width - edgeMargin * 2));
        var height = fontSize + paddingY * 2;
        var left = Math.Clamp(bounds.Left + (bounds.Right - bounds.Left - width) * 0.5f, edgeMargin, MathF.Max(edgeMargin, Width - width - edgeMargin));
        var top = bounds.Top - height - pointerGap;
        if (top < edgeMargin)
            top = MathF.Min(MathF.Max(edgeMargin, bounds.Bottom + pointerGap), MathF.Max(edgeMargin, Height - height - edgeMargin));
        return (left, top, width, height);
    }

    private void ApplyFocusedTextInputText(string text)
    {
        if (focusedTextInputId is null || !textInputs.TryGetValue(focusedTextInputId, out var state))
            return;

        textInputController.ApplyTextInput(state, text);
    }

    private void HandleFocusedTextInputKey(string key, int modifiers)
    {
        if (focusedTextInputId is null || !textInputs.TryGetValue(focusedTextInputId, out var state))
            return;

        textInputController.HandleKey(state, key, modifiers);
    }

    private void UpdateTextInputLayout(NativeTextInputState state)
    {
        InvalidateRender(state.CompositionText.Length > 0 || state.ImeOpen
            ? SceneDamageReason.Composition
            : SceneDamageReason.TextInput);
        var borderColor = state.IsFocused
            ? state.ActiveBorderColor ?? state.BorderColor ?? "#60a5fa"
            : state.BorderColor ?? "#334155";
        sceneStore.UpsertNode(
            state.Id,
            SceneNodeKind.TextInput,
            state.ParentId,
            state.Id,
            new SceneLayoutBox(
                SceneNodeKind.TextInput,
                state.Left,
                state.Top,
                state.Width,
                state.Height,
                state.BackgroundColor ?? "#0b1220",
                borderColor,
                1.5f,
                state.BorderRadius,
                state.BoxSizing,
                TextContent: state.Text,
                TextStyle: CreateTextInputTextStyle(state),
                PlaceholderText: state.PlaceholderText,
                PlaceholderColor: state.PlaceholderColor,
                PaddingLeft: state.PaddingLeft,
                PaddingTop: state.PaddingTop,
                PaddingRight: state.PaddingRight,
                PaddingBottom: state.PaddingBottom,
                Multiline: state.Multiline,
                LineHeight: state.LineHeight,
                CaretIndex: state.CaretIndex,
                SelectionStart: state.SelectionStart,
                SelectionEnd: state.SelectionEnd,
                IsFocused: state.IsFocused,
                BackgroundGradient: state.BackgroundGradient,
                BackgroundShader: state.BackgroundShader,
                BackgroundShadows: state.BackgroundShadows,
                CompositionText: state.CompositionText,
                CompositionStart: state.CompositionStartIndex,
                CompositionCursorOffset: state.CompositionCursorOffset,
                CompositionSelectionStart: state.CompositionSelectionStart,
                CompositionSelectionLength: state.CompositionSelectionLength,
                CompositionUnderlineColor: state.CompositionUnderlineColor,
                CompositionSelectionUnderlineColor: state.CompositionSelectionUnderlineColor,
                ImeOpen: state.IsFocused && state.ImeOpen,
                ImeIndicator: state.IsFocused ? state.ImeIndicator : null));
    }

    private void UpdateScrollViewLayout(NativeScrollViewState state, bool invalidateRender = true)
    {
        if (invalidateRender)
            InvalidateRender(SceneDamageReason.Scroll);
        sceneStore.UpsertNode(
            state.Id,
            SceneNodeKind.ScrollView,
            state.ParentId,
            state.Id,
            CreateScrollViewLayoutBox(state));
    }

    private static SceneLayoutBox CreateScrollViewLayoutBox(NativeScrollViewState state)
        => new(
            SceneNodeKind.ScrollView,
            state.Left,
            state.Top,
            state.Width,
            state.Height,
            state.BackgroundColor,
            state.BorderColor,
            state.BorderWidth,
            state.BorderRadius,
            state.BoxSizing,
            ClipContent: state.ClipContent,
            PaddingLeft: state.PaddingLeft,
            PaddingTop: state.PaddingTop,
            PaddingRight: state.PaddingRight,
            PaddingBottom: state.PaddingBottom,
            ScrollX: state.ScrollX,
            ScrollY: state.ScrollY,
            IsScrollContainer: true,
            ContentHeight: state.ContentHeight,
            ContentWidth: state.ContentWidth,
            HorizontalScrollEnabled: state.HorizontalScrollEnabled,
            BackgroundGradient: state.BackgroundGradient,
            BackgroundShader: state.BackgroundShader,
            BackgroundShadows: state.BackgroundShadows);

    private SceneLayoutCommit SyncScrollViewLayoutFromCommit(SceneLayoutCommit commit)
    {
        var layoutChanged = false;
        foreach (var state in scrollViews.Values)
        {
            if (!commit.Layout.TryGetValue(state.Id, out var box) || box.NodeKind != SceneNodeKind.ScrollView)
                continue;

            var nextContentWidth = state.HorizontalScrollEnabled
                ? Math.Max(state.Width, box.ContentWidth)
                : state.Width;
            var nextContentHeight = Math.Max(state.Height, box.ContentHeight);
            var nextScrollX = SceneScrollMetrics.ClampScrollX(state.ScrollX, state.Width, nextContentWidth, state.HorizontalScrollEnabled);
            var nextScrollY = SceneScrollMetrics.ClampScrollY(state.ScrollY, state.Height, nextContentHeight);
            var nextTargetScrollX = SceneScrollMetrics.ClampScrollX(state.TargetScrollX, state.Width, nextContentWidth, state.HorizontalScrollEnabled);
            var nextTargetScrollY = SceneScrollMetrics.ClampScrollY(state.TargetScrollY, state.Height, nextContentHeight);
            if (Math.Abs(state.ContentWidth - nextContentWidth) <= 0.001f &&
                Math.Abs(state.ContentHeight - nextContentHeight) <= 0.001f &&
                Math.Abs(state.ScrollX - nextScrollX) <= 0.001f &&
                Math.Abs(state.ScrollY - nextScrollY) <= 0.001f &&
                Math.Abs(state.TargetScrollX - nextTargetScrollX) <= 0.001f &&
                Math.Abs(state.TargetScrollY - nextTargetScrollY) <= 0.001f)
            {
                continue;
            }

            state.ContentWidth = nextContentWidth;
            state.ContentHeight = nextContentHeight;
            state.ScrollX = nextScrollX;
            state.ScrollY = nextScrollY;
            state.TargetScrollX = nextTargetScrollX;
            state.TargetScrollY = nextTargetScrollY;
            UpdateScrollViewLayout(state, invalidateRender: false);
            layoutChanged = true;
        }

        return layoutChanged ? sceneStore.Snapshot() : commit;
    }

    private void MoveSelectionCaret(NativeTextInputState state, int caretIndex, bool extendSelection)
    {
        textInputController.MoveSelectionCaret(state, caretIndex, extendSelection);
    }

    private void ClearSelection(NativeTextInputState state)
    {
        textInputController.ClearSelection(state);
    }

    private void SetSelection(NativeTextInputState state, int anchorIndex, int caretIndex)
    {
        textInputController.SetSelection(state, anchorIndex, caretIndex);
    }

    private void SelectWordAt(NativeTextInputState state, int caretIndex)
    {
        textInputController.SelectWordAt(state, caretIndex);
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

    private static SceneTextStyle CreateSceneTextStyle(HostStyle style)
        => new(
            style.FontSize,
            style.Color,
            TextAlign: ParseTextAlign(style.TextAlign),
            WrapText: style.WrapText,
            Font: new SceneFont(style.FontSize, style.FontFamily, style.FontWeight));

    private string ResolveAssetPath(string source)
    {
        var request = new RuntimeAssetRequest(
            source,
            entrySource.AssetBasePath,
            IsExplicitRelativeAssetPath(source) ? entrySource.DisplayPath : null);
        var resolved = assetService.Resolve(request);
        var materializedPath = assetService.Materialize(resolved);
        if (resolved.IsResolved)
            Log(RuntimeDiagnosticArea.Assets, $"resolved asset '{source}' against '{entrySource.AssetBasePath}' -> '{materializedPath}' ({resolved.Kind})");
        else
            Log(RuntimeDiagnosticArea.Assets, $"asset '{source}' was unresolved against '{entrySource.AssetBasePath}', using '{materializedPath}'");
        return materializedPath;
    }

    private static bool IsExplicitRelativeAssetPath(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return source.StartsWith(".\\", StringComparison.Ordinal) ||
               source.StartsWith("./", StringComparison.Ordinal) ||
               source.StartsWith("..\\", StringComparison.Ordinal) ||
               source.StartsWith("../", StringComparison.Ordinal);
    }

    private int HitTestCaretIndex(NativeTextInputState state, float pointerX, float pointerY)
    {
        var screenLeft = state.Left;
        var screenTop = state.Top;
        if (TryGetNodeScreenBounds(state.Id, out var bounds))
        {
            screenLeft = bounds.Left;
            screenTop = bounds.Top;
        }

        var localX = Math.Max(0, pointerX - (screenLeft + state.PaddingLeft));
        var localY = Math.Max(0, pointerY - (screenTop + state.PaddingTop));
        return backendServices.Text.HitTestCaretIndex(
            CreateTextInputTextStyle(state),
            state.Text,
            state.LineHeight,
            Math.Max(0, state.Width - state.PaddingLeft - state.PaddingRight),
            localX,
            localY);
    }

    private void NotifyTextInputEvent(NativeTextInputState state, string kind)
    {
        if (textInputEventFunction is null || runtime is null)
            return;

        if (string.Equals(kind, "change", StringComparison.Ordinal))
            state.PendingHostText = state.Text;

        Invoke(
            textInputEventFunction,
            state.Id,
            kind,
            state.Text,
            JsValue.FromInt32(state.CaretIndex),
            state.IsFocused);
        runtime.MainRealm.PumpJobs();
    }

    private void UpdateTrackedImages()
    {
        foreach (var state in images.Values)
        {
            var result = backendServices.Images.ResolveImage(state.Source);
            var nextState = MapImageLoadState(result.State);

            if (state.LoadState == nextState)
            {
                if (!string.IsNullOrEmpty(state.PlaceholderSource))
                {
                    var placeholderResult = backendServices.Images.ResolveImage(state.PlaceholderSource);
                    var nextPlaceholderState = MapImageLoadState(placeholderResult.State);
                    if (state.PlaceholderLoadState != nextPlaceholderState)
                    {
                        state.PlaceholderLoadState = nextPlaceholderState;
                        if (nextPlaceholderState == NativeImageLoadState.Failed)
                            LogImageLoadFailure("placeholder", state.RequestedPlaceholderSource, state.PlaceholderSource, placeholderResult);
                        InvalidateRender(SceneDamageReason.ImageReady);
                    }
                }

                continue;
            }

            state.LoadState = nextState;
            InvalidateRender(SceneDamageReason.ImageReady);
            if (nextState == NativeImageLoadState.Loaded)
                NotifyImageEvent(state, "load", state.RequestedSource, result.LocalPath);
            else if (nextState == NativeImageLoadState.Failed)
            {
                LogImageLoadFailure("image", state.RequestedSource, state.Source, result);
                NotifyImageEvent(state, "error", state.RequestedSource, result.Error ?? "Image loading failed.");
            }
        }
    }

    private static NativeImageLoadState MapImageLoadState(RuntimeImageResolveState state)
    {
        return state switch
        {
            RuntimeImageResolveState.Pending => NativeImageLoadState.Pending,
            RuntimeImageResolveState.Ready => NativeImageLoadState.Loaded,
            RuntimeImageResolveState.Failed => NativeImageLoadState.Failed,
            _ => NativeImageLoadState.Pending
        };
    }

    private void NotifyImageEvent(NativeImageState state, string kind, string source, string? detail)
    {
        if (imageEventFunction is null || runtime is null)
            return;

        Invoke(
            imageEventFunction,
            state.Id,
            kind,
            source,
            detail ?? string.Empty);
        runtime.MainRealm.PumpJobs();
    }

    private void LogImageLoadFailure(string kind, string requestedSource, string resolvedSource, RuntimeImageResolveResult result)
    {
        var resolvedPath = string.IsNullOrWhiteSpace(result.LocalPath) ? resolvedSource : result.LocalPath;
        var detail = result.Error ?? "Image loading failed.";
        var message = $"{kind} load failed: requested='{requestedSource}', resolved='{resolvedPath}', detail='{detail}'";
        Console.Error.WriteLine($"[{DiagnosticSourceName}:image-error] {message}");
        Log(RuntimeDiagnosticArea.Assets, message);
    }

    private static bool IsRepeatableKey(string key)
    {
        return key is "Backspace" or "BackSpace" or "Delete" or "Left" or "Right" or "Up" or "Down" or "ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown" or "Home" or "End";
    }

    private bool IsDoubleClick(string id, float x, float y)
    {
        return inputState.IsDoubleClick(id, x, y, currentElapsedMs);
    }

    private void RememberPrimaryClick(string id, float x, float y)
    {
        inputState.RememberPrimaryClick(id, x, y, currentElapsedMs);
    }

    private void ApplyScrollWheel(float deltaX, float deltaY, double elapsedMs)
    {
        var state = ResolveWheelScrollView(MouseX, MouseY, deltaX, deltaY, elapsedMs);
        if (state is null)
            return;

        if (SceneSmoothScrollController.ApplyWheelTarget(state, CreateScrollViewLayoutBox(state), deltaX, deltaY))
        {
            InvalidateRender(SceneDamageReason.Scroll);
            RenderWakeRequested?.Invoke();
        }
    }

    private NativeScrollViewState? ResolveWheelScrollView(float x, float y, float deltaX, float deltaY, double elapsedMs)
    {
        var id =
            wheelScrollTargetLatch.TryUseActive(elapsedMs, out var activeId) &&
            scrollViews.ContainsKey(activeId)
                ? activeId
                : wheelScrollTargetLatch.SetActive(FindScrollViewAt(x, y, deltaX, deltaY)?.Id);
        return id is not null && scrollViews.TryGetValue(id, out var state) ? state : null;
    }

    private void ClearWheelScrollTarget()
    {
        wheelScrollTargetLatch.Clear();
    }

    private bool MoveFocus(bool forward)
    {
        if (textInputs.Count == 0)
            return false;

        var ordered = textInputs.Values
            .Select(state =>
            {
                if (!TryGetNodeScreenBounds(state.Id, out var bounds))
                    bounds = new SceneScreenBounds(state.Left, state.Top, state.Left + state.Width, state.Top + state.Height, 0);
                return (State: state, Bounds: bounds);
            })
            .OrderBy(entry => entry.Bounds.Top)
            .ThenBy(entry => entry.Bounds.Left)
            .ThenBy(entry => entry.State.ZOrder)
            .ToArray();
        if (ordered.Length == 0)
            return false;

        var currentIndex = Array.FindIndex(ordered, entry => string.Equals(entry.State.Id, focusedTextInputId, StringComparison.Ordinal));
        var nextIndex = currentIndex < 0
            ? (forward ? 0 : ordered.Length - 1)
            : (currentIndex + (forward ? 1 : -1) + ordered.Length) % ordered.Length;
        SetFocusedTextInput(ordered[nextIndex].State.Id);
        return true;
    }

    private void EnsureTextInputVisible(NativeTextInputState state)
    {
        if (!TryGetNodeScreenBounds(state.Id, out var nodeBounds))
            return;

        foreach (var scrollViewId in GetAncestorScrollViewIds(state.Id))
        {
            if (!scrollViews.TryGetValue(scrollViewId, out var scrollState) || !TryGetNodeScreenBounds(scrollViewId, out var scrollBounds))
                continue;

            var delta = 0f;
            if (nodeBounds.Top < scrollBounds.Top)
                delta = nodeBounds.Top - scrollBounds.Top;
            else if (nodeBounds.Bottom > scrollBounds.Bottom)
                delta = nodeBounds.Bottom - scrollBounds.Bottom;

            if (Math.Abs(delta) < 0.001f)
                continue;

            scrollState.ScrollY += delta;
            ClampScrollOffset(scrollState);
            SceneSmoothScrollController.ResetTarget(scrollState);
            UpdateScrollViewLayout(scrollState);
            TryGetNodeScreenBounds(state.Id, out nodeBounds);
        }
    }

    private bool CanScrollBy(NativeScrollViewState state, float deltaX, float deltaY)
    {
        return SceneScrollMetrics.CanScrollBy(
            state.TargetScrollX,
            state.TargetScrollY,
            state.Width,
            state.Height,
            state.ContentWidth,
            state.ContentHeight,
            state.HorizontalScrollEnabled,
            deltaX,
            deltaY);
    }

    private IEnumerable<string> GetAncestorScrollViewIds(string nodeId)
    {
        var commit = sceneStore.Snapshot();
        if (!commit.Nodes.TryGetValue(nodeId, out var node))
            yield break;

        var parentId = node.ParentId;
        while (parentId is not null)
        {
            if (scrollViews.ContainsKey(parentId))
                yield return parentId;

            if (!commit.Nodes.TryGetValue(parentId, out var parentNode))
                yield break;

            parentId = parentNode.ParentId;
        }
    }

    private bool TryGetNodeScreenBounds(string nodeId, out SceneScreenBounds bounds)
    {
        var commit = sceneStore.Snapshot();
        return SceneScreenGeometry.TryGetNodeScreenBounds(commit, nodeId, out bounds);
    }

    private static bool TryGetScrollViewScreenBox(SceneLayoutCommit commit, NativeScrollViewState state, out SceneLayoutBox box, out SceneScreenBounds bounds)
    {
        box = default!;
        bounds = default;
        if (!commit.Layout.TryGetValue(state.Id, out var layoutBox) ||
            layoutBox.NodeKind != SceneNodeKind.ScrollView ||
            !SceneScreenGeometry.TryGetNodeScreenBounds(commit, state.Id, out bounds))
        {
            return false;
        }

        box = layoutBox with
        {
            AbsLeft = bounds.Left,
            AbsTop = bounds.Top
        };
        return true;
    }

    public bool TryGetNodeScreenRect(string nodeId, out SceneDamageRect bounds)
    {
        if (TryGetNodeScreenBounds(nodeId, out var screenBounds))
        {
            bounds = new SceneDamageRect(
                (int)MathF.Round(screenBounds.Left),
                (int)MathF.Round(screenBounds.Top),
                (int)MathF.Round(screenBounds.Right - screenBounds.Left),
                (int)MathF.Round(screenBounds.Bottom - screenBounds.Top));
            return true;
        }

        bounds = default;
        return false;
    }

    public bool TryGetNodeVisibleScreenRect(string nodeId, out SceneDamageRect bounds)
    {
        var commit = sceneStore.Snapshot();
        return TryGetNodeVisibleScreenRect(commit, nodeId, out bounds);
    }

    private bool TryGetNodeVisibleScreenBounds(string nodeId, out SceneScreenBounds bounds)
    {
        var commit = sceneStore.Snapshot();
        return TryGetNodeVisibleScreenBounds(commit, nodeId, out bounds);
    }

    private static bool TryGetNodeVisibleScreenBounds(SceneLayoutCommit commit, string nodeId, out SceneScreenBounds bounds)
    {
        bounds = default;
        if (!SceneScreenGeometry.TryGetNodeScreenBounds(commit, nodeId, out var screenBounds))
            return false;

        var clipped = IntersectWithClippingAncestorViewports(commit, nodeId, screenBounds.Left, screenBounds.Top, screenBounds.Right, screenBounds.Bottom);
        if (clipped is null)
            return false;

        bounds = new SceneScreenBounds(clipped.Value.Left, clipped.Value.Top, clipped.Value.Right, clipped.Value.Bottom, screenBounds.Depth);
        return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    private static bool TryGetNodeVisibleScreenRect(SceneLayoutCommit commit, string nodeId, out SceneDamageRect bounds)
    {
        bounds = default;
        if (!SceneScreenGeometry.TryGetNodeScreenBounds(commit, nodeId, out var screenBounds))
            return false;

        var clipped = IntersectWithClippingAncestorViewports(commit, nodeId, screenBounds.Left, screenBounds.Top, screenBounds.Right, screenBounds.Bottom);
        if (clipped is null)
            return false;

        bounds = new SceneDamageRect(
            (int)Math.Floor(clipped.Value.Left),
            (int)Math.Floor(clipped.Value.Top),
            (int)Math.Ceiling(clipped.Value.Right - clipped.Value.Left),
            (int)Math.Ceiling(clipped.Value.Bottom - clipped.Value.Top));
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static float GetAncestorScrollOffsetY(SceneLayoutCommit commit, string nodeId)
    {
        var offsetY = 0f;
        var currentId = nodeId;
        while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
        {
            if (commit.Layout.TryGetValue(parentId, out var parentBox) &&
                parentBox.NodeKind == SceneNodeKind.ScrollView)
            {
                offsetY += parentBox.ScrollY;
            }

            currentId = parentId;
        }

        return offsetY;
    }

    private static float GetAncestorScrollOffsetX(SceneLayoutCommit commit, string nodeId)
    {
        var offsetX = 0f;
        var currentId = nodeId;
        while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
        {
            if (commit.Layout.TryGetValue(parentId, out var parentBox) &&
                parentBox.NodeKind == SceneNodeKind.ScrollView)
            {
                offsetX += parentBox.ScrollX;
            }

            currentId = parentId;
        }

        return offsetX;
    }

    private static VisibleScreenRect? IntersectWithClippingAncestorViewports(
        SceneLayoutCommit commit,
        string nodeId,
        float left,
        float top,
        float right,
        float bottom)
    {
        VisibleScreenRect? result = VisibleScreenRect.Intersect(
            new VisibleScreenRect(left, top, right, bottom),
            new VisibleScreenRect(0, 0, commit.Viewport.Width, commit.Viewport.Height));
        if (result is null)
            return null;

        var currentId = nodeId;
        while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
        {
            if (commit.Layout.TryGetValue(parentId, out var parentBox) &&
                (parentBox.NodeKind == SceneNodeKind.ScrollView || parentBox.ClipContent))
            {
                var ancestorOffsetX = GetAncestorScrollOffsetX(commit, parentId);
                var ancestorOffsetY = GetAncestorScrollOffsetY(commit, parentId);
                var clipRect = new VisibleScreenRect(
                    parentBox.AbsLeft - ancestorOffsetX,
                    parentBox.AbsTop - ancestorOffsetY,
                    parentBox.AbsLeft + parentBox.Width - ancestorOffsetX,
                    parentBox.AbsTop + parentBox.Height - ancestorOffsetY);
                result = VisibleScreenRect.Intersect(result.Value, clipRect);
                if (result is null)
                    return null;
            }

            currentId = parentId;
        }

        return result;
    }

    private readonly record struct VisibleScreenRect(float Left, float Top, float Right, float Bottom)
    {
        public static VisibleScreenRect? Intersect(VisibleScreenRect first, VisibleScreenRect second)
        {
            var left = Math.Max(first.Left, second.Left);
            var top = Math.Max(first.Top, second.Top);
            var right = Math.Min(first.Right, second.Right);
            var bottom = Math.Min(first.Bottom, second.Bottom);
            return right > left && bottom > top
                ? new VisibleScreenRect(left, top, right, bottom)
                : null;
        }
    }

    private static void ClampScrollOffset(NativeScrollViewState state)
    {
        state.ScrollX = SceneScrollMetrics.ClampScrollX(state.ScrollX, state.Width, state.ContentWidth, state.HorizontalScrollEnabled);
        state.ScrollY = SceneScrollMetrics.ClampScrollY(state.ScrollY, state.Height, state.ContentHeight);
        state.TargetScrollX = SceneScrollMetrics.ClampScrollX(state.TargetScrollX, state.Width, state.ContentWidth, state.HorizontalScrollEnabled);
        state.TargetScrollY = SceneScrollMetrics.ClampScrollY(state.TargetScrollY, state.Height, state.ContentHeight);
    }

    private double ResolveScrollAnimationDeltaSeconds(double elapsedMs)
    {
        var previous = previousScrollAnimationElapsedMs;
        previousScrollAnimationElapsedMs = elapsedMs;
        if (previous is null || elapsedMs <= previous.Value)
            return 1.0 / 60.0;

        return Math.Clamp((elapsedMs - previous.Value) / 1000.0, 1.0 / 240.0, 0.05);
    }

    private bool AdvanceScrollAnimations(double deltaSeconds)
    {
        var changed = false;
        var hasPendingAnimation = false;
        foreach (var state in scrollViews.Values)
        {
            var box = CreateScrollViewLayoutBox(state);
            var previousScrollX = state.ScrollX;
            var previousScrollY = state.ScrollY;
            var isAnimating = SceneSmoothScrollController.Advance(state, box, deltaSeconds);
            var stateChanged =
                Math.Abs(previousScrollX - state.ScrollX) > 0.001f ||
                Math.Abs(previousScrollY - state.ScrollY) > 0.001f;
            if (!stateChanged && !isAnimating)
                continue;

            if (stateChanged)
            {
                UpdateScrollViewLayout(state, invalidateRender: false);
                changed = true;
            }

            hasPendingAnimation |= isAnimating;
        }

        if (hasPendingAnimation)
            RenderWakeRequested?.Invoke();

        return changed;
    }

    private void Invoke(JsFunction? function, params ReadOnlySpan<JsValue> args)
    {
        if (function is null || runtime is null)
            return;

        _ = runtime.MainRealm.Call(function, JsValue.Undefined, args);
    }

    private void OnWatchedFileChanged(object? _, string changedPath)
    {
        RequestRuntimeReload(changedPath);
        Log(RuntimeDiagnosticArea.Reload, $"watched file changed; scheduling reload for {changedPath}");
    }

    private void ReloadRuntime()
    {
        reloadCoordinator.MarkReloadStarted();
        ResetHostStateForReload();
        Log(RuntimeDiagnosticArea.Reload, $"reloading runtime from {entrySource.DisplayPath}");
        runtime?.Dispose();
        hostTaskScheduler?.Dispose();
        hostTaskScheduler = new RenderInvalidatingHostTaskScheduler(OnHostTaskQueued, timeProvider);
        var builder = NodeRuntime.CreateBuilder()
            .UseHostTaskScheduler(hostTaskScheduler)
            .ConfigureRuntime(builder => { builder.UseGlobals(InstallHostGlobals); configureRuntime?.Invoke(builder); });
        if (configureTerminal is not null)
            builder = builder.ConfigureTerminal(configureTerminal);
        runtime = builder
            .Build();
        hostPump = new HostPump(runtime.Runtime.MainAgent);
        runtime.MainRealm.Global["process"].AsObject()["env"].AsObject()["NODE_ENV"] =
            reloadCoordinator.Options.Mode == ReactRuntimeReloadMode.FastRefresh ? "development" : "production";
        propertyAtoms = new ReactAppPropertyAtoms(runtime.MainRealm.Agent.Atoms);
        stackLayoutCalculator = new LayoutCalculator(backendServices.Text);
        sceneStore.Reset("root", new SceneViewport(Width, Height));

        try
        {
            var runtimeEntryPath = entrySource.PrepareEntryPath();
            _ = runtime.RunMainModule(runtimeEntryPath);
            ApplyLoadedModuleCallbacks();
            PumpRuntimeJobs();
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            Log(RuntimeDiagnosticArea.Reload, $"runtime reload failed: {LastError}");
        }
    }

    private void ReloadModuleGraph()
    {
        reloadCoordinator.MarkReloadStarted();
        ResetHostStateForReload();
        Log(RuntimeDiagnosticArea.Reload, $"reloading module graph from {entrySource.DisplayPath}");

        if (runtime is null)
        {
            ReloadRuntime();
            return;
        }

        propertyAtoms = new ReactAppPropertyAtoms(runtime.MainRealm.Agent.Atoms);
        stackLayoutCalculator = new LayoutCalculator(backendServices.Text);
        sceneStore.Reset("root", new SceneViewport(Width, Height));

        try
        {
            runtime.Runtime.MainAgent.Modules.Clear();
            var runtimeEntryPath = entrySource.PrepareEntryPath();
            _ = runtime.RunMainModule(runtimeEntryPath);
            ApplyLoadedModuleCallbacks();
            PumpRuntimeJobs();
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            Log(RuntimeDiagnosticArea.Reload, $"module graph reload failed: {LastError}");
        }
    }

    private void ReloadFastRefresh()
    {
        reloadCoordinator.MarkReloadStarted();
        Log(RuntimeDiagnosticArea.Reload, $"fast refresh from {entrySource.DisplayPath}");

        if (runtime is null)
        {
            ReloadRuntime();
            return;
        }

        try
        {
            var runtimeEntryPath = entrySource.PrepareEntryPath();
            LastError = null;
            var changedPaths = reloadCoordinator.ConsumePendingPaths();
            if (changedPaths.Length == 0)
                changedPaths = [runtimeEntryPath];

            var invalidatedModuleIds = new HashSet<string>(StringComparer.Ordinal);
            var removedCount = 0;
            foreach (var changedPath in changedPaths)
            {
                string resolvedChangedId;
                try
                {
                    resolvedChangedId = runtime.Runtime.MainAgent.Modules.Resolve(changedPath);
                }
                catch
                {
                    continue;
                }

                var invalidation = runtime.Runtime.MainAgent.Modules.Invalidate(
                    resolvedChangedId,
                    JsAgent.JsAgentModuleApi.ModuleInvalidationScope.Importers);
                removedCount += invalidation.RemovedCount;
                invalidatedModuleIds.UnionWith(invalidation.ResolvedIds);
            }

            if (invalidatedModuleIds.Count == 0)
            {
                var resolvedEntryId = runtime.Runtime.MainAgent.Modules.Resolve(runtimeEntryPath);
                var invalidation = runtime.Runtime.MainAgent.Modules.Invalidate(
                    resolvedEntryId,
                    JsAgent.JsAgentModuleApi.ModuleInvalidationScope.Importers);
                removedCount += invalidation.RemovedCount;
                invalidatedModuleIds.UnionWith(invalidation.ResolvedIds);
            }

            Log(RuntimeDiagnosticArea.ModuleInvalidation, $"fast refresh invalidated {removedCount} cache entries across {invalidatedModuleIds.Count} module(s)");
            _ = runtime.RunMainModule(runtimeEntryPath);
            ApplyLoadedModuleCallbacks();
            PumpRuntimeJobs();
            InvalidateRender(SceneDamageReason.RuntimeReload);
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            Log(RuntimeDiagnosticArea.Reload, $"fast refresh failed: {LastError}");
        }
    }

    private void ResetHostStateForReload()
    {
        InvalidateRender(SceneDamageReason.RuntimeReload);
        FrameCount = 0;
        LastError = null;
        loggedRenderFrames = 0;
        loggedResets = 0;
        loggedTexts = 0;
        loggedViews = 0;
        pointerMoveFunction = null;
        pointerDownFunction = null;
        pointerUpFunction = null;
        keyDownFunction = null;
        keyUpFunction = null;
        imageEventFunction = null;
        textInputEventFunction = null;
        wheelFunction = null;
        textInputFunction = null;
        renderFrameFunction = null;
        stackLayoutCalculator = null;
        propertyAtoms = null;
        inputState.ResetForReload();
        hoverTargets.Clear();
        images.Clear();
        scrollViews.Clear();
        memoizedGlobalFunctionArguments.Clear();
        focusedTextInputId = null;
        ClearActiveScrollBarDrag();
        ClearWheelScrollTarget();
        ClearHoverState();
        foreach (var state in textInputs.Values)
            state.IsFocused = false;
    }

    private void ApplyLoadedModuleCallbacks()
    {
        renderFrameFunction = TryGetGlobalFunction("__nativeRenderFrame");
        pointerMoveFunction = TryGetGlobalFunction("__nativePointerMove");
        pointerDownFunction = TryGetGlobalFunction("__nativePointerDown");
        pointerUpFunction = TryGetGlobalFunction("__nativePointerUp");
        wheelFunction = TryGetGlobalFunction("__nativeWheel");
        keyDownFunction = TryGetGlobalFunction("__nativeKeyDown");
        keyUpFunction = TryGetGlobalFunction("__nativeKeyUp");
        imageEventFunction = TryGetGlobalFunction("__nativeImageEvent");
        textInputEventFunction = TryGetGlobalFunction("__nativeTextInputEvent");
        textInputFunction = TryGetGlobalFunction("__nativeTextInput");
        Log(RuntimeDiagnosticArea.RuntimeLifecycle, $"callbacks render={(renderFrameFunction is not null)} move={(pointerMoveFunction is not null)} down={(pointerDownFunction is not null)} up={(pointerUpFunction is not null)} wheel={(wheelFunction is not null)} key={(keyDownFunction is not null)} keyup={(keyUpFunction is not null)} text={(textInputFunction is not null)} textinputevent={(textInputEventFunction is not null)} imageevent={(imageEventFunction is not null)}");
    }

    private JsFunction? TryGetGlobalFunction(string name)
    {
        if (runtime is null)
            return null;

        return runtime.MainRealm.Global.TryGetValue(name, out var value) &&
               value.TryGetObject(out var obj) &&
               obj is JsFunction function
            ? function
            : null;
    }

    private void InstallHostGlobals(JsGlobalInstaller installer)
    {
        InstallGeneratedGlobals(installer);
        configureAdditionalGlobals?.Invoke(installer);
    }

    private void InvalidateRender(SceneDamageReason reason)
    {
        lock (renderInvalidationGate)
        {
            renderInvalidated = true;
            pendingDamageReasons |= reason;
            renderInvalidationVersion++;
        }
    }

    private bool IsRenderInvalidated()
    {
        lock (renderInvalidationGate)
        {
            return renderInvalidated;
        }
    }

    private void ClearRenderInvalidation(long consumedInvalidationVersion)
    {
        lock (renderInvalidationGate)
        {
            if (renderInvalidationVersion == consumedInvalidationVersion)
                renderInvalidated = false;
        }
    }

    private SceneDamageReason ConsumeFrameDamageReasons(out long consumedInvalidationVersion)
    {
        lock (renderInvalidationGate)
        {
            consumedInvalidationVersion = renderInvalidationVersion;
            return SceneDamageReasonState.Consume(ref pendingDamageReasons, animationEnabled, shaderAnimationEnabled);
        }
    }

    private void OnHostTaskQueued()
    {
        InvalidateRender(SceneDamageReason.FullFrameFallback);
        RenderWakeRequested?.Invoke();
    }

    private void PumpRuntimeJobs()
    {
        if (hostTaskScheduler is null || hostPump is null)
        {
            runtime?.MainRealm.PumpJobs();
            return;
        }

        for (var turn = 0; turn < 256; turn++)
        {
            if (!HostTurnRunner.RunTurn(hostTaskScheduler, hostPump, SEventLoopQueueOrder))
                return;
        }
    }

    private void Log(RuntimeDiagnosticArea area, string message)
    {
        if (!diagnostics.IsEnabled(area))
            return;

        diagnostics.Write(new RuntimeDiagnosticEvent(area, DiagnosticSourceName, message));
    }

    private void EnsureDebugInputEnabled()
    {
        if (!debugFeaturesEnabled)
            throw new InvalidOperationException("Synthetic mouse input is available only in --react-debug mode.");
    }

    private static SceneTextAlign ParseTextAlign(string? value)
    {
        if (string.Equals(value, "center", StringComparison.OrdinalIgnoreCase))
            return SceneTextAlign.Center;
        if (string.Equals(value, "right", StringComparison.OrdinalIgnoreCase))
            return SceneTextAlign.Right;
        return SceneTextAlign.Left;
    }

    private SceneDamageRect[] BuildHostAnimatedShaderDirtyRects(SceneLayoutCommit commit)
    {
        var dirtyRects = new List<SceneDamageRect>();
        foreach (var id in commit.HostAnimatedShaderRootIds)
        {
            if (SceneDamageEstimator.GetBoxDamageRect(commit, id, Width, Height) is { } rect)
                dirtyRects.Add(rect);
        }

        return dirtyRects.Count == 0
            ? []
            : [.. dirtyRects];
    }

    private sealed record HostStyle(
        string? BackgroundColor = null,
        SceneGradient? BackgroundGradient = null,
        SceneRuntimeShader? BackgroundShader = null,
        SceneBoxShadow[]? BackgroundShadows = null,
        string? BorderColor = null,
        float BorderWidth = 0,
        float BorderRadius = 0,
        SceneBoxSizing BoxSizing = SceneBoxSizing.ContentBox,
        bool ClipContent = false,
        float FontSize = 16,
        string? Color = null,
        string? FontFamily = null,
        int FontWeight = 400,
        string? TextAlign = null,
        bool WrapText = false,
        float? PaddingLeft = null,
        float? PaddingTop = null,
        float? PaddingRight = null,
        float? PaddingBottom = null,
        bool Multiline = false,
        float LineHeight = 0,
        string? ActiveBorderColor = null,
        string? PlaceholderColor = null,
        string? CompositionUnderlineColor = null,
        string? CompositionSelectionUnderlineColor = null,
        string? ImageFit = null,
        bool Hoverable = false,
        string? Tooltip = null,
        float? ContentWidth = null,
        float? ContentHeight = null,
        float ScrollX = 0,
        float ScrollY = 0)
    {
        public static HostStyle Default { get; } = new();
    }

    private readonly record struct ResolvedContainerLayoutData(
        float Left,
        float Top,
        float Width,
        float Height,
        float PaddingLeft,
        float PaddingTop,
        float PaddingRight,
        float PaddingBottom,
        float ContentLeft,
        float ContentTop,
        float ContentWidth,
        float ContentHeight);

}

[GenerateJsObject]
public sealed partial class NativeLayoutOffset
{
    public NativeLayoutOffset(float left, float top)
    {
        Left = left;
        Top = top;
    }

    [JsMember]
    public float Left { get; }

    [JsMember]
    public float Top { get; }
}

[GenerateJsObject]
public sealed partial class NativeLayoutSize
{
    public NativeLayoutSize(float width, float height)
    {
        Width = width;
        Height = height;
    }

    [JsMember]
    public float Width { get; }

    [JsMember]
    public float Height { get; }
}

[GenerateJsObject]
public sealed partial class NativeLayoutInsets
{
    public NativeLayoutInsets(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    [JsMember]
    public float Left { get; }

    [JsMember]
    public float Top { get; }

    [JsMember]
    public float Right { get; }

    [JsMember]
    public float Bottom { get; }
}

[GenerateJsObject]
public sealed partial class NativeResolvedContainerLayout
{
    public NativeResolvedContainerLayout(
        NativeLayoutInsets padding,
        NativeLayoutSize contentFrame,
        NativeLayoutOffset contentOffset)
    {
        Padding = padding;
        ContentFrame = contentFrame;
        ContentOffset = contentOffset;
    }

    [JsMember]
    public NativeLayoutInsets Padding { get; }

    [JsMember]
    public NativeLayoutSize ContentFrame { get; }

    [JsMember]
    public NativeLayoutOffset ContentOffset { get; }
}

