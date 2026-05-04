using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

internal sealed partial class HtmlLayoutBuilder
{
    private Span<LayoutFrameData?> CalculateFrames(
        LayoutContainerStyle containerStyle,
        ReadOnlySpan<LayoutChildRequest> childRequests,
        float parentWidth,
        float parentHeight)
    {
        var frames = scratch.AllocateFrames(childRequests.Length);
        layoutCalculator.ComputeFlexLayout(
            LayoutInput.Definite(parentWidth, parentHeight),
            containerStyle,
            childRequests,
            frames);
        return frames;
    }

    private static LayoutContainerStyle CreateLayoutContainerStyle(HtmlComputedStyle style)
        => CreateLayoutContainerStyle(
            style,
            LayoutBoxEdges.Zero);

    private static LayoutContainerStyle CreateLayoutContainerStyle(HtmlComputedStyle style, LayoutBoxEdges reservedScrollBarGutter)
        => new(
            style.FlexDirection,
            style.Direction,
            style.FlexWrap,
            RowGap: style.Gap,
            ColumnGap: style.Gap,
            style.AlignItems,
            style.JustifyContent,
            LayoutBoxEdges.ReplaceSidesWithReservedGutter(
                new LayoutBoxEdges(
                    style.PaddingLeft,
                    style.PaddingTop,
                    style.PaddingRight,
                    style.PaddingBottom),
                reservedScrollBarGutter));

    private bool TryResolveAutoHeightRequests(
        HtmlSceneNode[] children,
        Span<LayoutChildRequest> childRequests,
        ReadOnlySpan<LayoutFrameData?> frames,
        float availableHeight,
        FlexDirection parentFlexDirection,
        CrossAlignment parentAlignItems,
        out Span<LayoutChildRequest> resolvedRequests)
    {
        resolvedRequests = childRequests;
        var adjustedRequests = Span<LayoutChildRequest>.Empty;
        var changed = false;
        for (var index = 0; index < children.Length; index++)
        {
            if (frames[index] is not { } frame)
                continue;

            var child = children[index];
            if (child.Children.Length == 0 || LayoutValue.IsSet(child.Style.Height))
                continue;

            var stretchesInRow =
                FlexLayout.ResolveAxis(parentFlexDirection) == LayoutAxis.Row &&
                ResolveCrossAlignment(parentAlignItems, child.Style.AlignSelf) == CrossAlignment.Stretch;
            if (stretchesInRow)
                continue;

            var measuredHeight = MeasureNodeLayoutHeight(
                child,
                frame.Width,
                Math.Max(availableHeight, Math.Max(frame.Width, frame.Height)),
                parentIsFlexContainer: true,
                parentFlexDirection: parentFlexDirection);
            if (measuredHeight <= 0 || MathF.Abs(measuredHeight - frame.Height) < 0.5f)
                continue;

            if (adjustedRequests.IsEmpty)
            {
                adjustedRequests = scratch.AllocateRequests(childRequests.Length);
                childRequests.CopyTo(adjustedRequests);
            }

            adjustedRequests[index] = WithHeight(adjustedRequests[index], measuredHeight);
            changed = true;
        }

        if (changed)
            resolvedRequests = adjustedRequests;

        return changed;
    }

    private static CrossAlignment ResolveCrossAlignment(CrossAlignment parentAlignItems, CrossAlignment alignSelf)
    {
        return alignSelf == CrossAlignment.Auto
            ? parentAlignItems
            : alignSelf;
    }

