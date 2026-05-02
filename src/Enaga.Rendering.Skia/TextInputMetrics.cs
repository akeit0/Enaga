using Enaga.Scene;
using SkiaSharp;
using System.Buffers;
using System.Globalization;

namespace Enaga.Rendering.Skia;


internal sealed class TextInputMetrics : IDisposable
{
    private const int layoutCacheLimit = 1024;
    private const int layoutCacheMaxTextLength = 8192;
    private const int fontCacheLimit = 128;
    private readonly object layoutCacheSync = new();
    private readonly Dictionary<TextLayoutCacheKey, TextLayout> layoutCache = new();
    private readonly Queue<TextLayoutCacheKey> layoutCacheOrder = new();
    private readonly object fontCacheSync = new();
    private readonly Dictionary<SkiaFontCacheKey, SkiaFontCacheEntry> fontCache = new();
    private readonly Queue<SkiaFontCacheKey> fontCacheOrder = new();
    private readonly TextFontCatalog fontCatalog;

    public TextInputMetrics(TextFontCatalog fontCatalog)
    {
        this.fontCatalog = fontCatalog ?? throw new ArgumentNullException(nameof(fontCatalog));
    }

    public float MeasureCaretOffset(SKFont font, SKPaint paint, string text, int caretIndex)
    {
        var layout = CreateLayout(CreateTextStyle(font), paint, text, lineHeight: font.Size * 1.35f);
        var caret = GetCaretPosition(layout, Math.Clamp(caretIndex, 0, text.Length));
        return caret.X;
    }

    public int HitTestCaretIndex(SKFont font, SKPaint paint, string text, float x)
    {
        var layout = CreateLayout(CreateTextStyle(font), paint, text, lineHeight: font.Size * 1.35f);
        return HitTestCaretIndex(layout, x, 0);
    }

    public TextLayout CreateLayout(SceneTextStyle style, SKPaint paint, string text, float lineHeight, float maxWidth = float.PositiveInfinity)
    {
        text ??= string.Empty;
        var normalizedLineHeight = lineHeight > 0 ? lineHeight : style.FontSize * 1.35f;
        var canCache = TryCreateLayoutCacheKey(style, text, normalizedLineHeight, maxWidth, out var cacheKey);
        if (canCache)
        {
            if (TryGetCachedLayout(cacheKey, out var cachedLayout))
                return cachedLayout;
        }

        var layout = CreateLayoutCore(style, paint, text, normalizedLineHeight, maxWidth);
        if (canCache)
            StoreCachedLayout(cacheKey, layout);

        return layout;
    }

    private TextLayout CreateLayoutCore(SceneTextStyle style, SKPaint paint, string text, float normalizedLineHeight, float maxWidth)
    {
        var spans = new List<TextLineSpan>();
        var lineStart = 0;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && text[index] != '\n')
                continue;

