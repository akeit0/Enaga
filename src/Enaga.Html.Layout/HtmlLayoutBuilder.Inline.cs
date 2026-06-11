using Enaga.Layout;
using Enaga.Scene;

namespace Enaga.Html;

internal sealed partial class HtmlLayoutBuilder
{
    private bool TryLayoutInlineFormattingContext(
        HtmlSceneNodeId parentId,
        HtmlComputedStyle parentStyle,
        HtmlSceneNode[] children,
        float parentLeft,
        float parentTop,
        float parentWidth,
        float parentHeight,
        float viewportScale
    )
    {
        if (!IsInlineFormattingContext(parentStyle, children))
            return false;

        var contentWidth = Math.Max(
            0,
            parentWidth - parentStyle.PaddingLeft - parentStyle.PaddingRight
        );
        var contentHeight = Math.Max(
            0,
            parentHeight - parentStyle.PaddingTop - parentStyle.PaddingBottom
        );
        var scratchMark = scratch.Mark();
        try
        {
            var lineLayout = CreateInlineLineLayout(
                parentStyle,
                children,
                contentWidth,
                contentHeight
            );
            var childIds = new HtmlSceneNodeId[lineLayout.Items.Length];
            for (var index = 0; index < lineLayout.Items.Length; index++)
            {
                ref readonly var item = ref lineLayout.Items[index];
                var child = item.Node;
                var frame =
                    lineLayout.Frames[index]
                    ?? new LayoutFrameData(parentStyle.PaddingLeft, parentStyle.PaddingTop, 0, 0);
                if (
                    item.TextFragment is null
                    && TryResolveInlineTextFragmentFrame(child, frame, out var textFrame)
                )
                    frame = textFrame;
                var absLeft = parentLeft + frame.Left;
                var absTop = parentTop + frame.Top;
                var fragmentIndex = item.TextFragment is null ? -1 : item.FragmentIndex;
                AddPlacedNode(
                    child,
                    parentId,
                    absLeft,
                    absTop,
                    frame.Width,
                    frame.Height,
                    fragmentIndex,
                    item.TextFragment
                );
                childIds[index] =
                    fragmentIndex >= 0
                        ? HtmlSceneNodeId.Fragment(child.Id, fragmentIndex)
                        : child.Id;

                if (item.TextFragment is null && child.Children.Length > 0)
                    LayoutChildren(
                        child.Id,
                        child.Style,
                        child.Children,
                        absLeft,
                        absTop,
                        frame.Width,
                        frame.Height,
                        viewportScale
                    );
                else
                    AddChildRelation(childIds[index], []);
            }

            AddChildRelation(parentId, childIds);
        }
        finally
        {
            scratch.Rewind(scratchMark);
        }
        return true;
    }

    private bool TryResolveInlineTextFragmentFrame(
        HtmlSceneNode child,
        LayoutFrameData frame,
        out LayoutFrameData resolvedFrame
    )
    {
        resolvedFrame = frame;
        if (
            child.NodeKind != SceneNodeKind.Text
            || string.IsNullOrEmpty(child.TextContent)
            || child.Style.WrapText && !child.Style.PreferIntrinsicWidth
        )
        {
            return false;
        }

        var textStyle = textStyleCache.GetInlineMeasureStyle(child.Style);
        var measured = MeasureInlineText(child.TextContent, textStyle, child.Style.LineHeight);
        if (child.Style.Underline && frame.Width >= measured.Width)
            return false;
        if (measured.Width <= 0 || MathF.Abs(measured.Width - frame.Width) < 0.5f)
            return false;

        resolvedFrame = new LayoutFrameData(frame.Left, frame.Top, measured.Width, frame.Height);
        return true;
    }

