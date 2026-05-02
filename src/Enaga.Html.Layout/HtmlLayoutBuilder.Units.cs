using Enaga.Scene;

namespace Enaga.Html;

internal sealed partial class HtmlLayoutBuilder
{
    private static IReadOnlyList<HtmlSceneNode> ResolveViewportUnits(IReadOnlyList<HtmlSceneNode> nodes, float viewportWidth, float viewportHeight)
    {
        if (nodes.Count == 0)
            return nodes;

        HtmlSceneNode[]? resolved = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var style = node.Style.CloneWithResolvedViewportUnits(viewportWidth, viewportHeight);
            var children = ResolveViewportUnits(node.Children, viewportWidth, viewportHeight);
            if (ReferenceEquals(style, node.Style) &&
                ReferenceEquals(children, node.Children))
            {
                if (resolved is not null)
                    resolved[index] = node;
                continue;
            }

            if (resolved is null)
            {
                resolved = new HtmlSceneNode[nodes.Count];
                for (var copyIndex = 0; copyIndex < index; copyIndex++)
                    resolved[copyIndex] = nodes[copyIndex];
            }

            resolved[index] = node with
            {
                Style = style,
                Children = children
            };
        }

        return resolved ?? nodes;
    }

    private IReadOnlyList<HtmlSceneNode> ResolveContainerPercentUnits(IReadOnlyList<HtmlSceneNode> nodes, float containerWidth)
    {
        if (nodes.Count == 0)
            return nodes;

        var cacheKey = new ContainerPercentResolveKey(nodes, QuantizeMeasureKey(containerWidth));
        if (measurementCache.TryGetContainerPercentNodes(cacheKey, out var cached))
            return cached;

        HtmlSceneNode[]? resolved = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var style = node.Style.CloneWithResolvedContainerPercentUnits(
                containerWidth,
                resolveInlineSize: node.NodeKind != SceneNodeKind.Image);
            if (ReferenceEquals(style, node.Style))
            {
                if (resolved is not null)
                    resolved[index] = node;
                continue;
            }

            if (resolved is null)
            {
                resolved = new HtmlSceneNode[nodes.Count];
                for (var copyIndex = 0; copyIndex < index; copyIndex++)
                    resolved[copyIndex] = nodes[copyIndex];
            }

            resolved[index] = node with
            {
                Style = style
            };
        }

        var resolvedNodes = resolved ?? nodes;
        measurementCache.SetContainerPercentNodes(cacheKey, resolvedNodes);
        return resolvedNodes;
    }
}

