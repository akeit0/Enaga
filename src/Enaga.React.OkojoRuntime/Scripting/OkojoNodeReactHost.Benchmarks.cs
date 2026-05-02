using Okojo.Objects;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;
using Okojo.Node;
using Okojo.Runtime;
using Okojo;
namespace Enaga.React.OkojoRuntime;

public sealed partial class OkojoNodeReactHost
{
    internal void InitializeBenchmarkRuntime(int width = 1280, int height = 800)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        currentElapsedMs = 0;
        inputState.RestartClock();
        reloadCoordinator.MarkReloadStarted();
        LastError = null;
        FrameCount = 0;
        loggedRenderFrames = 0;
        loggedResets = 0;
        loggedTexts = 0;
        loggedViews = 0;
        requiresFullSceneFlush = true;
        currentFrameViewCallCount = 0;

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

        dirtyHostNodes.Clear();
        hostNodeLookup.Clear();
        hostInstanceShapeCache = null;
        stackLayoutCalculator = null;
        propertyAtoms = null;

        pendingLowLevelRepaintEvents.Clear();
        inputState.ResetForReload();
        hoverTargets.Clear();
        images.Clear();
        scrollViews.Clear();
        textInputs.Clear();
        focusedTextInputId = null;
        ClearActiveScrollBarDrag();
        ClearWheelScrollTarget();
        ClearHoverState();

        runtime?.Dispose();
        hostTaskScheduler?.Dispose();
        hostTaskScheduler = new RenderInvalidatingHostTaskScheduler(OnHostTaskQueued, timeProvider);
        runtime = NodeRuntime.CreateBuilder()
            .UseHostTaskScheduler(hostTaskScheduler)
            .ConfigureRuntime(builder => builder.UseGlobals(InstallHostGlobals))
            .Build();
        hostPump = new HostPump(runtime.Runtime.MainAgent);

        propertyAtoms = new ReactAppPropertyAtoms(runtime.MainRealm.Agent.Atoms);
        stackLayoutCalculator = new LayoutCalculator(backendServices.Text);
        sceneStore.Reset("root", new SceneViewport(Width, Height));
    }

    internal JsRealm BenchmarkRealm
        => runtime?.MainRealm ?? throw new InvalidOperationException("Benchmark runtime is not initialized.");

    internal ReactAppPropertyAtoms BenchmarkPropertyAtoms
        => propertyAtoms ?? throw new InvalidOperationException("Benchmark property atoms are not initialized.");

    internal JsObject BenchmarkCreateHostNode(string type, string runtimeId, JsObject? props = null, string? publicId = null)
    {
        return CreateHostInstanceObject(
            ResolveHostNodeKind(type),
            type,
            runtimeId,
            publicId,
            props is null ? JsValue.Undefined : JsValue.FromObject(props),
            text: null);
    }

    internal JsObject BenchmarkCreateTextNode(string runtimeId, string text)
    {
        return CreateHostInstanceObject(
            HostNodeKind.RawText,
            "__text__",
            runtimeId,
            publicId: null,
            JsValue.Undefined,
            text);
    }

    internal bool BenchmarkAppendChild(JsObject parent, JsObject child) => AppendChildNode(parent, child);

    internal void BenchmarkCommitHostUpdate(JsObject instance, JsObject props, bool layoutAffected)
    {
        CommitHostUpdate(instance, JsValue.FromObject(props), publicId: null, layoutAffected);
    }

    internal void BenchmarkMarkFullSceneFlush() => MarkFullSceneFlush();

    internal void BenchmarkResetAfterCommit(JsArray rootChildren, string backgroundColor = "#08111f")
    {
        ResetAfterCommit(rootChildren, backgroundColor);
    }

    internal SceneLayoutCommit BenchmarkSnapshot() => sceneStore.Snapshot();

    internal void BenchmarkPumpRuntimeJobs()
    {
        PumpRuntimeJobs();
    }
}
