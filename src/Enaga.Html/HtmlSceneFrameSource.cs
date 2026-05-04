using Enaga.Rendering;
using Enaga.Scene;
using Enaga.Html.Dom;
using Enaga.Input;

namespace Enaga.Html;

public sealed partial class HtmlSceneFrameSource : ISceneFrameSource, IRenderWakeSource, IRenderViewportScaleController, IRuntimeBackendServicesSource, IDisposable
{
    private readonly object sync = new();
    private readonly SceneNodeIdAllocator sceneNodeIdAllocator = new();
    private readonly HtmlDocumentSceneBuilder builder;
    private readonly HtmlDocumentParser documentParser = new();
    private HtmlDocument document;
    private HtmlParsedDocument? parsedDocument;
    private SceneLayoutCommit? cachedBaseCommit;
    private SceneLayoutCommit? cachedCommit;
    private HtmlFragmentTree? cachedBaseFragmentTree;
    private HtmlFragmentTree? lastRenderedFragmentTree;
    private IReadOnlyDictionary<SceneNodeId, HtmlNodeId> cachedSceneNodeDomIds =
        new Dictionary<SceneNodeId, HtmlNodeId>();
    private readonly Dictionary<HtmlNodeId, HtmlNodeId> cachedDomNodeParentIds = new();
    private readonly Dictionary<HtmlNodeId, int> cachedDomNodeDepths = new();
    private readonly Dictionary<HtmlNodeId, HtmlDomElement> cachedDomElements = new();
    private int cachedDomRelationshipCapacity;
    private SceneLayoutCommit? cachedHitTestCommit;
    private HtmlHitTestSpatialIndex? cachedHitTestIndex;
    private readonly List<HtmlHitTestEntry> hitTestEntryScratch = new();
    private readonly Dictionary<SceneNodeId, int> hitTestPaintOrderIndexScratch = new();
    private readonly HashSet<SceneNodeId> activeInputIdScratch = new();
    private readonly HashSet<SceneNodeId> activeScrollIdScratch = new();
    private ulong hitTestGeometryVersion;
    private ulong cachedHitTestGeometryVersion = ulong.MaxValue;
    private IReadOnlySet<HtmlNodeId>? cachedHoveredDomNodeIds;
    private HtmlNodeId? cachedActiveDomNodeId;
    private bool cachedBaseCommitHasPseudoState;
    private IReadOnlySet<HtmlNodeId>? hoveredDomNodeIds;
    private HtmlNodeId? activeDomNodeId;
    private int cachedWidth = -1;
    private int cachedHeight = -1;
    private TimeSpan? previousRenderElapsed;
    private readonly HashSet<SceneNodeId> dirtyScrollViewIds = new();
    private float viewportScale = 1f;
    private bool hoverRefreshDeferred;

