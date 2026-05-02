using Enaga.Scene;
using SkiaSharp;
using System.Globalization;

namespace Enaga.Rendering.Skia;

internal sealed class SkiaTextMeasurer
{
    private const float NormalLineHeightMultiplier = 1.35f;
    private readonly TextFontCatalog fontCatalog;
    private readonly SkiaFontCollection fontCollection;

    public SkiaTextMeasurer(TextFontCatalog fontCatalog, SkiaFontCollection fontCollection)
    {
        this.fontCatalog = fontCatalog ?? throw new ArgumentNullException(nameof(fontCatalog));
        this.fontCollection = fontCollection ?? throw new ArgumentNullException(nameof(fontCollection));
    }

    public float MeasureLineHeight(float fontSize)
    {
        return MeasureLineHeight(new SceneFont(fontSize));
    }

    public float MeasureLineHeight(SceneFont sceneFont)
    {
        using var fontLease = fontCollection.Get(sceneFont);
        return ResolveLineHeight(fontLease.Data, sceneFont.Size);
    }

    public float MeasureTextHeight(string content, float width, SceneTextStyle textStyle)
    {
        using var fontLease = fontCollection.Get(textStyle.Font);
        var fontData = fontLease.Data;
        var lineHeight = ResolveLineHeight(fontData, textStyle.Font.Size);
        var maxWidth = width > 0 ? width : float.PositiveInfinity;
        var lineCount = CountVisualLines((content ?? string.Empty).AsSpan(), maxWidth, textStyle);
        return Math.Max(lineHeight, lineCount * lineHeight);
    }

    public float MeasureTextWidth(ReadOnlySpan<char> content, SceneTextStyle textStyle)
    {
        if (content.IsEmpty)
            return 0;

        if (IsAsciiWithoutCombiningMarks(content))
        {
            using var fontLease = fontCollection.Get(textStyle.Font);
            return fontLease.Data.Font.MeasureText(content, null);
        }

        if (fontCatalog.TryResolveSingleTypefaceForText(textStyle.Font, content, out var singleTypeface))
        {
            using var font = SkiaFontSynthesis.CreateFont(singleTypeface, textStyle.Font);
            return font.MeasureText(content, null);
        }

        return MeasureFallbackTextWidth(content, textStyle.Font);
    }

    public int BreakText(ReadOnlySpan<char> content, float maxWidth, SceneTextStyle textStyle, out float measuredWidth)
    {
        measuredWidth = 0;
        if (content.IsEmpty || maxWidth <= 0)
            return 0;

        if (IsAsciiWithoutCombiningMarks(content))
        {
            using var fontLease = fontCollection.Get(textStyle.Font);
            return fontLease.Data.Font.BreakText(content, maxWidth, out measuredWidth, null);
        }

        if (fontCatalog.TryResolveSingleTypefaceForText(textStyle.Font, content, out var singleTypeface))
        {
            using var font = SkiaFontSynthesis.CreateFont(singleTypeface, textStyle.Font);
            return font.BreakText(content, maxWidth, out measuredWidth, null);
        }

        return BreakFallbackText(content, maxWidth, textStyle.Font, out measuredWidth);
    }

    private static float ResolveLineHeight(SkiaFontCollection.SkiaFontData fontData, float fallbackFontSize)
    {
        var metrics = fontData.Metrics;
        var measured = metrics.Descent - metrics.Ascent + Math.Max(0, metrics.Leading);
        var normalLineHeight = fallbackFontSize * NormalLineHeightMultiplier;
        if (float.IsFinite(measured) && measured > 0)
            return MathF.Ceiling(Math.Max(measured, normalLineHeight));

        return MathF.Ceiling(normalLineHeight);
    }