            var lineText = SliceText(text, lineStart, index - lineStart);
            AppendLineSpans(
                spans,
                style,
                paint,
                lineText,
                lineStart,
                index,
                normalizedLineHeight,
                maxWidth,
                endsWithNewline: index < text.Length);
            lineStart = index + 1;
        }

        if (spans.Count == 0)
            spans.Add(new TextLineSpan(0, 0, string.Empty, [0], [0], []));

        return new TextLayout(spans, normalizedLineHeight);
    }

    private bool TryCreateLayoutCacheKey(
        SceneTextStyle style,
        string text,
        float lineHeight,
        float maxWidth,
        out TextLayoutCacheKey cacheKey)
    {
        cacheKey = default;
        if (text.Length > layoutCacheMaxTextLength)
            return false;

        cacheKey = new TextLayoutCacheKey(
            fontCatalog.CurrentVersion,
            text,
            QuantizePixel(style.FontSize),
            style.Font.CacheIdentity,
            style.Font.Weight,
            style.Font.Italic,
            style.TextOverflowEllipsis,
            style.WrapText,
            QuantizePixel(lineHeight),
            QuantizePixel(maxWidth));
        return true;
    }

    private bool TryGetCachedLayout(TextLayoutCacheKey key, out TextLayout layout)
    {
        lock (layoutCacheSync)
            return layoutCache.TryGetValue(key, out layout!);
    }

    private void StoreCachedLayout(TextLayoutCacheKey key, TextLayout layout)
    {
        lock (layoutCacheSync)
        {
            if (layoutCache.TryAdd(key, layout))
                layoutCacheOrder.Enqueue(key);

            while (layoutCache.Count > layoutCacheLimit && layoutCacheOrder.Count > 0)
            {
                var oldestKey = layoutCacheOrder.Dequeue();
                layoutCache.Remove(oldestKey);
            }
        }
    }

    private int QuantizePixel(float value)
    {
        if (float.IsPositiveInfinity(value))
            return int.MaxValue;

        if (float.IsNegativeInfinity(value))
            return int.MinValue;

        if (float.IsNaN(value))
            return 0;

        return (int)MathF.Round(value * 4f);
    }

    public CaretPosition GetCaretPosition(TextLayout layout, int caretIndex)
    {
        var clampedCaretIndex = SnapCaretIndex(layout, caretIndex);
        var lineIndex = GetLineIndex(layout, clampedCaretIndex);
        var line = layout.Lines[lineIndex];
        var relativeIndex = Math.Clamp(clampedCaretIndex - line.StartIndex, 0, line.Text.Length);
        var boundaryIndex = GetCaretBoundaryIndex(line, relativeIndex);
        var x = line.CaretOffsets[boundaryIndex];
        return new CaretPosition(lineIndex, x, lineIndex * layout.LineHeight);
    }

    public int MoveCaretVertical(TextLayout layout, int caretIndex, int lineDelta, float? preferredX = null)
    {
        if (layout.Lines.Count == 0 || lineDelta == 0)
            return Math.Clamp(caretIndex, 0, layout.TextLength);

        var caret = GetCaretPosition(layout, caretIndex);
        var targetLineIndex = Math.Clamp(caret.LineIndex + lineDelta, 0, layout.Lines.Count - 1);
        if (targetLineIndex == caret.LineIndex)
            return Math.Clamp(caretIndex, 0, layout.TextLength);

        var targetLine = layout.Lines[targetLineIndex];
        var targetX = preferredX ?? caret.X;
        if (targetLine.Text.Length == 0 || targetX <= 0)
            return targetLine.StartIndex;

        for (var index = 0; index < targetLine.CaretOffsets.Length - 1; index++)
        {
            var left = targetLine.CaretOffsets[index];
            var right = targetLine.CaretOffsets[index + 1];
            if (targetX < right)
            {
                return targetX - left <= right - targetX
                    ? targetLine.StartIndex + targetLine.CaretIndices[index]
                    : targetLine.StartIndex + targetLine.CaretIndices[index + 1];
            }
        }

        return targetLine.EndIndex;
    }

    public int MoveCaretToLineEdge(TextLayout layout, int caretIndex, bool toEnd)
    {
        if (layout.Lines.Count == 0)
            return 0;

        var lineIndex = GetLineIndex(layout, Math.Clamp(caretIndex, 0, layout.TextLength));
        var line = layout.Lines[lineIndex];
        return toEnd ? line.EndIndex : line.StartIndex;
    }

    public int HitTestCaretIndex(TextLayout layout, float x, float y)
    {
        var lineIndex = Math.Clamp((int)Math.Floor(Math.Max(0, y) / layout.LineHeight), 0, layout.Lines.Count - 1);
        var line = layout.Lines[lineIndex];
        if (line.Text.Length == 0 || x <= 0)
            return line.StartIndex;

        var clampedX = Math.Max(0, x);
        for (var index = 0; index < line.CaretOffsets.Length - 1; index++)
        {
            var left = line.CaretOffsets[index];
            var right = line.CaretOffsets[index + 1];
            if (clampedX < right)
            {
                return clampedX - left <= right - clampedX
                    ? line.StartIndex + line.CaretIndices[index]
                    : line.StartIndex + line.CaretIndices[index + 1];
            }
        }

        return line.EndIndex;
    }

    public SelectionRect[] GetSelectionRects(TextLayout layout, int selectionStart, int selectionEnd)
    {
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, layout.TextLength);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, layout.TextLength);
        if (start == end)
            return [];

        var rects = new List<SelectionRect>();
        for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
        {
            var line = layout.Lines[lineIndex];
            if (end < line.StartIndex || start > line.EndIndex)
                continue;

            var lineSelectionStart = SnapCaretIndex(layout, Math.Max(start, line.StartIndex));
            var lineSelectionEnd = SnapCaretIndex(layout, Math.Min(end, line.EndIndex));
            var startOffset = line.CaretOffsets[GetCaretBoundaryIndex(line, lineSelectionStart - line.StartIndex)];
            var endOffset = line.CaretOffsets[GetCaretBoundaryIndex(line, lineSelectionEnd - line.StartIndex)];
            rects.Add(new SelectionRect(lineIndex, startOffset, endOffset, lineIndex * layout.LineHeight));
        }

        return [.. rects];
    }

    public int GetPreviousTextElementIndex(string text, int caretIndex)
    {
        var boundaries = GetTextElementBoundaries(text);
        var clamped = Math.Clamp(caretIndex, 0, text.Length);
        for (var index = boundaries.Length - 1; index >= 0; index--)
        {
            if (boundaries[index] < clamped)
                return boundaries[index];
        }

        return 0;
    }

    public int GetNextTextElementIndex(string text, int caretIndex)
    {
        var boundaries = GetTextElementBoundaries(text);
        var clamped = Math.Clamp(caretIndex, 0, text.Length);
        foreach (var boundary in boundaries)
        {
            if (boundary > clamped)
                return boundary;
        }

        return text.Length;
    }

    public int SnapCaretIndex(string text, int caretIndex)
    {
        var boundaries = GetTextElementBoundaries(text);
        var clamped = Math.Clamp(caretIndex, 0, text.Length);
        for (var index = boundaries.Length - 1; index >= 0; index--)
        {
            if (boundaries[index] <= clamped)
                return boundaries[index];
        }

        return 0;
    }

    public int SnapCaretIndex(TextLayout layout, int caretIndex)
    {
        var clamped = Math.Clamp(caretIndex, 0, layout.TextLength);
        var lineIndex = GetLineIndex(layout, clamped);
        var line = layout.Lines[lineIndex];
        var relativeIndex = Math.Clamp(clamped - line.StartIndex, 0, line.Text.Length);
        return line.StartIndex + line.CaretIndices[GetCaretBoundaryIndex(line, relativeIndex)];
    }

    public SKPaint CreatePaint()
    {
        return new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    public SKFont CreateFont(SceneTextStyle style)
    {
        return CreateFont(style.Font);
    }

    public SKFont CreateFont(SceneFont font)
    {
        return SkiaFontSynthesis.CreateFont(fontCatalog.ResolveTypeface(font), font);
    }

    internal SkiaFontLease BorrowFont(SceneTextStyle style)
    {
        return BorrowFont(style.Font);
    }

    internal SkiaFontLease BorrowFont(SceneFont font)
    {
        var key = new SkiaFontCacheKey(
            fontCatalog.CurrentVersion,
            QuantizePixel(font.Size),
            font.CacheIdentity,
            font.Weight,
            font.Italic);

        SkiaFontCacheEntry entry;
        lock (fontCacheSync)
        {
            if (!fontCache.TryGetValue(key, out entry!))
            {
                entry = new SkiaFontCacheEntry(CreateFont(font));
                fontCache[key] = entry;
                fontCacheOrder.Enqueue(key);
                TrimFontCache_NoLock();
            }

            entry.RefCount++;
        }

        return new SkiaFontLease(this, entry);
    }

    private int GetLineIndex(TextLayout layout, int caretIndex)
    {
        for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
        {
            var line = layout.Lines[lineIndex];
            if (caretIndex < line.EndIndex || lineIndex == layout.Lines.Count - 1)
                return lineIndex;

            if (caretIndex == line.EndIndex)
            {
                var sharedSoftWrapBoundary = !line.EndsWithNewline &&
                                             lineIndex < layout.Lines.Count - 1 &&
                                             layout.Lines[lineIndex + 1].StartIndex == caretIndex;
                if (!sharedSoftWrapBoundary)
                    return lineIndex;
            }
        }

        return layout.Lines.Count - 1;
    }

    private SceneTextStyle CreateTextStyle(SKFont font)
    {
        return new SceneTextStyle(
             font.Size,
             Font: new SceneFont(
                 font.Size,
                 font.Typeface?.FamilyName,
                 (int?)font.Typeface?.FontStyle.Weight ?? 400,
                 font.Typeface?.FontStyle.Slant != SKFontStyleSlant.Upright));
    }

    private TextRunSpan[] BuildTextRuns(SceneTextStyle style, SKPaint paint, string text, int[] caretIndices, float[] caretOffsets)
    {
        if (string.IsNullOrEmpty(text) || caretIndices.Length <= 1)
            return [];

        if (IsAsciiWithoutCombiningMarks(text))
            return BuildSingleTypefaceTextRuns(style, paint, text, caretIndices, caretOffsets);

        if (fontCatalog.TryResolveSingleTypefaceForText(style.Font, text, out var singleTypeface))
            return BuildSingleTypefaceTextRuns(style, paint, text, caretIndices, caretOffsets, singleTypeface);

        var rentedRuns = ArrayPool<TextRunSpan>.Shared.Rent(caretIndices.Length - 1);
        var runCount = 0;
        var boundaryIndex = 0;
        var accumulatedWidth = 0f;
        try
        {
            while (boundaryIndex < caretIndices.Length - 1)
            {
                var runStartBoundary = boundaryIndex;
                var runStart = caretIndices[runStartBoundary];
                var firstElement = SliceText(text, runStart, caretIndices[runStartBoundary + 1] - runStart);
                var typeface = fontCatalog.ResolveTypefaceForText(style.Font, firstElement);
                boundaryIndex++;

                while (boundaryIndex < caretIndices.Length - 1)
                {
                    var nextStart = caretIndices[boundaryIndex];
                    var nextElement = SliceText(text, nextStart, caretIndices[boundaryIndex + 1] - nextStart);
                    var nextTypeface = fontCatalog.ResolveTypefaceForText(style.Font, nextElement);
                    if (!ReferenceEquals(typeface, nextTypeface))
                        break;

                    boundaryIndex++;
                }

                using var font = SkiaFontSynthesis.CreateFont(typeface, style.Font);
                for (var offsetIndex = 1; offsetIndex <= boundaryIndex - runStartBoundary; offsetIndex++)
                {
                    var relativeLength = caretIndices[runStartBoundary + offsetIndex] - runStart;
                    caretOffsets[runStartBoundary + offsetIndex] = accumulatedWidth + font.MeasureText(text.AsSpan(runStart, relativeLength), paint);
                }

                var runEnd = caretIndices[boundaryIndex];
                var runWidth = caretOffsets[boundaryIndex] - accumulatedWidth;
                rentedRuns[runCount++] = new TextRunSpan(runStart, runEnd, SliceText(text, runStart, runEnd - runStart), runWidth, typeface);
                accumulatedWidth = caretOffsets[boundaryIndex];
            }

            var result = new TextRunSpan[runCount];
            Array.Copy(rentedRuns, result, runCount);
            return result;
        }
        finally
        {
            Array.Clear(rentedRuns, 0, runCount);
            ArrayPool<TextRunSpan>.Shared.Return(rentedRuns);
        }
    }

    private TextRunSpan[] BuildSingleTypefaceTextRuns(SceneTextStyle style, SKPaint paint, string text, int[] caretIndices, float[] caretOffsets)
    {
        var typeface = fontCatalog.ResolveTypefaceForText(style.Font, text);
        return BuildSingleTypefaceTextRuns(style, paint, text, caretIndices, caretOffsets, typeface);
    }

    private TextRunSpan[] BuildSingleTypefaceTextRuns(SceneTextStyle style, SKPaint paint, string text, int[] caretIndices, float[] caretOffsets, SKTypeface typeface)
    {
        using var font = SkiaFontSynthesis.CreateFont(typeface, style.Font);
        FillSingleTypefaceCaretOffsets(font, paint, text, caretIndices, caretOffsets);

        return [new TextRunSpan(0, text.Length, text, caretOffsets[^1], typeface)];
    }

    private void FillSingleTypefaceCaretOffsets(SKFont font, SKPaint paint, string text, int[] caretIndices, float[] caretOffsets)
    {
        if (caretIndices.Length == text.Length + 1)
        {
            var isDenseAsciiBoundaryArray = true;
            for (var index = 0; index < caretIndices.Length; index++)
            {
                if (caretIndices[index] != index)
                {
                    isDenseAsciiBoundaryArray = false;
                    break;
                }
            }

            if (isDenseAsciiBoundaryArray)
            {
                FillDenseAsciiCaretOffsets(font, paint, text, caretOffsets);
                return;
            }
        }

        for (var index = 1; index < caretIndices.Length; index++)
            caretOffsets[index] = font.MeasureText(text.AsSpan(0, caretIndices[index]), paint);
    }

    private void FillDenseAsciiCaretOffsets(SKFont font, SKPaint paint, string text, float[] caretOffsets)
    {
        caretOffsets[0] = 0;
        for (var index = 0; index < text.Length; index++)
            caretOffsets[index + 1] = caretOffsets[index] + font.MeasureText(text.AsSpan(index, 1), paint);
    }

    internal sealed record TextLayout(IReadOnlyList<TextLineSpan> Lines, float LineHeight)
    {
        public int TextLength => Lines.Count == 0 ? 0 : Lines[^1].EndIndex;
    }

    internal sealed record TextLineSpan(int StartIndex, int EndIndex, string Text, int[] CaretIndices, float[] CaretOffsets, IReadOnlyList<TextRunSpan> Runs, bool EndsWithNewline = false)
    {
        public float Width => CaretOffsets.Length == 0 ? 0 : CaretOffsets[^1];
    }

    internal sealed record TextRunSpan(int StartIndex, int EndIndex, string Text, float Width, SKTypeface Typeface);

    internal readonly record struct CaretPosition(int LineIndex, float X, float Y);

    internal readonly record struct SelectionRect(int LineIndex, float Left, float Right, float Top);

    internal struct SkiaFontLease : IDisposable
    {
        private readonly TextInputMetrics owner;
        private SkiaFontCacheEntry? entry;

        public SkiaFontLease(TextInputMetrics owner, SkiaFontCacheEntry entry)
        {
            this.owner = owner;
            this.entry = entry;
            Monitor.Enter(entry.Sync);
        }

        public SKFont Font => entry?.Font ?? throw new ObjectDisposedException(nameof(SkiaFontLease));

        public void Dispose()
        {
            var current = entry;
            if (current is null)
                return;

            entry = null;
            Monitor.Exit(current.Sync);
            owner.ReleaseFont(current);
        }
    }

    internal sealed class SkiaFontCacheEntry
    {
        public SkiaFontCacheEntry(SKFont font)
        {
            Font = font;
        }

        public object Sync { get; } = new();
        public SKFont Font { get; }
        public int RefCount { get; set; }
        public bool Evicted { get; set; }
    }

    private readonly record struct TextLayoutCacheKey(
        int FontVersion,
        string Text,
        int FontSizeQuarterPx,
        string FontFamily,
        int FontWeight,
        bool Italic,
        bool TextOverflowEllipsis,
        bool WrapText,
        int LineHeightQuarterPx,
        int MaxWidthQuarterPx);

    private readonly record struct SkiaFontCacheKey(
        int FontVersion,
        int FontSizeQuarterPx,
        string Identity,
        int Weight,
        bool Italic);

    private void TrimFontCache_NoLock()
    {
        while (fontCache.Count > fontCacheLimit && fontCacheOrder.Count > 0)
        {
            var oldestKey = fontCacheOrder.Dequeue();
            if (!fontCache.Remove(oldestKey, out var oldest))
                continue;

            oldest.Evicted = true;
            if (oldest.RefCount == 0)
                oldest.Font.Dispose();
        }
    }

    private void ReleaseFont(SkiaFontCacheEntry entry)
    {
        lock (fontCacheSync)
        {
            entry.RefCount--;
            if (entry.RefCount == 0 && entry.Evicted)
                entry.Font.Dispose();
        }
    }

    public void Dispose()
    {
        lock (fontCacheSync)
        {
            foreach (var entry in fontCache.Values)
            {
                entry.Evicted = true;
                if (entry.RefCount == 0)
                    entry.Font.Dispose();
            }

            fontCache.Clear();
            fontCacheOrder.Clear();
        }

        lock (layoutCacheSync)
        {
            layoutCache.Clear();
            layoutCacheOrder.Clear();
        }
    }

    private int[] GetTextElementBoundaries(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [0];

        if (IsAsciiWithoutCombiningMarks(text))
        {
            var asciiBoundaries = new int[text.Length + 1];
            for (var index = 0; index <= text.Length; index++)
                asciiBoundaries[index] = index;
            return asciiBoundaries;
        }

        var starts = StringInfo.ParseCombiningCharacters(text);
        var boundaries = new int[starts.Length + 1];
        var count = 0;
        if (starts.Length == 0 || starts[0] != 0)
            boundaries[count++] = 0;

        var previous = -1;
        for (var index = 0; index < starts.Length; index++)
        {
            var current = starts[index];
            if (current == previous)
                continue;

            boundaries[count++] = current;
            previous = current;
        }

        if (boundaries[count - 1] != text.Length)
            boundaries[count++] = text.Length;

        if (count == boundaries.Length)
            return boundaries;

        var compacted = new int[count];
        Array.Copy(boundaries, compacted, count);
        return compacted;
    }

    private bool IsAsciiWithoutCombiningMarks(string text)
    {
        foreach (var value in text)
        {
            if (value >= 0x80)
                return false;
        }

        return true;
    }

    private int GetCaretBoundaryIndex(TextLineSpan line, int relativeIndex)
    {
        for (var index = line.CaretIndices.Length - 1; index >= 0; index--)
        {
            if (line.CaretIndices[index] <= relativeIndex)
                return index;
        }

        return 0;
    }

    private void AppendLineSpans(
        List<TextLineSpan> spans,
        SceneTextStyle style,
        SKPaint paint,
        string lineText,
        int absoluteStart,
        int absoluteEnd,
        float lineHeight,
        float maxWidth,
        bool endsWithNewline)
    {
        if (!style.WrapText || !float.IsFinite(maxWidth) || maxWidth <= 0)
        {
            spans.Add(CreateLineSpan(style, paint, lineText, absoluteStart, absoluteEnd, endsWithNewline));
            return;
        }

        if (IsAsciiWithoutCombiningMarks(lineText))
        {
            using var asciiFont = CreateSingleTypefaceFont(style, lineText);
            AppendDenseAsciiLineSpans(spans, lineText, absoluteStart, maxWidth, endsWithNewline, asciiFont, paint);
            return;
        }

        var boundaries = GetTextElementBoundaries(lineText);
        if (boundaries.Length <= 1)
        {
            spans.Add(CreateLineSpan(style, paint, lineText, absoluteStart, absoluteEnd, endsWithNewline));
            return;
        }

        var lineStartBoundary = 0;
        var boundaryIndex = 0;
        var currentWidth = 0f;
        var lastWrapBoundary = -1;
        while (boundaryIndex < boundaries.Length - 1)
        {
            var segmentStart = boundaries[boundaryIndex];
            var segmentEnd = boundaries[boundaryIndex + 1];
            var segmentWidth = MeasureTextElement(style, paint, lineText, segmentStart, segmentEnd - segmentStart);
            if (currentWidth > 0 && currentWidth + segmentWidth > maxWidth)
            {
                var breakBoundary = lastWrapBoundary > lineStartBoundary ? lastWrapBoundary : boundaryIndex;
                if (breakBoundary <= lineStartBoundary)
                    breakBoundary = boundaryIndex + 1;
                if (breakBoundary < boundaries.Length - 1 &&
                    IsLineStartProhibited(text: lineText, index: boundaries[breakBoundary]) &&
                    breakBoundary + 1 <= boundaries.Length - 1)
                {
                    breakBoundary++;
                }

                spans.Add(CreateWrappedLineSpan(style, paint, lineText, boundaries, lineStartBoundary, breakBoundary, absoluteStart));
                lineStartBoundary = breakBoundary;
                boundaryIndex = breakBoundary;
                currentWidth = 0;
                lastWrapBoundary = -1;
                continue;
            }

            currentWidth += segmentWidth;
            if (IsWrapOpportunity(lineText, segmentStart, segmentEnd) ||
                IsCjkWrapOpportunity(lineText, segmentEnd))
                lastWrapBoundary = boundaryIndex + 1;
            boundaryIndex++;
        }

        spans.Add(CreateWrappedLineSpan(style, paint, lineText, boundaries, lineStartBoundary, boundaries.Length - 1, absoluteStart, endsWithNewline));
    }

    private void AppendDenseAsciiLineSpans(
        List<TextLineSpan> spans,
        string lineText,
        int absoluteStart,
        float maxWidth,
        bool endsWithNewline,
        SKFont font,
        SKPaint paint)
    {
        var fullCaretOffsets = new float[lineText.Length + 1];
        FillDenseAsciiCaretOffsets(font, paint, lineText, fullCaretOffsets);

        var lineStart = 0;
        var index = 0;
        var lastWrapBoundary = -1;
        while (index < lineText.Length)
        {
            var segmentWidth = fullCaretOffsets[index + 1] - fullCaretOffsets[index];
            var currentWidth = fullCaretOffsets[index] - fullCaretOffsets[lineStart];
            if (currentWidth > 0 && currentWidth + segmentWidth > maxWidth)
            {
                var breakIndex = lastWrapBoundary > lineStart ? lastWrapBoundary : index;
                if (breakIndex <= lineStart)
                    breakIndex = index + 1;

                spans.Add(CreateDenseAsciiLineSpan(lineText, lineStart, breakIndex, absoluteStart, fullCaretOffsets, font.Typeface, endsWithNewline: false));
                lineStart = breakIndex;
                index = breakIndex;
                lastWrapBoundary = -1;
                continue;
            }

            if (char.IsWhiteSpace(lineText[index]))
                lastWrapBoundary = index + 1;
            index++;
        }

        spans.Add(CreateDenseAsciiLineSpan(lineText, lineStart, lineText.Length, absoluteStart, fullCaretOffsets, font.Typeface, endsWithNewline));
    }

    private TextLineSpan CreateDenseAsciiLineSpan(
        string sourceText,
        int start,
        int end,
        int absoluteStart,
        float[] fullCaretOffsets,
        SKTypeface typeface,
        bool endsWithNewline)
    {
        var length = end - start;
        var lineText = SliceText(sourceText, start, length);
        var caretIndices = new int[length + 1];
        var caretOffsets = new float[length + 1];
        var baseOffset = fullCaretOffsets[start];
        for (var index = 0; index <= length; index++)
        {
            caretIndices[index] = index;
            caretOffsets[index] = fullCaretOffsets[start + index] - baseOffset;
        }

        var runs = length == 0
            ? Array.Empty<TextRunSpan>()
            : [new TextRunSpan(0, length, lineText, caretOffsets[^1], typeface)];

        return new TextLineSpan(
            absoluteStart + start,
            absoluteStart + end,
            lineText,
            caretIndices,
            caretOffsets,
            runs,
            endsWithNewline);
    }

    private TextLineSpan CreateWrappedLineSpan(
        SceneTextStyle style,
        SKPaint paint,
        string sourceText,
        int[] boundaries,
        int startBoundaryIndex,
        int endBoundaryIndex,
        int absoluteStart,
        bool endsWithNewline = false)
    {
        var relativeStart = boundaries[startBoundaryIndex];
        var relativeEnd = boundaries[endBoundaryIndex];
        return CreateLineSpan(
            style,
            paint,
            SliceText(sourceText, relativeStart, relativeEnd - relativeStart),
            absoluteStart + relativeStart,
            absoluteStart + relativeEnd,
            endsWithNewline);
    }

    private TextLineSpan CreateLineSpan(
        SceneTextStyle style,
        SKPaint paint,
        string lineText,
        int absoluteStart,
        int absoluteEnd,
        bool endsWithNewline)
    {
        var caretIndices = GetTextElementBoundaries(lineText);
        var caretOffsets = new float[caretIndices.Length];
        caretOffsets[0] = 0;
        var runs = BuildTextRuns(style, paint, lineText, caretIndices, caretOffsets);
        return new TextLineSpan(absoluteStart, absoluteEnd, lineText, caretIndices, caretOffsets, runs, endsWithNewline);
    }

    private float MeasureTextElement(SceneTextStyle style, SKPaint paint, string text, int start, int length)
    {
        if (length <= 0)
            return 0;

        var textElement = SliceText(text, start, length);
        var typeface = fontCatalog.ResolveTypefaceForText(style.Font, textElement);
        using var font = SkiaFontSynthesis.CreateFont(typeface, style.Font);
        return font.MeasureText(text.AsSpan(start, length), paint);
    }

    private SKFont CreateSingleTypefaceFont(SceneTextStyle style, string text)
        => SkiaFontSynthesis.CreateFont(fontCatalog.ResolveTypefaceForText(style.Font, text), style.Font);

    private bool IsWrapOpportunity(string text, int start, int end)
    {
        return end > start && char.IsWhiteSpace(text[end - 1]);
    }

    private static bool IsCjkWrapOpportunity(string text, int nextIndex)
    {
        if (nextIndex <= 0 || nextIndex >= text.Length)
            return false;

        return IsCjkCharacter(text[nextIndex - 1]) &&
               !IsLineStartProhibited(text, nextIndex);
    }

    private static bool IsLineStartProhibited(string text, int index)
        => index >= 0 &&
           index < text.Length &&
           IsLineStartProhibitedJapanesePunctuation(text[index]);

    private static bool IsCjkCharacter(char ch)
        => ch is >= '\u3040' and <= '\u30ff' ||
           ch is >= '\u3400' and <= '\u9fff' ||
           ch is >= '\uf900' and <= '\ufaff';

    private static bool IsLineStartProhibitedJapanesePunctuation(char ch)
        => ch is '。' or '、' or '，' or '．' or '！' or '？' or '）' or ')' or '］' or ']' or '｝' or '}' or '」' or '』' or '】' or '〉' or '》' or 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ' or 'っ' or 'ゃ' or 'ゅ' or 'ょ' or 'ァ' or 'ィ' or 'ゥ' or 'ェ' or 'ォ' or 'ッ' or 'ャ' or 'ュ' or 'ョ' or 'ー';

    private string SliceText(string text, int start, int length)
    {
        if (start == 0 && length == text.Length)
            return text;

        return text.Substring(start, length);
    }
}