    private LayoutChildRequest CreateLayoutRequest(
        HtmlSceneNode node,
        float availableWidth,
        float availableHeight,
        bool parentIsFlexContainer,
        FlexDirection parentFlexDirection = FlexDirection.Column,
        bool allowFlexShrink = true)
    {
        var style = node.Style;
        var width = style.Width;
        var height = style.Height;
        var minWidth = style.MinWidth;
        var maxWidth = style.MaxWidth;
        var minHeight = style.MinHeight;
        var maxHeight = style.MaxHeight;
        var units = style.UnitFlags;
        if (node.NodeKind == SceneNodeKind.Image &&
            style.IntrinsicImageWidth > 0 &&
            style.IntrinsicImageHeight > 0 &&
            style.IsWidthPercent &&
            style.IsHeightPercent &&
            LayoutValue.IsSet(width) &&
            LayoutValue.IsSet(height))
        {
            width = style.IntrinsicImageWidth * (width * 0.01f);
            height = style.IntrinsicImageHeight * (height * 0.01f);
            units &= ~(LayoutValueUnitFlags.WidthPercent | LayoutValueUnitFlags.HeightPercent);
        }
        else if (style.ImageAspectRatio > 0 &&
            LayoutValue.IsSet(width) &&
            (style.IsHeightPercent || !LayoutValue.IsSet(height)))
        {
            var resolvedWidth = style.IsWidthPercent
                ? LayoutValue.Resolve(width, isPercent: true, availableWidth)
                : width;
            if (resolvedWidth > 0)
            {
                height = resolvedWidth / style.ImageAspectRatio;
                units &= ~LayoutValueUnitFlags.HeightPercent;
            }
        }
        else if (style.ImageAspectRatio > 0 &&
                 LayoutValue.IsSet(height) &&
                 !LayoutValue.IsSet(width))
        {
            var resolvedHeight = style.IsHeightPercent
                ? LayoutValue.Resolve(height, isPercent: true, availableHeight)
                : height;
            if (resolvedHeight > 0)
                width = resolvedHeight * style.ImageAspectRatio;
        }

        AdjustExplicitFrameForBoxSizing(
            style,
            availableWidth,
            availableHeight,
            ref width,
            ref height,
            ref minWidth,
            ref maxWidth,
            ref minHeight,
            ref maxHeight,
            ref units);

        if (node.NodeKind == SceneNodeKind.Text &&
            style.PreferIntrinsicWidth &&
            !string.IsNullOrEmpty(node.TextContent))
        {
            var textStyle = textStyleCache.GetInlineMeasureStyle(style);
            var measured = MeasureInlineText(node.TextContent, textStyle, style.LineHeight);
            width = measured.Width;
            height = measured.Height;
        }
        else if (node.NodeKind == SceneNodeKind.TextInput &&
                 style.PreferIntrinsicWidth &&
                 !string.IsNullOrEmpty(node.TextContent))
        {
            var textStyle = textStyleCache.GetInlineMeasureStyle(style);
            var measured = MeasureInlineText(node.TextContent, textStyle, style.LineHeight);
            width = measured.Width + style.PaddingLeft + style.PaddingRight + style.BorderWidth * 2;
            if (!LayoutValue.IsSet(height))
                height = measured.Height + style.PaddingTop + style.PaddingBottom + style.BorderWidth * 2;
        }
        else if (node.NodeKind == SceneNodeKind.Text &&
            !LayoutValue.IsSet(width) &&
            !style.PreferIntrinsicWidth &&
            availableWidth > 0)
        {
            width = availableWidth;
        }

        if (!style.HasExplicitWidth &&
            style.Float != HtmlFloat.None &&
            node.Children.Length > 0)
        {
            var shrinkToFit = MeasureShrinkToFitSize(node, availableWidth, availableHeight);
            var floatAutoWidth = MeasureFloatAutoWidth(node, availableWidth, availableHeight);
            if (floatAutoWidth > 0)
                width = availableWidth > 0 ? Math.Min(floatAutoWidth, availableWidth) : floatAutoWidth;
            if (!LayoutValue.IsSet(height) && shrinkToFit.Height > 0)
                height = shrinkToFit.Height;
        }

        if (!LayoutValue.IsSet(width) &&
            style.Display == HtmlDisplay.InlineBlock &&
            node.Children.Length > 0)
        {
            var maxContentWidth = MeasureMaxContentWidth(node, availableWidth, availableHeight);
            if (maxContentWidth > 0)
                width = maxContentWidth;
        }

        if (!LayoutValue.IsSet(width) &&
            style.PreferIntrinsicWidth &&
            TryMeasurePreferredIntrinsicSize(node, out var preferredWidth, out var preferredHeight))
        {
            width = preferredWidth;
            if (!LayoutValue.IsSet(height))
                height = preferredHeight;
        }

        var useFullWidthByDefault = style.ShouldUseFullWidthByDefaultInParent(parentIsFlexContainer, parentFlexDirection);
        var parentIsRowFlexContainer =
            parentIsFlexContainer &&
            FlexLayout.ResolveAxis(parentFlexDirection) == LayoutAxis.Row;
        var rowFlexGrowBasisZeroAutoWidth = ShouldTreatWidthAsAutoForRowFlexGrowBasisZero(style, availableWidth);
        if (parentIsRowFlexContainer && !LayoutValue.IsSet(minWidth))
        {
            var automaticMinWidth = ResolveAutomaticFlexItemMinWidth(node, style, width, availableWidth, availableHeight);
            if (automaticMinWidth > 0)
                minWidth = automaticMinWidth;
        }

        if (node.Children.Length > 0)
        {
            var intrinsic = MeasureNodeIntrinsicSize(node, availableWidth, availableHeight, parentIsFlexContainer, parentFlexDirection);
            if (parentIsRowFlexContainer && (!LayoutValue.IsSet(style.Width) || rowFlexGrowBasisZeroAutoWidth))
            {
                var minContentWidth = MeasureMinContentWidth(node, availableWidth, availableHeight);
                if (minContentWidth > 0)
                    intrinsic.Width = minContentWidth;
            }

            if (!LayoutValue.IsSet(width) && intrinsic.Width > 0 && !useFullWidthByDefault)
                width = intrinsic.Width;
            if (!LayoutValue.IsSet(height) && intrinsic.Height > 0 && !parentIsRowFlexContainer)
                height = intrinsic.Height;
            if (!LayoutValue.IsSet(height) && intrinsic.Height > 0 && parentIsRowFlexContainer)
                minHeight = LayoutValue.IsSet(minHeight) ? Math.Max(minHeight, intrinsic.Height) : intrinsic.Height;
        }

        var flexBasis = parentIsFlexContainer ? style.FlexBasis : float.NaN;
        if (parentIsRowFlexContainer &&
            style.FlexGrow > 0 &&
            (!LayoutValue.IsSet(style.Width) || rowFlexGrowBasisZeroAutoWidth) &&
            (!LayoutValue.IsSet(flexBasis) ||
             LayoutValue.Resolve(flexBasis, (units & LayoutValueUnitFlags.FlexBasisPercent) != 0, availableWidth) <= 0))
        {
            flexBasis = 0;
            width = 0;
        }

        return new LayoutChildRequest(
            Kind: LayoutChildKind.Element,
            Left: style.Left,
            Top: style.Top,
            Right: style.Right,
            Bottom: style.Bottom,
            Width: width,
            Height: height,
            MinWidth: minWidth,
            MaxWidth: maxWidth,
            MinHeight: minHeight,
            MaxHeight: maxHeight,
            MarginLeft: style.MarginLeft,
            MarginTop: style.MarginTop,
            MarginRight: style.MarginRight,
            MarginBottom: style.MarginBottom,
            Text: node.NodeKind is SceneNodeKind.Text or SceneNodeKind.TextInput ? node.TextContent : null,
            FontSize: style.FontSize,
            FontFamily: style.FontFamily,
            FontWeight: style.FontWeight,
            Italic: style.Italic,
            Font: textStyleCache.GetFont(style, 16, 400),
            Wrap: node.NodeKind == SceneNodeKind.TextInput ? style.Multiline : style.WrapText,
            AlignSelf: style.AlignSelf,
            FlexGrow: parentIsFlexContainer ? style.FlexGrow : 0,
            FlexShrink: parentIsFlexContainer && allowFlexShrink ? style.FlexShrink : 0,
            FlexBasis: flexBasis,
            Units: units);
    }

    private float ResolveAutomaticFlexItemMinWidth(
        HtmlSceneNode node,
        HtmlComputedStyle style,
        float width,
        float availableWidth,
        float availableHeight)
    {
        if (!LayoutValue.IsSet(style.FlexBasis))
            return 0;

        if (ShouldTreatWidthAsAutoForRowFlexGrowBasisZero(style, availableWidth))
            return 0;

        if (style.FlexGrow > 0 &&
            LayoutValue.Resolve(style.FlexBasis, (style.UnitFlags & LayoutValueUnitFlags.FlexBasisPercent) != 0, availableWidth) <= 0 &&
            !LayoutValue.IsSet(style.Width))
        {
            return 0;
        }

        if (LayoutValue.IsSet(style.Width))
            return LayoutValue.Resolve(style.Width, style.IsWidthPercent, availableWidth);

        if (node.NodeKind == SceneNodeKind.Text && !string.IsNullOrEmpty(node.TextContent))
        {
            var textStyle = textStyleCache.GetInlineMeasureStyle(style);
            return MeasureInlineText(node.TextContent, textStyle, style.LineHeight).Width;
        }

        return node.Children.Length > 0 ? MeasureMinContentWidth(node, availableWidth, availableHeight) : 0;
    }

    private static bool ShouldTreatWidthAsAutoForRowFlexGrowBasisZero(HtmlComputedStyle style, float availableWidth)
    {
        if (style.FlexGrow <= 0)
            return false;

        var hasZeroBasis = !LayoutValue.IsSet(style.FlexBasis) ||
            LayoutValue.Resolve(style.FlexBasis, (style.UnitFlags & LayoutValueUnitFlags.FlexBasisPercent) != 0, availableWidth) <= 0;
        if (!hasZeroBasis)
            return false;

        return !LayoutValue.IsSet(style.Width) ||
               style.IsWidthPercent && MathF.Abs(style.Width - 100) < 0.001f;
    }

    private static void AdjustExplicitFrameForBoxSizing(
        HtmlComputedStyle style,
        float availableWidth,
        float availableHeight,
        ref float width,
        ref float height,
        ref float minWidth,
        ref float maxWidth,
        ref float minHeight,
        ref float maxHeight,
        ref LayoutValueUnitFlags units)
    {
        if (style.BoxSizing != SceneBoxSizing.ContentBox)
            return;

        var horizontalInsets = style.PaddingLeft + style.PaddingRight + style.BorderWidth * 2;
        var verticalInsets = style.PaddingTop + style.PaddingBottom + style.BorderWidth * 2;
        if (LayoutValue.IsSet(width))
        {
            width = LayoutValue.Resolve(width, (units & LayoutValueUnitFlags.WidthPercent) != 0, availableWidth);
            if (style.HasExplicitWidth)
                width += horizontalInsets;
            units &= ~LayoutValueUnitFlags.WidthPercent;
        }

        if (LayoutValue.IsSet(height))
        {
            height = LayoutValue.Resolve(height, (units & LayoutValueUnitFlags.HeightPercent) != 0, availableHeight) + verticalInsets;
            units &= ~LayoutValueUnitFlags.HeightPercent;
        }

        if (LayoutValue.IsSet(minWidth))
        {
            minWidth = LayoutValue.Resolve(minWidth, (units & LayoutValueUnitFlags.MinWidthPercent) != 0, availableWidth) + horizontalInsets;
            units &= ~LayoutValueUnitFlags.MinWidthPercent;
        }

        if (LayoutValue.IsSet(maxWidth))
        {
            maxWidth = LayoutValue.Resolve(maxWidth, (units & LayoutValueUnitFlags.MaxWidthPercent) != 0, availableWidth) + horizontalInsets;
            units &= ~LayoutValueUnitFlags.MaxWidthPercent;
        }

        if (LayoutValue.IsSet(minHeight))
        {
            minHeight = LayoutValue.Resolve(minHeight, (units & LayoutValueUnitFlags.MinHeightPercent) != 0, availableHeight) + verticalInsets;
            units &= ~LayoutValueUnitFlags.MinHeightPercent;
        }

        if (LayoutValue.IsSet(maxHeight))
        {
            maxHeight = LayoutValue.Resolve(maxHeight, (units & LayoutValueUnitFlags.MaxHeightPercent) != 0, availableHeight) + verticalInsets;
            units &= ~LayoutValueUnitFlags.MaxHeightPercent;
        }
    }