    private InlineLineLayout CreateInlineLineLayout(
        HtmlComputedStyle parentStyle,
        HtmlSceneNode[] children,
        float contentWidth,
        float contentHeight
    )
    {
        var itemCount = CountInlineLayoutItems(children);
        var items = scratch.AllocateInlineItems(itemCount);
        CreateInlineLayoutItems(children, contentWidth, contentHeight, items);

        var frames = scratch.AllocateFrames(itemCount);
        var allowWrap = parentStyle.FlexWrap == FlexWrap.Wrap && contentWidth > 0;
        var lineStart = 0;
        var lineWidth = 0f;
        var lineAscent = 0f;
        var lineDescent = 0f;
        var lineTop = parentStyle.PaddingTop;

        for (var index = 0; index < itemCount; index++)
        {
            ref readonly var item = ref items[index];
            if (item.ForcedLineBreak)
            {
                if (index > lineStart)
                {
                    CommitInlineLine(
                        parentStyle,
                        items,
                        frames,
                        lineStart,
                        index,
                        lineTop,
                        lineAscent,
                        lineDescent,
                        contentWidth
                    );
                    lineTop += Math.Max(1, lineAscent + lineDescent) + parentStyle.Gap;
                }

                frames[index] = new LayoutFrameData(
                    parentStyle.PaddingLeft,
                    lineTop,
                    contentWidth,
                    0
                );
                lineTop += parentStyle.Gap;
                lineStart = index + 1;
                lineWidth = 0;
                lineAscent = 0;
                lineDescent = 0;
                continue;
            }

            var gap = index > lineStart ? ResolveInlineGap(parentStyle, items[index - 1], item) : 0;
            var itemOuterWidth = item.MarginLeft + item.Width + item.MarginRight;
            if (
                allowWrap
                && index > lineStart
                && lineWidth + gap + itemOuterWidth > contentWidth + 0.001f
            )
            {
                CommitInlineLine(
                    parentStyle,
                    items,
                    frames,
                    lineStart,
                    index,
                    lineTop,
                    lineAscent,
                    lineDescent,
                    contentWidth
                );
                lineTop += Math.Max(1, lineAscent + lineDescent) + parentStyle.Gap;
                lineStart = index;
                lineWidth = 0;
                lineAscent = 0;
                lineDescent = 0;
                gap = 0;
            }

            lineWidth += gap + itemOuterWidth;
            lineAscent = Math.Max(lineAscent, item.Ascent + item.MarginTop);
            lineDescent = Math.Max(lineDescent, item.Descent + item.MarginBottom);
        }

        if (lineStart < itemCount)
            CommitInlineLine(
                parentStyle,
                items,
                frames,
                lineStart,
                itemCount,
                lineTop,
                lineAscent,
                lineDescent,
                contentWidth
            );

        var contentRight = parentStyle.PaddingLeft;
        var contentBottom = parentStyle.PaddingTop;
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames[index] is not { } frame)
                continue;

