using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Layout;

public sealed class LayoutCalculator
{
    private StackLayoutEntry[] entryScratch = [];
    private readonly IRuntimeTextServices textServices;

    public LayoutCalculator(IRuntimeTextServices textServices)
    {
        this.textServices = textServices;
    }

    public LayoutOutput ComputeFlexLayout(
        in LayoutInput input,
        in LayoutContainerStyle style,
        ReadOnlySpan<LayoutChildRequest> children,
        Span<LayoutFrameData?> frames)
    {
        if (input.PerformsLayout && frames.Length < children.Length)
            throw new ArgumentException("Frame buffer is smaller than child request count.", nameof(frames));

        var width = ResolveInputWidth(input);
        var height = ResolveInputHeight(input);
        if (!input.PerformsLayout)
        {
            var measured = MeasureFlexIntrinsic(
                style.FlexDirection,
                style.Direction,
                style.FlexWrap,
                width,
                height,
                style.RowGap,
                style.ColumnGap,
                style.AlignItems,
                style.Padding.Left,
                style.Padding.Top,
                style.Padding.Right,
                style.Padding.Bottom,
                children,
                textServices);
            var axis = FlexLayout.ResolveAxis(style.FlexDirection);
            var measuredContentWidth = axis == LayoutAxis.Row ? measured.MainSize : measured.CrossSize;
            var measuredContentHeight = axis == LayoutAxis.Row ? measured.CrossSize : measured.MainSize;
            var resolvedWidth = input.KnownDimensions.Width ?? measuredContentWidth + style.Padding.Horizontal;
            var resolvedHeight = input.KnownDimensions.Height ?? measuredContentHeight + style.Padding.Vertical;
            return new LayoutOutput(
                new LayoutSize(resolvedWidth, resolvedHeight),
                new LayoutSize(measuredContentWidth, measuredContentHeight),
                new LayoutRect(0, 0, resolvedWidth, resolvedHeight));
        }

        for (var index = 0; index < children.Length; index++)
            frames[index] = null;

        if (children.Length > 0)
        {
            CalculateCore(
                style.FlexDirection,
                style.Direction,
                style.FlexWrap,
                width,
                height,
                style.RowGap,
                style.ColumnGap,
                style.AlignItems,
                style.JustifyContent,
                style.Padding.Left,
                style.Padding.Top,
                style.Padding.Right,
                style.Padding.Bottom,
                children,
                textServices,
                GetEntryScratch(children.Length),
                frames);
        }

        var contentRight = 0f;
        var contentBottom = 0f;
        for (var index = 0; index < children.Length; index++)
        {
            if (frames[index] is not { } frame)
                continue;

            contentRight = Math.Max(contentRight, frame.Left + frame.Width);
            contentBottom = Math.Max(contentBottom, frame.Top + frame.Height);
        }

        var contentWidth = Math.Max(0, contentRight - style.Padding.Left);
        var contentHeight = Math.Max(0, contentBottom - style.Padding.Top);
        return new LayoutOutput(
            new LayoutSize(width, height),
            new LayoutSize(contentWidth, contentHeight),
            new LayoutRect(0, 0, Math.Max(width, contentRight + style.Padding.Right), Math.Max(height, contentBottom + style.Padding.Bottom)));
    }

    private static float ResolveInputWidth(in LayoutInput input)
        => Math.Max(0, input.KnownDimensions.Width
            ?? input.AvailableSpace.Width.Resolve(input.ParentSize.Width ?? 0));

    private static float ResolveInputHeight(in LayoutInput input)
        => Math.Max(0, input.KnownDimensions.Height
            ?? input.AvailableSpace.Height.Resolve(input.ParentSize.Height ?? 0));

