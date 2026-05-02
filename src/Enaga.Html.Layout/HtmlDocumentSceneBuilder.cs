using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;
using Enaga.Html.Dom;

namespace Enaga.Html;

internal sealed class HtmlDocumentSceneBuilder
{
    private readonly string rootId;
    private readonly HtmlSceneTreeBuilder sceneTreeBuilder;
    private readonly HtmlStyleTraversal styleTraversal;
    private readonly HtmlLayoutBuilder layoutBuilder;
    private readonly HtmlLayoutOutputStore layoutOutputStore = new();
    private readonly HtmlPipelineMetrics metrics = new();

    public HtmlDocumentSceneBuilder(HtmlOptions options)
    {
        rootId = options.RootId;
        var layoutConfig = options.LayoutConfig ?? LayoutEngineConfig.WebDefaults;
        var textServices = (options.BackendServices ?? DummyRuntimeBackendServices.Create()).Text;
        sceneTreeBuilder = new HtmlSceneTreeBuilder(options, layoutConfig, metrics);
        styleTraversal = new HtmlStyleTraversal(options, layoutConfig);
        layoutBuilder = new HtmlLayoutBuilder(options.RootId, textServices, metrics);
    }

    public HtmlPipelineMetricsSnapshot LastMetrics { get; private set; }

    public Enaga.Html.Style.RestyleHint LastInvalidationHints { get; private set; }

    public Enaga.Html.Style.RenderDamage LastDamage { get; private set; }

    public HtmlFragmentTree? LastFragmentTree { get; private set; }

    public HtmlComputedStyleTree? LastComputedStyleTree { get; private set; }

    public IReadOnlyDictionary<string, HtmlNodeId> LastSceneNodeDomIds { get; private set; } =
        new Dictionary<string, HtmlNodeId>(StringComparer.Ordinal);

    public void ApplyHoverSnapshot(IReadOnlySet<HtmlNodeId>? oldHoveredNodeIds, IReadOnlySet<HtmlNodeId>? newHoveredNodeIds)
        => sceneTreeBuilder.ApplyHoverSnapshot(oldHoveredNodeIds, newHoveredNodeIds);

    public void ApplyActiveSnapshot(HtmlNodeId? oldActiveNodeId, HtmlNodeId? newActiveNodeId)
        => sceneTreeBuilder.ApplyActiveSnapshot(oldActiveNodeId, newActiveNodeId);

    public SceneLayoutCommit Build(
        HtmlParsedDocument document,
        int width,
        int height,
        float viewportScale,
        IReadOnlySet<HtmlNodeId>? hoveredDomNodeIds = null,
        HtmlNodeId? activeDomNodeId = null)
    {
        metrics.Reset();
        LastComputedStyleTree = styleTraversal.Resolve(
            document,
            width,
            height,
            hoveredDomNodeIds is null ? null : element => hoveredDomNodeIds.Contains(element.NodeId),
            activeDomNodeId is null ? null : element => element.NodeId == activeDomNodeId.Value);
        var styleTree = LastComputedStyleTree;
        metrics.AddStyleMatchCascade(styleTree.Styles.Count);
        var styledTree = sceneTreeBuilder.GetOrCreate(document, width, height, styleTree);
        LastInvalidationHints = sceneTreeBuilder.LastInvalidationHints;
        LastDamage = sceneTreeBuilder.LastDamage;
        layoutOutputStore.BeginDocument(document, styledTree.StyleStoreGeneration);
        layoutOutputStore.UpdateLayoutTree(rootId, styledTree.RootChildren);
        layoutOutputStore.InvalidateNodes(sceneTreeBuilder.LastLayoutDirtyNodes);
        var commit = layoutBuilder.Build(styledTree, layoutOutputStore, width, height, viewportScale);
        LastFragmentTree = layoutBuilder.LastFragmentTree;
        LastSceneNodeDomIds = layoutBuilder.LastSceneNodeDomIds;
        LastMetrics = metrics.Snapshot();
        return commit;
    }
}
