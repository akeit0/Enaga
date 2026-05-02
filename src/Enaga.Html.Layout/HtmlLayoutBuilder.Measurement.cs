using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

internal sealed partial class HtmlLayoutBuilder
{
    private InlineTextMeasure MeasureInlineText(string text, SceneTextStyle textStyle, float lineHeight)
    {
        var key = new InlineTextMeasureKey(
            text,
            textStyle.Font.Size,
            textStyle.Font.CacheIdentity,
            textStyle.Font.Weight,
            textStyle.Underline,
            textStyle.Font.Italic,
            textStyle.TextOverflowEllipsis,
            lineHeight);
        if (measurementCache.TryGetInlineText(key, out var cached))
            return cached;

        var resolvedLineHeight = ResolveNormalLineHeight(textStyle.Font, lineHeight);
        if (text.IndexOf('\n') >= 0)
        {
            var maxLineWidth = 0f;
            var lineCount = 1;
            var lineStart = 0;
            for (var index = 0; index <= text.Length; index++)
            {
                if (index < text.Length && text[index] != '\n')
                    continue;

                var line = text.AsSpan(lineStart, index - lineStart);
                maxLineWidth = MathF.Max(maxLineWidth, MathF.Ceiling(textServices.MeasureTextWidth(line, textStyle)));
                lineCount += index < text.Length ? 1 : 0;
                lineStart = index + 1;
            }

            var multilineMeasured = new InlineTextMeasure(maxLineWidth, resolvedLineHeight * lineCount);
            measurementCache.SetInlineText(key, multilineMeasured);
            return multilineMeasured;
        }

        var measured = new InlineTextMeasure(
            MathF.Ceiling(textServices.MeasureTextWidth(text.AsSpan(), textStyle)),
            resolvedLineHeight);
        measurementCache.SetInlineText(key, measured);
        return measured;
    }

    private sealed class HtmlLayoutMeasurementCache(HtmlPipelineMetrics metrics)
    {
        private readonly Dictionary<InlineTextMeasureKey, InlineTextMeasure> inlineText = new();
        private readonly Dictionary<ContainerPercentResolveKey, IReadOnlyList<HtmlSceneNode>> containerPercentNodes = new();
        private LayoutOutputCache? layoutOutputs;

        public void BeginLayoutPass(LayoutOutputCache nextLayoutOutputs)
        {
            layoutOutputs = nextLayoutOutputs;
            containerPercentNodes.Clear();
        }

        public bool TryGetInlineText(InlineTextMeasureKey key, out InlineTextMeasure measure)
            => inlineText.TryGetValue(key, out measure);

        public void SetInlineText(InlineTextMeasureKey key, InlineTextMeasure measure)
            => inlineText[key] = measure;

        public bool TryGetIntrinsicSize(LayoutCacheKey key, out (float Width, float Height) measure)
        {
            if (layoutOutputs is not null && layoutOutputs.TryGet(key, out var output))
            {
                metrics.AddLayoutCacheHit();
                measure = (output.Size.Width, output.Size.Height);
                return true;
            }

            metrics.AddLayoutCacheMiss();
            measure = default;
            return false;
        }

        public void SetIntrinsicSize(LayoutCacheKey key, (float Width, float Height) measure)
            => (layoutOutputs ?? throw new InvalidOperationException("Layout output cache was not bound for this layout pass.")).Store(
                key,
                new LayoutOutput(
                    new LayoutSize(measure.Width, measure.Height),
                    new LayoutSize(measure.Width, measure.Height),
                    new LayoutRect(0, 0, measure.Width, measure.Height)));

        public bool TryGetLayoutHeight(LayoutCacheKey key, out float height)
        {
            if (layoutOutputs is not null && layoutOutputs.TryGet(key, out var output))
            {
                metrics.AddLayoutCacheHit();
                height = output.Size.Height;
                return true;
            }

            metrics.AddLayoutCacheMiss();
            height = 0;
            return false;
        }

        public void SetLayoutHeight(LayoutCacheKey key, float height)
            => (layoutOutputs ?? throw new InvalidOperationException("Layout output cache was not bound for this layout pass.")).Store(
                key,
                new LayoutOutput(
                    new LayoutSize(0, height),
                    new LayoutSize(0, height),
                    new LayoutRect(0, 0, 0, height)));

        public bool TryGetContainerPercentNodes(ContainerPercentResolveKey key, out IReadOnlyList<HtmlSceneNode> nodes)
            => containerPercentNodes.TryGetValue(key, out nodes!);

        public void SetContainerPercentNodes(ContainerPercentResolveKey key, IReadOnlyList<HtmlSceneNode> nodes)
            => containerPercentNodes[key] = nodes;
    }

    private readonly record struct InlineTextMeasureKey(
        string Text,
        float FontSize,
        string FontIdentity,
        int FontWeight,
        bool Underline,
        bool Italic,
        bool TextOverflowEllipsis,
        float LineHeight);

    private readonly record struct ContainerPercentResolveKey(IReadOnlyList<HtmlSceneNode> Nodes, int QuantizedContainerWidth);

    private readonly record struct InlineTextMeasure(float Width, float Height);

    private float ResolveNormalLineHeight(SceneFont font, float explicitLineHeight)
        => explicitLineHeight > 0
            ? explicitLineHeight
            : textServices.MeasureLineHeight(font);
}