    private static LayoutMeasurement MeasureFlexIntrinsic(
        FlexDirection flexDirection,
        LayoutDirection layoutDirection,
        FlexWrap flexWrap,
        float width,
        float height,
        float rowGap,
        float columnGap,
        CrossAlignment alignItems,
        float paddingLeft,
        float paddingTop,
        float paddingRight,
        float paddingBottom,
        ReadOnlySpan<LayoutChildRequest> children,
        IRuntimeTextServices textServices)
    {
        var axis = FlexLayout.ResolveAxis(flexDirection);
        var isColumn = axis == LayoutAxis.Column;
        var isRow = axis == LayoutAxis.Row;
        var mainGap = isColumn ? rowGap : columnGap;
        var crossGap = isColumn ? columnGap : rowGap;
        var innerWidth = Math.Max(0, width - paddingLeft - paddingRight);
        var innerHeight = Math.Max(0, height - paddingTop - paddingBottom);
        var availableMainSize = isColumn ? innerHeight : innerWidth;
        var availableCrossSize = isColumn ? innerWidth : innerHeight;
        var wraps = flexWrap == FlexWrap.Wrap && availableMainSize > 0;
        Span<StackLayoutEntry> entries = children.Length <= 256
            ? stackalloc StackLayoutEntry[children.Length]
            : new StackLayoutEntry[children.Length];
        Span<int> lineStarts = children.Length <= 256
            ? stackalloc int[children.Length]
            : new int[children.Length];
        Span<int> lineEnds = children.Length <= 256
            ? stackalloc int[children.Length]
            : new int[children.Length];
        Span<float> resolvedWidths = children.Length <= 256
            ? stackalloc float[children.Length]
            : new float[children.Length];
        Span<float> resolvedHeights = children.Length <= 256
            ? stackalloc float[children.Length]
            : new float[children.Length];
        Span<float> occupiedMainSizes = children.Length <= 256
            ? stackalloc float[children.Length]
            : new float[children.Length];
        Span<float> occupiedCrossSizes = children.Length <= 256
            ? stackalloc float[children.Length]
            : new float[children.Length];

        for (var index = 0; index < children.Length; index++)
            entries[index] = MeasureChild(children[index], textServices, isColumn, isRow, innerWidth, innerHeight, alignItems, applyCrossStretch: !wraps);

        var lineCount = BuildLines(children, entries, isColumn, availableMainSize, mainGap, wraps, lineStarts, lineEnds);
        if (lineCount == 0)
            return new LayoutMeasurement(0, 0);

        var mainSize = 0f;
        var crossSize = 0f;
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            var lineMetrics = ResolveLineMetrics(
                children,
                entries,
                lineStarts[lineIndex],
                lineEnds[lineIndex],
                isColumn,
                availableMainSize,
                availableCrossSize,
                wraps,
                mainGap,
                textServices,
                resolvedWidths,
                resolvedHeights,
                occupiedMainSizes,
                occupiedCrossSizes);
            mainSize = wraps
                ? Math.Max(mainSize, lineMetrics.OccupiedMainSize)
                : lineMetrics.OccupiedMainSize;
            crossSize += lineMetrics.CrossSize;
        }

        if (wraps && lineCount > 1)
            crossSize += crossGap * (lineCount - 1);

