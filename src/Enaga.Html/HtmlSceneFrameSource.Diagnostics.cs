using Enaga.Html.Dom;
using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

public sealed partial class HtmlSceneFrameSource
{
    private const HtmlPipelineInvalidation BaseCommitInvalidation =
        HtmlPipelineInvalidation.Document |
        HtmlPipelineInvalidation.Style |
        HtmlPipelineInvalidation.Layout |
        HtmlPipelineInvalidation.Fragments |
        HtmlPipelineInvalidation.DisplayList |
        HtmlPipelineInvalidation.Viewport;

    private HtmlPipelineInvalidation pendingInvalidation =
        HtmlPipelineInvalidation.HitTest;

    private HtmlRenderDamageBits pendingDamage =
        HtmlRenderDamageBits.FullFrame |
        HtmlRenderDamageBits.Document;

    public HtmlPipelineMetricsSnapshot LastPipelineMetrics { get; private set; }
    private HtmlPipelineMetricsSnapshot frameBuildMetrics;
    private Enaga.Html.Style.RenderDamage frameStyleDamage;

    private void Invalidate(HtmlPipelineInvalidation invalidation, HtmlRenderDamageBits damage)
    {
        pendingInvalidation |= invalidation;
        pendingDamage |= damage;
    }

    private static bool HasAny(HtmlPipelineInvalidation value, HtmlPipelineInvalidation flags)
        => (value & flags) != 0;

    private static bool HasAny(HtmlRenderDamageBits value, HtmlRenderDamageBits flags)
        => (value & flags) != 0;

    private SceneDamageReason ResolveDamageReasons(
        HtmlPipelineInvalidation consumedInvalidation,
        HtmlRenderDamageBits consumedDamage,
        Enaga.Html.Style.RenderDamage styleDamage,
        int previousWidth,
        int previousHeight,
        int width,
        int height)
    {
        var reasons = SceneDamageReason.None;
        if (HasAny(consumedDamage, HtmlRenderDamageBits.Document) ||
            HasAny(consumedInvalidation, HtmlPipelineInvalidation.Document | HtmlPipelineInvalidation.Style))
        {
            reasons |= SceneDamageReason.RuntimeReload;
        }

        if (previousWidth != width || previousHeight != height ||
            HasAny(consumedDamage, HtmlRenderDamageBits.Resize))
        {
            reasons |= SceneDamageReason.Resize;
        }

        if (HasAny(consumedDamage, HtmlRenderDamageBits.Scroll | HtmlRenderDamageBits.Interactive) ||
            HasAny(consumedInvalidation, HtmlPipelineInvalidation.Scroll | HtmlPipelineInvalidation.Interactive))
        {
            reasons |= SceneDamageReason.Scroll;
        }

        if (((styleDamage & (
                Enaga.Html.Style.RenderDamage.RebuildLayoutTree |
                Enaga.Html.Style.RenderDamage.Relayout |
                Enaga.Html.Style.RenderDamage.Refragment |
                Enaga.Html.Style.RenderDamage.Repaint |
                Enaga.Html.Style.RenderDamage.Reraster |
                Enaga.Html.Style.RenderDamage.RebuildLayer |
                Enaga.Html.Style.RenderDamage.RebuildHitTest |
                Enaga.Html.Style.RenderDamage.FullFrame)) != 0 ||
             HasAny(consumedDamage, HtmlRenderDamageBits.FullFrame)) &&
            reasons == SceneDamageReason.None)
        {
            reasons |= SceneDamageReason.FullFrameFallback;
        }

        return reasons;
    }

    private void RecordDirtyMetrics(ReadOnlySpan<SceneDamageRect> dirtyRects)
        => LastPipelineMetrics = frameBuildMetrics.WithDirtyRects(
            dirtyRects.Length,
            CalculateDirtyRectArea(dirtyRects));

    private void RecordFullFrameMetrics(int width, int height)
        => LastPipelineMetrics = frameBuildMetrics.WithDirtyRects(1, Math.Max(0, width) * (long)Math.Max(0, height));

    private void RecordNoDamageMetrics()
        => LastPipelineMetrics = frameBuildMetrics.WithDirtyRects(0, 0);

    private SceneLayoutCommit BuildDocumentCommit(
        HtmlParsedDocument parsed,
        int width,
        int height)
    {
        var commit = builder.Build(parsed, width, height, viewportScale, hoveredDomNodeIds, activeDomNodeId);
        frameBuildMetrics = builder.LastMetrics;
        frameStyleDamage = builder.LastDamage;
        cachedBaseFragmentTree = builder.LastFragmentTree;
        cachedSceneNodeDomIds = builder.LastSceneNodeDomIds;
        UpdateDomNodeRelationshipMaps(parsed.RootElement);
        hitTestGeometryVersion++;
        return commit;
    }

    private void UpdateDomNodeRelationshipMaps(HtmlDomElement root)
    {
        if (cachedDomRelationshipCapacity == 0)
            cachedDomRelationshipCapacity = CountElementNodes(root);
        cachedDomNodeParentIds.Clear();
        cachedDomNodeDepths.Clear();
        cachedDomElements.Clear();
        cachedDomNodeParentIds.EnsureCapacity(Math.Max(0, cachedDomRelationshipCapacity - 1));
        cachedDomNodeDepths.EnsureCapacity(cachedDomRelationshipCapacity);
        cachedDomElements.EnsureCapacity(cachedDomRelationshipCapacity);
        cachedDomElements[root.NodeId] = root;
        cachedDomNodeDepths[root.NodeId] = 0;
        AddChildren(root, depth: 0, cachedDomNodeParentIds, cachedDomNodeDepths, cachedDomElements);

        static int CountElementNodes(HtmlDomElement element)
        {
            var count = 1;
            for (var index = 0; index < element.Children.Count; index++)
            {
                if (element.Children[index] is HtmlDomElement childElement)
                    count += CountElementNodes(childElement);
            }

            return count;
        }

        static void AddChildren(
            HtmlDomElement parent,
            int depth,
            Dictionary<HtmlNodeId, HtmlNodeId> parents,
            Dictionary<HtmlNodeId, int> depths,
            Dictionary<HtmlNodeId, HtmlDomElement> elements)
        {
            for (var index = 0; index < parent.Children.Count; index++)
            {
                var child = parent.Children[index];
                if (child is not HtmlDomElement childElement)
                    continue;

                parents[childElement.NodeId] = parent.NodeId;
                depths[childElement.NodeId] = depth + 1;
                elements[childElement.NodeId] = childElement;
                AddChildren(childElement, depth + 1, parents, depths, elements);
            }
        }
    }

    private static long CalculateDirtyRectArea(ReadOnlySpan<SceneDamageRect> dirtyRects)
    {
        var area = 0L;
        foreach (var rect in dirtyRects)
            area += Math.Max(0, rect.Width) * (long)Math.Max(0, rect.Height);

        return area;
    }
}

[Flags]
internal enum HtmlPipelineInvalidation
{
    None = 0,
    Document = 1 << 0,
    Style = 1 << 1,
    Layout = 1 << 2,
    Fragments = 1 << 3,
    DisplayList = 1 << 4,
    Interactive = 1 << 5,
    Scroll = 1 << 6,
    Viewport = 1 << 7,
    HitTest = 1 << 8,
    Hover = 1 << 9
}

[Flags]
internal enum HtmlRenderDamageBits
{
    None = 0,
    FullFrame = 1 << 0,
    DirtyRects = 1 << 1,
    Document = 1 << 2,
    Resize = 1 << 3,
    Interactive = 1 << 4,
    Scroll = 1 << 5
}