            contentRight = Math.Max(
                contentRight,
                frame.Left + frame.Width + items[index].MarginRight
            );
            contentBottom = Math.Max(
                contentBottom,
                frame.Top + frame.Height + items[index].MarginBottom
            );
        }

        return new InlineLineLayout(
            items,
            frames,
            contentRight + parentStyle.PaddingRight,
            contentBottom + parentStyle.PaddingBottom
        );
    }

    private void CreateInlineLayoutItems(
        HtmlSceneNode[] children,
        float contentWidth,
        float contentHeight,
        Span<InlineLayoutItem> items
    )
    {
        var itemIndex = 0;
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            if (ShouldFragmentInlineText(child))
            {
                AddInlineTextFragments(items, ref itemIndex, child, contentWidth);
                continue;
            }

            items[itemIndex++] = MeasureInlineLayoutItem(child, contentWidth, contentHeight);
        }

        if (itemIndex != items.Length)
            throw new InvalidOperationException(
                "Inline layout item count changed while building the line layout."
            );
    }

    private static int CountInlineLayoutItems(HtmlSceneNode[] children)
    {
        var count = 0;
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            count +=
                ShouldFragmentInlineText(child) && child.TextContent is { } text
                    ? CountInlineTextFragments(text)
                    : 1;
        }

        return count;
    }

    private void AddInlineTextFragments(
        Span<InlineLayoutItem> items,
        ref int itemIndex,
        HtmlSceneNode child,
        float contentWidth
    )
    {
        var text = child.TextContent;
        if (string.IsNullOrEmpty(text))
            return;

        var style = child.Style;
        var textStyle = textStyleCache.GetInlineMeasureStyle(style);
        var font = textStyle.Font;
        var lineHeight = ResolveNormalLineHeight(font, style.LineHeight);
        var ascent = Math.Min(lineHeight, font.Size);
        var start = 0;
        var fragmentIndex = 0;
        while (start < text.Length)
        {
            var end = FindInlineTextBreakEnd(text, start);
            var fragment = text[start..end];
            var measured = MeasureInlineText(fragment, textStyle, style.LineHeight);
            items[itemIndex++] = new InlineLayoutItem(
                child,
                measured.Width,
                Math.Max(measured.Height, lineHeight),
                ascent,
                Math.Max(0, lineHeight - ascent),
                0,
                0,
                0,
                0,
                SuppressLeadingInlineGap: fragmentIndex > 0,
                SuppressTrailingInlineGap: end < text.Length,
                fragment,
                fragmentIndex
            );
            start = end;
            fragmentIndex++;
        }
    }

    private static int CountInlineTextFragments(string text)
    {
        if (text.Length == 0)
            return 0;

        var count = 0;
        var start = 0;
        while (start < text.Length)
        {
            start = FindInlineTextBreakEnd(text, start);
            count++;
        }

        return count;
    }

    private InlineLayoutItem MeasureInlineLayoutItem(
        HtmlSceneNode child,
        float contentWidth,
        float contentHeight
    )
    {
        if (IsInlineBreakNode(child))
            return new InlineLayoutItem(
                child,
                contentWidth,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                ForcedLineBreak: true
            );

        var request = CreateLayoutRequest(
            child,
            contentWidth,
            contentHeight,
            parentIsFlexContainer: true,
            FlexDirection.Row,
            CrossAlignment.Start,
            allowFlexShrink: false
        );

        var width = LayoutValue.IsSet(request.Width) ? request.Width : 0;
        var height = LayoutValue.IsSet(request.Height) ? request.Height : 0;
        if (child.Children.Length > 0 && height <= 0)
            height = MeasureNodeLayoutHeight(
                child,
                Math.Max(width, contentWidth),
                contentHeight,
                parentIsFlexContainer: true,
                parentFlexDirection: FlexDirection.Row,
                parentAlignItems: CrossAlignment.Start
            );

        if (child.NodeKind == SceneNodeKind.Text)
        {
            var textStyle = textStyleCache.GetInlineMeasureStyle(child.Style);
            var measured = MeasureInlineText(
                child.TextContent ?? string.Empty,
                textStyle,
                child.Style.LineHeight
            );
            width = measured.Width;
            height = measured.Height;
            var font = textStyle.Font;
            var lineHeight = ResolveNormalLineHeight(font, child.Style.LineHeight);
            height = Math.Max(height, lineHeight);
            var ascent = Math.Min(height, font.Size);
            return new InlineLayoutItem(
                child,
                width,
                height,
                ascent,
                Math.Max(0, height - ascent),
                request.MarginLeft,
                request.MarginTop,
                request.MarginRight,
                request.MarginBottom
            );
        }

        if (child.NodeKind == SceneNodeKind.Image)
        {
            if (width <= 0 && LayoutValue.IsSet(child.Style.IntrinsicImageWidth))
                width = child.Style.IntrinsicImageWidth;
            if (height <= 0 && LayoutValue.IsSet(child.Style.IntrinsicImageHeight))
                height = child.Style.IntrinsicImageHeight;
            return new InlineLayoutItem(
                child,
                width,
                height,
                height,
                0,
                request.MarginLeft,
                request.MarginTop,
                request.MarginRight,
                request.MarginBottom
            );
        }

        if (child.NodeKind == SceneNodeKind.TextInput)
            return new InlineLayoutItem(
                child,
                width,
                height,
                height,
                0,
                request.MarginLeft,
                request.MarginTop,
                request.MarginRight,
                request.MarginBottom
            );

        if (
            child.ControlKind
            is SceneControlKind.Button
                or SceneControlKind.Select
                or SceneControlKind.TextInput
                or SceneControlKind.TextArea
        )
            return new InlineLayoutItem(
                child,
                width,
                height,
                height,
                0,
                request.MarginLeft,
                request.MarginTop,
                request.MarginRight,
                request.MarginBottom
            );

        var inlineBoxAscent = Math.Min(
            height,
            child.Style.FontSize > 0 ? child.Style.FontSize : 16
        );
        return new InlineLayoutItem(
            child,
            width,
            height,
            inlineBoxAscent,
            Math.Max(0, height - inlineBoxAscent),
            request.MarginLeft,
            request.MarginTop,
            request.MarginRight,
            request.MarginBottom
        );
    }

    private static void CommitInlineLine(
        HtmlComputedStyle parentStyle,
        ReadOnlySpan<InlineLayoutItem> items,
        Span<LayoutFrameData?> frames,
        int start,
        int end,
        float lineTop,
        float lineAscent,
        float lineDescent,
        float contentWidth
    )
    {
        var lineWidth = 0f;
        for (var index = start; index < end; index++)
        {
            if (index > start)
                lineWidth += ResolveInlineGap(parentStyle, in items[index - 1], in items[index]);
            lineWidth += items[index].MarginLeft + items[index].Width + items[index].MarginRight;
        }

        var cursor =
            parentStyle.PaddingLeft
            + parentStyle.JustifyContent switch
            {
                MainAxisJustification.Center => Math.Max(0, (contentWidth - lineWidth) * 0.5f),
                MainAxisJustification.End => Math.Max(0, contentWidth - lineWidth),
                _ => 0,
            };

        var baseline = lineTop + lineAscent;
        for (var index = start; index < end; index++)
        {
            ref readonly var item = ref items[index];
            if (index > start)
            {
                var gap = ResolveInlineGap(parentStyle, in items[index - 1], in item);
                if (
                    gap > 0
                    && ShouldBridgeUnderlineGap(in items[index - 1], in item)
                    && frames[index - 1] is { } previousFrame
                )
                {
                    frames[index - 1] = new LayoutFrameData(
                        previousFrame.Left,
                        previousFrame.Top,
                        previousFrame.Width + gap,
                        previousFrame.Height
                    );
                }

                cursor += gap;
            }

            cursor += item.MarginLeft;
            var top = baseline - item.Ascent + item.MarginTop;
            frames[index] = new LayoutFrameData(cursor, top, item.Width, item.Height);
            cursor += item.Width + item.MarginRight;
        }
    }

    private static bool ShouldBridgeUnderlineGap(
        in InlineLayoutItem previous,
        in InlineLayoutItem current
    ) =>
        previous.Node.NodeKind == SceneNodeKind.Text
        && current.Node.NodeKind == SceneNodeKind.Text
        && previous.Node.Style.Underline
        && current.Node.Style.Underline
        && previous.TextFragment is null
        && current.TextFragment is null;

    private static bool IsInlineFormattingContext(
        HtmlComputedStyle parentStyle,
        HtmlSceneNode[] children
    )
    {
        if (children.Length == 0)
            return false;

        if (
            parentStyle.Display == HtmlDisplay.Flex
            && FlexLayout.ResolveAxis(parentStyle.FlexDirection) != LayoutAxis.Row
        )
        {
            return false;
        }

        if (
            parentStyle.Display
            is not HtmlDisplay.Block
                and not HtmlDisplay.Flex
                and not HtmlDisplay.Inline
                and not HtmlDisplay.InlineBlock
        )
        {
            return false;
        }

        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            if (child.NodeKind is SceneNodeKind.Text or SceneNodeKind.Image)
                continue;
            if (IsInlineBreakNode(child))
                continue;
            if (child.Style.Display is HtmlDisplay.Inline or HtmlDisplay.InlineBlock)
                continue;
            return false;
        }

        return true;
    }

    private static float ResolveInlineGap(
        HtmlComputedStyle parentStyle,
        in InlineLayoutItem previous,
        in InlineLayoutItem current
    ) =>
        previous.SuppressTrailingInlineGap || current.SuppressLeadingInlineGap
            ? 0
            : parentStyle.Gap;

    private static bool ShouldFragmentInlineText(HtmlSceneNode node) =>
        node.NodeKind == SceneNodeKind.Text
        && node.TextContent is { Length: >= 16 } text
        && ContainsCjkWithoutWhitespace(text);

    private static bool IsInlineBreakNode(HtmlSceneNode node) =>
        node.NodeKind == SceneNodeKind.View
        && node.Children.Length == 0
        && node.Style.Display == HtmlDisplay.Block
        && node.Style.Height == 0
        && node.Style.IsWidthPercent
        && MathF.Abs(node.Style.Width - 100) < 0.001f;

    private static bool ContainsCjkWithoutWhitespace(string text)
    {
        var hasCjk = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (char.IsWhiteSpace(ch))
                return false;
            if (IsCjkCharacter(ch))
                hasCjk = true;
        }

        return hasCjk;
    }

    private static int FindInlineTextBreakEnd(string text, int start)
    {
        var end = Math.Min(text.Length, start + 4);
        while (end < text.Length && IsLineStartProhibitedJapanesePunctuation(text[end]))
            end++;
        return end;
    }

    private static bool IsCjkCharacter(char ch) =>
        ch is >= '\u3040' and <= '\u30ff'
        || ch is >= '\u3400' and <= '\u9fff'
        || ch is >= '\uf900' and <= '\ufaff';

    private static bool IsLineStartProhibitedJapanesePunctuation(char ch) =>
        ch
            is '。'
                or '、'
                or '，'
                or '．'
                or '！'
                or '？'
                or '）'
                or ')'
                or '］'
                or ']'
                or '｝'
                or '}'
                or '」'
                or '』'
                or '】'
                or '〉'
                or '》'
                or 'ぁ'
                or 'ぃ'
                or 'ぅ'
                or 'ぇ'
                or 'ぉ'
                or 'っ'
                or 'ゃ'
                or 'ゅ'
                or 'ょ'
                or 'ァ'
                or 'ィ'
                or 'ゥ'
                or 'ェ'
                or 'ォ'
                or 'ッ'
                or 'ャ'
                or 'ュ'
                or 'ョ'
                or 'ー';

    private readonly record struct InlineLayoutItem(
        HtmlSceneNode Node,
        float Width,
        float Height,
        float Ascent,
        float Descent,
        float MarginLeft,
        float MarginTop,
        float MarginRight,
        float MarginBottom,
        bool SuppressLeadingInlineGap = false,
        bool SuppressTrailingInlineGap = false,
        string? TextFragment = null,
        int FragmentIndex = 0,
        bool ForcedLineBreak = false
    );

    private readonly ref struct InlineLineLayout
    {
        public InlineLineLayout(
            ReadOnlySpan<InlineLayoutItem> items,
            Span<LayoutFrameData?> frames,
            float width,
            float height
        )
        {
            Items = items;
            Frames = frames;
            Width = width;
            Height = height;
        }

        public ReadOnlySpan<InlineLayoutItem> Items { get; }

        public Span<LayoutFrameData?> Frames { get; }

        public float Width { get; }

        public float Height { get; }
    }
}
