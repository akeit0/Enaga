using System.Globalization;
using Enaga.Html.Css;
using Enaga.Html.Dom;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

internal sealed partial class HtmlComputedStyle
{
    static readonly HtmlLengthProperty[] lengthProperties = Enum.GetValues<HtmlLengthProperty>();
    public HtmlDisplay Display { get; private set; } = Defaults.Display;
    public bool HasExplicitDisplay { get; private set; }
    public FlexDirection FlexDirection { get; private set; } = Defaults.FlexDirection;
    public FlexWrap FlexWrap { get; private set; } = Defaults.FlexWrap;
    public LayoutDirection Direction { get; private set; } = Defaults.Direction;
    public MainAxisJustification JustifyContent { get; private set; } = Defaults.JustifyContent;
    public CrossAlignment AlignItems { get; private set; } = Defaults.AlignItems;
    public CrossAlignment AlignSelf { get; private set; } = Defaults.AlignSelf;
    public int Order { get; private set; }
    public PositionMode Position { get; private set; } = Defaults.Position;
    public SceneBoxSizing BoxSizing { get; private set; } = Defaults.BoxSizing;
    public float Left { get; private set; } = Defaults.UnsetLength;
    public float Top { get; private set; } = Defaults.UnsetLength;
    public float Right { get; private set; } = Defaults.UnsetLength;
    public float Bottom { get; private set; } = Defaults.UnsetLength;
    public float Width { get; private set; } = Defaults.UnsetLength;
    public float Height { get; private set; } = Defaults.UnsetLength;
    public float MinWidth { get; private set; } = Defaults.UnsetLength;
    public float MaxWidth { get; private set; } = Defaults.UnsetLength;
    public float MinHeight { get; private set; } = Defaults.UnsetLength;
    public float MaxHeight { get; private set; } = Defaults.UnsetLength;
    public float MarginLeft { get; private set; }
    public float MarginTop { get; private set; }
    public float MarginRight { get; private set; }
    public float MarginBottom { get; private set; }
    public float PaddingLeft { get; private set; }
    public float PaddingTop { get; private set; }
    public float PaddingRight { get; private set; }
    public float PaddingBottom { get; private set; }
    public float Gap { get; private set; }
    public float TableBorderSpacing { get; private set; } = Defaults.UnsetLength;
    public bool TableBorderCollapse { get; private set; }
    public float BorderWidth { get; private set; }
    public float BorderLeftWidth { get; private set; }
    public float BorderTopWidth { get; private set; }
    public float BorderRightWidth { get; private set; }
    public float BorderBottomWidth { get; private set; }
    public SceneBorderStyle BorderStyle { get; private set; } = Defaults.BorderStyle;
    public SceneBorderStyle BorderLeftStyle { get; private set; } = Defaults.BorderStyle;
    public SceneBorderStyle BorderTopStyle { get; private set; } = Defaults.BorderStyle;
    public SceneBorderStyle BorderRightStyle { get; private set; } = Defaults.BorderStyle;
    public SceneBorderStyle BorderBottomStyle { get; private set; } = Defaults.BorderStyle;
    public float BorderRadius { get; private set; }
    public float FontSize { get; private set; }
    public int FontWeight { get; private set; }
    public float FlexGrow { get; private set; }
    public float FlexShrink { get; private set; } = Defaults.FlexShrink;
    public float FlexBasis { get; private set; } = Defaults.UnsetLength;
    public HtmlFloat Float { get; private set; } = Defaults.Float;
    public HtmlClear Clear { get; private set; } = Defaults.Clear;
    public float LineHeight { get; private set; }
    public string? FontFamily { get; private set; }
    public string? BackgroundColor { get; private set; }
    public string? BackgroundImageSource { get; private set; }
    public string? BackgroundImageFit { get; private set; }
    public SceneBoxShadow[]? BackgroundShadows { get; private set; }
    public string? BorderColor { get; private set; }
    public string? BorderLeftColor { get; private set; }
    public string? BorderTopColor { get; private set; }
    public string? BorderRightColor { get; private set; }
    public string? BorderBottomColor { get; private set; }
    public string? Color { get; private set; }
    public bool HasExplicitColor { get; private set; }
    public bool HasExplicitTextAlign { get; private set; }
    public string? PlaceholderColor { get; private set; }
    public string? ImageFit { get; private set; }
    public float IntrinsicImageWidth { get; private set; } = Defaults.UnsetLength;
    public float IntrinsicImageHeight { get; private set; } = Defaults.UnsetLength;
    public float ImageAspectRatio { get; private set; } = Defaults.UnsetLength;
    public SceneTextAlign TextAlign { get; private set; } = SceneTextAlign.Left;
    public HtmlWhiteSpace WhiteSpace { get; private set; } = HtmlWhiteSpace.Normal;
    public HtmlTextTransform TextTransform { get; private set; } = HtmlTextTransform.None;
    public bool WrapText { get; private set; } = Defaults.WrapText;
    public bool TextOverflowEllipsis { get; private set; }
    public bool Underline { get; private set; }
    public bool Italic { get; private set; }
    public SceneBoxShadow[]? TextShadows { get; private set; }
    public bool HasExplicitTextDecoration { get; private set; }
    public bool SuppressListMarker { get; private set; }
    public string UnorderedListMarkerText { get; private set; } = Defaults.UnorderedListMarkerText;
    public bool ClipContent { get; private set; }
    public bool IsScrollContainer { get; private set; }
    public HtmlContainment Containment { get; private set; }
    public bool Multiline { get; private set; }
    public bool PreferIntrinsicWidth { get; private set; }
    public float ScrollbarWidth { get; private set; } = Defaults.ScrollbarWidth;
    public string? ScrollbarTrackColor { get; private set; }
    public string? ScrollbarThumbColor { get; private set; }
    public LayoutValueUnitFlags UnitFlags { get; private set; }
    private HtmlViewportLengthFlags viewportLengthFlags;
    private HtmlContainerPercentLengthFlags containerPercentLengthFlags;
    private HtmlExplicitLengthFlags explicitLengthFlags;
    private float fontSizeReference;
    private float rootFontSize;
    private string? inheritedColor;
    public bool IsWidthPercent => (UnitFlags & LayoutValueUnitFlags.WidthPercent) != 0;
    public bool IsHeightPercent => (UnitFlags & LayoutValueUnitFlags.HeightPercent) != 0;
    public bool IsMinWidthPercent => (UnitFlags & LayoutValueUnitFlags.MinWidthPercent) != 0;
    public bool IsMaxWidthPercent => (UnitFlags & LayoutValueUnitFlags.MaxWidthPercent) != 0;
    public bool HasExplicitWidth => IsLengthExplicit(HtmlLengthProperty.Width);
    public bool HasExplicitHeight => IsLengthExplicit(HtmlLengthProperty.Height);
    public bool StopsLayoutDirtyPropagation =>
        (Containment & HtmlContainment.Size) != 0 ||
        (LayoutValue.IsSet(Width) &&
         LayoutValue.IsSet(Height) &&
         !IsWidthPercent &&
         !IsHeightPercent &&
         (IsScrollContainer || ClipContent));
    public bool ShouldUseFullWidthByDefault => Display is HtmlDisplay.Block or HtmlDisplay.Flex && !LayoutValue.IsSet(Width);
    public bool ShouldUseFullWidthByDefaultInParent(bool parentIsFlexContainer, FlexDirection parentFlexDirection = FlexDirection.Column)
    {
        if (LayoutValue.IsSet(Width))
            return false;

        if (PreferIntrinsicWidth)
            return false;

        if (!parentIsFlexContainer)
            return Display is HtmlDisplay.Block or HtmlDisplay.Flex;

        return FlexLayout.ResolveAxis(parentFlexDirection) == LayoutAxis.Column;
    }
    public bool CanCollapseTextOnlyContent =>
        string.IsNullOrWhiteSpace(BackgroundColor) &&
        string.IsNullOrWhiteSpace(BackgroundImageSource) &&
        !HasAnyVisibleBorder &&
        BorderRadius <= 0 &&
        PaddingLeft <= 0 &&
        PaddingTop <= 0 &&
        PaddingRight <= 0 &&
        PaddingBottom <= 0 &&
        MarginLeft <= 0 &&
        MarginTop <= 0 &&
        MarginRight <= 0 &&
        MarginBottom <= 0 &&
        !LayoutValue.IsSet(Width) &&
        !LayoutValue.IsSet(Height) &&
        Float == HtmlFloat.None &&
        !IsScrollContainer &&
        !ClipContent;
    public bool HasInlineBoxMetrics =>
        PaddingLeft > 0 ||
        PaddingTop > 0 ||
        PaddingRight > 0 ||
        PaddingBottom > 0 ||
        MarginLeft > 0 ||
        MarginTop > 0 ||
        MarginRight > 0 ||
        MarginBottom > 0 ||
        HasAnyVisibleBorder;

    public bool HasAnyVisibleBorder =>
        BorderWidth > 0 && BorderStyle != Defaults.BorderStyle ||
        BorderLeftWidth > 0 && BorderLeftStyle != Defaults.BorderStyle ||
        BorderTopWidth > 0 && BorderTopStyle != Defaults.BorderStyle ||
        BorderRightWidth > 0 && BorderRightStyle != Defaults.BorderStyle ||
        BorderBottomWidth > 0 && BorderBottomStyle != Defaults.BorderStyle;

    public static HtmlComputedStyle CreateDefault(HtmlOptions options, LayoutEngineConfig layoutConfig)
    {
        return new HtmlComputedStyle
        {
            FontSize = options.DefaultFontSize,
            rootFontSize = options.DefaultFontSize,
            FontFamily = options.DefaultFontFamily,
            FontWeight = options.DefaultFontWeight,
            Color = options.DefaultTextColor,
            Position = layoutConfig.DefaultPositionMode
        };
    }

    public static bool HasSameLayoutIdentity(HtmlComputedStyle left, HtmlComputedStyle right)
        => left.Display == right.Display &&
           left.FlexDirection == right.FlexDirection &&
           left.FlexWrap == right.FlexWrap &&
           left.Direction == right.Direction &&
           left.JustifyContent == right.JustifyContent &&
           left.AlignItems == right.AlignItems &&
           left.AlignSelf == right.AlignSelf &&
           left.Order == right.Order &&
           left.Position == right.Position &&
           left.BoxSizing == right.BoxSizing &&
           Same(left.Left, right.Left) &&
           Same(left.Top, right.Top) &&
           Same(left.Right, right.Right) &&
           Same(left.Bottom, right.Bottom) &&
           Same(left.Width, right.Width) &&
           Same(left.Height, right.Height) &&
           Same(left.MinWidth, right.MinWidth) &&
           Same(left.MaxWidth, right.MaxWidth) &&
           Same(left.MinHeight, right.MinHeight) &&
           Same(left.MaxHeight, right.MaxHeight) &&
           Same(left.MarginLeft, right.MarginLeft) &&
           Same(left.MarginTop, right.MarginTop) &&
           Same(left.MarginRight, right.MarginRight) &&
           Same(left.MarginBottom, right.MarginBottom) &&
           Same(left.PaddingLeft, right.PaddingLeft) &&
           Same(left.PaddingTop, right.PaddingTop) &&
           Same(left.PaddingRight, right.PaddingRight) &&
           Same(left.PaddingBottom, right.PaddingBottom) &&
           Same(left.Gap, right.Gap) &&
           Same(left.TableBorderSpacing, right.TableBorderSpacing) &&
           left.TableBorderCollapse == right.TableBorderCollapse &&
           Same(left.BorderWidth, right.BorderWidth) &&
           Same(left.BorderLeftWidth, right.BorderLeftWidth) &&
           Same(left.BorderTopWidth, right.BorderTopWidth) &&
           Same(left.BorderRightWidth, right.BorderRightWidth) &&
           Same(left.BorderBottomWidth, right.BorderBottomWidth) &&
           left.BorderStyle == right.BorderStyle &&
           left.BorderLeftStyle == right.BorderLeftStyle &&
           left.BorderTopStyle == right.BorderTopStyle &&
           left.BorderRightStyle == right.BorderRightStyle &&
           left.BorderBottomStyle == right.BorderBottomStyle &&
           Same(left.BorderRadius, right.BorderRadius) &&
           Same(left.FontSize, right.FontSize) &&
           left.FontWeight == right.FontWeight &&
           string.Equals(left.FontFamily, right.FontFamily, StringComparison.Ordinal) &&
           Same(left.FlexGrow, right.FlexGrow) &&
           Same(left.FlexShrink, right.FlexShrink) &&
           Same(left.FlexBasis, right.FlexBasis) &&
           left.Float == right.Float &&
           left.Clear == right.Clear &&
           Same(left.LineHeight, right.LineHeight) &&
           string.Equals(left.ImageFit, right.ImageFit, StringComparison.Ordinal) &&
           Same(left.IntrinsicImageWidth, right.IntrinsicImageWidth) &&
           Same(left.IntrinsicImageHeight, right.IntrinsicImageHeight) &&
           Same(left.ImageAspectRatio, right.ImageAspectRatio) &&
           left.TextAlign == right.TextAlign &&
           left.HasExplicitTextAlign == right.HasExplicitTextAlign &&
           left.WhiteSpace == right.WhiteSpace &&
           left.TextTransform == right.TextTransform &&
           left.WrapText == right.WrapText &&
           left.TextOverflowEllipsis == right.TextOverflowEllipsis &&
           left.Underline == right.Underline &&
           left.Italic == right.Italic &&
           left.SuppressListMarker == right.SuppressListMarker &&
           string.Equals(left.UnorderedListMarkerText, right.UnorderedListMarkerText, StringComparison.Ordinal) &&
           left.ClipContent == right.ClipContent &&
           left.IsScrollContainer == right.IsScrollContainer &&
           left.Containment == right.Containment &&
           left.Multiline == right.Multiline &&
           left.PreferIntrinsicWidth == right.PreferIntrinsicWidth &&
           Same(left.ScrollbarWidth, right.ScrollbarWidth) &&
           string.Equals(left.ScrollbarTrackColor, right.ScrollbarTrackColor, StringComparison.Ordinal) &&
           string.Equals(left.ScrollbarThumbColor, right.ScrollbarThumbColor, StringComparison.Ordinal) &&
           left.UnitFlags == right.UnitFlags;