    public HtmlSceneFrameSource(HtmlDocument document, HtmlOptions? options = null)
    {
        this.document = document;
        Options = options ?? new HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create());
        builder = new HtmlDocumentSceneBuilder(Options, sceneNodeIdAllocator);
        overlaySceneNodeIds = new SceneNodeIdentityMap<string>("__html-overlay-root", sceneNodeIdAllocator, StringComparer.Ordinal);
        textServices = (Options.BackendServices ?? DummyRuntimeBackendServices.Create()).Text;
        textInputController = new HtmlTextInputController(textServices, RequestInteractiveUpdate, MoveFocus, SetFocusedTextInput);
    }

    public HtmlOptions Options { get; }

    private TimeProvider TimeSource => Options.TimeProvider ?? TimeProvider.System;

    private TimeProvider TimeProvider => Options.TimeProvider ?? TimeProvider.System;

    public RuntimeBackendServices BackendServices => Options.BackendServices ?? RuntimeBackendServices.Missing;

    public string? LastError { get; private set; }

    public float ViewportScale => viewportScale;

    public event Action? RenderWakeRequested;

    public event Action? BeforeRenderFrame;

    public void Dispose()
    {
        if (!ReferenceEquals(Options.BackendServices, RuntimeBackendServices.Missing))
            Options.BackendServices?.Dispose();
    }

    public void UpdateDocument(HtmlDocument nextDocument)
    {
        lock (sync)
        {
            document = nextDocument;
            parsedDocument = null;
            cachedBaseCommit = null;
            cachedCommit = null;
            cachedBaseCommitHasPseudoState = false;
            cachedDomRelationshipCapacity = 0;
            Invalidate(BaseCommitInvalidation | HtmlPipelineInvalidation.HitTest, HtmlRenderDamageBits.FullFrame | HtmlRenderDamageBits.Document);
            ResetInteractiveState();
        }

        RenderWakeRequested?.Invoke();
    }

    public void RequestRenderWake()
    {
        RenderWakeRequested?.Invoke();
    }

    public SceneLayoutCommit BuildCommit(int width, int height)
    {
        lock (sync)
        {
            return BuildCommitCore(Math.Max(1, width), Math.Max(1, height), scrollDeltaSeconds: 0);
        }
    }

    public SceneFrameResult RenderFrame(int width, int height, TimeSpan elapsed)
    {
        BeforeRenderFrame?.Invoke();
        lock (sync)
        {
            var resolvedWidth = Math.Max(1, width);
            var resolvedHeight = Math.Max(1, height);
            var previousWidth = cachedWidth;
            var previousHeight = cachedHeight;
            var previousCommit = cachedCommit;
            var consumedInvalidation = pendingInvalidation;
            var consumedDamage = pendingDamage;
            var scrollDeltaSeconds = ResolveScrollAnimationDelta(elapsed);
            frameBuildMetrics = default;
            frameStyleDamage = Enaga.Html.Style.RenderDamage.None;
            var commit = BuildCommitCore(resolvedWidth, resolvedHeight, scrollDeltaSeconds);
            var reasons = ResolveDamageReasons(consumedInvalidation, consumedDamage, frameStyleDamage, previousWidth, previousHeight, resolvedWidth, resolvedHeight);

            if (TryConsumeHoverPaintDirtyRects(out var hoverDirtyRects))
            {
                RecordDirtyMetrics(hoverDirtyRects);
                return new SceneFrameResult(commit, hoverDirtyRects, SceneDamageReason.FragmentDamage);
            }

            if (reasons == SceneDamageReason.None &&
                TryResolveSceneDiffFrameResult(previousCommit, commit, resolvedWidth, resolvedHeight, out var sceneDiffFrameResult))
            {
                lastRenderedFragmentTree = cachedBaseFragmentTree;
                return sceneDiffFrameResult;
            }

            if (reasons == SceneDamageReason.None)
            {
                RecordNoDamageMetrics();
                return SceneFrameResult.NoDamage(commit);
            }

            if (reasons == SceneDamageReason.Scroll &&
                TryConsumeScrollDirtyRects(commit, out var dirtyRects))
            {
                RecordDirtyMetrics(dirtyRects);
                return new SceneFrameResult(commit, dirtyRects, reasons);
            }

            if (TryResolveFragmentFrameResult(commit, reasons, out var fragmentFrameResult))
            {
                lastRenderedFragmentTree = cachedBaseFragmentTree;
                return fragmentFrameResult;
            }

            dirtyScrollViewIds.Clear();
            lastRenderedFragmentTree = cachedBaseFragmentTree;
            RecordFullFrameMetrics(resolvedWidth, resolvedHeight);
            return SceneFrameResult.FullFrame(commit, resolvedWidth, resolvedHeight, reasons);
        }
    }

    private bool TryResolveSceneDiffFrameResult(
        SceneLayoutCommit? previousCommit,
        SceneLayoutCommit commit,
        int width,
        int height,
        out SceneFrameResult frameResult)
    {
        frameResult = default!;
        if (previousCommit is null || ReferenceEquals(previousCommit, commit))
            return false;

        using var resultBuffer = new SceneDamageRectBufferWriter(32);
        using var scratchBuffer = new SceneDamageRectBufferWriter(32);
        var dirtyRects = SceneDamageEstimator.Resolve(
            previousCommit,
            commit,
            [],
            SceneDamageReason.None,
            width,
            height,
            forceFullFrame: false,
            resultBuffer,
            scratchBuffer);

        if (dirtyRects.Length == 0)
            return false;

        var resolvedDirtyRects = dirtyRects.ToArray();
        RecordDirtyMetrics(resolvedDirtyRects);
        frameResult = new SceneFrameResult(commit, resolvedDirtyRects, SceneDamageReason.FragmentDamage);
        return true;
    }

    private bool TryConsumeHoverPaintDirtyRects(out SceneDamageRect[] dirtyRects)
    {
        if (hoverPaintDirtyRects.Count == 0)
        {
            dirtyRects = [];
            return false;
        }

        dirtyRects = hoverPaintDirtyRects.ToArray();
        hoverPaintDirtyRects.Clear();
        return dirtyRects.Length > 0;
    }

    private bool TryConsumeScrollDirtyRects(SceneLayoutCommit commit, out SceneDamageRect[] dirtyRects)
    {
        if (dirtyScrollViewIds.Count == 0)
        {
            dirtyRects = [];
            return false;
        }

        var rects = new List<SceneDamageRect>(dirtyScrollViewIds.Count);
        foreach (var id in dirtyScrollViewIds)
        {
            if (!commit.Layout.TryGetValue(id, out var box))
                continue;

            var screenBox = SceneScreenGeometry.ResolveScreenBox(commit, commit.Layout, id, box);
            rects.Add(new SceneDamageRect(
                Math.Max(0, (int)MathF.Floor(screenBox.AbsLeft)),
                Math.Max(0, (int)MathF.Floor(screenBox.AbsTop)),
                Math.Max(1, (int)MathF.Ceiling(box.Width)),
                Math.Max(1, (int)MathF.Ceiling(box.Height))));
        }

        dirtyScrollViewIds.Clear();
        dirtyRects = [.. rects];
        return dirtyRects.Length > 0;
    }

    private bool TryResolveFragmentFrameResult(
        SceneLayoutCommit commit,
        SceneDamageReason reasons,
        out SceneFrameResult frameResult)
    {
        frameResult = default!;
        var fragmentReasons = reasons & ~SceneDamageReason.FullFrameFallback;

        if (cachedBaseFragmentTree is null ||
            reasons == SceneDamageReason.None ||
            reasons.HasFlag(SceneDamageReason.Resize) ||
            reasons.HasFlag(SceneDamageReason.Scroll) ||
            reasons.HasFlag(SceneDamageReason.RuntimeReload))
        {
            return false;
        }

        var damage = HtmlFragmentDamage.Diff(lastRenderedFragmentTree, cachedBaseFragmentTree);
        if (!damage.HasDamage)
        {
            RecordNoDamageMetrics();
            frameResult = SceneFrameResult.NoDamage(commit);
            return true;
        }

        var dirtyRects = new List<SceneDamageRect>(damage.DirtyRects.Count);
        foreach (var rect in damage.DirtyRects)
        {
            if (rect.IsEmpty)
                continue;

            dirtyRects.Add(ToScreenDamageRect(commit, rect));
        }

        if (dirtyRects.Count == 0)
        {
            RecordNoDamageMetrics();
            frameResult = SceneFrameResult.NoDamage(commit);
            return true;
        }

        var resolvedDirtyRects = dirtyRects.ToArray();
        if (fragmentReasons == SceneDamageReason.None)
            fragmentReasons = SceneDamageReason.FragmentDamage;
        RecordDirtyMetrics(resolvedDirtyRects);
        frameResult = new SceneFrameResult(commit, resolvedDirtyRects, fragmentReasons);
        return true;
    }

    private static SceneDamageRect ToScreenDamageRect(SceneLayoutCommit commit, HtmlDirtyRect rect)
    {
        var x = rect.X;
        var y = rect.Y;
        if (commit.Layout.TryGetValue(commit.RootId, out var rootBox) &&
            rootBox.NodeKind == SceneNodeKind.ScrollView)
        {
            x -= (int)MathF.Round(rootBox.ScrollX);
            y -= (int)MathF.Round(rootBox.ScrollY);
        }

        return new SceneDamageRect(x, y, rect.Width, rect.Height);
    }

    private double ResolveScrollAnimationDelta(TimeSpan elapsed)
    {
        var previous = previousRenderElapsed;
        previousRenderElapsed = elapsed;
        if (previous is null || elapsed <= previous.Value)
            return 1.0 / 60.0;

        return Math.Clamp((elapsed - previous.Value).TotalSeconds, 1.0 / 240.0, 0.05);
    }

    private SceneLayoutCommit BuildCommitCore(int width, int height, double scrollDeltaSeconds)
    {
        var rebuildInvalidation = HasAny(pendingInvalidation, BaseCommitInvalidation);
        var hoverInvalidation = HasAny(pendingInvalidation, HtmlPipelineInvalidation.Hover);
        var interactiveInvalidation = HasAny(pendingInvalidation, HtmlPipelineInvalidation.Interactive | HtmlPipelineInvalidation.Scroll);
        if (!rebuildInvalidation &&
            !hoverInvalidation &&
            SameDomNodeSet(cachedHoveredDomNodeIds, hoveredDomNodeIds) &&
            cachedActiveDomNodeId == activeDomNodeId &&
            !interactiveInvalidation &&
            cachedCommit is not null &&
            cachedWidth == width &&
            cachedHeight == height)
        {
            return cachedCommit;
        }

        try
        {
            var activeRequiresRebuild =
                cachedActiveDomNodeId != activeDomNodeId &&
                CanActiveAffectRendering(cachedActiveDomNodeId, activeDomNodeId);
            var needsRebuild =
                rebuildInvalidation ||
                cachedBaseCommit is null ||
                cachedWidth != width ||
                cachedHeight != height ||
                hoverInvalidation ||
                !SameDomNodeSet(cachedHoveredDomNodeIds, hoveredDomNodeIds) ||
                activeRequiresRebuild;

            if (needsRebuild &&
                !rebuildInvalidation &&
                cachedBaseCommit is not null &&
                cachedCommit is not null &&
                cachedWidth == width &&
                cachedHeight == height &&
                CanUseHoverOverlayFastPath() &&
                !activeRequiresRebuild)
            {
                parsedDocument ??= documentParser.Parse(document);
                if (TryApplyHoverPaintOverlay(parsedDocument, cachedCommit, cachedCommit, width, height, out var overlayCommit))
                {
                    cachedCommit = overlayCommit;
                    cachedHoveredDomNodeIds = hoveredDomNodeIds;
                    cachedActiveDomNodeId = activeDomNodeId;
                    RecordNoDamageMetrics();
                    pendingInvalidation &= ~(HtmlPipelineInvalidation.Hover | HtmlPipelineInvalidation.HitTest);
                    pendingDamage = HtmlRenderDamageBits.None;
                    LastError = null;
                    return cachedCommit;
                }
            }

            if (needsRebuild)
            {
                parsedDocument ??= documentParser.Parse(document);
                cachedBaseCommit = BuildDocumentCommit(parsedDocument, width, height);
                cachedBaseCommitHasPseudoState = CurrentPseudoStateRequiresBaseCommit();
                cachedWidth = width;
                cachedHeight = height;
                cachedHoveredDomNodeIds = hoveredDomNodeIds;
                cachedActiveDomNodeId = activeDomNodeId;
                pendingInvalidation &= ~BaseCommitInvalidation;
            }

            parsedDocument ??= documentParser.Parse(document);
            var baseCommit = cachedBaseCommit ?? BuildDocumentCommit(parsedDocument, width, height);
            cachedBaseCommit = baseCommit;

            var keepInteractiveDirty = false;
            if (needsRebuild || interactiveInvalidation || cachedCommit is null)
            {
                var previousVisibleCommit = cachedCommit ?? baseCommit;
                cachedCommit = ApplyInteractiveState(baseCommit, scrollDeltaSeconds, out keepInteractiveDirty, out var hitTestGeometryChanged);
                if (hitTestGeometryChanged)
                    hitTestGeometryVersion++;
                if (ShouldDeferHoverRefreshForScroll(hitTestGeometryChanged))
                {
                    hoverRefreshDeferred = true;
                }
                else if (hasPointerPosition &&
                         UpdateHoveredNodeId(cachedCommit, lastPointerX, lastPointerY, requestUpdate: false))
                {
                    cachedBaseCommit = BuildDocumentCommit(parsedDocument, width, height);
                    cachedBaseCommitHasPseudoState = CurrentPseudoStateRequiresBaseCommit();
                    cachedHoveredDomNodeIds = hoveredDomNodeIds;
                    cachedActiveDomNodeId = activeDomNodeId;
                    hoverRefreshDeferred = false;
                    previousVisibleCommit = cachedCommit;
                    cachedCommit = ApplyInteractiveState(cachedBaseCommit, scrollDeltaSeconds: 0, out var hoverRefreshDirty, out var hoverHitTestGeometryChanged);
                    if (hoverHitTestGeometryChanged)
                        hitTestGeometryVersion++;
                    keepInteractiveDirty |= hoverRefreshDirty;
                }

                if (TryApplyHoverPaintOverlay(parsedDocument, previousVisibleCommit, cachedCommit, width, height, out var overlayCommit))
                    cachedCommit = overlayCommit;
            }

            cachedWidth = width;
            cachedHeight = height;
            cachedActiveDomNodeId = activeDomNodeId;
            pendingInvalidation &= ~(HtmlPipelineInvalidation.Interactive | HtmlPipelineInvalidation.Scroll | HtmlPipelineInvalidation.HitTest | HtmlPipelineInvalidation.Hover);
            pendingDamage = HtmlRenderDamageBits.None;
            if (keepInteractiveDirty)
                Invalidate(HtmlPipelineInvalidation.Interactive | HtmlPipelineInvalidation.Scroll, HtmlRenderDamageBits.Scroll | HtmlRenderDamageBits.DirtyRects);
            if (keepInteractiveDirty)
                RenderWakeRequested?.Invoke();
            LastError = null;
            return cachedCommit;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            var fallbackDocument = documentParser.Parse(new HtmlDocument("<body></body>"));
            var fallbackBaseCommit = cachedBaseCommit ?? BuildDocumentCommit(fallbackDocument, width, height);
            cachedBaseCommit = fallbackBaseCommit;
            cachedBaseCommitHasPseudoState = CurrentPseudoStateRequiresBaseCommit();
            cachedCommit = ApplyInteractiveState(fallbackBaseCommit, scrollDeltaSeconds, out _, out var hitTestGeometryChanged);
            if (hitTestGeometryChanged)
                hitTestGeometryVersion++;
            cachedWidth = width;
            cachedHeight = height;
            cachedHoveredDomNodeIds = hoveredDomNodeIds;
            cachedActiveDomNodeId = activeDomNodeId;
            pendingInvalidation = HtmlPipelineInvalidation.None;
            pendingDamage = HtmlRenderDamageBits.None;
            return cachedCommit;
        }
    }

    private bool CanUseHoverOverlayFastPath()
        => !cachedBaseCommitHasPseudoState &&
           ((cachedHoveredDomNodeIds is null || cachedHoveredDomNodeIds.Count == 0) ||
            cachedCommit?.PaintOverrides.Count > 0);

    private bool CurrentPseudoStateRequiresBaseCommit()
        => activeDomNodeId is not null ||
           hoveredDomNodeIds is { Count: > 0 };

    private bool ShouldDeferHoverRefreshForScroll(bool hitTestGeometryChanged)
        => hitTestGeometryChanged && dirtyScrollViewIds.Count > 0;
}