        return new LayoutMeasurement(mainSize, crossSize);
    }

    private static void CalculateCore(
        FlexDirection flexDirection,
        LayoutDirection layoutDirection,
        FlexWrap flexWrap,
        float width,
        float height,
        float rowGap,
        float columnGap,
        CrossAlignment alignItems,
        MainAxisJustification justifyContent,
        float paddingLeft,
        float paddingTop,
        float paddingRight,
        float paddingBottom,
        ReadOnlySpan<LayoutChildRequest> children,
        IRuntimeTextServices textServices,
        Span<StackLayoutEntry> entries,
        Span<LayoutFrameData?> frames)
    {
        var axis = FlexLayout.ResolveAxis(flexDirection);
        var isColumn = axis == LayoutAxis.Column;
        var isRow = axis == LayoutAxis.Row;
        var isMainAxisReversed = FlexLayout.IsMainAxisReversed(flexDirection, layoutDirection);
        var isCrossAxisReversed = FlexLayout.IsCrossAxisReversed(flexDirection, layoutDirection);
        var mainGap = isColumn ? rowGap : columnGap;
        var crossGap = isColumn ? columnGap : rowGap;
        var innerWidth = Math.Max(0, width - paddingLeft - paddingRight);
        var innerHeight = Math.Max(0, height - paddingTop - paddingBottom);
        var availableMainSize = isColumn ? innerHeight : innerWidth;
        var availableCrossSize = isColumn ? innerWidth : innerHeight;
        var wraps = flexWrap == FlexWrap.Wrap && availableMainSize > 0;
        Span<int> lineStarts = children.Length <= 256
            ? stackalloc int[children.Length]
            : new int[children.Length];
        Span<int> lineEnds = children.Length <= 256
            ? stackalloc int[children.Length]
            : new int[children.Length];
        Span<float> resolvedWidths = children.Length <= 256
            ? stackalloc float[children.Length]
            : new float[children.Length];
        Span<float> resolvedHeights = children.Length <= 256
            ? stackalloc float[children.Length]
            : new float[children.Length];
        Span<float> occupiedMainSizes = children.Length <= 256
            ? stackalloc float[children.Length]
            : new float[children.Length];
        Span<float> occupiedCrossSizes = children.Length <= 256
            ? stackalloc float[children.Length]
            : new float[children.Length];

        for (var index = 0; index < children.Length; index++)
        {
            entries[index] = MeasureChild(children[index], textServices, isColumn, isRow, innerWidth, innerHeight, alignItems, applyCrossStretch: !wraps);
            frames[index] = null;
        }

        var lineCount = BuildLines(children, entries, isColumn, availableMainSize, mainGap, wraps, lineStarts, lineEnds);
        if (lineCount == 0)
            return;

        var totalCrossSize = 0f;
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            var lineMetrics = ResolveLineMetrics(
                children,
                entries,
                lineStarts[lineIndex],
                lineEnds[lineIndex],
                isColumn,
                availableMainSize,
                availableCrossSize,
                wraps,
                mainGap,
                textServices,
                resolvedWidths,
                resolvedHeights,
                occupiedMainSizes,
                occupiedCrossSizes);
            totalCrossSize += lineMetrics.CrossSize;
        }

        if (wraps && lineCount > 1)
            totalCrossSize += crossGap * (lineCount - 1);

        var lineStartCross = wraps
            ? ResolveCrossStart(CrossAlignment.Start, availableCrossSize, totalCrossSize, isCrossAxisReversed)
            : 0;
        var lineCursor = 0f;
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            var lineMetrics = ResolveLineMetrics(
                children,
                entries,
                lineStarts[lineIndex],
                lineEnds[lineIndex],
                isColumn,
                availableMainSize,
                availableCrossSize,
                wraps,
                mainGap,
                textServices,
                resolvedWidths,
                resolvedHeights,
                occupiedMainSizes,
                occupiedCrossSizes);
            var justifyRemainingMainSize = lineMetrics.HasFlexibleAdjustment
                ? 0
                : Math.Max(0, availableMainSize - lineMetrics.OccupiedMainSize);
            var autoMainMarginSize = lineMetrics.AutoMainMarginCount > 0 && !lineMetrics.HasFlexibleAdjustment
                ? Math.Max(0, availableMainSize - lineMetrics.OccupiedMainSize) / lineMetrics.AutoMainMarginCount
                : 0;
            var (startMain, gapBetweenItems) = lineMetrics.AutoMainMarginCount > 0
                ? (0, mainGap)
                : ResolveJustifyLayout(justifyContent, justifyRemainingMainSize, lineMetrics.ValidCount, mainGap);
            var lineAvailableCross = wraps ? lineMetrics.CrossSize : availableCrossSize;
            var cursor = 0f;

            for (var index = lineStarts[lineIndex]; index < lineEnds[lineIndex]; index++)
            {
                var entry = entries[index];
                if (!entry.Valid)
                    continue;

                var occupiedMainSize = ResolveOccupiedMainSize(entry, isColumn ? resolvedHeights[index] : resolvedWidths[index], isColumn, autoMainMarginSize);
                var crossSize = occupiedCrossSizes[index];
                var crossStart = ResolveCrossStart(entry.CrossAlign, lineAvailableCross, crossSize, isCrossAxisReversed);
                var mainStart = ResolveMainStart(startMain, cursor, occupiedMainSize, availableMainSize, isMainAxisReversed);
                var mainStartMargin = ResolveMainStartMargin(entry, isColumn, autoMainMarginSize);

                if (!entry.IsSpacer)
                {
                    var left = isColumn
                        ? paddingLeft + lineStartCross + lineCursor + crossStart + entry.MarginLeft + entry.OffsetLeft
                        : paddingLeft + mainStart + mainStartMargin + entry.OffsetLeft;
                    var top = isColumn
                        ? paddingTop + mainStart + mainStartMargin + entry.OffsetTop
                        : paddingTop + lineStartCross + lineCursor + crossStart + entry.MarginTop + entry.OffsetTop;
                    frames[index] = new LayoutFrameData(left, top, resolvedWidths[index], resolvedHeights[index]);
                }

                cursor += occupiedMainSize + gapBetweenItems;
            }

            lineCursor += lineMetrics.CrossSize + (lineIndex < lineCount - 1 ? crossGap : 0);
        }
    }

    private Span<StackLayoutEntry> GetEntryScratch(int requiredLength)
    {
        if (requiredLength <= 0)
            return Span<StackLayoutEntry>.Empty;

        if (entryScratch.Length < requiredLength)
        {
            var nextLength = entryScratch.Length == 0
                ? requiredLength
                : Math.Max(requiredLength, entryScratch.Length * 2);
            Array.Resize(ref entryScratch, nextLength);
        }

        return entryScratch.AsSpan(0, requiredLength);
    }

    private static (float StartMain, float GapBetweenItems) ResolveJustifyLayout(
        MainAxisJustification justifyContent,
        float remainingMainSize,
        int validCount,
        float gap)
    {
        if (justifyContent == MainAxisJustification.SpaceBetween && validCount > 1)
            return (0, gap + remainingMainSize / (validCount - 1));

        if (justifyContent == MainAxisJustification.SpaceAround && validCount > 0)
        {
            var slot = remainingMainSize / validCount;
            return (slot * 0.5f, gap + slot);
        }

        if (justifyContent == MainAxisJustification.SpaceEvenly && validCount > 0)
        {
            var slot = remainingMainSize / (validCount + 1);
            return (slot, gap + slot);
        }

        return justifyContent switch
        {
            MainAxisJustification.Center => (Math.Max(0, remainingMainSize * 0.5f), gap),
            MainAxisJustification.End => (Math.Max(0, remainingMainSize), gap),
            _ => (0, gap)
        };
    }

    private static float ResolveMainStart(
        float startMain,
        float consumedMainSize,
        float occupiedMainSize,
        float availableMainSize,
        bool isMainAxisReversed)
    {
        return isMainAxisReversed
            ? Math.Max(0, availableMainSize - startMain - consumedMainSize - occupiedMainSize)
            : startMain + consumedMainSize;
    }

    private static float ResolveCrossStart(
        CrossAlignment crossAlign,
        float availableCross,
        float occupiedCrossSize,
        bool isCrossAxisReversed)
    {
        return crossAlign switch
        {
            CrossAlignment.Center => Math.Max(0, (availableCross - occupiedCrossSize) * 0.5f),
            CrossAlignment.End => isCrossAxisReversed
                ? 0
                : Math.Max(0, availableCross - occupiedCrossSize),
            CrossAlignment.Start => isCrossAxisReversed
                ? Math.Max(0, availableCross - occupiedCrossSize)
                : 0,
            _ => isCrossAxisReversed
                ? Math.Max(0, availableCross - occupiedCrossSize)
                : 0
        };
    }

    private static int BuildLines(
        ReadOnlySpan<LayoutChildRequest> children,
        ReadOnlySpan<StackLayoutEntry> entries,
        bool isColumn,
        float availableMainSize,
        float gap,
        bool wraps,
        Span<int> lineStarts,
        Span<int> lineEnds)
    {
        var lineCount = 0;
        var currentLineStart = -1;
        var currentLineMain = 0f;
        var currentValidCount = 0;

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (!entry.Valid)
                continue;

            var occupiedMain = ResolveBaseOccupiedMainSize(children[index], entry, isColumn, availableMainSize);
            if (currentValidCount > 0
                && wraps
                && currentLineMain + gap + occupiedMain > availableMainSize)
            {
                lineStarts[lineCount] = currentLineStart;
                lineEnds[lineCount] = index;
                lineCount++;
                currentLineStart = index;
                currentLineMain = occupiedMain;
                currentValidCount = 1;
                continue;
            }

            if (currentLineStart < 0)
                currentLineStart = index;

            currentLineMain += currentValidCount > 0 ? gap + occupiedMain : occupiedMain;
            currentValidCount++;
        }

        if (currentValidCount > 0)
        {
            lineStarts[lineCount] = currentLineStart;
            lineEnds[lineCount] = entries.Length;
            lineCount++;
        }

        return lineCount;
    }

    private static StackLayoutLineMetrics ResolveLineMetrics(
        ReadOnlySpan<LayoutChildRequest> children,
        ReadOnlySpan<StackLayoutEntry> entries,
        int start,
        int end,
        bool isColumn,
        float availableMainSize,
        float availableCrossSize,
        bool wraps,
        float gap,
        IRuntimeTextServices textServices,
        Span<float> resolvedWidths,
        Span<float> resolvedHeights,
        Span<float> occupiedMainSizes,
        Span<float> occupiedCrossSizes)
    {
        var validCount = 0;
        var baseOccupiedMainSize = 0f;
        var totalGrow = 0f;
        var totalShrinkWeight = 0f;
        var autoMainMarginCount = 0;

        for (var index = start; index < end; index++)
        {
            var entry = entries[index];
            if (!entry.Valid)
                continue;

            var baseMainSize = ResolveBaseMainSize(children[index], entry, isColumn, availableMainSize);
            var occupiedMainSize = ResolveOccupiedMainSize(entry, baseMainSize, isColumn);
            baseOccupiedMainSize += validCount > 0 ? gap + occupiedMainSize : occupiedMainSize;
            validCount++;
            autoMainMarginCount += CountAutoMainMargins(entry, isColumn);
            if (entry.FlexGrow > 0)
                totalGrow += entry.FlexGrow;
            if (entry.FlexShrink > 0)
                totalShrinkWeight += entry.FlexShrink * Math.Max(0, baseMainSize);
        }

        if (validCount == 0)
            return StackLayoutLineMetrics.Empty;

        var baseRemainingMainSize = availableMainSize - baseOccupiedMainSize;
        var occupiedMainTotal = 0f;
        var crossSize = 0f;

        for (var index = start; index < end; index++)
        {
            var entry = entries[index];
            if (!entry.Valid)
                continue;

            var child = children[index];
            var baseMainSize = ResolveBaseMainSize(child, entry, isColumn, availableMainSize);
            var mainSize = ResolveFlexibleMainSize(baseMainSize, entry, baseRemainingMainSize, totalGrow, totalShrinkWeight);
            var resultWidth = isColumn ? entry.Width : mainSize;
            var resultHeight = isColumn ? mainSize : entry.Height;

            if (!wraps && !entry.IsSpacer && entry.CrossAlign == CrossAlignment.Stretch)
            {
                var stretchedCross = Math.Max(
                    0,
                    availableCrossSize - (isColumn
                        ? entry.MarginLeft + entry.MarginRight
                        : entry.MarginTop + entry.MarginBottom));
                if (isColumn && !HasExplicitCrossAxisSize(child, isColumn))
                    resultWidth = ResolveInsetSize(child.Width, child.IsWidthPercent, child.Left, child.IsLeftPercent, child.Right, child.IsRightPercent, stretchedCross, stretchedCross);
                else if (!isColumn && !HasExplicitCrossAxisSize(child, isColumn))
                    resultHeight = ResolveInsetSize(child.Height, child.IsHeightPercent, child.Top, child.IsTopPercent, child.Bottom, child.IsBottomPercent, stretchedCross, stretchedCross);
            }

            if (!entry.IsSpacer)
            {
                var adjusted = MeasureFlexAdjustedSize(child, textServices, resultWidth, resultHeight);
                resultWidth = adjusted.Width;
                resultHeight = adjusted.Height;
                var constrained = ApplySizeConstraints(
                    child,
                    resultWidth,
                    resultHeight,
                    isColumn ? availableCrossSize : availableMainSize,
                    isColumn ? availableMainSize : availableCrossSize);
                resultWidth = constrained.Width;
                resultHeight = constrained.Height;
            }

            resolvedWidths[index] = resultWidth;
            resolvedHeights[index] = resultHeight;
            occupiedMainSizes[index] = ResolveOccupiedMainSize(entry, isColumn ? resultHeight : resultWidth, isColumn);
            occupiedCrossSizes[index] = ResolveOccupiedCrossSize(entry, resultWidth, resultHeight, isColumn);
            occupiedMainTotal += occupiedMainTotal > 0 ? gap + occupiedMainSizes[index] : occupiedMainSizes[index];
            crossSize = Math.Max(crossSize, occupiedCrossSizes[index]);
        }

        if (occupiedMainTotal > availableMainSize)
        {
            ShrinkResolvedMainSizesToFit(
                children,
                entries,
                start,
                end,
                isColumn,
                availableMainSize,
                gap,
                resolvedWidths,
                resolvedHeights,
                occupiedMainSizes,
                occupiedCrossSizes,
                ref occupiedMainTotal,
                ref crossSize);
        }

        if (wraps && crossSize <= 0)
            crossSize = 0;

        return new StackLayoutLineMetrics(
            validCount,
            occupiedMainTotal,
            crossSize,
            baseRemainingMainSize,
            totalGrow,
            totalShrinkWeight,
            autoMainMarginCount);
    }

    private static void ShrinkResolvedMainSizesToFit(
        ReadOnlySpan<LayoutChildRequest> children,
        ReadOnlySpan<StackLayoutEntry> entries,
        int start,
        int end,
        bool isColumn,
        float availableMainSize,
        float gap,
        Span<float> resolvedWidths,
        Span<float> resolvedHeights,
        Span<float> occupiedMainSizes,
        Span<float> occupiedCrossSizes,
        ref float occupiedMainTotal,
        ref float crossSize)
    {
        var overflow = occupiedMainTotal - availableMainSize;
        if (overflow <= 0.5f)
            return;

        while (overflow > 0.5f)
        {
            var shrinkable = 0f;
            for (var index = start; index < end; index++)
            {
                var entry = entries[index];
                if (!entry.Valid || entry.IsSpacer || entry.FlexShrink <= 0)
                    continue;

                var minimum = ResolveMinimumMainSize(children[index], isColumn, availableMainSize);
                var current = isColumn ? resolvedHeights[index] : resolvedWidths[index];
                shrinkable += Math.Max(0, current - minimum);
            }

            if (shrinkable <= 0)
                break;

            var consumed = 0f;
            for (var index = start; index < end; index++)
            {
                var entry = entries[index];
                if (!entry.Valid || entry.IsSpacer || entry.FlexShrink <= 0)
                    continue;

                var minimum = ResolveMinimumMainSize(children[index], isColumn, availableMainSize);
                var current = isColumn ? resolvedHeights[index] : resolvedWidths[index];
                var capacity = Math.Max(0, current - minimum);
                if (capacity <= 0)
                    continue;

                var shrink = Math.Min(capacity, overflow * (capacity / shrinkable));
                if (isColumn)
                    resolvedHeights[index] -= shrink;
                else
                    resolvedWidths[index] -= shrink;
                consumed += shrink;
            }

            if (consumed <= 0.5f)
                break;

            overflow -= consumed;
        }

        occupiedMainTotal = 0;
        crossSize = 0;
        for (var index = start; index < end; index++)
        {
            var entry = entries[index];
            if (!entry.Valid)
                continue;

            occupiedMainSizes[index] = ResolveOccupiedMainSize(entry, isColumn ? resolvedHeights[index] : resolvedWidths[index], isColumn);
            occupiedCrossSizes[index] = ResolveOccupiedCrossSize(entry, resolvedWidths[index], resolvedHeights[index], isColumn);
            occupiedMainTotal += occupiedMainTotal > 0 ? gap + occupiedMainSizes[index] : occupiedMainSizes[index];
            crossSize = Math.Max(crossSize, occupiedCrossSizes[index]);
        }
    }

    private static float ResolveMinimumMainSize(in LayoutChildRequest child, bool isColumn, float availableMainSize)
    {
        if (isColumn)
            return child.HasMinHeight
                ? Math.Max(0, LayoutBoxModel.ResolveOuterSize(child.MinHeight, child.IsMinHeightPercent, availableMainSize, child.VerticalContentInset, child.BoxSizing))
                : child.VerticalContentInset;

        return child.HasMinWidth
            ? Math.Max(0, LayoutBoxModel.ResolveOuterSize(child.MinWidth, child.IsMinWidthPercent, availableMainSize, child.HorizontalContentInset, child.BoxSizing))
            : child.HorizontalContentInset;
    }

    private static float ResolveFlexibleMainSize(
        float baseMainSize,
        in StackLayoutEntry entry,
        float baseRemainingMainSize,
        float totalGrow,
        float totalShrinkWeight)
    {
        if (baseRemainingMainSize > 0 && entry.FlexGrow > 0 && totalGrow > 0)
            return baseMainSize + baseRemainingMainSize * (entry.FlexGrow / totalGrow);

        if (baseRemainingMainSize < 0 && entry.FlexShrink > 0 && totalShrinkWeight > 0)
        {
            var shrinkWeight = entry.FlexShrink * Math.Max(0, baseMainSize);
            return Math.Max(0, baseMainSize + baseRemainingMainSize * (shrinkWeight / totalShrinkWeight));
        }

        return baseMainSize;
    }

    private static float ResolveBaseMainSize(
        in LayoutChildRequest child,
        in StackLayoutEntry entry,
        bool isColumn,
        float availableMainSize)
    {
        var baseMainSize = child.HasFlexBasis
            ? Math.Max(0, LayoutBoxModel.ResolveOuterSize(child.FlexBasis, child.IsFlexBasisPercent, availableMainSize, isColumn ? child.VerticalContentInset : child.HorizontalContentInset, child.BoxSizing))
            : Math.Max(0, isColumn ? entry.Height : entry.Width);
        if (isColumn)
        {
            if (child.HasMinHeight)
                baseMainSize = Math.Max(baseMainSize, LayoutBoxModel.ResolveOuterSize(child.MinHeight, child.IsMinHeightPercent, availableMainSize, child.VerticalContentInset, child.BoxSizing));
            if (child.HasMaxHeight)
                baseMainSize = Math.Min(baseMainSize, LayoutBoxModel.ResolveOuterSize(child.MaxHeight, child.IsMaxHeightPercent, availableMainSize, child.VerticalContentInset, child.BoxSizing));
        }
        else
        {
            if (child.HasMinWidth)
                baseMainSize = Math.Max(baseMainSize, LayoutBoxModel.ResolveOuterSize(child.MinWidth, child.IsMinWidthPercent, availableMainSize, child.HorizontalContentInset, child.BoxSizing));
            if (child.HasMaxWidth)
                baseMainSize = Math.Min(baseMainSize, LayoutBoxModel.ResolveOuterSize(child.MaxWidth, child.IsMaxWidthPercent, availableMainSize, child.HorizontalContentInset, child.BoxSizing));
        }

        return baseMainSize;
    }

    private static int CountAutoMainMargins(in StackLayoutEntry entry, bool isColumn)
    {
        if (isColumn)
            return (entry.AutoMarginTop ? 1 : 0) + (entry.AutoMarginBottom ? 1 : 0);

        return (entry.AutoMarginLeft ? 1 : 0) + (entry.AutoMarginRight ? 1 : 0);
    }

    private static float ResolveMainStartMargin(in StackLayoutEntry entry, bool isColumn, float autoMarginSize)
    {
        return isColumn
            ? entry.AutoMarginTop ? autoMarginSize : entry.MarginTop
            : entry.AutoMarginLeft ? autoMarginSize : entry.MarginLeft;
    }

    private static float ResolveMainEndMargin(in StackLayoutEntry entry, bool isColumn, float autoMarginSize)
    {
        return isColumn
            ? entry.AutoMarginBottom ? autoMarginSize : entry.MarginBottom
            : entry.AutoMarginRight ? autoMarginSize : entry.MarginRight;
    }

    private static float ResolveOccupiedMainSize(in StackLayoutEntry entry, float mainSize, bool isColumn, float autoMarginSize = 0)
    {
        return ResolveMainStartMargin(entry, isColumn, autoMarginSize) +
            mainSize +
            ResolveMainEndMargin(entry, isColumn, autoMarginSize);
    }

    private static float ResolveOccupiedCrossSize(in StackLayoutEntry entry, float width, float height, bool isColumn)
    {
        return isColumn
            ? entry.MarginLeft + width + entry.MarginRight
            : entry.MarginTop + height + entry.MarginBottom;
    }

    private static float ResolveBaseOccupiedMainSize(
        in LayoutChildRequest child,
        in StackLayoutEntry entry,
        bool isColumn,
        float availableMainSize)
    {
        return ResolveOccupiedMainSize(entry, ResolveBaseMainSize(child, entry, isColumn, availableMainSize), isColumn);
    }

    private static (float Width, float Height) MeasureFlexAdjustedSize(
        in LayoutChildRequest child,
        IRuntimeTextServices textServices,
        float width,
        float height)
    {
        if (string.IsNullOrEmpty(child.Text) || child.HasHeight)
            return (width, height);

        if (!child.Wrap)
            return (width, Math.Max(height, textServices.MeasureLineHeight(CreateChildFont(child, 18, 400))));

        var textStyle = new SceneTextStyle(
            child.FontSize > 0 ? child.FontSize : 18,
            Font: CreateChildFont(child, 18, 400),
            WrapText: true);
        return (width, textServices.MeasureTextHeight(child.Text, width, textStyle));
    }

    private static (float Width, float Height) ApplySizeConstraints(in LayoutChildRequest child, float width, float height, float availableWidth, float availableHeight)
    {
        return (
            LayoutBoxModel.ClampOuterSize(width, child.MinWidth, child.IsMinWidthPercent, child.MaxWidth, child.IsMaxWidthPercent, availableWidth, child.HorizontalContentInset, child.BoxSizing),
            LayoutBoxModel.ClampOuterSize(height, child.MinHeight, child.IsMinHeightPercent, child.MaxHeight, child.IsMaxHeightPercent, availableHeight, child.VerticalContentInset, child.BoxSizing));
    }

    private static StackLayoutEntry MeasureChild(
        in LayoutChildRequest child,
        IRuntimeTextServices textServices,
        bool isColumn,
        bool isRow,
        float innerWidth,
        float innerHeight,
        CrossAlignment alignItems,
        bool applyCrossStretch)
    {
        if (child.Kind == LayoutChildKind.Invalid)
            return StackLayoutEntry.Invalid;

        if (child.Kind == LayoutChildKind.Spacer)
        {
            var size = Math.Max(0, child.Size);
            var flexGrow = Math.Max(0, child.FlexGrow);
            return new StackLayoutEntry(
                Valid: true,
                IsSpacer: true,
                OffsetLeft: 0,
                OffsetTop: 0,
                    Width: isRow ? size : 0,
                    Height: isColumn ? size : 0,
                    MarginLeft: 0,
                MarginTop: 0,
                MarginRight: 0,
                MarginBottom: 0,
                AutoMarginLeft: false,
                AutoMarginTop: false,
                AutoMarginRight: false,
                AutoMarginBottom: false,
                CrossAlign: alignItems,
                FlexGrow: flexGrow,
                FlexShrink: 0);
        }

        var crossAlign = ResolveCrossAlign(child.AlignSelf, alignItems);
        var width = ResolveInsetSize(
            child.Width,
            child.IsWidthPercent,
            child.Left,
            child.IsLeftPercent,
            child.Right,
            child.IsRightPercent,
            innerWidth,
            applyCrossStretch && isColumn && crossAlign == CrossAlignment.Stretch
                ? Math.Max(0, innerWidth - child.MarginLeft - child.MarginRight)
                : 0);
        var height = ResolveInsetSize(
            child.Height,
            child.IsHeightPercent,
            child.Top,
            child.IsTopPercent,
            child.Bottom,
            child.IsBottomPercent,
            innerHeight,
            applyCrossStretch && isRow && crossAlign == CrossAlignment.Stretch
                ? Math.Max(0, innerHeight - child.MarginTop - child.MarginBottom)
                : 0);
        width = LayoutBoxModel.ResolveOuterSize(width, false, innerWidth, child.HorizontalContentInset, child.HasWidth ? child.BoxSizing : BoxSizingMode.BorderBox);
        height = LayoutBoxModel.ResolveOuterSize(height, false, innerHeight, child.VerticalContentInset, child.HasHeight ? child.BoxSizing : BoxSizingMode.BorderBox);

        if (child.Kind == LayoutChildKind.Button)
        {
            var textStyle = new SceneTextStyle(
                child.FontSize > 0 ? child.FontSize : 14,
                Font: CreateChildFont(child, 14, 700));
            var labelWidth = textServices.MeasureTextWidth(child.Text ?? string.Empty, textStyle);
            var labelHeight = Math.Max(18, MathF.Ceiling(textStyle.FontSize + 4));
            width = width > 0 ? width : labelWidth + 36 + child.HorizontalContentInset;
            height = height > 0 ? height : Math.Max(40, labelHeight + 16 + child.VerticalContentInset);
        }

        if (!string.IsNullOrEmpty(child.Text) && height <= 0)
        {
            var textStyle = new SceneTextStyle(
                child.FontSize > 0 ? child.FontSize : 18,
                Font: CreateChildFont(child, 18, 400),
                WrapText: child.Wrap);
            var contentWidth = Math.Max(0, width - child.HorizontalContentInset);
            height = (child.Wrap
                ? textServices.MeasureTextHeight(child.Text, contentWidth, textStyle)
                : textServices.MeasureLineHeight(textStyle.Font)) + child.VerticalContentInset;
        }

        if (child.Kind == LayoutChildKind.Divider && (width <= 0 || height <= 0))
        {
            var thickness = child.Thickness > 0 ? child.Thickness : 1;
            width = (child.Vertical ? thickness : Math.Max(0, child.Length)) + child.HorizontalContentInset;
            height = (child.Vertical ? Math.Max(0, child.Length) : thickness) + child.VerticalContentInset;
        }

        width = Math.Max(width, child.HorizontalContentInset);
        height = Math.Max(height, child.VerticalContentInset);

        var constrained = ApplySizeConstraints(child, width, height, innerWidth, innerHeight);

        return new StackLayoutEntry(
            Valid: true,
            IsSpacer: false,
            OffsetLeft: child.HasLeft ? LayoutValue.Resolve(child.Left, child.IsLeftPercent, innerWidth) : 0,
            OffsetTop: child.HasTop ? LayoutValue.Resolve(child.Top, child.IsTopPercent, innerHeight) : 0,
            Width: constrained.Width,
            Height: constrained.Height,
            MarginLeft: child.MarginLeft,
            MarginTop: child.MarginTop,
            MarginRight: child.MarginRight,
            MarginBottom: child.MarginBottom,
            AutoMarginLeft: child.IsMarginLeftAuto,
            AutoMarginTop: child.IsMarginTopAuto,
            AutoMarginRight: child.IsMarginRightAuto,
            AutoMarginBottom: child.IsMarginBottomAuto,
            CrossAlign: crossAlign,
            FlexGrow: child.FlexGrow > 0 ? child.FlexGrow : 0,
            FlexShrink: child.FlexShrink > 0 ? child.FlexShrink : 0);
    }

    private static CrossAlignment ResolveCrossAlign(CrossAlignment alignSelf, CrossAlignment alignItems)
    {
        return alignSelf == CrossAlignment.Auto
            ? alignItems
            : alignSelf;
    }

    private static bool HasExplicitCrossAxisSize(in LayoutChildRequest child, bool isColumn)
    {
        return isColumn
            ? child.HasWidth
            : child.HasHeight;
    }

    private static float ResolveInsetSize(
        float explicitSize,
        bool explicitSizeIsPercent,
        float startInset,
        bool startInsetIsPercent,
        float endInset,
        bool endInsetIsPercent,
        float referenceSize,
        float fallbackSize)
    {
        var resolvedExplicitSize = LayoutValue.Resolve(explicitSize, explicitSizeIsPercent, referenceSize);
        if (LayoutValue.IsSet(explicitSize))
            return resolvedExplicitSize;

        var resolvedStartInset = LayoutValue.Resolve(startInset, startInsetIsPercent, referenceSize);
        var resolvedEndInset = LayoutValue.Resolve(endInset, endInsetIsPercent, referenceSize);
        if (LayoutValue.IsSet(endInset))
            return Math.Max(0, referenceSize - (LayoutValue.IsSet(startInset) ? resolvedStartInset : 0) - resolvedEndInset);

        return fallbackSize;
    }

    private readonly record struct StackLayoutLineMetrics(
        int ValidCount,
        float OccupiedMainSize,
        float CrossSize,
        float BaseRemainingMainSize,
        float TotalGrow,
        float TotalShrinkWeight,
        int AutoMainMarginCount)
    {
        public static StackLayoutLineMetrics Empty => new(0, 0, 0, 0, 0, 0, 0);

        public bool HasFlexibleAdjustment => (BaseRemainingMainSize > 0 && TotalGrow > 0)
            || (BaseRemainingMainSize < 0 && TotalShrinkWeight > 0);
    }

    private readonly record struct StackLayoutEntry(
        bool Valid,
        bool IsSpacer,
        float OffsetLeft,
        float OffsetTop,
        float Width,
        float Height,
        float MarginLeft,
        float MarginTop,
        float MarginRight,
        float MarginBottom,
        bool AutoMarginLeft,
        bool AutoMarginTop,
        bool AutoMarginRight,
        bool AutoMarginBottom,
        CrossAlignment CrossAlign,
        float FlexGrow,
        float FlexShrink)
    {
        public static StackLayoutEntry Invalid => new(false, false, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false, false, CrossAlignment.Start, 0, 0);
    }

    private static SceneFont CreateChildFont(in LayoutChildRequest child, float defaultSize, int defaultWeight)
    {
        if (child.Font.Size > 0 &&
            (child.Font.Weight > 0 || defaultWeight == 400))
        {
            return child.Font;
        }

        return child.Font with
        {
            Size = child.Font.Size > 0 ? child.Font.Size : defaultSize,
            Weight = child.Font.Weight > 0 ? child.Font.Weight : defaultWeight
        };
    }
}