    public static bool HasSameStyleSharingIdentity(HtmlComputedStyle left, HtmlComputedStyle right)
        => HasSameLayoutIdentity(left, right) &&
           left.HasExplicitDisplay == right.HasExplicitDisplay &&
           left.HasExplicitColor == right.HasExplicitColor &&
           left.HasExplicitTextDecoration == right.HasExplicitTextDecoration &&
           left.HasAnyVisibleBorder == right.HasAnyVisibleBorder &&
           left.viewportLengthFlags == right.viewportLengthFlags &&
           left.containerPercentLengthFlags == right.containerPercentLengthFlags &&
           left.explicitLengthFlags == right.explicitLengthFlags &&
           Same(left.fontSizeReference, right.fontSizeReference) &&
           Same(left.rootFontSize, right.rootFontSize) &&
           string.Equals(left.inheritedColor, right.inheritedColor, StringComparison.Ordinal) &&
           string.Equals(left.BackgroundColor, right.BackgroundColor, StringComparison.Ordinal) &&
           string.Equals(left.BackgroundImageSource, right.BackgroundImageSource, StringComparison.Ordinal) &&
           string.Equals(left.BackgroundImageFit, right.BackgroundImageFit, StringComparison.Ordinal) &&
           SameShadows(left.BackgroundShadows, right.BackgroundShadows) &&
           string.Equals(left.BorderColor, right.BorderColor, StringComparison.Ordinal) &&
           string.Equals(left.BorderLeftColor, right.BorderLeftColor, StringComparison.Ordinal) &&
           string.Equals(left.BorderTopColor, right.BorderTopColor, StringComparison.Ordinal) &&
           string.Equals(left.BorderRightColor, right.BorderRightColor, StringComparison.Ordinal) &&
           string.Equals(left.BorderBottomColor, right.BorderBottomColor, StringComparison.Ordinal) &&
           string.Equals(left.Color, right.Color, StringComparison.Ordinal) &&
           string.Equals(left.PlaceholderColor, right.PlaceholderColor, StringComparison.Ordinal) &&
           SameShadows(left.TextShadows, right.TextShadows);

    public int GetStyleSharingHash()
    {
        var hash = new HashCode();
        hash.Add(Display);
        hash.Add(FlexDirection);
        hash.Add(FlexWrap);
        hash.Add(Direction);
        hash.Add(JustifyContent);
        hash.Add(AlignItems);
        hash.Add(AlignSelf);
        hash.Add(Order);
        hash.Add(Position);
        hash.Add(BoxSizing);
        hash.Add(Left);
        hash.Add(Top);
        hash.Add(Right);
        hash.Add(Bottom);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(MinWidth);
        hash.Add(MaxWidth);
        hash.Add(MinHeight);
        hash.Add(MaxHeight);
        hash.Add(MarginLeft);
        hash.Add(MarginTop);
        hash.Add(MarginRight);
        hash.Add(MarginBottom);
        hash.Add(PaddingLeft);
        hash.Add(PaddingTop);
        hash.Add(PaddingRight);
        hash.Add(PaddingBottom);
        hash.Add(Gap);
        hash.Add(TableBorderSpacing);
        hash.Add(TableBorderCollapse);
        hash.Add(BorderWidth);
        hash.Add(BorderLeftWidth);
        hash.Add(BorderTopWidth);
        hash.Add(BorderRightWidth);
        hash.Add(BorderBottomWidth);
        hash.Add(BorderStyle);
        hash.Add(BorderLeftStyle);
        hash.Add(BorderTopStyle);
        hash.Add(BorderRightStyle);
        hash.Add(BorderBottomStyle);
        hash.Add(BorderRadius);
        hash.Add(FontSize);
        hash.Add(FontWeight);
        hash.Add(FontFamily);
        hash.Add(FlexGrow);
        hash.Add(FlexShrink);
        hash.Add(FlexBasis);
        hash.Add(Float);
        hash.Add(Clear);
        hash.Add(LineHeight);
        hash.Add(ImageFit);
        hash.Add(IntrinsicImageWidth);
        hash.Add(IntrinsicImageHeight);
        hash.Add(ImageAspectRatio);
        hash.Add(TextAlign);
        hash.Add(HasExplicitTextAlign);
        hash.Add(WhiteSpace);
        hash.Add(TextTransform);
        hash.Add(WrapText);
        hash.Add(TextOverflowEllipsis);
        hash.Add(Underline);
        hash.Add(Italic);
        hash.Add(SuppressListMarker);
        hash.Add(UnorderedListMarkerText);
        hash.Add(ClipContent);
        hash.Add(IsScrollContainer);
        hash.Add(Containment);
        hash.Add(Multiline);
        hash.Add(PreferIntrinsicWidth);
        hash.Add(ScrollbarWidth);
        hash.Add(ScrollbarTrackColor);
        hash.Add(ScrollbarThumbColor);
        hash.Add(UnitFlags);
        hash.Add(HasExplicitDisplay);
        hash.Add(HasExplicitColor);
        hash.Add(HasExplicitTextDecoration);
        hash.Add(viewportLengthFlags);
        hash.Add(containerPercentLengthFlags);
        hash.Add(explicitLengthFlags);
        hash.Add(fontSizeReference);
        hash.Add(rootFontSize);
        hash.Add(inheritedColor);
        hash.Add(BackgroundColor);
        hash.Add(BackgroundImageSource);
        hash.Add(BackgroundImageFit);
        AddShadowsHash(ref hash, BackgroundShadows);
        hash.Add(BorderColor);
        hash.Add(BorderLeftColor);
        hash.Add(BorderTopColor);
        hash.Add(BorderRightColor);
        hash.Add(BorderBottomColor);
        hash.Add(Color);
        hash.Add(PlaceholderColor);
        AddShadowsHash(ref hash, TextShadows);
        return hash.ToHashCode();
    }

    public static bool HasSameOuterLayoutDependency(HtmlComputedStyle left, HtmlComputedStyle right)
        => left.Display == right.Display &&
           left.FlexDirection == right.FlexDirection &&
           left.FlexWrap == right.FlexWrap &&
           left.Direction == right.Direction &&
           left.AlignSelf == right.AlignSelf &&
           left.Order == right.Order &&
           left.Position == right.Position &&
           left.BoxSizing == right.BoxSizing &&
           Same(left.Left, right.Left) &&
           Same(left.Top, right.Top) &&
           Same(left.Right, right.Right) &&
           Same(left.Bottom, right.Bottom) &&
           Same(left.Width, right.Width) &&
           Same(left.Height, right.Height) &&
           Same(left.MinWidth, right.MinWidth) &&
           Same(left.MaxWidth, right.MaxWidth) &&
           Same(left.MinHeight, right.MinHeight) &&
           Same(left.MaxHeight, right.MaxHeight) &&
           Same(left.MarginLeft, right.MarginLeft) &&
           Same(left.MarginTop, right.MarginTop) &&
           Same(left.MarginRight, right.MarginRight) &&
           Same(left.MarginBottom, right.MarginBottom) &&
           Same(left.PaddingLeft, right.PaddingLeft) &&
           Same(left.PaddingTop, right.PaddingTop) &&
           Same(left.PaddingRight, right.PaddingRight) &&
           Same(left.PaddingBottom, right.PaddingBottom) &&
           Same(left.BorderWidth, right.BorderWidth) &&
           Same(left.BorderLeftWidth, right.BorderLeftWidth) &&
           Same(left.BorderTopWidth, right.BorderTopWidth) &&
           Same(left.BorderRightWidth, right.BorderRightWidth) &&
           Same(left.BorderBottomWidth, right.BorderBottomWidth) &&
           Same(left.FlexGrow, right.FlexGrow) &&
           Same(left.FlexShrink, right.FlexShrink) &&
           Same(left.FlexBasis, right.FlexBasis) &&
           left.Float == right.Float &&
           left.Clear == right.Clear &&
           left.IsScrollContainer == right.IsScrollContainer &&
           left.ClipContent == right.ClipContent &&
           left.Containment == right.Containment &&
           left.UnitFlags == right.UnitFlags;

    private static bool Same(float left, float right)
        => left.Equals(right) || float.IsNaN(left) && float.IsNaN(right);

    private static bool SameShadows(SceneBoxShadow[]? left, SceneBoxShadow[]? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Length != right.Length)
            return false;

        for (var index = 0; index < left.Length; index++)
        {
            if (!left[index].Equals(right[index]))
                return false;
        }