    private (float Width, float Height) MeasureShrinkToFitSize(HtmlSceneNode node, float availableWidth, float availableHeight)
    {
        var style = node.Style;
        if (node.NodeKind == SceneNodeKind.Image)
        {
            var width = style.IntrinsicImageWidth > 0 &&
                        style.IsWidthPercent &&
                        style.IsHeightPercent &&
                        LayoutValue.IsSet(style.Width)
                ? style.IntrinsicImageWidth * (style.Width * 0.01f)
                : ResolveExplicitSize(style.Width, style.IsWidthPercent, availableWidth);
            var height = style.IntrinsicImageHeight > 0 &&
                         style.IsWidthPercent &&
                         style.IsHeightPercent &&
                         LayoutValue.IsSet(style.Height)
                ? style.IntrinsicImageHeight * (style.Height * 0.01f)
                : ResolveExplicitSize(style.Height, style.IsHeightPercent, availableHeight);
            if (LayoutValue.IsSet(width) && style.ImageAspectRatio > 0)
                height = width / style.ImageAspectRatio;
            else if (LayoutValue.IsSet(height) && style.ImageAspectRatio > 0)
                width = height * style.ImageAspectRatio;

            return (
                LayoutValue.IsSet(width) ? AdjustResolvedSizeForBoxSizing(style, width, horizontal: true) : 0,
                LayoutValue.IsSet(height) ? AdjustResolvedSizeForBoxSizing(style, height, horizontal: false) : 0);
        }

        if (node.NodeKind == SceneNodeKind.Text && !string.IsNullOrEmpty(node.TextContent))
        {
            var textStyle = textStyleCache.GetInlineMeasureStyle(style);
            var measured = MeasureInlineText(node.TextContent, textStyle, style.LineHeight);
            return (measured.Width, measured.Height);
        }

        if (node.Children.Length == 0)
            return (0, 0);

        if (style.Display == HtmlDisplay.Block && ContainsFloatChildren(node.Children))
        {
            var scratchMark = scratch.Mark();
            try
            {
                var floatRequests = CreateFloatMeasureRequests(node.Children, availableWidth, availableHeight);
                var measured = MeasureFloatContent(style, node.Children, floatRequests, availableWidth, wrapLines: false);
                return (
                    measured.Width + style.PaddingRight + style.BorderWidth * 2,
                    measured.Height + style.PaddingBottom + style.BorderWidth * 2);
            }
            finally
            {
                scratch.Rewind(scratchMark);
            }
        }

        var isRow = style.Display == HtmlDisplay.Flex && FlexLayout.ResolveAxis(style.FlexDirection) == LayoutAxis.Row ||
                    style.Display is HtmlDisplay.Inline or HtmlDisplay.InlineBlock;
        var contentWidth = 0f;
        var contentHeight = 0f;
        for (var index = 0; index < node.Children.Length; index++)
        {
            var child = node.Children[index];
            var measured = MeasureShrinkToFitSize(child, availableWidth, availableHeight);
            var childWidth = measured.Width + child.Style.MarginLeft + child.Style.MarginRight;
            var childHeight = measured.Height + child.Style.MarginTop + child.Style.MarginBottom;
            if (child.Children.Length > 0 && ContainsFloatChildren(child.Children))
            {
                var scratchMark = scratch.Mark();
                try
                {
                    var floatRequests = CreateFloatMeasureRequests(child.Children, availableWidth, availableHeight);
                    var preferredFloatWidth =
                        MeasureFloatContent(child.Style, child.Children, floatRequests, availableWidth, wrapLines: false).Width +
                        child.Style.PaddingLeft +
                        child.Style.PaddingRight +
                        child.Style.BorderWidth * 2 +
                        child.Style.MarginLeft +
                        child.Style.MarginRight;
                    childWidth = Math.Max(childWidth, preferredFloatWidth);
                }
                finally
                {
                    scratch.Rewind(scratchMark);
                }
            }

            if (isRow)
            {
                contentWidth += childWidth;
                if (index > 0)
                    contentWidth += style.Gap;
                contentHeight = Math.Max(contentHeight, childHeight);
            }
            else
            {
                contentWidth = Math.Max(contentWidth, childWidth);
                contentHeight += childHeight;
                if (index > 0)
                    contentHeight += style.Gap;
            }
        }

        return (
            contentWidth + style.PaddingLeft + style.PaddingRight + style.BorderWidth * 2,
            contentHeight + style.PaddingTop + style.PaddingBottom + style.BorderWidth * 2);
    }

    private float MeasureShrinkToFitPreferredWidth(HtmlSceneNode node, float availableWidth, float availableHeight)
    {
        var style = node.Style;
        if (node.NodeKind == SceneNodeKind.Text && !string.IsNullOrEmpty(node.TextContent))
        {
            var textStyle = textStyleCache.GetInlineMeasureStyle(style);
            return MeasureInlineText(node.TextContent, textStyle, style.LineHeight).Width;
        }

        if (node.NodeKind == SceneNodeKind.Image)
        {
            var width = ResolveReplacedElementContributionWidth(style, availableWidth);
            if (!LayoutValue.IsSet(width) && LayoutValue.IsSet(style.IntrinsicImageWidth))
                width = style.IntrinsicImageWidth;
            return LayoutValue.IsSet(width)
                ? AdjustResolvedSizeForBoxSizing(style, width, horizontal: true)
                : 0;
        }

        if (LayoutValue.IsSet(style.Width) && style.HasExplicitWidth)
            return ResolveExplicitOuterSize(style, style.Width, style.IsWidthPercent, availableWidth, horizontal: true);

        if (node.Children.Length == 0)
            return 0;

        var rowWidth = 0f;
        var maxWidth = 0f;
        var allInlineLike = true;
        for (var index = 0; index < node.Children.Length; index++)
        {
            var child = node.Children[index];
            var childWidth = SanitizeShrinkMeasure(MeasureShrinkToFitPreferredWidth(child, availableWidth, availableHeight)) +
                             SanitizeShrinkMeasure(child.Style.MarginLeft) +
                             SanitizeShrinkMeasure(child.Style.MarginRight);
            maxWidth = Math.Max(maxWidth, childWidth);

            if (child.Style.Float != HtmlFloat.None ||
                child.NodeKind is SceneNodeKind.Text or SceneNodeKind.Image ||
                child.Style.Display is HtmlDisplay.Inline or HtmlDisplay.InlineBlock ||
                style.Display == HtmlDisplay.Flex && FlexLayout.ResolveAxis(style.FlexDirection) == LayoutAxis.Row)
            {
                if (rowWidth > 0)
                    rowWidth += style.Gap;
                rowWidth += childWidth;
            }
            else
            {
                allInlineLike = false;
            }
        }

        var contentWidth = allInlineLike || rowWidth > maxWidth ? Math.Max(rowWidth, maxWidth) : maxWidth;
        return contentWidth + style.PaddingLeft + style.PaddingRight + style.BorderWidth * 2;
    }