    private int CountVisualLines(ReadOnlySpan<char> content, float maxWidth, SceneTextStyle textStyle)
    {
        var lineCount = 0;
        var lineStart = 0;
        var index = 0;
        while (index < content.Length)
        {
            if (!TryGetLineBreakLength(content, index, out var lineBreakLength))
            {
                index++;
                continue;
            }

            lineCount += CountWrappedLine(content[lineStart..index], maxWidth, textStyle);
            index += lineBreakLength;
            lineStart = index;
        }

        lineCount += CountWrappedLine(content[lineStart..], maxWidth, textStyle);
        return Math.Max(1, lineCount);
    }

    private int CountWrappedLine(ReadOnlySpan<char> line, float maxWidth, SceneTextStyle textStyle)
    {
        if (line.IsEmpty ||
            !textStyle.WrapText ||
            !float.IsFinite(maxWidth) ||
            maxWidth <= 0)
        {
            return 1;
        }

        var count = 0;
        var remaining = line;
        while (!remaining.IsEmpty)
        {
            var fitCount = BreakText(remaining, maxWidth, textStyle, out _);
            if (fitCount >= remaining.Length)
            {
                count++;
                break;
            }

            var breakCount = FindLastWrapOpportunity(remaining[..Math.Max(0, fitCount)]);
            if (breakCount <= 0)
                breakCount = fitCount > 0 ? fitCount : GetFirstTextElementLength(remaining);
            if (breakCount < remaining.Length && IsLineStartProhibitedJapanesePunctuation(remaining[breakCount]))
                breakCount += GetFirstTextElementLength(remaining[breakCount..]);

            remaining = remaining[breakCount..];
            count++;
        }

        return Math.Max(1, count);
    }

    private static bool TryGetLineBreakLength(ReadOnlySpan<char> text, int index, out int length)
    {
        if (text[index] == '\n')
        {
            length = 1;
            return true;
        }

        if (text[index] == '\r')
        {
            length = index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
            return true;
        }

        length = 0;
        return false;
    }

    private static int FindLastWrapOpportunity(ReadOnlySpan<char> text)
    {
        for (var index = text.Length - 1; index >= 0; index--)
        {
            if (char.IsWhiteSpace(text[index]))
                return index + 1;
            if (index > 0 &&
                IsCjkCharacter(text[index - 1]) &&
                !IsLineStartProhibitedJapanesePunctuation(text[index]))
                return index;
        }

        return -1;
    }

    private static bool IsCjkCharacter(char ch)
        => ch is >= '\u3040' and <= '\u30ff' ||
           ch is >= '\u3400' and <= '\u9fff' ||
           ch is >= '\uf900' and <= '\ufaff';

    private static bool IsLineStartProhibitedJapanesePunctuation(char ch)
        => ch is '。' or '、' or '，' or '．' or '！' or '？' or '）' or ')' or '］' or ']' or '｝' or '}' or '」' or '』' or '】' or '〉' or '》' or 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ' or 'っ' or 'ゃ' or 'ゅ' or 'ょ' or 'ァ' or 'ィ' or 'ゥ' or 'ェ' or 'ォ' or 'ッ' or 'ャ' or 'ュ' or 'ョ' or 'ー';

    private static int GetFirstTextElementLength(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return 0;

        if (char.IsHighSurrogate(text[0]) &&
            text.Length > 1 &&
            char.IsLowSurrogate(text[1]))
        {
            return 2;
        }

        return 1;
    }

    private float MeasureFallbackTextWidth(ReadOnlySpan<char> text, SceneFont sceneFont)
    {
        var width = 0f;
        var runStart = 0;
        var runLength = 0;
        SKTypeface? runTypeface = null;
        var index = 0;
        while (index < text.Length)
        {
            var elementStart = index;
            var elementLength = GetTextElementLength(text[index..]);
            var typeface = fontCatalog.ResolveTypefaceForText(sceneFont, text.Slice(elementStart, elementLength));
            if (runTypeface is not null && ReferenceEquals(runTypeface, typeface))
            {
                runLength = elementStart + elementLength - runStart;
                index += elementLength;
                continue;
            }

            if (runTypeface is not null)
                width += MeasureRun(text, runStart, runLength, sceneFont, runTypeface);

            runStart = elementStart;
            runLength = elementLength;
            runTypeface = typeface;
            index += elementLength;
        }

        if (runTypeface is not null)
            width += MeasureRun(text, runStart, runLength, sceneFont, runTypeface);

        return width;
    }