        return true;
    }

    private static void AddShadowsHash(ref HashCode hash, SceneBoxShadow[]? shadows)
    {
        if (shadows is null)
        {
            hash.Add(0);
            return;
        }

        hash.Add(shadows.Length);
        for (var index = 0; index < shadows.Length; index++)
            hash.Add(shadows[index]);
    }

    public HtmlComputedStyle CloneWithResolvedViewportUnits(float viewportWidth, float viewportHeight)
    {
        if (viewportLengthFlags == 0)
            return this;

        var clone = Clone();
        clone.ResolveViewportUnits(viewportWidth, viewportHeight);
        return clone;
    }

    public HtmlComputedStyle CloneWithResolvedContainerPercentUnits(float containerWidth, bool resolveInlineSize = true)
    {
        if (containerPercentLengthFlags == 0)
            return this;

        var clone = Clone();
        clone.ResolveContainerPercentUnits(containerWidth, resolveInlineSize);
        return clone;
    }

    internal HtmlComputedStyle CloneForFormatting()
    {
        return Clone();
    }

    public static HtmlComputedStyle CreateInlineRunDefault(HtmlComputedStyle inherited)
    {
        var style = inherited.Clone();
        style.Display = HtmlDisplay.Flex;
        style.FlexDirection = FlexDirection.Row;
        style.FlexWrap = inherited.WrapText ? FlexWrap.Wrap : FlexWrap.NoWrap;
        style.AlignItems = CrossAlignment.Start;
        style.JustifyContent = inherited.TextAlign switch
        {
            SceneTextAlign.Center => MainAxisJustification.Center,
            SceneTextAlign.Right => MainAxisJustification.End,
            _ => MainAxisJustification.Start
        };
        style.Gap = style.Gap > 0 ? style.Gap : MathF.Max(2, style.FontSize * 0.25f);
        style.Width = Defaults.UnsetLength;
        style.Height = Defaults.UnsetLength;
        style.PreferIntrinsicWidth = true;
        style.BackgroundColor = null;
        style.BackgroundImageSource = null;
        style.BackgroundImageFit = null;
        style.BackgroundShadows = null;
        style.BorderColor = null;
        style.BorderWidth = 0;
        style.BorderStyle = Defaults.BorderStyle;
        style.ClearBorderSides();
        style.BorderRadius = 0;
        style.PaddingLeft = 0;
        style.PaddingTop = 0;
        style.PaddingRight = 0;
        style.PaddingBottom = 0;
        style.MarginLeft = 0;
        style.MarginTop = 0;
        style.MarginRight = 0;
        style.MarginBottom = 0;
        style.containerPercentLengthFlags = 0;
        style.explicitLengthFlags = 0;
        return style;
    }

    public static HtmlComputedStyle CreateInlineFlowDefault(HtmlComputedStyle inherited)
    {
        var style = CreateInlineRunDefault(inherited);
        style.PreferIntrinsicWidth = false;
        style.Width = Defaults.FormInputWidth;
        style.SetUnit(LayoutValueUnitFlags.WidthPercent, true);
        return style;
    }

    public static HtmlComputedStyle CreateInlineControlRunDefault(HtmlComputedStyle inherited)
    {
        var style = CreateInlineRunDefault(inherited);
        style.WrapText = false;
        style.FlexWrap = FlexWrap.NoWrap;
        style.PreferIntrinsicWidth = true;
        style.Width = Defaults.UnsetLength;
        style.UnitFlags &= ~LayoutValueUnitFlags.WidthPercent;
        return style;
    }

    public static HtmlComputedStyle CreateAlignedInlineTextDefault(HtmlComputedStyle inherited)
    {
        var style = inherited.Clone();
        style.PreferIntrinsicWidth = false;
        style.Width = Defaults.FormInputWidth;
        style.SetUnit(LayoutValueUnitFlags.WidthPercent, true);
        return style;
    }

    public static HtmlComputedStyle CreateInlineWrappedTextDefault(HtmlComputedStyle inherited)
    {
        var style = inherited.Clone();
        style.WrapText = true;
        style.PreferIntrinsicWidth = false;
        style.Width = Defaults.FormInputWidth;
        style.SetUnit(LayoutValueUnitFlags.WidthPercent, true);
        return style;
    }

    public void ApplyRootDefaults(HtmlOptions options)
    {
        BackgroundColor ??= options.DefaultBackgroundColor;
    }

    public HtmlComputedStyle CreateTextStyle()
    {
        var clone = Clone();
        clone.Display = HtmlDisplay.Block;
        clone.BackgroundColor = null;
        clone.BackgroundImageSource = null;
        clone.BackgroundImageFit = null;
        clone.BackgroundShadows = null;
        clone.BorderColor = null;
        clone.BorderWidth = 0;
        clone.BorderStyle = Defaults.BorderStyle;
        clone.ClearBorderSides();
        clone.BorderRadius = 0;
        clone.PaddingLeft = 0;
        clone.PaddingTop = 0;
        clone.PaddingRight = 0;
        clone.PaddingBottom = 0;
        clone.MarginLeft = 0;
        clone.MarginTop = 0;
        clone.MarginRight = 0;
        clone.MarginBottom = 0;
        clone.Left = Defaults.UnsetLength;
        clone.Top = Defaults.UnsetLength;
        clone.Right = Defaults.UnsetLength;
        clone.Bottom = Defaults.UnsetLength;
        clone.Width = Defaults.UnsetLength;
        clone.Height = Defaults.UnsetLength;
        clone.MinWidth = Defaults.UnsetLength;
        clone.MaxWidth = Defaults.UnsetLength;
        clone.MinHeight = Defaults.UnsetLength;
        clone.MaxHeight = Defaults.UnsetLength;
        clone.FlexGrow = 0;
        clone.FlexShrink = Defaults.FlexShrink;
        clone.FlexBasis = Defaults.UnsetLength;
        clone.UnitFlags = 0;
        clone.viewportLengthFlags = 0;
        clone.containerPercentLengthFlags = 0;
        clone.PreferIntrinsicWidth = false;
        clone.Gap = 0;
        clone.ClipContent = false;
        clone.IsScrollContainer = false;
        return clone;
    }

    public HtmlComputedStyle CreateInlineImageStyle()
    {
        var clone = Clone();
        clone.Display = HtmlDisplay.InlineBlock;
        clone.ApplyInlineBlockDefaults();
        clone.MarginLeft = 0;
        clone.MarginTop = 0;
        clone.MarginRight = 0;
        clone.MarginBottom = 0;
        return clone;
    }

    internal void ApplyInlineTextDefaults(bool preserveWhiteSpaceWrapping = false)
    {
        Display = HtmlDisplay.Inline;
        if (!preserveWhiteSpaceWrapping)
            WrapText = false;
        PreferIntrinsicWidth = true;
        Width = Defaults.UnsetLength;
        Height = Defaults.UnsetLength;
        MinWidth = Defaults.UnsetLength;
        MaxWidth = Defaults.UnsetLength;
        UnitFlags &= ~(
            LayoutValueUnitFlags.WidthPercent |
            LayoutValueUnitFlags.HeightPercent |
            LayoutValueUnitFlags.MinWidthPercent |
            LayoutValueUnitFlags.MaxWidthPercent);
    }

    internal void ApplyInlineBlockDefaults()
    {
        PreferIntrinsicWidth = true;
        FlexGrow = 0;
        FlexShrink = 0;
    }

    internal void ApplyInlineBoxDefaults()
    {
        Display = HtmlDisplay.Flex;
        FlexDirection = FlexDirection.Row;
        FlexWrap = WrapText ? FlexWrap.Wrap : FlexWrap.NoWrap;
        AlignItems = CrossAlignment.Start;
        PreferIntrinsicWidth = true;
        FlexGrow = 0;
        FlexShrink = 0;
        Width = Defaults.UnsetLength;
        Height = Defaults.UnsetLength;
        UnitFlags &= ~(LayoutValueUnitFlags.WidthPercent | LayoutValueUnitFlags.HeightPercent);
    }

    internal void ApplyListItemDefaults()
    {
        Display = HtmlDisplay.Flex;
        FlexDirection = FlexDirection.Row;
        FlexWrap = FlexWrap.NoWrap;
        AlignItems = CrossAlignment.Start;
        Gap = Math.Max(Gap, Defaults.ListItemGap);
    }

    internal HtmlComputedStyle CreateListItemContentStyle()
    {
        var style = Clone();
        style.Width = 0;
        style.FlexGrow = 1;
        style.FlexShrink = 1;
        style.MarginLeft = 0;
        style.MarginTop = 0;
        style.MarginRight = 0;
        style.MarginBottom = 0;
        style.UnitFlags &= ~LayoutValueUnitFlags.WidthPercent;
        return style;
    }

    internal void ApplyListItemContainerDefaults(string markerText)
    {
        var markerGap = Math.Max(Gap, Defaults.ListItemGap);
        var markerWidth = GetListMarkerMinWidth(markerText);
        MarginLeft = Math.Max(0, MarginLeft - markerWidth - markerGap);
        Display = HtmlDisplay.Flex;
        FlexDirection = FlexDirection.Row;
        FlexWrap = FlexWrap.NoWrap;
        AlignItems = CrossAlignment.Start;
        Gap = markerGap;
        BackgroundColor = null;
        BackgroundImageSource = null;
        BackgroundImageFit = null;
        BackgroundShadows = null;
        containerPercentLengthFlags = 0;
        BorderColor = null;
        BorderWidth = 0;
        BorderStyle = Defaults.BorderStyle;
        ClearBorderSides();
        BorderRadius = 0;
        PaddingLeft = 0;
        PaddingTop = 0;
        PaddingRight = 0;
        PaddingBottom = 0;
    }

    internal void ApplyListMarkerDefaults(string markerText)
    {
        Width = Defaults.UnsetLength;
        Height = Defaults.UnsetLength;
        MinWidth = GetListMarkerMinWidth(markerText);
        PreferIntrinsicWidth = true;
        TextAlign = SceneTextAlign.Left;
    }

    private static float GetListMarkerMinWidth(string markerText)
        => markerText.Length > 1 ? Defaults.UlListMarkerPadding : Defaults.PaddingSmall;

    internal void ApplyListContentDefaults()
    {
        Width = 0;
        FlexGrow = 1;
        FlexShrink = 1;
        UnitFlags &= ~LayoutValueUnitFlags.WidthPercent;
    }

    internal void ApplyInlineBreakDefaults()
    {
        Display = HtmlDisplay.Block;
        Width = Defaults.BlockWidthPercent;
        Height = 0;
        MinWidth = 0;
        MinHeight = 0;
        FlexGrow = 0;
        FlexShrink = 0;
        PreferIntrinsicWidth = false;
        SetUnit(LayoutValueUnitFlags.WidthPercent, true);
    }

    public void ApplyInheritedValues(HtmlComputedStyle inherited)
    {
        FontSize = inherited.FontSize;
        FontFamily = inherited.FontFamily;
        FontWeight = inherited.FontWeight;
        Color = inherited.Color;
        inheritedColor = inherited.Color;
        Direction = inherited.Direction;
        TextAlign = inherited.TextAlign;
        HasExplicitTextAlign = inherited.HasExplicitTextAlign;
        WrapText = inherited.WrapText;
        WhiteSpace = inherited.WhiteSpace;
        TextTransform = inherited.TextTransform;
        TextOverflowEllipsis = inherited.TextOverflowEllipsis;
        Underline = inherited.Underline;
        Italic = inherited.Italic;
        TextShadows = inherited.TextShadows;
        TableBorderSpacing = inherited.TableBorderSpacing;
        TableBorderCollapse = inherited.TableBorderCollapse;
        HasExplicitTextDecoration = inherited.HasExplicitTextDecoration;
        SuppressListMarker = inherited.SuppressListMarker;
        UnorderedListMarkerText = inherited.UnorderedListMarkerText;
        LineHeight = inherited.LineHeight;
        ScrollbarWidth = inherited.ScrollbarWidth;
        ScrollbarTrackColor = inherited.ScrollbarTrackColor;
        ScrollbarThumbColor = inherited.ScrollbarThumbColor;
    }

    public void ApplyElementDefaults(string localName, LayoutEngineConfig layoutConfig)
    {
        switch (localName)
        {
            case "body":
                Display = HtmlDisplay.Block;
                ClipContent = true;
                IsScrollContainer = true;
                PaddingLeft = Math.Max(PaddingLeft, Defaults.BodyDefaultPadding);
                PaddingTop = Math.Max(PaddingTop, Defaults.BodyDefaultPadding);
                PaddingRight = Math.Max(PaddingRight, Defaults.BodyDefaultPadding);
                PaddingBottom = Math.Max(PaddingBottom, Defaults.BodyDefaultPadding);
                break;
            case "center":
                Display = HtmlDisplay.Flex;
                FlexDirection = FlexDirection.Column;
                AlignItems = CrossAlignment.Center;
                TextAlign = SceneTextAlign.Center;
                break;
            case "table":
                Display = HtmlDisplay.Flex;
                FlexDirection = FlexDirection.Column;
                AlignItems = CrossAlignment.Stretch;
                PreferIntrinsicWidth = true;
                if (!LayoutValue.IsSet(TableBorderSpacing))
                    TableBorderSpacing = Defaults.TableBorderSpacing;
                break;
            case "tbody":
            case "thead":
            case "tfoot":
                Display = HtmlDisplay.Flex;
                FlexDirection = FlexDirection.Column;
                AlignItems = CrossAlignment.Stretch;
                PreferIntrinsicWidth = true;
                break;
            case "tr":
                Display = HtmlDisplay.Flex;
                FlexDirection = FlexDirection.Row;
                AlignItems = CrossAlignment.Start;
                PreferIntrinsicWidth = true;
                break;
            case "td":
            case "th":
                Display = HtmlDisplay.Block;
                PreferIntrinsicWidth = true;
                MarginLeft = 0;
                MarginTop = 0;
                MarginRight = 0;
                MarginBottom = 0;
                if (string.Equals(localName, "th", StringComparison.OrdinalIgnoreCase))
                {
                    FontWeight = Math.Max(FontWeight, 700);
                    if (!HasExplicitTextAlign)
                        TextAlign = SceneTextAlign.Center;
                }
                break;
            case "hr":
                Display = HtmlDisplay.Block;
                Height = LayoutValue.IsSet(Height) ? Height : Defaults.HRuleHeight;
                MinHeight = Math.Max(MinHeight, Defaults.HRuleHeight);
                BorderWidth = Math.Max(BorderWidth, 0);
                BackgroundColor ??= Defaults.ColorHr;
                break;
            case "br":
                Display = HtmlDisplay.Block;
                Height = LayoutValue.IsSet(Height) ? Height : MathF.Max(Defaults.HRuleHeight, LineHeight > 0 ? LineHeight : (FontSize > 0 ? FontSize : Defaults.DefaultFontSizeFallback) * Defaults.BrDefaultHeightMultiplier);
                MinHeight = Math.Max(MinHeight, Defaults.HRuleHeight);
                PreferIntrinsicWidth = false;
                break;
            case "img":
                Display = HtmlDisplay.Block;
                break;
            case "textarea":
                if (layoutConfig.ApplyFormControlDefaults)
                {
                    var hadExplicitWidth = LayoutValue.IsSet(Width);
                    Multiline = true;
                    Width = LayoutValue.IsSet(Width) ? Width : Defaults.FormInputWidth;
                    Height = LayoutValue.IsSet(Height) ? Height : Defaults.TextareaHeight;
                    SetUnit(LayoutValueUnitFlags.WidthPercent, !hadExplicitWidth || IsWidthPercent);
                    PaddingLeft = Math.Max(PaddingLeft, Defaults.InputPaddingX);
                    PaddingRight = Math.Max(PaddingRight, Defaults.InputPaddingX);
                    PaddingTop = Math.Max(PaddingTop, Defaults.InputPaddingY);
                    PaddingBottom = Math.Max(PaddingBottom, Defaults.InputPaddingY);
                    BorderWidth = Math.Max(BorderWidth, Defaults.DefaultBorderWidth);
                    BorderStyle = BorderStyle == Defaults.BorderStyle ? Defaults.BorderStyleSolid : BorderStyle;
                    BorderColor ??= Defaults.ColorInputBorder;
                    BorderRadius = Math.Max(BorderRadius, Defaults.DefaultRadius);
                    BackgroundColor ??= Defaults.ColorWhite;
                    if (!HasExplicitColor)
                        Color = Defaults.ColorInputText;
                    PlaceholderColor ??= Defaults.ColorPlaceholder;
                }
                break;
            case "p":
            case "li":
                if (layoutConfig.ApplySemanticTextSpacing)
                {
                    if (MarginTop <= 0 && localName != "li")
                        MarginTop = FontSize > 0 ? FontSize : Defaults.DefaultFontSizeFallback;
                    if (MarginBottom <= 0)
                        MarginBottom = localName == "li" ? 0 : FontSize > 0 ? FontSize : Defaults.DefaultFontSizeFallback;
                }
                break;
            case "ul":
            case "ol":
                if (layoutConfig.ApplySemanticTextSpacing)
                {
                    if (!IsLengthExplicit(HtmlLengthProperty.PaddingLeft))
                        PaddingLeft = Math.Max(PaddingLeft, Defaults.UlListMarkerPadding);
                    if (MarginBottom <= 0)
                        MarginBottom = FontSize > 0 ? FontSize : Defaults.DefaultFontSizeFallback;
                }
                break;
            case "h1":
                if (layoutConfig.ApplySemanticTextSpacing)
                {
                    if (MarginTop <= 0)
                        MarginTop = MathF.Ceiling((FontSize > 0 ? FontSize : Defaults.H1FontSize) * Defaults.H1SpacingScale);
                    if (MarginBottom <= 0)
                        MarginBottom = MathF.Ceiling((FontSize > 0 ? FontSize : Defaults.H1FontSize) * Defaults.H1SpacingScale);
                }
                FontSize = Math.Max(FontSize, Defaults.H1FontSize);
                FontWeight = Math.Max(FontWeight, 700);
                break;
            case "h2":
                if (layoutConfig.ApplySemanticTextSpacing)
                {
                    if (MarginTop <= 0)
                        MarginTop = MathF.Ceiling((FontSize > 0 ? FontSize : Defaults.H2FontSize) * Defaults.H2SpacingScale);
                    if (MarginBottom <= 0)
                        MarginBottom = MathF.Ceiling((FontSize > 0 ? FontSize : Defaults.H2FontSize) * Defaults.H2SpacingScale);
                }
                FontSize = Math.Max(FontSize, Defaults.H2FontSize);
                FontWeight = Math.Max(FontWeight, 700);
                break;
            case "h3":
                if (layoutConfig.ApplySemanticTextSpacing)
                {
                    if (MarginTop <= 0)
                        MarginTop = FontSize > 0 ? FontSize : Defaults.H3FontSize;
                    if (MarginBottom <= 0)
                        MarginBottom = FontSize > 0 ? FontSize : Defaults.H3FontSize;
                }
                FontSize = Math.Max(FontSize, Defaults.H3FontSize);
                FontWeight = Math.Max(FontWeight, 600);
                break;
            case "input":
                if (layoutConfig.ApplyFormControlDefaults)
                {
                    var hadExplicitWidth = LayoutValue.IsSet(Width);
                    Width = LayoutValue.IsSet(Width) ? Width : Defaults.FormInputWidth;
                    Height = LayoutValue.IsSet(Height) ? Height : Defaults.FormInputHeight;
                    SetUnit(LayoutValueUnitFlags.WidthPercent, !hadExplicitWidth || IsWidthPercent);
                    PaddingLeft = Math.Max(PaddingLeft, Defaults.InputPaddingX);
                    PaddingRight = Math.Max(PaddingRight, Defaults.InputPaddingX);
                    PaddingTop = Math.Max(PaddingTop, Defaults.InputPaddingY);
                    PaddingBottom = Math.Max(PaddingBottom, Defaults.InputPaddingY);
                    BorderWidth = Math.Max(BorderWidth, Defaults.DefaultBorderWidth);
                    BorderStyle = BorderStyle == Defaults.BorderStyle ? Defaults.BorderStyleSolid : BorderStyle;
                    BorderColor ??= Defaults.ColorInputBorder;
                    BorderRadius = Math.Max(BorderRadius, Defaults.DefaultRadius);
                    BackgroundColor ??= Defaults.ColorWhite;
                    if (!HasExplicitColor)
                        Color = Defaults.ColorInputText;
                    PlaceholderColor ??= Defaults.ColorPlaceholder;
                }
                break;
            case "select":
                if (layoutConfig.ApplyFormControlDefaults)
                {
                    Display = HtmlDisplay.InlineBlock;
                    PreferIntrinsicWidth = true;
                    WrapText = false;
                    if (!LayoutValue.IsSet(Width))
                        Width = Defaults.UnsetLength;
                    Height = LayoutValue.IsSet(Height) ? Height : Defaults.FormInputHeight;
                    MinWidth = Math.Max(MinWidth, Defaults.SelectMinWidth);
                    SetUnit(LayoutValueUnitFlags.WidthPercent, IsWidthPercent);
                    PaddingLeft = Math.Max(PaddingLeft, Defaults.ButtonPaddingX);
                    PaddingRight = Math.Max(PaddingRight, Defaults.SelectPaddingRight);
                    PaddingTop = Math.Max(PaddingTop, Defaults.ButtonPaddingY);
                    PaddingBottom = Math.Max(PaddingBottom, Defaults.ButtonPaddingY);
                    BorderWidth = Math.Max(BorderWidth, Defaults.DefaultBorderWidth);
                    BorderStyle = BorderStyle == Defaults.BorderStyle ? Defaults.BorderStyleSolid : BorderStyle;
                    BorderColor ??= Defaults.ColorSelectBorder;
                    BorderRadius = Math.Max(BorderRadius, Defaults.DefaultRadius);
                    BackgroundColor ??= Defaults.ColorWhite;
                    if (!HasExplicitColor)
                        Color = Defaults.ColorInputText;
                    PlaceholderColor ??= Defaults.ColorPlaceholder;
                }
                break;
            case "button":
                if (layoutConfig.ApplyFormControlDefaults)
                {
                    Display = HtmlDisplay.InlineBlock;
                    PreferIntrinsicWidth = true;
                    WrapText = false;
                    Height = LayoutValue.IsSet(Height) ? Height : Defaults.FormInputHeight;
                    PaddingLeft = Math.Max(PaddingLeft, Defaults.ButtonPaddingX);
                    PaddingRight = Math.Max(PaddingRight, Defaults.ButtonPaddingX);
                    PaddingTop = Math.Max(PaddingTop, Defaults.ButtonPaddingY);
                    PaddingBottom = Math.Max(PaddingBottom, Defaults.ButtonPaddingY);
                    BorderWidth = Math.Max(BorderWidth, Defaults.DefaultBorderWidth);
                    BorderStyle = BorderStyle == Defaults.BorderStyle ? Defaults.BorderStyleSolid : BorderStyle;
                    BorderColor ??= Defaults.ColorButtonBorder;
                    BorderRadius = Math.Max(BorderRadius, Defaults.DefaultRadius);
                    BackgroundColor ??= Defaults.ColorButtonBackground;
                    Color ??= Defaults.ColorButton;
                }
                break;
        }

        if (layoutConfig.ApplyBlockWidthAsPercent &&
            Display == HtmlDisplay.Block &&
            Float == HtmlFloat.None &&
            !LayoutValue.IsSet(Width) &&
            localName is not "button" and not "select" and not "img" and not "table" and not "tbody" and not "thead" and not "tfoot" and not "tr" and not "td" and not "th" and not "hr")
        {
            Width = Defaults.BlockWidthPercent;
            SetUnit(LayoutValueUnitFlags.WidthPercent, true);
        }
    }

    internal void ApplyElementAttributes(HtmlDomElement element)
    {
        ApplyLegacyCommonAttributes(element);

        if (string.Equals(element.LocalName, "body", StringComparison.OrdinalIgnoreCase))
        {
            if (element.GetAttribute("background") is { Length: > 0 } background)
            {
                BackgroundImageSource = background;
                BackgroundImageFit ??= Defaults.BgFitRepeat;
            }
            if (TryParseLegacySpacing(element.GetAttribute("marginwidth"), out var marginWidth))
            {
                PaddingLeft = Math.Max(Defaults.PaddingSmall, marginWidth);
                PaddingRight = Math.Max(Defaults.PaddingSmall, marginWidth);
            }
            if (TryParseLegacySpacing(element.GetAttribute("marginheight"), out var marginHeight))
            {
                PaddingTop = Math.Max(Defaults.PaddingSmall, marginHeight);
                PaddingBottom = Math.Max(Defaults.PaddingSmall, marginHeight);
            }
        }

        if (string.Equals(element.LocalName, "font", StringComparison.OrdinalIgnoreCase))
            ApplyLegacyFontAttributes(element);

        if (string.Equals(element.LocalName, "table", StringComparison.OrdinalIgnoreCase) &&
            TryParseLegacySpacing(element.GetAttribute("cellspacing"), out var cellSpacing))
        {
            Gap = cellSpacing;
            TableBorderSpacing = cellSpacing;
        }

        if (string.Equals(element.LocalName, "img", StringComparison.OrdinalIgnoreCase))
        {
            if (!LayoutValue.IsSet(Width))
                ApplyAttributeLength(element.GetAttribute("width"), static (style, next) => style.Width = next.Value, static (style, isPercent) => style.SetUnit(LayoutValueUnitFlags.WidthPercent, isPercent));
            if (!LayoutValue.IsSet(Height))
                ApplyAttributeLength(element.GetAttribute("height"), static (style, next) => style.Height = next.Value, static (style, isPercent) => style.SetUnit(LayoutValueUnitFlags.HeightPercent, isPercent));
            ImageFit ??= Defaults.BgFitFill;
            if (TryParseLegacySpacing(element.GetAttribute("border"), out var borderWidth))
                BorderWidth = borderWidth;
            if (BorderWidth > 0)
            {
                BorderStyle = BorderStyle == Defaults.BorderStyle ? Defaults.BorderStyleSolid : BorderStyle;
                BorderColor ??= Defaults.ColorInputBorder;
            }
        }

        if (string.Equals(element.LocalName, "hr", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAttributeLength(element.GetAttribute("size"), static (style, next) => style.Height = next.Value, static (_, _) => { });
            BackgroundColor ??= BorderColor ?? Defaults.ColorHr;
        }
    }

    internal void NormalizeAfterCascade(string localName, LayoutEngineConfig layoutConfig)
    {
        if (layoutConfig.ApplyBlockWidthAsPercent &&
            Float != HtmlFloat.None &&
            !HasExplicitWidth &&
            IsWidthPercent &&
            MathF.Abs(Width - Defaults.BlockWidthPercent) < 0.001f &&
            localName is not "img" and not "table" and not "tbody" and not "thead" and not "tfoot" and not "tr" and not "td" and not "th")
        {
            Width = Defaults.UnsetLength;
            UnitFlags &= ~LayoutValueUnitFlags.WidthPercent;
            PreferIntrinsicWidth = true;
        }
    }

    internal void ApplyIntrinsicImageSize(float intrinsicWidth, float intrinsicHeight)
    {
        if (intrinsicWidth <= 0 || intrinsicHeight <= 0)
            return;

        IntrinsicImageWidth = intrinsicWidth;
        IntrinsicImageHeight = intrinsicHeight;
        ImageAspectRatio = intrinsicWidth / intrinsicHeight;

        if (!LayoutValue.IsSet(Width) && !LayoutValue.IsSet(Height))
        {
            Width = intrinsicWidth;
            Height = intrinsicHeight;
            return;
        }

        if (LayoutValue.IsSet(Width) && !LayoutValue.IsSet(Height))
            Height = Width * intrinsicHeight / intrinsicWidth;
        else if (!LayoutValue.IsSet(Width) && LayoutValue.IsSet(Height))
            Width = Height * intrinsicWidth / intrinsicHeight;
    }

    internal void ApplyDefaultImageSize()
    {
        if (LayoutValue.IsSet(Width) || LayoutValue.IsSet(Height))
            return;

        Width = 300;
        Height = 150;
    }

    private void ApplyLegacyCommonAttributes(HtmlDomElement element)
    {
        if (!LayoutValue.IsSet(Width))
            ApplyAttributeLength(element.GetAttribute("width"), static (style, next) => style.Width = next.Value, static (style, isPercent) => style.SetUnit(LayoutValueUnitFlags.WidthPercent, isPercent));
        if (!LayoutValue.IsSet(Height))
            ApplyAttributeLength(element.GetAttribute("height"), static (style, next) => style.Height = next.Value, static (style, isPercent) => style.SetUnit(LayoutValueUnitFlags.HeightPercent, isPercent));

        if (element.GetAttribute("align") is { } align)
        {
            var alignsElementBox = string.Equals(element.LocalName, "table", StringComparison.OrdinalIgnoreCase);
            switch (align.AsSpan().Trim().ToString().ToLowerInvariant())
            {
                case "center":
                    if (alignsElementBox)
                        AlignSelf = CrossAlignment.Center;
                    else
                        TextAlign = SceneTextAlign.Center;
                    break;
                case "right":
                    if (alignsElementBox)
                        AlignSelf = CrossAlignment.End;
                    else
                        TextAlign = SceneTextAlign.Right;
                    break;
                case "left":
                    if (alignsElementBox)
                        AlignSelf = CrossAlignment.Start;
                    else
                        TextAlign = SceneTextAlign.Left;
                    break;
            }
        }

        if (element.GetAttribute("valign") is { } valign)
        {
            AlignItems = valign.AsSpan().Trim().ToString().ToLowerInvariant() switch
            {
                "middle" or "center" => CrossAlignment.Center,
                "bottom" => CrossAlignment.End,
                _ => CrossAlignment.Start
            };
        }
    }

    private void ApplyLegacyFontAttributes(HtmlDomElement element)
    {
        if (element.GetAttribute("face") is { Length: > 0 } face)
            FontFamily = face;
        if (element.GetAttribute("color") is { Length: > 0 } color)
        {
            Color = color;
            HasExplicitColor = true;
        }
        if (element.GetAttribute("size") is { Length: > 0 } sizeText &&
            int.TryParse(sizeText.AsSpan().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
        {
            FontSize = size switch
            {
                <= 1 => Defaults.SmallFontSize,
                2 => Defaults.FontSizeFromLevel2,
                3 => Defaults.FontSizeFromLevel3,
                4 => Defaults.FontSizeFromLevel4,
                5 => Defaults.H4FontSize,
                6 => Defaults.H5FontSize,
                _ => Defaults.H6FontSize
            };
        }
    }

    private static bool TryParseLegacySpacing(string? value, out float parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var span = value.AsSpan().Trim();
        if (span.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase))
            span = span[..^2].Trim();
        return float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }

    internal void ApplyAnchorDefaults()
    {
        if (!HasExplicitDisplay)
            Display = HtmlDisplay.Inline;
        if (!HasExplicitColor)
            Color = Defaults.ColorAnchor;
        if (!HasExplicitTextDecoration)
            Underline = true;
        WrapText = Defaults.WrapText;
        if (Display == HtmlDisplay.Inline)
        {
            PreferIntrinsicWidth = true;
            Width = Defaults.UnsetLength;
            Height = Defaults.UnsetLength;
            MinWidth = Defaults.UnsetLength;
            MaxWidth = Defaults.UnsetLength;
            UnitFlags &= ~(
                LayoutValueUnitFlags.WidthPercent |
                LayoutValueUnitFlags.HeightPercent |
                LayoutValueUnitFlags.MinWidthPercent |
                LayoutValueUnitFlags.MaxWidthPercent);
        }
        else
        {
            PreferIntrinsicWidth = Display == HtmlDisplay.InlineBlock;
        }
    }

    internal void ApplyInlineElementDefaults(string localName)
    {
        if (!HasExplicitDisplay)
            Display = HtmlDisplay.Inline;

        switch (localName)
        {
            case "strong":
            case "b":
                FontWeight = Math.Max(FontWeight, 700);
                break;
            case "em":
            case "i":
                Italic = true;
                break;
            case "u":
                Underline = true;
                break;
            case "small":
                if (FontSize > 0)
                    FontSize = MathF.Max(1, FontSize * 0.875f);
                break;
        }
    }

    internal void ApplyDefaultInteraction(string localName, bool isHovered, bool isActive)
    {
        if (!string.Equals(localName, "button", StringComparison.OrdinalIgnoreCase))
            return;

        var hasDefaultBackground = string.Equals(BackgroundColor, Defaults.ColorButtonBackground, StringComparison.OrdinalIgnoreCase);
        var hasDefaultBorder = string.Equals(BorderColor, Defaults.ColorButtonBorder, StringComparison.OrdinalIgnoreCase);
        if (!hasDefaultBackground && !hasDefaultBorder)
            return;

        if (isActive)
        {
            if (hasDefaultBackground)
                BackgroundColor = Defaults.ColorButtonBackgroundActive;
            if (hasDefaultBorder)
                BorderColor = Defaults.ColorButtonBorderActive;
        }
        else if (isHovered)
        {
            if (hasDefaultBackground)
                BackgroundColor = Defaults.ColorButtonBackgroundHover;
            if (hasDefaultBorder)
                BorderColor = Defaults.ColorButtonBorderHover;
        }
    }

    public void Apply(HtmlCssDeclarationBlock declarations)
    {
        fontSizeReference = ResolveCurrentFontSize();
        for (var index = 0; index < declarations.Count; index++)
        {
            if (declarations[index].Property is CssPropertyId.FontSize or CssPropertyId.Direction)
                Apply(declarations[index]);
        }

        for (var index = 0; index < declarations.Count; index++)
        {
            if (declarations[index].Property is CssPropertyId.FontSize or CssPropertyId.Direction)
                continue;

            Apply(declarations[index]);
        }

        if (Float != HtmlFloat.None && Display == HtmlDisplay.Inline)
        {
            Display = HtmlDisplay.Block;
            MarginTop = 0;
            MarginBottom = 0;
        }

        fontSizeReference = 0;
    }

    private void Apply(HtmlCssDeclaration declaration)
    {
        var normalized = declaration.Value.AsSpan().Trim();
        switch (declaration.Property)
        {
            case CssPropertyId.Display:
                var parsedDisplay = normalized switch
                {
                    "none" => (HtmlDisplay?)HtmlDisplay.None,
                    "contents" => HtmlDisplay.Contents,
                    "flex" => HtmlDisplay.Flex,
                    "inline-flex" => HtmlDisplay.Flex,
                    "inline-block" => HtmlDisplay.InlineBlock,
                    "inline" => HtmlDisplay.Inline,
                    "block" or "flow-root" or "list-item" => HtmlDisplay.Block,
                    _ => null
                };
                if (parsedDisplay is not { } display)
                    break;

                HasExplicitDisplay = true;
                Display = display;
                if (Display is HtmlDisplay.Inline or HtmlDisplay.InlineBlock ||
                    CssEquals(normalized, "inline-flex"))
                {
                    PreferIntrinsicWidth = true;
                    FlexGrow = 0;
                    FlexShrink = 0;
                }
                break;
            case CssPropertyId.FlexDirection:
                FlexDirection = normalized switch
                {
                    "row" => FlexDirection.Row,
                    "row-reverse" => FlexDirection.RowReverse,
                    "column-reverse" => FlexDirection.ColumnReverse,
                    _ => FlexDirection.Column
                };
                break;
            case CssPropertyId.FlexWrap:
                FlexWrap = CssEquals(normalized, "wrap") ? FlexWrap.Wrap : FlexWrap.NoWrap;
                break;
            case CssPropertyId.Direction:
                Direction = CssEquals(normalized, "rtl") ? LayoutDirection.Rtl : LayoutDirection.Ltr;
                break;
            case CssPropertyId.JustifyContent:
                JustifyContent = ParseJustifyContent(normalized);
                break;
            case CssPropertyId.AlignItems:
                AlignItems = ParseAlignment(normalized);
                break;
            case CssPropertyId.AlignSelf:
                AlignSelf = ParseAlignment(normalized);
                break;
            case CssPropertyId.Order:
                if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
                    Order = order;
                break;
            case CssPropertyId.Position:
                Position = normalized switch
                {
                    "absolute" => PositionMode.Absolute,
                    "static" => PositionMode.Static,
                    _ => PositionMode.Relative
                };
                break;
            case CssPropertyId.BoxSizing:
                BoxSizing = CssEquals(normalized, "content-box") ? SceneBoxSizing.ContentBox : SceneBoxSizing.BorderBox;
                break;
            case CssPropertyId.Width:
                SetLength(normalized, HtmlLengthProperty.Width);
                break;
            case CssPropertyId.Height:
                SetLength(normalized, HtmlLengthProperty.Height);
                break;
            case CssPropertyId.MinWidth:
                SetLength(normalized, HtmlLengthProperty.MinWidth);
                break;
            case CssPropertyId.MaxWidth:
                SetLength(normalized, HtmlLengthProperty.MaxWidth);
                break;
            case CssPropertyId.MinHeight:
                SetLength(normalized, HtmlLengthProperty.MinHeight);
                break;
            case CssPropertyId.MaxHeight:
                SetLength(normalized, HtmlLengthProperty.MaxHeight);
                break;
            case CssPropertyId.AspectRatio:
                if (TryParseAspectRatio(normalized, out var aspectRatio))
                    ImageAspectRatio = aspectRatio;
                break;
            case CssPropertyId.Left:
                SetLength(normalized, HtmlLengthProperty.Left);
                break;
            case CssPropertyId.Top:
                SetLength(normalized, HtmlLengthProperty.Top);
                break;
            case CssPropertyId.Right:
                SetLength(normalized, HtmlLengthProperty.Right);
                break;
            case CssPropertyId.Bottom:
                SetLength(normalized, HtmlLengthProperty.Bottom);
                break;
            case CssPropertyId.FlexGrow:
                FlexGrow = ParseFloat(normalized, FlexGrow);
                break;
            case CssPropertyId.FlexShrink:
                FlexShrink = ParseFloat(normalized, FlexShrink);
                break;
            case CssPropertyId.FlexBasis:
                SetLength(normalized, HtmlLengthProperty.FlexBasis);
                break;
            case CssPropertyId.Flex:
                ApplyFlexShorthand(normalized);
                break;
            case CssPropertyId.Float:
                Float = normalized switch
                {
                    "left" => HtmlFloat.Left,
                    "right" => HtmlFloat.Right,
                    _ => HtmlFloat.None
                };
                if (Float != HtmlFloat.None)
                {
                    PreferIntrinsicWidth = true;
                    FlexGrow = 0;
                    FlexShrink = 0;
                    if (Display == HtmlDisplay.Inline)
                    {
                        Display = HtmlDisplay.Block;
                        MarginTop = 0;
                        MarginBottom = 0;
                    }
                }
                break;
            case CssPropertyId.Clear:
                Clear = normalized switch
                {
                    "left" => HtmlClear.Left,
                    "right" => HtmlClear.Right,
                    "both" => HtmlClear.Both,
                    _ => HtmlClear.None
                };
                break;
            case CssPropertyId.Margin:
                ApplyBoxSpacing(normalized, HtmlLengthProperty.MarginTop, HtmlLengthProperty.MarginRight, HtmlLengthProperty.MarginBottom, HtmlLengthProperty.MarginLeft);
                break;
            case CssPropertyId.MarginInline:
                ApplyTwoValueSpacing(normalized, ResolveInlineStartMargin(), ResolveInlineEndMargin());
                break;
            case CssPropertyId.MarginInlineStart:
                SetLength(normalized, ResolveInlineStartMargin());
                break;
            case CssPropertyId.MarginInlineEnd:
                SetLength(normalized, ResolveInlineEndMargin());
                break;
            case CssPropertyId.MarginBlock:
                ApplyTwoValueSpacing(normalized, HtmlLengthProperty.MarginTop, HtmlLengthProperty.MarginBottom);
                break;
            case CssPropertyId.MarginBlockStart:
                SetLength(normalized, HtmlLengthProperty.MarginTop);
                break;
            case CssPropertyId.MarginBlockEnd:
                SetLength(normalized, HtmlLengthProperty.MarginBottom);
                break;
            case CssPropertyId.MarginTop:
                SetLength(normalized, HtmlLengthProperty.MarginTop);
                break;
            case CssPropertyId.MarginRight:
                SetLength(normalized, HtmlLengthProperty.MarginRight);
                break;
            case CssPropertyId.MarginBottom:
                SetLength(normalized, HtmlLengthProperty.MarginBottom);
                break;
            case CssPropertyId.MarginLeft:
                SetLength(normalized, HtmlLengthProperty.MarginLeft);
                break;
            case CssPropertyId.Padding:
                ApplyBoxSpacing(normalized, HtmlLengthProperty.PaddingTop, HtmlLengthProperty.PaddingRight, HtmlLengthProperty.PaddingBottom, HtmlLengthProperty.PaddingLeft);
                break;
            case CssPropertyId.PaddingTop:
                SetLength(normalized, HtmlLengthProperty.PaddingTop);
                break;
            case CssPropertyId.PaddingRight:
                SetLength(normalized, HtmlLengthProperty.PaddingRight);
                break;
            case CssPropertyId.PaddingBottom:
                SetLength(normalized, HtmlLengthProperty.PaddingBottom);
                break;
            case CssPropertyId.PaddingLeft:
                SetLength(normalized, HtmlLengthProperty.PaddingLeft);
                break;
            case CssPropertyId.Gap:
                SetLength(normalized, HtmlLengthProperty.Gap);
                break;
            case CssPropertyId.BorderWidth:
                if (ParseLengthValue(normalized, HtmlLengthProperty.BorderWidth) is { } borderWidth)
                    SetBorderWidth(HtmlBorderSide.All, borderWidth.ValueOrDefault(BorderWidth));
                break;
            case CssPropertyId.BorderTopWidth:
                if (ParseLengthValue(normalized, HtmlLengthProperty.BorderWidth) is { } borderTopWidth)
                    SetBorderWidth(HtmlBorderSide.Top, borderTopWidth.ValueOrDefault(BorderTopWidth));
                break;
            case CssPropertyId.BorderRightWidth:
                if (ParseLengthValue(normalized, HtmlLengthProperty.BorderWidth) is { } borderRightWidth)
                    SetBorderWidth(HtmlBorderSide.Right, borderRightWidth.ValueOrDefault(BorderRightWidth));
                break;
            case CssPropertyId.BorderBottomWidth:
                if (ParseLengthValue(normalized, HtmlLengthProperty.BorderWidth) is { } borderBottomWidth)
                    SetBorderWidth(HtmlBorderSide.Bottom, borderBottomWidth.ValueOrDefault(BorderBottomWidth));
                break;
            case CssPropertyId.BorderLeftWidth:
                if (ParseLengthValue(normalized, HtmlLengthProperty.BorderWidth) is { } borderLeftWidth)
                    SetBorderWidth(HtmlBorderSide.Left, borderLeftWidth.ValueOrDefault(BorderLeftWidth));
                break;
            case CssPropertyId.BorderStyle:
                SetBorderStyle(HtmlBorderSide.All, ParseBorderStyle(normalized, BorderStyle));
                break;
            case CssPropertyId.BorderTopStyle:
                SetBorderStyle(HtmlBorderSide.Top, ParseBorderStyle(normalized, BorderTopStyle));
                break;
            case CssPropertyId.BorderRightStyle:
                SetBorderStyle(HtmlBorderSide.Right, ParseBorderStyle(normalized, BorderRightStyle));
                break;
            case CssPropertyId.BorderBottomStyle:
                SetBorderStyle(HtmlBorderSide.Bottom, ParseBorderStyle(normalized, BorderBottomStyle));
                break;
            case CssPropertyId.BorderLeftStyle:
                SetBorderStyle(HtmlBorderSide.Left, ParseBorderStyle(normalized, BorderLeftStyle));
                break;
            case CssPropertyId.Border:
                ApplyBorderShorthand(normalized);
                break;
            case CssPropertyId.BorderTop:
                ApplyBorderShorthand(normalized, HtmlBorderSide.Top);
                break;
            case CssPropertyId.BorderRight:
                ApplyBorderShorthand(normalized, HtmlBorderSide.Right);
                break;
            case CssPropertyId.BorderBottom:
                ApplyBorderShorthand(normalized, HtmlBorderSide.Bottom);
                break;
            case CssPropertyId.BorderLeft:
                ApplyBorderShorthand(normalized, HtmlBorderSide.Left);
                break;
            case CssPropertyId.BorderRadius:
                SetLength(normalized, HtmlLengthProperty.BorderRadius);
                break;
            case CssPropertyId.BorderCollapse:
                TableBorderCollapse = CssEquals(normalized, "collapse");
                if (TableBorderCollapse)
                    TableBorderSpacing = Defaults.CollapsedTableBorderSpacing;
                break;
            case CssPropertyId.BorderSpacing:
                if (!TableBorderCollapse)
                    SetLength(normalized, HtmlLengthProperty.TableBorderSpacing);
                break;
            case CssPropertyId.BoxShadow:
                BackgroundShadows = ParseBoxShadows(normalized);
                break;
            case CssPropertyId.TextShadow:
                TextShadows = ParseTextShadows(normalized);
                break;
            case CssPropertyId.BorderColor:
                SetBorderColor(HtmlBorderSide.All, normalized.ToString());
                break;
            case CssPropertyId.BorderTopColor:
                SetBorderColor(HtmlBorderSide.Top, normalized.ToString());
                break;
            case CssPropertyId.BorderRightColor:
                SetBorderColor(HtmlBorderSide.Right, normalized.ToString());
                break;
            case CssPropertyId.BorderBottomColor:
                SetBorderColor(HtmlBorderSide.Bottom, normalized.ToString());
                break;
            case CssPropertyId.BorderLeftColor:
                SetBorderColor(HtmlBorderSide.Left, normalized.ToString());
                break;
            case CssPropertyId.Background:
                if (TryExtractBackgroundColor(normalized, out var backgroundColor))
                    BackgroundColor = backgroundColor;
                if (TryExtractBackgroundImageSource(normalized, out var backgroundImageSource))
                {
                    BackgroundImageSource = backgroundImageSource;
                    BackgroundImageFit ??= Defaults.BgFitCover;
                }
                else if (CssEquals(normalized, "none"))
                {
                    BackgroundImageSource = null;
                    BackgroundImageFit = null;
                }
                break;
            case CssPropertyId.BackgroundImage:
                if (CssEquals(normalized, "none"))
                {
                    BackgroundImageSource = null;
                    BackgroundImageFit = null;
                }
                else if (TryExtractBackgroundImageSource(normalized, out var explicitBackgroundImageSource))
                {
                    BackgroundImageSource = explicitBackgroundImageSource;
                    BackgroundImageFit ??= Defaults.BgFitCover;
                }
                break;
            case CssPropertyId.BackgroundSize:
                BackgroundImageFit = normalized switch
                {
                    "contain" => Defaults.BgFitContain,
                    "cover" => Defaults.BgFitCover,
                    _ => Defaults.BgFitFill
                };
                break;
            case CssPropertyId.Color:
                if (CssEquals(normalized, "inherit"))
                {
                    Color = inheritedColor ?? Color;
                    HasExplicitColor = true;
                }
                else
                {
                    Color = CssEquals(normalized, "initial") ? Defaults.ColorBlack : normalized.ToString();
                    HasExplicitColor = true;
                }
                break;
            case CssPropertyId.FontSize:
                SetLength(normalized, HtmlLengthProperty.FontSize);
                break;
            case CssPropertyId.FontFamily:
                FontFamily = TrimQuotes(normalized).ToString();
                break;
            case CssPropertyId.FontWeight:
                FontWeight = ParseFontWeight(normalized);
                break;
            case CssPropertyId.FontStyle:
                Italic = normalized is "italic" or "oblique";
                break;
            case CssPropertyId.TextAlign:
                HasExplicitTextAlign = true;
                TextAlign = normalized switch
                {
                    "center" => SceneTextAlign.Center,
                    "right" => SceneTextAlign.Right,
                    "left" => SceneTextAlign.Left,
                    "end" => Direction == LayoutDirection.Rtl ? SceneTextAlign.Left : SceneTextAlign.Right,
                    "start" => Direction == LayoutDirection.Rtl ? SceneTextAlign.Right : SceneTextAlign.Left,
                    _ => SceneTextAlign.Left
                };
                break;
            case CssPropertyId.TextTransform:
                TextTransform = normalized switch
                {
                    "uppercase" => HtmlTextTransform.Uppercase,
                    "lowercase" => HtmlTextTransform.Lowercase,
                    _ => HtmlTextTransform.None
                };
                break;
            case CssPropertyId.TextDecoration:
                HasExplicitTextDecoration = true;
                Underline = normalized.IndexOf("underline".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
                break;
            case CssPropertyId.ListStyle:
                SuppressListMarker = IsListStyleNone(normalized);
                if (normalized.Contains("square".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    UnorderedListMarkerText = Defaults.MarkerSquare;
                else if (normalized.Contains("circle".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    UnorderedListMarkerText = Defaults.MarkerCircle;
                else if (normalized.Contains("disc".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    UnorderedListMarkerText = Defaults.UnorderedListMarkerText;
                break;
            case CssPropertyId.WhiteSpace:
                ApplyWhiteSpace(normalized);
                break;
            case CssPropertyId.TextOverflow:
                TextOverflowEllipsis = CssEquals(normalized, "ellipsis");
                break;
            case CssPropertyId.LineHeight:
                SetLength(normalized, HtmlLengthProperty.LineHeight);
                break;
            case CssPropertyId.Overflow:
                ApplyOverflow(normalized);
                break;
            case CssPropertyId.Contain:
                Containment = ParseContainment(normalized);
                break;
            case CssPropertyId.ObjectFit:
                ImageFit = normalized.ToString();
                break;
            case CssPropertyId.PlaceContent:
                JustifyContent = ParseJustifyContent(ParseSecondOrFirstPlaceValue(normalized));
                break;
            case CssPropertyId.PlaceItems:
                AlignItems = ParseAlignment(ParseFirstPlaceValue(normalized));
                break;
            case CssPropertyId.PlaceSelf:
                AlignSelf = ParseAlignment(ParseFirstPlaceValue(normalized));
                break;
            case CssPropertyId.ScrollbarWidth:
                if (ParseLengthValue(normalized, HtmlLengthProperty.Width) is { } scrollbarWidth)
                    ScrollbarWidth = Math.Max(0, scrollbarWidth.ValueOrDefault(ScrollbarWidth));
                break;
            case CssPropertyId.ScrollbarTrackColor:
                ScrollbarTrackColor = TryExtractBackgroundColor(normalized, out var trackColor)
                    ? trackColor
                    : normalized.ToString();
                break;
            case CssPropertyId.ScrollbarThumbColor:
                ScrollbarThumbColor = TryExtractBackgroundColor(normalized, out var thumbColor)
                    ? thumbColor
                    : normalized.ToString();
                break;
        }
    }

    private static bool IsListStyleNone(ReadOnlySpan<char> normalized)
    {
        Span<Range> parts = stackalloc Range[6];
        var partCount = SplitWhitespace(normalized, parts);
        for (var index = 0; index < partCount; index++)
        {
            if (CssEquals(normalized[parts[index]], "none"))
                return true;
        }

        return false;
    }

    private void ApplyFlexShorthand(ReadOnlySpan<char> normalized)
    {
        Span<Range> parts = stackalloc Range[4];
        var partCount = SplitWhitespace(normalized, parts);
        if (partCount == 1 && float.TryParse(normalized[parts[0]], NumberStyles.Float, CultureInfo.InvariantCulture, out var grow))
        {
            FlexGrow = grow;
            FlexShrink = 1;
            FlexBasis = 0;
            return;
        }

        if (partCount > 0)
            FlexGrow = ParseFloat(normalized[parts[0]], FlexGrow);
        if (partCount > 1)
            FlexShrink = ParseFloat(normalized[parts[1]], FlexShrink);
        if (partCount > 2)
            ApplyLength(HtmlLengthProperty.FlexBasis, ParseLengthValue(normalized[parts[2]], HtmlLengthProperty.FlexBasis), FlexBasis);
    }

    private void ApplyWhiteSpace(ReadOnlySpan<char> normalized)
    {
        WhiteSpace = CssEquals(normalized, "nowrap")
            ? HtmlWhiteSpace.NoWrap
            : CssEquals(normalized, "pre")
                ? HtmlWhiteSpace.Pre
                : CssEquals(normalized, "pre-wrap")
                    ? HtmlWhiteSpace.PreWrap
                    : CssEquals(normalized, "pre-line")
                        ? HtmlWhiteSpace.PreLine
                        : HtmlWhiteSpace.Normal;
        WrapText = WhiteSpace is HtmlWhiteSpace.Normal or HtmlWhiteSpace.PreWrap or HtmlWhiteSpace.PreLine;
    }

    private void ApplyOverflow(ReadOnlySpan<char> normalized)
    {
        ClipContent = CssEquals(normalized, "hidden") || CssEquals(normalized, "clip") || CssEquals(normalized, "auto") || CssEquals(normalized, "scroll");
        IsScrollContainer = CssEquals(normalized, "auto") || CssEquals(normalized, "scroll");
    }

    private static HtmlContainment ParseContainment(ReadOnlySpan<char> normalized)
    {
        var containment = HtmlContainment.None;
        foreach (var token in normalized.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            containment |= token switch
            {
                "none" => HtmlContainment.None,
                "strict" => HtmlContainment.Layout | HtmlContainment.Paint | HtmlContainment.Size | HtmlContainment.Style,
                "content" => HtmlContainment.Layout | HtmlContainment.Paint | HtmlContainment.Style,
                "layout" => HtmlContainment.Layout,
                "paint" => HtmlContainment.Paint,
                "size" => HtmlContainment.Size,
                "style" => HtmlContainment.Style,
                _ => HtmlContainment.None
            };
        }

        return containment;
    }

    private static bool CssEquals(ReadOnlySpan<char> value, string expected)
    {
        return value.Equals(expected.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyBorderShorthand(ReadOnlySpan<char> normalized)
    {
        ApplyBorderShorthand(normalized, HtmlBorderSide.All);
    }

    private void ApplyBorderShorthand(ReadOnlySpan<char> normalized, HtmlBorderSide side)
    {
        Span<Range> parts = stackalloc Range[8];
        var partCount = SplitWhitespace(normalized, parts);
        for (var index = 0; index < partCount; index++)
        {
            var part = normalized[parts[index]];
            var style = ParseBorderStyle(part, Defaults.BorderStyle);
            if (style != Defaults.BorderStyle)
            {
                SetBorderStyle(side, style);
                continue;
            }

            var length = ParseLengthValue(part, HtmlLengthProperty.BorderWidth);
            if (length is not null)
            {
                SetBorderWidth(side, length.Value.ValueOrDefault(BorderWidth));
                continue;
            }

            SetBorderColor(side, part.ToString());
        }
    }

    private void SetBorderWidth(HtmlBorderSide side, float width)
    {
        if ((side & HtmlBorderSide.Left) != 0)
            BorderLeftWidth = width;
        if ((side & HtmlBorderSide.Top) != 0)
            BorderTopWidth = width;
        if ((side & HtmlBorderSide.Right) != 0)
            BorderRightWidth = width;
        if ((side & HtmlBorderSide.Bottom) != 0)
            BorderBottomWidth = width;
        BorderWidth = Math.Max(Math.Max(BorderLeftWidth, BorderRightWidth), Math.Max(BorderTopWidth, BorderBottomWidth));
    }

    private void SetBorderStyle(HtmlBorderSide side, SceneBorderStyle style)
    {
        if ((side & HtmlBorderSide.Left) != 0)
            BorderLeftStyle = style;
        if ((side & HtmlBorderSide.Top) != 0)
            BorderTopStyle = style;
        if ((side & HtmlBorderSide.Right) != 0)
            BorderRightStyle = style;
        if ((side & HtmlBorderSide.Bottom) != 0)
            BorderBottomStyle = style;
        BorderStyle = ResolveAggregateBorderStyle();
    }

    private void SetBorderColor(HtmlBorderSide side, string? color)
    {
        if ((side & HtmlBorderSide.Left) != 0)
            BorderLeftColor = color;
        if ((side & HtmlBorderSide.Top) != 0)
            BorderTopColor = color;
        if ((side & HtmlBorderSide.Right) != 0)
            BorderRightColor = color;
        if ((side & HtmlBorderSide.Bottom) != 0)
            BorderBottomColor = color;
        BorderColor = BorderTopColor ?? BorderRightColor ?? BorderBottomColor ?? BorderLeftColor ?? BorderColor;
    }

    private SceneBorderStyle ResolveAggregateBorderStyle()
        => BorderLeftStyle != Defaults.BorderStyle ? BorderLeftStyle :
           BorderTopStyle != Defaults.BorderStyle ? BorderTopStyle :
           BorderRightStyle != Defaults.BorderStyle ? BorderRightStyle :
           BorderBottomStyle != Defaults.BorderStyle ? BorderBottomStyle :
           Defaults.BorderStyle;

    private void ClearBorderSides()
    {
        BorderLeftWidth = 0;
        BorderTopWidth = 0;
        BorderRightWidth = 0;
        BorderBottomWidth = 0;
        BorderLeftStyle = Defaults.BorderStyle;
        BorderTopStyle = Defaults.BorderStyle;
        BorderRightStyle = Defaults.BorderStyle;
        BorderBottomStyle = Defaults.BorderStyle;
        BorderLeftColor = null;
        BorderTopColor = null;
        BorderRightColor = null;
        BorderBottomColor = null;
    }

    private SceneBoxShadow[]? ParseBoxShadows(ReadOnlySpan<char> normalized)
        => ParseShadows(normalized, allowSpread: true);

    private SceneBoxShadow[]? ParseTextShadows(ReadOnlySpan<char> normalized)
        => ParseShadows(normalized, allowSpread: false);

    private SceneBoxShadow[]? ParseShadows(ReadOnlySpan<char> normalized, bool allowSpread)
    {
        if (CssEquals(normalized, "none"))
            return null;

        List<SceneBoxShadow>? shadows = null;
        var start = 0;
        while (start < normalized.Length)
        {
            var comma = FindTopLevelComma(normalized, start);
            var end = comma < 0 ? normalized.Length : comma;
            if (TryParseShadow(normalized[start..end].Trim(), allowSpread, out var shadow))
            {
                shadows ??= [];
                shadows.Add(shadow);
            }

            if (comma < 0)
                break;

            start = comma + 1;
        }

        return shadows is { Count: > 0 } ? [.. shadows] : null;
    }

    private bool TryParseShadow(ReadOnlySpan<char> value, bool allowSpread, out SceneBoxShadow shadow)
    {
        shadow = new SceneBoxShadow();
        if (value.IsWhiteSpace())
            return false;

        Span<float> lengths = stackalloc float[4];
        var lengthCount = 0;
        string? color = null;
        var cursor = 0;
        while (TryReadCssToken(value, ref cursor, out var token))
        {
            if (CssEquals(token, "inset"))
                return false;

            if (IsColorFunction(token) || token.StartsWith('#') || IsLikelyNamedColor(token))
            {
                color = token.ToString();
                continue;
            }

            if (lengthCount >= lengths.Length)
                continue;

            var length = ParseLengthValue(token, HtmlLengthProperty.Width);
            if (length is null || length.Value.Unit == CssLengthUnit.Percent)
                continue;

            lengths[lengthCount++] = length.Value.Value;
        }

        if (lengthCount < 2)
            return false;

        shadow = new SceneBoxShadow(
            color,
            lengths[0],
            lengths[1],
            lengthCount > 2 ? Math.Max(0, lengths[2]) : 0,
            allowSpread && lengthCount > 3 ? lengths[3] : 0);
        return true;
    }

    private static int FindTopLevelComma(ReadOnlySpan<char> span, int start)
    {
        var quote = '\0';
        var parenDepth = 0;
        for (var index = start; index < span.Length; index++)
        {
            var ch = span[index];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                else if (ch == '\\' && index + 1 < span.Length)
                    index++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                continue;
            }

            if (ch == ')' && parenDepth > 0)
            {
                parenDepth--;
                continue;
            }

            if (ch == ',' && parenDepth == 0)
                return index;
        }

        return -1;
    }

    private static bool TryExtractBackgroundColor(ReadOnlySpan<char> normalized, out string color)
    {
        color = string.Empty;
        var cursor = 0;
        while (TryReadCssToken(normalized, ref cursor, out var token))
        {
            if (IsBackgroundKeyword(token))
                continue;

            if (IsColorFunction(token) || token.StartsWith('#') || IsLikelyNamedColor(token))
            {
                color = token.ToString();
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractBackgroundImageSource(ReadOnlySpan<char> normalized, out string source)
    {
        source = string.Empty;
        var cursor = 0;
        while (TryReadCssToken(normalized, ref cursor, out var token))
        {
            if (!token.StartsWith("url(".AsSpan(), StringComparison.OrdinalIgnoreCase) || !token.EndsWith(')'))
                continue;

            var inner = token[4..^1].Trim();
            if (inner.Length == 0)
                return false;

            source = TrimQuotes(inner).ToString();
            return source.Length > 0;
        }

        return false;
    }

    internal void ResolveBackgroundImageUrl(Func<string, string?> resolve)
    {
        if (string.IsNullOrWhiteSpace(BackgroundImageSource))
            return;

        BackgroundImageSource = resolve(BackgroundImageSource);
    }

    private static bool TryReadCssToken(ReadOnlySpan<char> value, ref int cursor, out ReadOnlySpan<char> token)
    {
        token = default;
        while (cursor < value.Length && char.IsWhiteSpace(value[cursor]))
            cursor++;

        if (cursor >= value.Length)
            return false;

        var start = cursor;
        var quote = '\0';
        var parenDepth = 0;
        while (cursor < value.Length)
        {
            var ch = value[cursor];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                cursor++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                cursor++;
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                cursor++;
                continue;
            }

            if (ch == ')' && parenDepth > 0)
            {
                parenDepth--;
                cursor++;
                continue;
            }

            if (parenDepth == 0 && char.IsWhiteSpace(ch))
                break;

            cursor++;
        }

        token = value[start..cursor].Trim();
        return token.Length > 0;
    }

    private static bool IsColorFunction(ReadOnlySpan<char> token)
        => token.StartsWith("rgb(".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
           token.StartsWith("rgba(".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
           token.StartsWith("hsl(".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
           token.StartsWith("hsla(".AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyNamedColor(ReadOnlySpan<char> token)
        => token is "transparent" or "currentcolor" or "black" or "white" or "red" or "green" or "blue" or "yellow" or "orange" or "purple" or "gray" or "grey";

    private static bool IsBackgroundKeyword(ReadOnlySpan<char> token)
        => token is "none" or "repeat" or "repeat-x" or "repeat-y" or "no-repeat" or "space" or "round" or
           "scroll" or "fixed" or "local" or "left" or "right" or "top" or "bottom" or "center" or
           "cover" or "contain" or "border-box" or "padding-box" or "content-box" or "text" ||
           token == "/" ||
           token.StartsWith("url(".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
           token.StartsWith("linear-gradient(".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
           token.StartsWith("radial-gradient(".AsSpan(), StringComparison.OrdinalIgnoreCase);

    private void ApplyBoxSpacing(
        ReadOnlySpan<char> normalized,
        HtmlLengthProperty top,
        HtmlLengthProperty right,
        HtmlLengthProperty bottom,
        HtmlLengthProperty left)
    {
        Span<Range> parts = stackalloc Range[4];
        var partCount = SplitWhitespace(normalized, parts);
        if (partCount == 0)
            return;

        var first = ParseLengthValue(normalized[parts[0]], top);
        var second = partCount > 1 ? ParseLengthValue(normalized[parts[1]], right) : first;
        var third = partCount > 2 ? ParseLengthValue(normalized[parts[2]], bottom) : first;
        var fourth = partCount > 3 ? ParseLengthValue(normalized[parts[3]], left) : second;
        ApplyLength(top, first, 0);
        ApplyLength(right, second, 0);
        ApplyLength(bottom, third, 0);
        ApplyLength(left, fourth, 0);
    }

    private void ApplyTwoValueSpacing(ReadOnlySpan<char> normalized, HtmlLengthProperty start, HtmlLengthProperty end)
    {
        Span<Range> parts = stackalloc Range[2];
        var partCount = SplitWhitespace(normalized, parts);
        if (partCount == 0)
            return;

        var first = ParseLengthValue(normalized[parts[0]], start);
        var second = partCount > 1 ? ParseLengthValue(normalized[parts[1]], end) : first;
        ApplyLength(start, first, 0);
        ApplyLength(end, second, 0);
    }

    private HtmlLengthProperty ResolveInlineStartMargin()
        => Direction == LayoutDirection.Rtl ? HtmlLengthProperty.MarginRight : HtmlLengthProperty.MarginLeft;

    private HtmlLengthProperty ResolveInlineEndMargin()
        => Direction == LayoutDirection.Rtl ? HtmlLengthProperty.MarginLeft : HtmlLengthProperty.MarginRight;

    private static SceneBorderStyle ParseBorderStyle(ReadOnlySpan<char> normalized, SceneBorderStyle fallback)
    {
        return normalized switch
        {
            "none" or "hidden" => SceneBorderStyle.None,
            "dotted" => SceneBorderStyle.Dotted,
            "solid" or "double" or "groove" or "ridge" or "inset" or "outset" or "dashed" => SceneBorderStyle.Solid,
            _ => fallback
        };
    }

    private void SetLength(ReadOnlySpan<char> value, HtmlLengthProperty property)
    {
        var token = FirstWhitespaceToken(value.Trim());
        if (token is "auto" or "none")
        {
            ClearAutoLength(property);
            return;
        }

        ApplyLength(property, ParseLengthValue(value, property), GetLengthPropertyValue(property));
    }

    private void ClearAutoLength(HtmlLengthProperty property)
    {
        switch (property)
        {
            case HtmlLengthProperty.Width:
            case HtmlLengthProperty.Height:
            case HtmlLengthProperty.MinWidth:
            case HtmlLengthProperty.MaxWidth:
            case HtmlLengthProperty.MinHeight:
            case HtmlLengthProperty.MaxHeight:
            case HtmlLengthProperty.FlexBasis:
                SetLengthPropertyValue(property, Defaults.UnsetLength);
                SetLengthPropertyUnit(property, CssLengthUnit.Px);
                explicitLengthFlags |= ResolveExplicitLengthFlag(property);
                break;
            case HtmlLengthProperty.Left:
            case HtmlLengthProperty.Top:
            case HtmlLengthProperty.Right:
            case HtmlLengthProperty.Bottom:
                SetLengthPropertyValue(property, Defaults.UnsetLength);
                SetLengthPropertyUnit(property, CssLengthUnit.Px);
                explicitLengthFlags |= ResolveExplicitLengthFlag(property);
                break;
            case HtmlLengthProperty.MarginLeft:
            case HtmlLengthProperty.MarginTop:
            case HtmlLengthProperty.MarginRight:
            case HtmlLengthProperty.MarginBottom:
                SetLengthPropertyValue(property, 0);
                SetLengthPropertyUnit(property, CssLengthUnit.Px);
                explicitLengthFlags |= ResolveExplicitLengthFlag(property);
                break;
        }
    }

    private bool IsLengthExplicit(HtmlLengthProperty property)
    {
        var flag = ResolveExplicitLengthFlag(property);
        return flag != 0 && (explicitLengthFlags & flag) != 0;
    }

    private void ApplyAttributeLength(string? value, Action<HtmlComputedStyle, CssLength> assign, Action<HtmlComputedStyle, bool> setUnit)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var parsed = ParseLengthValue(value.AsSpan(), HtmlLengthProperty.Width);
        if (!parsed.HasValue)
            return;

        assign(this, parsed.Value);
        setUnit(this, parsed.Value.Unit == CssLengthUnit.Percent);
    }

    private void SetUnit(LayoutValueUnitFlags flag, bool enabled)
    {
        UnitFlags = enabled
            ? UnitFlags | flag
            : UnitFlags & ~flag;
    }

    private static CrossAlignment ParseAlignment(ReadOnlySpan<char> value)
    {
        return value switch
        {
            "center" => CrossAlignment.Center,
            "flex-end" or "end" or "self-end" => CrossAlignment.End,
            "stretch" => CrossAlignment.Stretch,
            _ => CrossAlignment.Start
        };
    }

    private static MainAxisJustification ParseJustifyContent(ReadOnlySpan<char> value)
    {
        return value switch
        {
            "center" => MainAxisJustification.Center,
            "flex-end" or "end" or "self-end" => MainAxisJustification.End,
            "space-between" => MainAxisJustification.SpaceBetween,
            "space-around" => MainAxisJustification.SpaceAround,
            "space-evenly" => MainAxisJustification.SpaceEvenly,
            _ => MainAxisJustification.Start
        };
    }

    private static ReadOnlySpan<char> ParseFirstPlaceValue(ReadOnlySpan<char> value)
    {
        Span<Range> parts = stackalloc Range[2];
        var partCount = SplitWhitespace(value, parts);
        return partCount == 0 ? value : value[parts[0]];
    }

    private static ReadOnlySpan<char> ParseSecondOrFirstPlaceValue(ReadOnlySpan<char> value)
    {
        Span<Range> parts = stackalloc Range[2];
        var partCount = SplitWhitespace(value, parts);
        return partCount > 1 ? value[parts[1]] : partCount == 1 ? value[parts[0]] : value;
    }

    private static bool TryParseAspectRatio(ReadOnlySpan<char> value, out float aspectRatio)
    {
        aspectRatio = 0;
        var normalized = value.Trim();
        if (normalized.IsWhiteSpace() || CssEquals(normalized, "auto"))
            return false;

        var slash = normalized.IndexOf('/');
        if (slash >= 0)
        {
            return float.TryParse(normalized[..slash].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
                   float.TryParse(normalized[(slash + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
                   numerator > 0 &&
                   denominator > 0 &&
                   (aspectRatio = numerator / denominator) > 0;
        }

        return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out aspectRatio) &&
               aspectRatio > 0;
    }

    private CssLength? ParseLengthValue(ReadOnlySpan<char> value, HtmlLengthProperty property)
    {
        var normalizedText = value.Trim();
        if (normalizedText.Length == 0)
            return null;

        var normalized = FirstWhitespaceToken(normalizedText);
        if (normalized is "auto" or "none")
            return null;

        if (normalized.EndsWith('%') &&
            float.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            if (property is HtmlLengthProperty.FontSize or HtmlLengthProperty.LineHeight)
                return new CssLength(ResolveFontRelativeBase(property) * (percent * 0.01f), CssLengthUnit.Px);

            return new CssLength(percent, CssLengthUnit.Percent);
        }

        if (TryParseUnitLength(normalized, "rem", out var rem))
            return new CssLength(rem * ResolveRootFontSize(), CssLengthUnit.Px);

        if (TryParseUnitLength(normalized, "em", out var em))
            return new CssLength(em * ResolveFontRelativeBase(property), CssLengthUnit.Px);

        if (TryParseUnitLength(normalized, "pt", out var pt))
            return new CssLength(pt * (4f / 3f), CssLengthUnit.Px);

        if (TryParseUnitLength(normalized, "vw", out var vw))
            return new CssLength(vw, CssLengthUnit.Vw);

        if (TryParseUnitLength(normalized, "vh", out var vh))
            return new CssLength(vh, CssLengthUnit.Vh);

        if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^2];

        if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
            return null;

        if (property == HtmlLengthProperty.LineHeight && !normalizedText.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return new CssLength(Math.Max(0, pixels) * ResolveCurrentFontSize(), CssLengthUnit.Px);

        return new CssLength(pixels, CssLengthUnit.Px);
    }

    private static bool TryParseUnitLength(ReadOnlySpan<char> value, string unit, out float numeric)
    {
        numeric = 0;
        if (!value.EndsWith(unit.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        return float.TryParse(value[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out numeric);
    }

    private float ResolveCurrentFontSize()
        => FontSize > 0 ? FontSize : ResolveRootFontSize();

    private float ResolveFontRelativeBase(HtmlLengthProperty property)
    {
        if (property == HtmlLengthProperty.FontSize && fontSizeReference > 0)
            return fontSizeReference;

        return ResolveCurrentFontSize();
    }

    private float ResolveRootFontSize()
        => rootFontSize > 0 ? rootFontSize : Defaults.DefaultFontSizeFallback;

    private static float ParseFloat(ReadOnlySpan<char> value, float fallback)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static ReadOnlySpan<char> FirstWhitespaceToken(ReadOnlySpan<char> value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
                return value[..index];
        }

        return value;
    }

    private static ReadOnlySpan<char> TrimQuotes(ReadOnlySpan<char> value)
    {
        while (value.Length > 0 && value[0] is '"' or '\'')
            value = value[1..];

        while (value.Length > 0 && value[^1] is '"' or '\'')
            value = value[..^1];

        return value;
    }

    private static int SplitWhitespace(ReadOnlySpan<char> value, Span<Range> ranges)
    {
        var count = 0;
        var index = 0;
        while (index < value.Length && count < ranges.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
                index++;

            var start = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index]))
                index++;

            if (start < index)
                ranges[count++] = start..index;
        }

        return count;
    }

    private static int ParseFontWeight(ReadOnlySpan<char> value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            return numeric;

        return value switch
        {
            "bold" => 700,
            "medium" => 500,
            _ => 400
        };
    }

    private HtmlComputedStyle Clone()
    {
        return (HtmlComputedStyle)MemberwiseClone();
    }

    private void ApplyLength(HtmlLengthProperty property, CssLength? value, float fallback)
    {
        if (value is not { } parsed)
            return;

        explicitLengthFlags |= ResolveExplicitLengthFlag(property);
        SetLengthPropertyValue(property, parsed.ValueOrDefault(fallback));
        SetLengthPropertyUnit(property, parsed.Unit);
    }

    private float GetLengthPropertyValue(HtmlLengthProperty property)
        => property switch
        {
            HtmlLengthProperty.Left => Left,
            HtmlLengthProperty.Top => Top,
            HtmlLengthProperty.Right => Right,
            HtmlLengthProperty.Bottom => Bottom,
            HtmlLengthProperty.Width => Width,
            HtmlLengthProperty.Height => Height,
            HtmlLengthProperty.MinWidth => MinWidth,
            HtmlLengthProperty.MaxWidth => MaxWidth,
            HtmlLengthProperty.MinHeight => MinHeight,
            HtmlLengthProperty.MaxHeight => MaxHeight,
            HtmlLengthProperty.MarginLeft => MarginLeft,
            HtmlLengthProperty.MarginTop => MarginTop,
            HtmlLengthProperty.MarginRight => MarginRight,
            HtmlLengthProperty.MarginBottom => MarginBottom,
            HtmlLengthProperty.PaddingLeft => PaddingLeft,
            HtmlLengthProperty.PaddingTop => PaddingTop,
            HtmlLengthProperty.PaddingRight => PaddingRight,
            HtmlLengthProperty.PaddingBottom => PaddingBottom,
            HtmlLengthProperty.Gap => Gap,
            HtmlLengthProperty.TableBorderSpacing => TableBorderSpacing,
            HtmlLengthProperty.BorderWidth => BorderWidth,
            HtmlLengthProperty.BorderRadius => BorderRadius,
            HtmlLengthProperty.FontSize => FontSize,
            HtmlLengthProperty.LineHeight => LineHeight,
            HtmlLengthProperty.FlexBasis => FlexBasis,
            _ => Defaults.UnsetLength
        };

    private void SetLengthPropertyValue(HtmlLengthProperty property, float value)
    {
        switch (property)
        {
            case HtmlLengthProperty.Left:
                Left = value;
                break;
            case HtmlLengthProperty.Top:
                Top = value;
                break;
            case HtmlLengthProperty.Right:
                Right = value;
                break;
            case HtmlLengthProperty.Bottom:
                Bottom = value;
                break;
            case HtmlLengthProperty.Width:
                Width = value;
                break;
            case HtmlLengthProperty.Height:
                Height = value;
                break;
            case HtmlLengthProperty.MinWidth:
                MinWidth = value;
                break;
            case HtmlLengthProperty.MaxWidth:
                MaxWidth = value;
                break;
            case HtmlLengthProperty.MinHeight:
                MinHeight = value;
                break;
            case HtmlLengthProperty.MaxHeight:
                MaxHeight = value;
                break;
            case HtmlLengthProperty.MarginLeft:
                MarginLeft = value;
                break;
            case HtmlLengthProperty.MarginTop:
                MarginTop = value;
                break;
            case HtmlLengthProperty.MarginRight:
                MarginRight = value;
                break;
            case HtmlLengthProperty.MarginBottom:
                MarginBottom = value;
                break;
            case HtmlLengthProperty.PaddingLeft:
                PaddingLeft = value;
                break;
            case HtmlLengthProperty.PaddingTop:
                PaddingTop = value;
                break;
            case HtmlLengthProperty.PaddingRight:
                PaddingRight = value;
                break;
            case HtmlLengthProperty.PaddingBottom:
                PaddingBottom = value;
                break;
            case HtmlLengthProperty.Gap:
                Gap = value;
                break;
            case HtmlLengthProperty.TableBorderSpacing:
                TableBorderSpacing = value;
                break;
            case HtmlLengthProperty.BorderWidth:
                BorderWidth = value;
                break;
            case HtmlLengthProperty.BorderRadius:
                BorderRadius = value;
                break;
            case HtmlLengthProperty.FontSize:
                FontSize = value;
                break;
            case HtmlLengthProperty.LineHeight:
                LineHeight = value;
                break;
            case HtmlLengthProperty.FlexBasis:
                FlexBasis = value;
                break;
        }
    }

    private void SetLengthPropertyUnit(HtmlLengthProperty property, CssLengthUnit unit)
    {
        SetUnit(ResolvePercentFlag(property), unit == CssLengthUnit.Percent);
        SetViewportUnit(ResolveViewportFlag(property), unit);
        SetContainerPercentUnit(ResolveContainerPercentFlag(property), unit == CssLengthUnit.Percent);
    }

    private void ResolveViewportUnits(float viewportWidth, float viewportHeight)
    {
        if (viewportLengthFlags == 0)
            return;

        foreach (var property in lengthProperties)
        {
            var flag = ResolveViewportFlag(property);
            if (flag == 0)
                continue;

            var vhFlag = ToVhFlag(flag);
            var usesVw = (viewportLengthFlags & flag) != 0;
            var usesVh = (viewportLengthFlags & vhFlag) != 0;
            if (!usesVw && !usesVh)
                continue;

            var value = GetLengthPropertyValue(property);
            if (!LayoutValue.IsSet(value))
                continue;

            var relativeTo = usesVh ? viewportHeight : viewportWidth;
            SetLengthPropertyValue(property, relativeTo * (value * 0.01f));
        }

        viewportLengthFlags = 0;
    }

    private void ResolveContainerPercentUnits(float containerWidth, bool resolveInlineSize)
    {
        if (containerPercentLengthFlags == 0)
            return;

        foreach (var property in lengthProperties)
        {
            var flag = ResolveContainerPercentFlag(property);
            if (flag == 0 || (containerPercentLengthFlags & flag) == 0)
                continue;
            if (!resolveInlineSize &&
                property is HtmlLengthProperty.Width or HtmlLengthProperty.MinWidth or HtmlLengthProperty.MaxWidth)
            {
                continue;
            }

            var value = GetLengthPropertyValue(property);
            if (LayoutValue.IsSet(value))
            {
                SetLengthPropertyValue(property, containerWidth * (value * 0.01f));
                SetUnit(ResolvePercentFlag(property), false);
            }
        }

        containerPercentLengthFlags = 0;
    }

    private static LayoutValueUnitFlags ResolvePercentFlag(HtmlLengthProperty property)
        => property switch
        {
            HtmlLengthProperty.Left => LayoutValueUnitFlags.LeftPercent,
            HtmlLengthProperty.Top => LayoutValueUnitFlags.TopPercent,
            HtmlLengthProperty.Right => LayoutValueUnitFlags.RightPercent,
            HtmlLengthProperty.Bottom => LayoutValueUnitFlags.BottomPercent,
            HtmlLengthProperty.Width => LayoutValueUnitFlags.WidthPercent,
            HtmlLengthProperty.Height => LayoutValueUnitFlags.HeightPercent,
            HtmlLengthProperty.MinWidth => LayoutValueUnitFlags.MinWidthPercent,
            HtmlLengthProperty.MaxWidth => LayoutValueUnitFlags.MaxWidthPercent,
            HtmlLengthProperty.MinHeight => LayoutValueUnitFlags.MinHeightPercent,
            HtmlLengthProperty.MaxHeight => LayoutValueUnitFlags.MaxHeightPercent,
            HtmlLengthProperty.FlexBasis => LayoutValueUnitFlags.FlexBasisPercent,
            _ => LayoutValueUnitFlags.None
        };

    private static HtmlViewportLengthFlags ResolveViewportFlag(HtmlLengthProperty property)
        => property switch
        {
            HtmlLengthProperty.Left => HtmlViewportLengthFlags.LeftVw,
            HtmlLengthProperty.Top => HtmlViewportLengthFlags.TopVw,
            HtmlLengthProperty.Right => HtmlViewportLengthFlags.RightVw,
            HtmlLengthProperty.Bottom => HtmlViewportLengthFlags.BottomVw,
            HtmlLengthProperty.Width => HtmlViewportLengthFlags.WidthVw,
            HtmlLengthProperty.Height => HtmlViewportLengthFlags.HeightVw,
            HtmlLengthProperty.MinWidth => HtmlViewportLengthFlags.MinWidthVw,
            HtmlLengthProperty.MaxWidth => HtmlViewportLengthFlags.MaxWidthVw,
            HtmlLengthProperty.MinHeight => HtmlViewportLengthFlags.MinHeightVw,
            HtmlLengthProperty.MaxHeight => HtmlViewportLengthFlags.MaxHeightVw,
            HtmlLengthProperty.MarginLeft => HtmlViewportLengthFlags.MarginLeftVw,
            HtmlLengthProperty.MarginTop => HtmlViewportLengthFlags.MarginTopVw,
            HtmlLengthProperty.MarginRight => HtmlViewportLengthFlags.MarginRightVw,
            HtmlLengthProperty.MarginBottom => HtmlViewportLengthFlags.MarginBottomVw,
            HtmlLengthProperty.PaddingLeft => HtmlViewportLengthFlags.PaddingLeftVw,
            HtmlLengthProperty.PaddingTop => HtmlViewportLengthFlags.PaddingTopVw,
            HtmlLengthProperty.PaddingRight => HtmlViewportLengthFlags.PaddingRightVw,
            HtmlLengthProperty.PaddingBottom => HtmlViewportLengthFlags.PaddingBottomVw,
            HtmlLengthProperty.Gap => HtmlViewportLengthFlags.GapVw,
            HtmlLengthProperty.BorderWidth => HtmlViewportLengthFlags.BorderWidthVw,
            HtmlLengthProperty.BorderRadius => HtmlViewportLengthFlags.BorderRadiusVw,
            HtmlLengthProperty.FontSize => HtmlViewportLengthFlags.FontSizeVw,
            HtmlLengthProperty.LineHeight => HtmlViewportLengthFlags.LineHeightVw,
            HtmlLengthProperty.FlexBasis => HtmlViewportLengthFlags.FlexBasisVw,
            _ => 0
        };

    private static HtmlContainerPercentLengthFlags ResolveContainerPercentFlag(HtmlLengthProperty property)
        => property switch
        {
            HtmlLengthProperty.Width => HtmlContainerPercentLengthFlags.Width,
            HtmlLengthProperty.MinWidth => HtmlContainerPercentLengthFlags.MinWidth,
            HtmlLengthProperty.MaxWidth => HtmlContainerPercentLengthFlags.MaxWidth,
            HtmlLengthProperty.MarginLeft => HtmlContainerPercentLengthFlags.MarginLeft,
            HtmlLengthProperty.MarginTop => HtmlContainerPercentLengthFlags.MarginTop,
            HtmlLengthProperty.MarginRight => HtmlContainerPercentLengthFlags.MarginRight,
            HtmlLengthProperty.MarginBottom => HtmlContainerPercentLengthFlags.MarginBottom,
            HtmlLengthProperty.PaddingLeft => HtmlContainerPercentLengthFlags.PaddingLeft,
            HtmlLengthProperty.PaddingTop => HtmlContainerPercentLengthFlags.PaddingTop,
            HtmlLengthProperty.PaddingRight => HtmlContainerPercentLengthFlags.PaddingRight,
            HtmlLengthProperty.PaddingBottom => HtmlContainerPercentLengthFlags.PaddingBottom,
            HtmlLengthProperty.Gap => HtmlContainerPercentLengthFlags.Gap,
            _ => 0
        };

    private static HtmlExplicitLengthFlags ResolveExplicitLengthFlag(HtmlLengthProperty property)
        => property switch
        {
            HtmlLengthProperty.Left => HtmlExplicitLengthFlags.Left,
            HtmlLengthProperty.Top => HtmlExplicitLengthFlags.Top,
            HtmlLengthProperty.Right => HtmlExplicitLengthFlags.Right,
            HtmlLengthProperty.Bottom => HtmlExplicitLengthFlags.Bottom,
            HtmlLengthProperty.Width => HtmlExplicitLengthFlags.Width,
            HtmlLengthProperty.Height => HtmlExplicitLengthFlags.Height,
            HtmlLengthProperty.MinWidth => HtmlExplicitLengthFlags.MinWidth,
            HtmlLengthProperty.MaxWidth => HtmlExplicitLengthFlags.MaxWidth,
            HtmlLengthProperty.MinHeight => HtmlExplicitLengthFlags.MinHeight,
            HtmlLengthProperty.MaxHeight => HtmlExplicitLengthFlags.MaxHeight,
            HtmlLengthProperty.MarginLeft => HtmlExplicitLengthFlags.MarginLeft,
            HtmlLengthProperty.MarginTop => HtmlExplicitLengthFlags.MarginTop,
            HtmlLengthProperty.MarginRight => HtmlExplicitLengthFlags.MarginRight,
            HtmlLengthProperty.MarginBottom => HtmlExplicitLengthFlags.MarginBottom,
            HtmlLengthProperty.PaddingLeft => HtmlExplicitLengthFlags.PaddingLeft,
            HtmlLengthProperty.PaddingTop => HtmlExplicitLengthFlags.PaddingTop,
            HtmlLengthProperty.PaddingRight => HtmlExplicitLengthFlags.PaddingRight,
            HtmlLengthProperty.PaddingBottom => HtmlExplicitLengthFlags.PaddingBottom,
            HtmlLengthProperty.FlexBasis => HtmlExplicitLengthFlags.FlexBasis,
            _ => 0
        };

    private static HtmlViewportLengthFlags ToVhFlag(HtmlViewportLengthFlags vwFlag)
        => (HtmlViewportLengthFlags)((ulong)vwFlag << 32);

    private void SetViewportUnit(HtmlViewportLengthFlags vwFlag, CssLengthUnit unit)
    {
        if (vwFlag == 0)
            return;

        var vhFlag = ToVhFlag(vwFlag);
        viewportLengthFlags &= ~vwFlag;
        viewportLengthFlags &= ~vhFlag;
        if (unit == CssLengthUnit.Vw)
            viewportLengthFlags |= vwFlag;
        else if (unit == CssLengthUnit.Vh)
            viewportLengthFlags |= vhFlag;
    }

    private void SetContainerPercentUnit(HtmlContainerPercentLengthFlags flag, bool enabled)
    {
        if (flag == 0)
            return;

        containerPercentLengthFlags = enabled
            ? containerPercentLengthFlags | flag
            : containerPercentLengthFlags & ~flag;
    }

    private readonly record struct CssLength(float Value, CssLengthUnit Unit)
    {
        public float ValueOrDefault(float fallback) => float.IsNaN(Value) ? fallback : Value;
    }
}

internal enum CssLengthUnit : byte
{
    Px,
    Percent,
    Vw,
    Vh
}

internal enum HtmlLengthProperty : byte
{
    Left,
    Top,
    Right,
    Bottom,
    Width,
    Height,
    MinWidth,
    MaxWidth,
    MinHeight,
    MaxHeight,
    MarginLeft,
    MarginTop,
    MarginRight,
    MarginBottom,
    PaddingLeft,
    PaddingTop,
    PaddingRight,
    PaddingBottom,
    Gap,
    TableBorderSpacing,
    BorderWidth,
    BorderRadius,
    FontSize,
    LineHeight,
    FlexBasis
}

[Flags]
internal enum HtmlExplicitLengthFlags : uint
{
    None = 0,
    Left = 1u << 0,
    Top = 1u << 1,
    Right = 1u << 2,
    Bottom = 1u << 3,
    Width = 1u << 4,
    Height = 1u << 5,
    MinWidth = 1u << 6,
    MaxWidth = 1u << 7,
    MinHeight = 1u << 8,
    MaxHeight = 1u << 9,
    MarginLeft = 1u << 10,
    MarginTop = 1u << 11,
    MarginRight = 1u << 12,
    MarginBottom = 1u << 13,
    PaddingLeft = 1u << 14,
    PaddingTop = 1u << 15,
    PaddingRight = 1u << 16,
    PaddingBottom = 1u << 17,
    FlexBasis = 1u << 18
}

[Flags]
internal enum HtmlContainerPercentLengthFlags : ushort
{
    None = 0,
    Width = 1 << 0,
    MinWidth = 1 << 1,
    MaxWidth = 1 << 2,
    MarginLeft = 1 << 3,
    MarginTop = 1 << 4,
    MarginRight = 1 << 5,
    MarginBottom = 1 << 6,
    PaddingLeft = 1 << 7,
    PaddingTop = 1 << 8,
    PaddingRight = 1 << 9,
    PaddingBottom = 1 << 10,
    Gap = 1 << 11
}

[Flags]
internal enum HtmlViewportLengthFlags : ulong
{
    None = 0,
    LeftVw = 1UL << 0,
    TopVw = 1UL << 1,
    RightVw = 1UL << 2,
    BottomVw = 1UL << 3,
    WidthVw = 1UL << 4,
    HeightVw = 1UL << 5,
    MinWidthVw = 1UL << 6,
    MaxWidthVw = 1UL << 7,
    MinHeightVw = 1UL << 8,
    MaxHeightVw = 1UL << 9,
    MarginLeftVw = 1UL << 10,
    MarginTopVw = 1UL << 11,
    MarginRightVw = 1UL << 12,
    MarginBottomVw = 1UL << 13,
    PaddingLeftVw = 1UL << 14,
    PaddingTopVw = 1UL << 15,
    PaddingRightVw = 1UL << 16,
    PaddingBottomVw = 1UL << 17,
    GapVw = 1UL << 18,
    BorderWidthVw = 1UL << 19,
    BorderRadiusVw = 1UL << 20,
    FontSizeVw = 1UL << 21,
    LineHeightVw = 1UL << 22,
    FlexBasisVw = 1UL << 23
}

internal enum HtmlDisplay : byte
{
    None,
    Contents,
    Inline,
    InlineBlock,
    Block,
    Flex
}

internal enum HtmlFloat : byte
{
    None,
    Left,
    Right
}

internal enum HtmlClear : byte
{
    None,
    Left,
    Right,
    Both
}

[Flags]
internal enum HtmlContainment : byte
{
    None = 0,
    Layout = 1 << 0,
    Paint = 1 << 1,
    Size = 1 << 2,
    Style = 1 << 3
}

internal enum HtmlWhiteSpace : byte
{
    Normal,
    NoWrap,
    Pre,
    PreWrap,
    PreLine
}

internal enum HtmlTextTransform : byte
{
    None,
    Uppercase,
    Lowercase
}

[Flags]
internal enum HtmlBorderSide : byte
{
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    All = Left | Top | Right | Bottom
}