    private static float SanitizeShrinkMeasure(float value)
        => float.IsFinite(value) && value > 0 ? value : 0;

    private float MeasureFloatAutoWidth(HtmlSceneNode node, float availableWidth, float availableHeight)
    {
        var preferredWidth = Math.Max(
            SanitizeShrinkMeasure(MeasureShrinkToFitPreferredWidth(node, availableWidth, availableHeight)),
            SanitizeShrinkMeasure(MeasureDescendantInlinePreferredWidth(node, availableWidth, availableHeight)));
        var preferredMinimumWidth = SanitizeShrinkMeasure(MeasureMinContentWidth(node, availableWidth, availableHeight));
        if (preferredWidth <= 0)
            return preferredMinimumWidth;
        if (availableWidth <= 0)
            return Math.Max(preferredMinimumWidth, preferredWidth);

        return Math.Min(Math.Max(preferredMinimumWidth, availableWidth), preferredWidth);
    }

    private float MeasureDescendantInlinePreferredWidth(HtmlSceneNode node, float availableWidth, float availableHeight)
    {
        if (node.NodeKind == SceneNodeKind.Text && !string.IsNullOrEmpty(node.TextContent))
        {
            var textStyle = textStyleCache.GetInlineMeasureStyle(node.Style);
            return MeasureInlineText(node.TextContent, textStyle, node.Style.LineHeight).Width;
        }

        if (node.NodeKind == SceneNodeKind.Image)
            return MeasureShrinkToFitSize(node, availableWidth, availableHeight).Width;

        var width = 0f;
        for (var index = 0; index < node.Children.Length; index++)
            width += MeasureDescendantInlinePreferredWidth(node.Children[index], availableWidth, availableHeight);

        return width + node.Style.PaddingLeft + node.Style.PaddingRight + node.Style.BorderWidth * 2;
    }

    private bool TryMeasurePreferredIntrinsicSize(HtmlSceneNode node, out float width, out float height)
    {
        width = 0;
        height = 0;
        if (node.Children.Length != 1)
            return false;

        var textNode = node.Children[0];
        if (textNode.NodeKind != SceneNodeKind.Text || string.IsNullOrWhiteSpace(textNode.TextContent))
            return false;

        var textStyle = textStyleCache.GetInlineMeasureStyle(textNode.Style);
        var lineHeight = ResolveNormalLineHeight(textStyle.Font, textNode.Style.LineHeight);
        var measured = MeasureInlineText(textNode.TextContent, textStyle, textNode.Style.LineHeight);
        width =
            measured.Width +
            node.Style.PaddingLeft +
            node.Style.PaddingRight +
            node.Style.BorderWidth * 2;
        height =
            Math.Max(lineHeight, measured.Height) +
            node.Style.PaddingTop +
            node.Style.PaddingBottom +
            node.Style.BorderWidth * 2;
        return width > 0 && height > 0;
    }

    private float MeasureDescendantFloatPreferredWidth(HtmlSceneNode node, float availableWidth, float availableHeight)
    {
        if (node.Children.Length == 0)
            return 0;

        if (ContainsFloatChildren(node.Children))
        {
            var scratchMark = scratch.Mark();
            try
            {
                var floatRequests = CreateFloatMeasureRequests(node.Children, availableWidth, availableHeight);
                return MeasureFloatContent(node.Style, node.Children, floatRequests, availableWidth, wrapLines: false).Width +
                       node.Style.PaddingLeft +
                       node.Style.PaddingRight +
                       node.Style.BorderWidth * 2;
            }
            finally
            {
                scratch.Rewind(scratchMark);
            }
        }

        var preferred = 0f;
        for (var index = 0; index < node.Children.Length; index++)
            preferred = Math.Max(preferred, MeasureDescendantFloatPreferredWidth(node.Children[index], availableWidth, availableHeight));
        return preferred + node.Style.PaddingLeft + node.Style.PaddingRight + node.Style.BorderWidth * 2;
    }

    private float MeasureMaxContentWidth(HtmlSceneNode node, float availableWidth, float availableHeight)
    {
        var style = node.Style;
        if (LayoutValue.IsSet(style.Width) &&
            style.HasExplicitWidth &&
            !style.IsWidthPercent &&
            !(style.FlexGrow > 0 && style.Width <= 0 && node.Children.Length > 0))
            return style.Width + style.MarginLeft + style.MarginRight;

        if (node.NodeKind == SceneNodeKind.Text && !string.IsNullOrEmpty(node.TextContent))
        {
            var textStyle = textStyleCache.GetInlineMeasureStyle(style);
            return MeasureInlineText(node.TextContent, textStyle, style.LineHeight).Width +
                   style.PaddingLeft +
                   style.PaddingRight +
                   style.BorderWidth * 2 +
                   style.MarginLeft +
                   style.MarginRight;
        }

        if (node.NodeKind == SceneNodeKind.Image)
        {
            var width = ResolveReplacedElementContributionWidth(style, availableWidth);
            if (!LayoutValue.IsSet(width) && LayoutValue.IsSet(style.IntrinsicImageWidth))
                width = style.IntrinsicImageWidth;
            return (LayoutValue.IsSet(width) ? width : 0) +
                   style.PaddingLeft +
                   style.PaddingRight +
                   style.BorderWidth * 2 +
                   style.MarginLeft +
                   style.MarginRight;
        }

        if (node.Children.Length == 0)
            return style.PaddingLeft + style.PaddingRight + style.BorderWidth * 2 + style.MarginLeft + style.MarginRight;

        var shouldSum =
            style.Display is HtmlDisplay.Inline or HtmlDisplay.InlineBlock ||
            IsInlineFormattingRow(node) ||
            ContainsFloatChildren(node.Children) ||
            style.Display == HtmlDisplay.Flex &&
            FlexLayout.ResolveAxis(style.FlexDirection) == LayoutAxis.Row &&
            style.FlexWrap == FlexWrap.NoWrap;
        var contentWidth = 0f;
        for (var index = 0; index < node.Children.Length; index++)
        {
            var childWidth = MeasureMaxContentWidth(node.Children[index], availableWidth, availableHeight);
            if (shouldSum)
            {
                contentWidth += childWidth;
                if (index > 0)
                    contentWidth += style.Gap;
            }
            else
            {
                contentWidth = Math.Max(contentWidth, childWidth);
            }
        }

        return contentWidth + style.PaddingLeft + style.PaddingRight + style.BorderWidth * 2 + style.MarginLeft + style.MarginRight;
    }

