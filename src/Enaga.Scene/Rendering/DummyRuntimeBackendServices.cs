using Enaga.Scene;

namespace Enaga.Rendering;

public sealed class DummyRuntimeTextServices : IRuntimeTextServices
{
    public void ConfigureFonts(string? defaultFamily = null, IReadOnlyList<string>? fallbackFamilies = null)
    {
    }

    public void RegisterFont(string family, string source)
    {
    }

    public float MeasureTextHeight(string content, float width, SceneTextStyle style)
    {
        var lineHeight = MeasureLineHeight(style.Font);
        if (!style.WrapText || width <= 1)
            return lineHeight;

        var lineCount = Math.Max(1, (int)MathF.Ceiling(MeasureTextWidth(content, style) / width));
        return lineCount * lineHeight;
    }

    public float MeasureLineHeight(float fontSize) => MathF.Ceiling(fontSize * 1.35f);

    public float MeasureLineHeight(SceneFont font) => MeasureLineHeight(font.Size);

    public float MeasureTextWidth(string content, SceneTextStyle style)
        => MeasureTextWidth((content ?? string.Empty).AsSpan(), style);

    public float MeasureTextWidth(ReadOnlySpan<char> content, SceneTextStyle style)
    {
        var widthPerCharacter = style.Font.Weight >= 600 ? 0.62f : 0.58f;
        return content.Length * style.FontSize * widthPerCharacter;
    }

    public int BreakText(ReadOnlySpan<char> content, float maxWidth, SceneTextStyle style, out float measuredWidth)
    {
        var charWidth = Math.Max(1f, style.Font.Size * (style.Font.Weight >= 600 ? 0.62f : 0.58f));
        var count = maxWidth <= 0
            ? 0
            : Math.Clamp((int)MathF.Floor(maxWidth / charWidth), 0, content.Length);
        measuredWidth = count * charWidth;
        return count;
    }

    public int SnapCaretIndex(string text, int caretIndex) => Math.Clamp(caretIndex, 0, text.Length);

    public int GetPreviousTextElementIndex(string text, int caretIndex)
        => Math.Max(0, SnapCaretIndex(text, caretIndex) - 1);

    public int GetNextTextElementIndex(string text, int caretIndex)
        => Math.Min(text.Length, SnapCaretIndex(text, caretIndex) + 1);

    public RuntimeCaretPosition GetCaretPosition(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex)
    {
        var clampedIndex = SnapCaretIndex(text, caretIndex);
        var width = MeasureTextWidth(text[..clampedIndex], style);
        var resolvedLineHeight = lineHeight > 0 ? lineHeight : MeasureLineHeight(style.Font);
        if (!style.WrapText || maxWidth <= 1)
            return new RuntimeCaretPosition(width, 0);

        var row = MathF.Floor(width / maxWidth);
        var x = width - row * maxWidth;
        return new RuntimeCaretPosition(x, row * resolvedLineHeight);
    }

    public int HitTestCaretIndex(SceneTextStyle style, string text, float lineHeight, float maxWidth, float x, float y)
    {
        if (text.Length == 0)
            return 0;

        var charWidth = Math.Max(1f, style.FontSize * 0.58f);
        var resolvedLineHeight = lineHeight > 0 ? lineHeight : MeasureLineHeight(style.Font);
        var line = maxWidth > 1 ? Math.Max(0, (int)MathF.Floor(y / resolvedLineHeight)) : 0;
        var column = Math.Max(0, (int)MathF.Round(x / charWidth));
        var charactersPerLine = maxWidth > 1 ? Math.Max(1, (int)MathF.Floor(maxWidth / charWidth)) : text.Length;
        return Math.Clamp(line * charactersPerLine + column, 0, text.Length);
    }

    public int MoveCaretVertical(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex, int lineDelta, float? preferredX)
    {
        if (text.Length == 0)
            return 0;

        var position = GetCaretPosition(style, text, lineHeight, maxWidth, caretIndex);
        var resolvedLineHeight = lineHeight > 0 ? lineHeight : MeasureLineHeight(style.Font);
        var targetX = preferredX ?? position.X;
        var targetY = position.Y + lineDelta * resolvedLineHeight;
        return HitTestCaretIndex(style, text, lineHeight, maxWidth, targetX, targetY);
    }

    public int MoveCaretToLineEdge(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex, bool toEnd)
    {
        if (!style.WrapText || maxWidth <= 1)
            return toEnd ? text.Length : 0;

        var charWidth = Math.Max(1f, style.FontSize * 0.58f);
        var charsPerLine = Math.Max(1, (int)MathF.Floor(maxWidth / charWidth));
        var clamped = SnapCaretIndex(text, caretIndex);
        var lineStart = clamped / charsPerLine * charsPerLine;
        return toEnd ? Math.Min(text.Length, lineStart + charsPerLine) : lineStart;
    }

    public void Dispose()
    {
    }
}

public sealed class DummyRuntimeImageResolver : IRuntimeImageResolver
{
    public RuntimeImageResolveResult ResolveImage(string source)
    {
        return new RuntimeImageResolveResult(RuntimeImageResolveState.Failed, Error: $"Dummy backend cannot resolve image '{source}'.");
    }
}

public static class DummyRuntimeBackendServices
{
    public static RuntimeBackendServices Create()
    {
        return new RuntimeBackendServices(
            new DummyRuntimeTextServices(),
            new DummyRuntimeImageResolver());
    }
}