    private int BreakFallbackText(ReadOnlySpan<char> text, float maxWidth, SceneFont sceneFont, out float measuredWidth)
    {
        measuredWidth = 0;
        var fitCount = 0;
        var runStart = 0;
        var runLength = 0;
        SKTypeface? runTypeface = null;
        var index = 0;
        while (index < text.Length)
        {
            var elementStart = index;
            var elementLength = GetTextElementLength(text[index..]);
            var element = text.Slice(elementStart, elementLength);
            var typeface = fontCatalog.ResolveTypefaceForText(sceneFont, element);
            if (runTypeface is not null && ReferenceEquals(runTypeface, typeface))
            {
                runLength = elementStart + elementLength - runStart;
                index += elementLength;
                continue;
            }

            if (runTypeface is not null)
            {
                var runFitCount = BreakRun(text, runStart, runLength, sceneFont, runTypeface, maxWidth - measuredWidth, out var runMeasuredWidth);
                measuredWidth += runMeasuredWidth;
                fitCount += runFitCount;
                if (runFitCount < runLength)
                    return fitCount;
            }

            runStart = elementStart;
            runLength = elementLength;
            runTypeface = typeface;
            index += elementLength;
        }

        if (runTypeface is not null)
        {
            var runFitCount = BreakRun(text, runStart, runLength, sceneFont, runTypeface, maxWidth - measuredWidth, out var runMeasuredWidth);
            measuredWidth += runMeasuredWidth;
            fitCount += runFitCount;
        }

        return fitCount;
    }

    private static float MeasureRun(ReadOnlySpan<char> text, int start, int length, SceneFont sceneFont, SKTypeface typeface)
    {
        using var font = SkiaFontSynthesis.CreateFont(typeface, sceneFont);
        return font.MeasureText(text.Slice(start, length), null);
    }

    private static int BreakRun(ReadOnlySpan<char> text, int start, int length, SceneFont sceneFont, SKTypeface typeface, float maxWidth, out float measuredWidth)
    {
        using var font = SkiaFontSynthesis.CreateFont(typeface, sceneFont);
        return font.BreakText(text.Slice(start, length), maxWidth, out measuredWidth, null);
    }

    private static int GetTextElementLength(ReadOnlySpan<char> text)
    {
        var length = GetFirstScalarLength(text);
        while (length < text.Length)
        {
            if (text[length] == '\u200d')
            {
                var nextLength = length + 1 < text.Length ? GetFirstScalarLength(text[(length + 1)..]) : 0;
                if (nextLength <= 0)
                    break;

                length += 1 + nextLength;
                continue;
            }

            var nextScalarLength = GetFirstScalarLength(text[length..]);
            if (nextScalarLength <= 0)
                break;

            var ch = text[length];
            if (IsCombiningOrVariation(ch))
            {
                length += nextScalarLength;
                continue;
            }

            break;
        }

        return length;
    }

    private static int GetFirstScalarLength(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return 0;

        return char.IsHighSurrogate(text[0]) &&
               text.Length > 1 &&
               char.IsLowSurrogate(text[1])
            ? 2
            : 1;
    }

    private static bool IsCombiningOrVariation(char ch)
        => CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark ||
           ch is >= '\ufe00' and <= '\ufe0f';

    private static bool IsAsciiWithoutCombiningMarks(ReadOnlySpan<char> text)
    {
        foreach (var ch in text)
        {
            if (ch > 0x7f || CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                return false;
        }

        return true;
    }
}