    private float MeasureMinContentWidth(HtmlSceneNode node, float availableWidth, float availableHeight)
    {
        var style = node.Style;
        if (LayoutValue.IsSet(style.Width) &&
            style.HasExplicitWidth &&
            !style.IsWidthPercent &&
            !(style.FlexGrow > 0 && style.Width <= 0 && node.Children.Length > 0))
            return style.Width + style.MarginLeft + style.MarginRight;

        if (node.NodeKind == SceneNodeKind.Text && !string.IsNullOrEmpty(node.TextContent))
        {
            var textStyle = textStyleCache.GetInlineMeasureStyle(style);
            return MeasureLongestUnbreakableTextWidth(node.TextContent, textStyle) +
                   style.PaddingLeft +
                   style.PaddingRight +
                   style.BorderWidth * 2 +
                   style.MarginLeft +
                   style.MarginRight;
        }

        if (node.NodeKind == SceneNodeKind.Image)
            return MeasureMaxContentWidth(node, availableWidth, availableHeight);

        if (node.Children.Length == 0)
            return style.PaddingLeft + style.PaddingRight + style.BorderWidth * 2 + style.MarginLeft + style.MarginRight;

        var shouldSum =
            style.Display is HtmlDisplay.Inline or HtmlDisplay.InlineBlock ||
            ContainsFloatChildren(node.Children) ||
            style.Display == HtmlDisplay.Flex &&
            FlexLayout.ResolveAxis(style.FlexDirection) == LayoutAxis.Row &&
            style.FlexWrap == FlexWrap.NoWrap;
        var contentWidth = 0f;
        for (var index = 0; index < node.Children.Length; index++)
        {
            var childWidth = MeasureMinContentWidth(node.Children[index], availableWidth, availableHeight);
            if (shouldSum)
            {
                contentWidth += childWidth;
                if (index > 0)
                    contentWidth += style.Gap;
            }
            else
            {
                contentWidth = Math.Max(contentWidth, childWidth);
            }
        }

        return contentWidth + style.PaddingLeft + style.PaddingRight + style.BorderWidth * 2 + style.MarginLeft + style.MarginRight;
    }

    private static bool IsInlineFormattingRow(HtmlSceneNode node)
        => node.Style.Display == HtmlDisplay.Flex &&
           FlexLayout.ResolveAxis(node.Style.FlexDirection) == LayoutAxis.Row &&
           ContainsInlineChildren(node.Children);

    private float MeasureLongestUnbreakableTextWidth(string text, SceneTextStyle textStyle)
    {
        var maxWidth = 0f;
        var segmentStart = 0;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && !IsBreakableTextBoundary(text[index]) && !IsCjkBreakableCharacter(text[index]))
                continue;

            if (index > segmentStart)
                maxWidth = MathF.Max(maxWidth, MathF.Ceiling(textServices.MeasureTextWidth(text.AsSpan(segmentStart, index - segmentStart), textStyle)));

            if (index < text.Length && IsCjkBreakableCharacter(text[index]))
                maxWidth = MathF.Max(maxWidth, MathF.Ceiling(textServices.MeasureTextWidth(text.AsSpan(index, 1), textStyle)));

