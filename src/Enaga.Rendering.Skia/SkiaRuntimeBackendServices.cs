using Enaga.Scene;
using SkiaSharp;

namespace Enaga.Rendering.Skia;


public sealed class SkiaRuntimeTextServices : IRuntimeTextServices
{
    private readonly SkiaTextResources textResources;

    public SkiaRuntimeTextServices()
        : this(new SkiaTextResources())
    {
    }

    internal SkiaRuntimeTextServices(SkiaTextResources textResources)
    {
        this.textResources = textResources ?? throw new ArgumentNullException(nameof(textResources));
    }

    internal SkiaTextResources TextResources => textResources;

    public void ConfigureFonts(string? defaultFamily = null, IReadOnlyList<string>? fallbackFamilies = null)
    {
        textResources.FontCatalog.Configure(defaultFamily, fallbackFamilies);
    }

    public void RegisterFont(string family, string source)
    {
        textResources.FontCatalog.RegisterFont(family, source);
    }

    public float MeasureTextHeight(string content, float width, SceneTextStyle style)
    {
        return textResources.TextMeasurer.MeasureTextHeight(content, width, style);
    }

    public float MeasureLineHeight(float fontSize)
    {
        return textResources.TextMeasurer.MeasureLineHeight(fontSize);
    }

    public float MeasureLineHeight(SceneFont font)
    {
        return textResources.TextMeasurer.MeasureLineHeight(font);
    }

    public float MeasureTextWidth(string content, SceneTextStyle style)
        => MeasureTextWidth((content ?? string.Empty).AsSpan(), style);

    public float MeasureTextWidth(ReadOnlySpan<char> content, SceneTextStyle style)
    {
        return textResources.TextMeasurer.MeasureTextWidth(content, style);
    }

    public int BreakText(ReadOnlySpan<char> content, float maxWidth, SceneTextStyle style, out float measuredWidth)
    {
        return textResources.TextMeasurer.BreakText(content, maxWidth, style, out measuredWidth);
    }

    public int SnapCaretIndex(string text, int caretIndex)
    {
        return textResources.InputMetrics.SnapCaretIndex(text, caretIndex);
    }

    public int GetPreviousTextElementIndex(string text, int caretIndex)
    {
        return textResources.InputMetrics.GetPreviousTextElementIndex(text, caretIndex);
    }

    public int GetNextTextElementIndex(string text, int caretIndex)
    {
        return textResources.InputMetrics.GetNextTextElementIndex(text, caretIndex);
    }

    public RuntimeCaretPosition GetCaretPosition(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex)
    {
        using var paint = textResources.InputMetrics.CreatePaint();
        var layout = textResources.InputMetrics.CreateLayout(style, paint, text, lineHeight, maxWidth);
        var caret = textResources.InputMetrics.GetCaretPosition(layout, caretIndex);
        return new RuntimeCaretPosition(caret.X, caret.Y);
    }

    public int HitTestCaretIndex(SceneTextStyle style, string text, float lineHeight, float maxWidth, float x, float y)
    {
        using var paint = textResources.InputMetrics.CreatePaint();
        var layout = textResources.InputMetrics.CreateLayout(style, paint, text, lineHeight, maxWidth);
        return textResources.InputMetrics.HitTestCaretIndex(layout, x, y);
    }

    public int MoveCaretVertical(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex, int lineDelta, float? preferredX)
    {
        using var paint = textResources.InputMetrics.CreatePaint();
        var layout = textResources.InputMetrics.CreateLayout(style, paint, text, lineHeight, maxWidth);
        return textResources.InputMetrics.MoveCaretVertical(layout, caretIndex, lineDelta, preferredX);
    }

    public int MoveCaretToLineEdge(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex, bool toEnd)
    {
        using var paint = textResources.InputMetrics.CreatePaint();
        var layout = textResources.InputMetrics.CreateLayout(style, paint, text, lineHeight, maxWidth);
        return textResources.InputMetrics.MoveCaretToLineEdge(layout, caretIndex, toEnd);
    }

    public void Dispose()
    {
        textResources.Dispose();
    }
}

public sealed class SkiaRuntimeImageResolver : IRuntimeImageResolver
{
    public RuntimeImageResolveResult ResolveImage(string source)
    {
        var result = WebImageCache.Resolve(source);
        if (result.State == WebImageCacheState.Failed)
            return new RuntimeImageResolveResult(RuntimeImageResolveState.Failed, result.LocalPath, result.Error);

        if (result.State == WebImageCacheState.Pending)
            return new RuntimeImageResolveResult(RuntimeImageResolveState.Pending, result.LocalPath, result.Error);

        var asset = SkiaImageAssetCache.Resolve(result.LocalPath);
        var state = asset.State switch
        {
            SkiaImageAssetState.Pending => RuntimeImageResolveState.Pending,
            SkiaImageAssetState.Ready => RuntimeImageResolveState.Ready,
            SkiaImageAssetState.Failed => RuntimeImageResolveState.Failed,
            _ => RuntimeImageResolveState.Pending
        };
        return new RuntimeImageResolveResult(state, result.LocalPath, asset.Error);
    }
}

public static class SkiaRuntimeBackendServices
{
    public static RuntimeBackendServices Create()
    {
        var textResources = new SkiaTextResources();
        return new RuntimeBackendServices(
            new SkiaRuntimeTextServices(textResources),
            new SkiaRuntimeImageResolver());
    }
}