            segmentStart = index + 1;
        }

        return maxWidth;
    }

    private static float ResolveReplacedElementContributionWidth(HtmlComputedStyle style, float availableWidth)
    {
        if (!LayoutValue.IsSet(style.Width))
            return float.NaN;

        if (style.IsWidthPercent && style.IntrinsicImageWidth > 0)
        {
            if (style.Width is > 0 and <= 100)
                return style.IntrinsicImageWidth * (style.Width * 0.01f);

            return style.IntrinsicImageWidth;
        }

        return ResolveExplicitSize(style.Width, style.IsWidthPercent, availableWidth);
    }

    private static bool IsBreakableTextBoundary(char value)
        => value is ' ' or '\t' or '\r' or '\n' or '\f';

    private static bool IsCjkBreakableCharacter(char value)
        => value is >= '\u3040' and <= '\u30ff' ||
           value is >= '\u3400' and <= '\u9fff' ||
           value is >= '\uff00' and <= '\uffef';

    private static bool ContainsInlineChildren(HtmlSceneNode[] children)
    {
        for (var index = 0; index < children.Length; index++)
        {
            if (children[index].Style.Display is HtmlDisplay.Inline or HtmlDisplay.InlineBlock)
                return true;
        }

        return false;
    }

    private float MeasureNodeLayoutHeight(HtmlSceneNode node, float availableWidth, float availableHeight, bool parentIsFlexContainer = false, FlexDirection parentFlexDirection = FlexDirection.Column)
    {
        if (node.Children.Length == 0)
            return MeasureAutoHeightForResolvedWidth(node, availableWidth, availableHeight);

        var cacheKey = CreateLayoutMeasureCacheKey(node, availableWidth, availableHeight, parentIsFlexContainer, parentFlexDirection, LayoutRunMode.PerformHiddenLayout);
        if (measurementCache.TryGetLayoutHeight(cacheKey, out var cachedHeight))
            return cachedHeight;

        var measuredHeight = MeasureNodeLayoutHeightUncached(node, availableWidth, availableHeight, parentIsFlexContainer, parentFlexDirection);
        measurementCache.SetLayoutHeight(cacheKey, measuredHeight);
        return measuredHeight;
    }

    private float MeasureNodeLayoutHeightUncached(HtmlSceneNode node, float availableWidth, float availableHeight, bool parentIsFlexContainer, FlexDirection parentFlexDirection)
    {
        var style = node.Style;
        var explicitWidth = ResolveExplicitOuterSize(style, style.Width, style.IsWidthPercent, availableWidth, horizontal: true);
        var explicitHeight = ResolveExplicitOuterSize(style, style.Height, style.IsHeightPercent, availableHeight, horizontal: false);
        var containerWidth = LayoutValue.IsSet(explicitWidth)
            ? explicitWidth
            : style.ShouldUseFullWidthByDefaultInParent(parentIsFlexContainer, parentFlexDirection)
                ? availableWidth
                : 0;
        var containerHeight = LayoutValue.IsSet(explicitHeight) ? explicitHeight : 0;
        var childAvailableWidth = Math.Max(0, (containerWidth > 0 ? containerWidth : availableWidth) - style.PaddingLeft - style.PaddingRight);
        var childAvailableHeight = Math.Max(0, (containerHeight > 0 ? containerHeight : availableHeight) - style.PaddingTop - style.PaddingBottom);

        var resolvedChildren = ResolveContainerPercentUnits(node.Children, childAvailableWidth);
        if (IsInlineFormattingContext(style, resolvedChildren))
        {
            var scratchMark = scratch.Mark();
            try
            {
                return CreateInlineLineLayout(style, resolvedChildren, childAvailableWidth, childAvailableHeight).Height;
            }
            finally
            {
                scratch.Rewind(scratchMark);
            }
        }

        if (IsTableRowCollection(resolvedChildren))
        {
            var tableGrid = CreateTableGridLayout(resolvedChildren, childAvailableWidth, childAvailableHeight, style.Gap);
            return tableGrid.Height + style.PaddingTop + style.PaddingBottom;
        }

        if (style.Display == HtmlDisplay.Block && ContainsFloatChildren(resolvedChildren))
        {
            var scratchMark = scratch.Mark();
            try
            {
                var floatRequests = CreateFloatMeasureRequests(resolvedChildren, childAvailableWidth, childAvailableHeight);
                var floatContent = MeasureFloatContent(style, resolvedChildren, floatRequests, containerWidth > 0 ? containerWidth : availableWidth, wrapLines: true);
                return floatContent.Height + style.PaddingBottom;
            }
            finally
            {
                scratch.Rewind(scratchMark);
            }
        }

        var layoutScratchMark = scratch.Mark();
        try
        {
            var childRequests = scratch.AllocateRequests(resolvedChildren.Length);
            var nodeIsFlexContainer = IsFlexContainer(style);
            var allowChildFlexShrink = ShouldAllowChildFlexShrink(style);
            for (var index = 0; index < resolvedChildren.Length; index++)
                childRequests[index] = CreateLayoutRequest(resolvedChildren[index], childAvailableWidth, childAvailableHeight, nodeIsFlexContainer, style.FlexDirection, allowChildFlexShrink);

            childRequests = ApplyBlockMarginCollapse(style, childRequests);
            if (FlexLayout.ResolveAxis(style.FlexDirection) == LayoutAxis.Row)
                ResolveRowAutoHeights(resolvedChildren, childRequests, childAvailableWidth, childAvailableHeight, style.Gap, style.AlignItems);

            var layoutWidth = containerWidth > 0 ? containerWidth : availableWidth;
            var layoutHeight = containerHeight > 0
                ? containerHeight
                : FlexLayout.ResolveAxis(style.FlexDirection) == LayoutAxis.Row
                    ? 0
                    : availableHeight;
            var containerStyle = CreateLayoutContainerStyle(style);
            var frames = CalculateFrames(containerStyle, childRequests, layoutWidth, layoutHeight);
            if (TryResolveAutoHeightRequests(resolvedChildren, childRequests, frames, childAvailableHeight, style.FlexDirection, style.AlignItems, out var resolvedRequests))
            {
                childRequests = resolvedRequests;
                frames = CalculateFrames(containerStyle, childRequests, layoutWidth, layoutHeight);
            }

            var contentBottom = 0f;
            for (var index = 0; index < childRequests.Length; index++)
            {
                if (frames[index] is not { } frame)
                    continue;

                ref readonly var request = ref childRequests[index];
                contentBottom = Math.Max(contentBottom, frame.Top + frame.Height + request.MarginBottom);
            }

            return contentBottom + style.PaddingBottom;
        }
        finally
        {
            scratch.Rewind(layoutScratchMark);
        }
    }

    private (float Width, float Height) MeasureNodeIntrinsicSize(HtmlSceneNode node, float availableWidth, float availableHeight, bool parentIsFlexContainer = false, FlexDirection parentFlexDirection = FlexDirection.Column)
    {
        if (node.Children.Length == 0)
            return (0, 0);

        var cacheKey = CreateLayoutMeasureCacheKey(node, availableWidth, availableHeight, parentIsFlexContainer, parentFlexDirection, LayoutRunMode.ComputeSize);
        if (measurementCache.TryGetIntrinsicSize(cacheKey, out var cached))
            return cached;

        var style = node.Style;
        var explicitWidth = ResolveExplicitOuterSize(style, style.Width, style.IsWidthPercent, availableWidth, horizontal: true);
        var explicitHeight = ResolveExplicitOuterSize(style, style.Height, style.IsHeightPercent, availableHeight, horizontal: false);
        var containerWidth = LayoutValue.IsSet(explicitWidth)
            ? explicitWidth
            : style.ShouldUseFullWidthByDefaultInParent(parentIsFlexContainer, parentFlexDirection)
                ? availableWidth
                : 0;
        var containerHeight = LayoutValue.IsSet(explicitHeight) ? explicitHeight : 0;
        var childAvailableWidth = Math.Max(0, (containerWidth > 0 ? containerWidth : availableWidth) - style.PaddingLeft - style.PaddingRight);
        var childAvailableHeight = Math.Max(0, (containerHeight > 0 ? containerHeight : availableHeight) - style.PaddingTop - style.PaddingBottom);

        var resolvedChildren = ResolveContainerPercentUnits(node.Children, childAvailableWidth);
        if (IsInlineFormattingContext(style, resolvedChildren))
        {
            (float Width, float Height) measuredInline;
            var scratchMark = scratch.Mark();
            try
            {
                var lineLayout = CreateInlineLineLayout(style, resolvedChildren, childAvailableWidth, childAvailableHeight);
                measuredInline = (lineLayout.Width, lineLayout.Height);
            }
            finally
            {
                scratch.Rewind(scratchMark);
            }

            measurementCache.SetIntrinsicSize(cacheKey, measuredInline);
            return measuredInline;
        }

        if (IsTableRowCollection(resolvedChildren))
        {
            var tableGrid = CreateTableGridLayout(resolvedChildren, childAvailableWidth, childAvailableHeight, style.Gap);
            var measuredTable = (
                tableGrid.Width + style.PaddingLeft + style.PaddingRight,
                tableGrid.Height + style.PaddingTop + style.PaddingBottom);
            measurementCache.SetIntrinsicSize(cacheKey, measuredTable);
            return measuredTable;
        }

        if (style.Display == HtmlDisplay.Block && ContainsFloatChildren(resolvedChildren))
        {
            var scratchMark = scratch.Mark();
            try
            {
                var floatRequests = CreateFloatMeasureRequests(resolvedChildren, childAvailableWidth, childAvailableHeight);
                var floatLayoutWidth = containerWidth > 0 ? containerWidth : availableWidth;
                var floatContent = MeasureFloatContent(style, resolvedChildren, floatRequests, floatLayoutWidth, wrapLines: true);
                var measuredFloat = (
                    floatContent.Width + style.PaddingRight,
                    floatContent.Height + style.PaddingBottom);
                measurementCache.SetIntrinsicSize(cacheKey, measuredFloat);
                return measuredFloat;
            }
            finally
            {
                scratch.Rewind(scratchMark);
            }
        }

        var layoutScratchMark = scratch.Mark();
        try
        {
            var childRequests = scratch.AllocateRequests(resolvedChildren.Length);
            var nodeIsFlexContainer = IsFlexContainer(style);
            var allowChildFlexShrink = ShouldAllowChildFlexShrink(style);
            for (var index = 0; index < resolvedChildren.Length; index++)
                childRequests[index] = CreateLayoutRequest(resolvedChildren[index], childAvailableWidth, childAvailableHeight, nodeIsFlexContainer, style.FlexDirection, allowChildFlexShrink);

            childRequests = ApplyBlockMarginCollapse(style, childRequests);

            if (FlexLayout.ResolveAxis(style.FlexDirection) == LayoutAxis.Row)
                ResolveRowAutoHeights(resolvedChildren, childRequests, childAvailableWidth, childAvailableHeight, style.Gap, style.AlignItems);

            var layoutWidth = containerWidth > 0 ? containerWidth : availableWidth;
            var layoutHeight = containerHeight > 0
                ? containerHeight
                : FlexLayout.ResolveAxis(style.FlexDirection) == LayoutAxis.Row
                    ? 0
                    : availableHeight;
            var containerStyle = CreateLayoutContainerStyle(style);
            var resolvedLayoutFrames = CalculateFrames(containerStyle, childRequests, layoutWidth, layoutHeight);
            if (TryResolveAutoHeightRequests(resolvedChildren, childRequests, resolvedLayoutFrames, childAvailableHeight, style.FlexDirection, style.AlignItems, out var resolvedRequests))
                childRequests = resolvedRequests;

            var intrinsicRequests = CreateIntrinsicMeasurementRequests(childRequests);
            var frames = CalculateFrames(containerStyle, intrinsicRequests, layoutWidth, layoutHeight);
            if (TryResolveAutoHeightRequests(resolvedChildren, intrinsicRequests, frames, childAvailableHeight, style.FlexDirection, style.AlignItems, out resolvedRequests))
            {
                intrinsicRequests = resolvedRequests;
                frames = CalculateFrames(containerStyle, intrinsicRequests, layoutWidth, layoutHeight);
            }

            var contentRight = 0f;
            var contentBottom = 0f;
            for (var index = 0; index < intrinsicRequests.Length; index++)
            {
                if (frames[index] is not { } frame)
                    continue;

                ref readonly var request = ref intrinsicRequests[index];
                contentRight = Math.Max(contentRight, frame.Left + frame.Width + request.MarginRight);
                contentBottom = Math.Max(contentBottom, frame.Top + frame.Height + request.MarginBottom);
            }

            var measurement = layoutCalculator.ComputeFlexLayout(
                LayoutInput.Definite(layoutWidth, layoutHeight, LayoutRunMode.ComputeSize),
                CreateLayoutContainerStyle(style),
                intrinsicRequests,
                []);
            contentRight = Math.Max(contentRight, measurement.ContentSize.Width);
            contentBottom = Math.Max(contentBottom, measurement.ContentSize.Height);

            var measured = (
                contentRight + style.PaddingRight,
                contentBottom + style.PaddingBottom);
            measurementCache.SetIntrinsicSize(cacheKey, measured);
            return measured;
        }
        finally
        {
            scratch.Rewind(layoutScratchMark);
        }
    }

    private static LayoutCacheKey CreateLayoutMeasureCacheKey(
        HtmlSceneNode node,
        float availableWidth,
        float availableHeight,
        bool parentIsFlexContainer,
        FlexDirection parentFlexDirection,
        LayoutRunMode runMode)
    {
        var quantizedWidth = QuantizeMeasureKey(availableWidth);
        var quantizedHeight = QuantizeMeasureKey(availableHeight);
        var keyWidth = DequantizeMeasureKey(quantizedWidth);
        var keyHeight = DequantizeMeasureKey(quantizedHeight);
        var input = new LayoutInput(
            new LayoutKnownSize(null, null),
            new LayoutKnownSize(keyWidth, keyHeight),
            new LayoutAvailableSize(
                LayoutAvailableSpace.Definite(keyWidth),
                LayoutAvailableSpace.Definite(keyHeight)),
            runMode);
        return new LayoutCacheKey(
            HtmlLayoutVersion.ToLayoutNodeId(node.Id),
            node.StyleVersion,
            node.LayoutVersion,
            input,
            CreateLayoutContainerStyle(node.Style),
            CreateMeasureContextVersion(quantizedWidth, quantizedHeight, parentIsFlexContainer, parentFlexDirection, runMode));
    }

    private static uint CreateMeasureContextVersion(
        int quantizedWidth,
        int quantizedHeight,
        bool parentIsFlexContainer,
        FlexDirection parentFlexDirection,
        LayoutRunMode runMode)
    {
        var hash = new HashCode();
        hash.Add(quantizedWidth);
        hash.Add(quantizedHeight);
        hash.Add(parentIsFlexContainer);
        hash.Add(parentFlexDirection);
        hash.Add(runMode);
        return unchecked((uint)hash.ToHashCode());
    }

    private static int QuantizeMeasureKey(float value)
        => float.IsFinite(value) ? (int)MathF.Round(value * 2f) : 0;

    private static float DequantizeMeasureKey(int value)
        => value * 0.5f;

    private void ResolveRowAutoHeights(
        HtmlSceneNode[] children,
        Span<LayoutChildRequest> childRequests,
        float availableWidth,
        float availableHeight,
        float gap,
        CrossAlignment parentAlignItems)
    {
        if (availableWidth <= 0 || childRequests.Length == 0)
            return;

        var totalBaseWidth = 0f;
        var totalGrow = 0f;
        var totalShrinkWeight = 0f;
        var baseWidths = childRequests.Length <= 256
            ? stackalloc float[childRequests.Length]
            : scratch.AllocateFloats(childRequests.Length);
        for (var index = 0; index < childRequests.Length; index++)
        {
            ref readonly var request = ref childRequests[index];
            var baseWidth = ResolveRowBaseWidth(in request, availableWidth);
            baseWidths[index] = baseWidth;
            totalBaseWidth += request.MarginLeft + baseWidth + request.MarginRight;
            if (request.FlexGrow > 0)
                totalGrow += request.FlexGrow;
            if (request.FlexShrink > 0)
                totalShrinkWeight += request.FlexShrink * Math.Max(0, baseWidth);
        }

        if (childRequests.Length > 1)
            totalBaseWidth += gap * (childRequests.Length - 1);

        var remainingWidth = availableWidth - totalBaseWidth;
        var measuredHeights = Span<float>.Empty;
        var maxStretchHeight = 0f;
        for (var index = 0; index < childRequests.Length; index++)
        {
            var child = children[index];
            if (LayoutValue.IsSet(child.Style.Height))
                continue;

            ref readonly var request = ref childRequests[index];
            var resolvedWidth = ResolveFlexibleWidth(baseWidths[index], in request, remainingWidth, totalGrow, totalShrinkWeight);
            if (resolvedWidth <= 0)
                continue;

            var height = MeasureAutoHeightForResolvedWidth(child, resolvedWidth, Math.Max(availableHeight, resolvedWidth));
            if (height <= 0)
                continue;

            if (ResolveCrossAlignment(parentAlignItems, child.Style.AlignSelf) == CrossAlignment.Stretch)
            {
                if (measuredHeights.IsEmpty)
                    measuredHeights = scratch.AllocateFloats(childRequests.Length);
                measuredHeights[index] = height;
                maxStretchHeight = Math.Max(maxStretchHeight, height);
            }
            else
            {
                childRequests[index] = WithHeight(in request, height);
            }
        }

        if (measuredHeights.IsEmpty || maxStretchHeight <= 0)
            return;

        for (var index = 0; index < measuredHeights.Length; index++)
        {
            if (measuredHeights[index] > 0)
                childRequests[index] = WithHeight(in childRequests[index], maxStretchHeight);
        }
    }

    private float MeasureAutoHeightForResolvedWidth(HtmlSceneNode node, float resolvedWidth, float availableHeight)
    {
        if (node.Children.Length > 0)
            return MeasureNodeIntrinsicSize(node, resolvedWidth, availableHeight, parentIsFlexContainer: true, parentFlexDirection: FlexDirection.Row).Height;

        if (node.NodeKind != SceneNodeKind.Text || string.IsNullOrEmpty(node.TextContent))
            return 0;

        var style = node.Style;
        var textStyle = textStyleCache.GetTextStyle(style);
        var contentWidth = Math.Max(0, resolvedWidth - style.PaddingLeft - style.PaddingRight - style.BorderWidth * 2);
        var textHeight = style.WrapText
            ? textServices.MeasureTextHeight(node.TextContent, contentWidth, textStyle)
            : ResolveNormalLineHeight(textStyle.Font, style.LineHeight);
        return textHeight + style.PaddingTop + style.PaddingBottom + style.BorderWidth * 2;
    }

    private SceneFont CreateSceneFont(HtmlComputedStyle style, float defaultSize, int defaultWeight)
        => textStyleCache.GetFont(style, defaultSize, defaultWeight);

    private static bool ShouldAllowChildFlexShrink(HtmlComputedStyle parentStyle)
    {
        if (!IsFlexContainer(parentStyle))
            return false;

        if (FlexLayout.ResolveAxis(parentStyle.FlexDirection) == LayoutAxis.Row)
            return true;

        return LayoutValue.IsSet(parentStyle.Height) && !parentStyle.IsScrollContainer;
    }

    private Span<LayoutChildRequest> CreateIntrinsicMeasurementRequests(Span<LayoutChildRequest> childRequests)
    {
        var hasFlex = false;
        for (var index = 0; index < childRequests.Length; index++)
        {
            ref readonly var request = ref childRequests[index];
            if (request.FlexGrow > 0 || request.FlexShrink > 0)
            {
                hasFlex = true;
                break;
            }
        }

        if (!hasFlex)
            return childRequests;

        var measurementRequests = scratch.AllocateRequests(childRequests.Length);
        for (var index = 0; index < childRequests.Length; index++)
            measurementRequests[index] = WithFlex(in childRequests[index], 0, 0);
        return measurementRequests;
    }

    private Span<LayoutChildRequest> ApplyBlockMarginCollapse(HtmlComputedStyle parentStyle, Span<LayoutChildRequest> childRequests)
    {
        if (parentStyle.Display != HtmlDisplay.Block || childRequests.Length < 2)
            return childRequests;

        var collapsedRequests = scratch.AllocateRequests(childRequests.Length);
        childRequests.CopyTo(collapsedRequests);
        var changed = false;
        for (var index = 1; index < collapsedRequests.Length; index++)
        {
            ref readonly var previous = ref collapsedRequests[index - 1];
            ref readonly var current = ref collapsedRequests[index];
            var collapsedMargin = Math.Max(previous.MarginBottom, current.MarginTop);
            if (MathF.Abs(previous.MarginBottom) < 0.01f &&
                MathF.Abs(current.MarginTop - collapsedMargin) < 0.01f)
            {
                continue;
            }

            collapsedRequests[index - 1] = WithMargins(in previous, previous.MarginTop, 0);
            collapsedRequests[index] = WithMargins(in current, collapsedMargin, current.MarginBottom);
            changed = true;
        }

        return changed ? collapsedRequests : childRequests;
    }

    private static LayoutChildRequest WithHeight(in LayoutChildRequest request, float height)
        => new(
            request.Kind,
            request.Left,
            request.Top,
            request.Right,
            request.Bottom,
            request.Width,
            height,
            request.MinWidth,
            request.MaxWidth,
            request.MinHeight,
            request.MaxHeight,
            request.MarginLeft,
            request.MarginTop,
            request.MarginRight,
            request.MarginBottom,
            request.Text,
            request.FontSize,
            request.FontFamily,
            request.FontWeight,
            request.Wrap,
            request.AlignSelf,
            request.Length,
            request.Thickness,
            request.Vertical,
            request.Size,
            request.FlexGrow,
            request.FlexShrink,
            request.FlexBasis,
            request.Units);

    private static LayoutChildRequest WithWidth(in LayoutChildRequest request, float width)
        => new(
            request.Kind,
            request.Left,
            request.Top,
            request.Right,
            request.Bottom,
            width,
            request.Height,
            request.MinWidth,
            request.MaxWidth,
            request.MinHeight,
            request.MaxHeight,
            request.MarginLeft,
            request.MarginTop,
            request.MarginRight,
            request.MarginBottom,
            request.Text,
            request.FontSize,
            request.FontFamily,
            request.FontWeight,
            request.Wrap,
            request.AlignSelf,
            request.Length,
            request.Thickness,
            request.Vertical,
            request.Size,
            request.FlexGrow,
            request.FlexShrink,
            request.FlexBasis,
            request.Units,
            request.Italic,
            request.Font);

    private static LayoutChildRequest WithFlex(in LayoutChildRequest request, float flexGrow, float flexShrink)
        => new(
            request.Kind,
            request.Left,
            request.Top,
            request.Right,
            request.Bottom,
            request.Width,
            request.Height,
            request.MinWidth,
            request.MaxWidth,
            request.MinHeight,
            request.MaxHeight,
            request.MarginLeft,
            request.MarginTop,
            request.MarginRight,
            request.MarginBottom,
            request.Text,
            request.FontSize,
            request.FontFamily,
            request.FontWeight,
            request.Wrap,
            request.AlignSelf,
            request.Length,
            request.Thickness,
            request.Vertical,
            request.Size,
            flexGrow,
            flexShrink,
            request.FlexBasis,
            request.Units);

    private static LayoutChildRequest WithMargins(in LayoutChildRequest request, float marginTop, float marginBottom)
        => new(
            request.Kind,
            request.Left,
            request.Top,
            request.Right,
            request.Bottom,
            request.Width,
            request.Height,
            request.MinWidth,
            request.MaxWidth,
            request.MinHeight,
            request.MaxHeight,
            request.MarginLeft,
            marginTop,
            request.MarginRight,
            marginBottom,
            request.Text,
            request.FontSize,
            request.FontFamily,
            request.FontWeight,
            request.Wrap,
            request.AlignSelf,
            request.Length,
            request.Thickness,
            request.Vertical,
            request.Size,
            request.FlexGrow,
            request.FlexShrink,
            request.FlexBasis,
            request.Units);

    private static float ResolveExplicitSize(float value, bool isPercent, float relativeTo)
        => LayoutValue.IsSet(value)
            ? LayoutValue.Resolve(value, isPercent, relativeTo)
            : float.NaN;

    private static float ResolveExplicitOuterSize(HtmlComputedStyle style, float value, bool isPercent, float relativeTo, bool horizontal)
    {
        var resolved = ResolveExplicitSize(value, isPercent, relativeTo);
        return AdjustResolvedSizeForBoxSizing(style, resolved, horizontal);
    }

    private static float AdjustResolvedSizeForBoxSizing(HtmlComputedStyle style, float resolved, bool horizontal)
    {
        if (!LayoutValue.IsSet(resolved) || style.BoxSizing != SceneBoxSizing.ContentBox)
            return resolved;

        var insets = horizontal
            ? style.PaddingLeft + style.PaddingRight + style.BorderWidth * 2
            : style.PaddingTop + style.PaddingBottom + style.BorderWidth * 2;
        return resolved + insets;
    }

    private static float ResolveRowBaseWidth(in LayoutChildRequest request, float availableWidth)
    {
        float width;
        if (request.HasFlexBasis)
            width = LayoutValue.Resolve(request.FlexBasis, request.IsFlexBasisPercent, availableWidth);
        else if (request.HasWidth)
            width = LayoutValue.Resolve(request.Width, request.IsWidthPercent, availableWidth);
        else
            width = 0;

        return ClampRowBaseWidth(Math.Max(0, width), request, availableWidth);
    }

    private static float ClampRowBaseWidth(float width, in LayoutChildRequest request, float availableWidth)
    {
        if (request.HasMinWidth)
            width = Math.Max(width, LayoutValue.Resolve(request.MinWidth, request.IsMinWidthPercent, availableWidth));
        if (request.HasMaxWidth)
            width = Math.Min(width, LayoutValue.Resolve(request.MaxWidth, request.IsMaxWidthPercent, availableWidth));
        return Math.Max(0, width);
    }

    private static float ResolveFlexibleWidth(
        float baseWidth,
        in LayoutChildRequest request,
        float remainingWidth,
        float totalGrow,
        float totalShrinkWeight)
    {
        if (remainingWidth > 0 && request.FlexGrow > 0 && totalGrow > 0)
            return baseWidth + remainingWidth * (request.FlexGrow / totalGrow);

        if (remainingWidth < 0 && request.FlexShrink > 0 && totalShrinkWeight > 0)
        {
            var shrinkWeight = request.FlexShrink * Math.Max(0, baseWidth);
            return Math.Max(0, baseWidth + remainingWidth * (shrinkWeight / totalShrinkWeight));
        }

        return baseWidth;
    }
}

