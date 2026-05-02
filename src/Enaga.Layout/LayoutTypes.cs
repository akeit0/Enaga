using Enaga.Scene;

namespace Enaga.Layout;

public enum LayoutChildKind : byte
{
    Invalid,
    Element,
    Button,
    Divider,
    Spacer
}

public enum LayoutAxis : byte
{
    Column,
    Row
}

public enum LayoutDirection : byte
{
    Ltr,
    Rtl
}

public enum FlexDirection : byte
{
    Column,
    ColumnReverse,
    Row,
    RowReverse
}

public enum FlexWrap : byte
{
    NoWrap,
    Wrap
}

public enum PositionMode : byte
{
    Absolute,
    Relative,
    Static
}

public enum BoxSizingMode : byte
{
    BorderBox,
    ContentBox
}

public enum LayoutAvailableSpaceKind : byte
{
    Definite,
    MinContent,
    MaxContent
}

public enum LayoutRunMode : byte
{
    ComputeSize,
    PerformLayout,
    PerformHiddenLayout
}

public readonly struct LayoutAvailableSpace
{
    public LayoutAvailableSpace(LayoutAvailableSpaceKind Kind, float Value = 0)
    {
        this.Kind = Kind;
        this.Value = Value;
    }

    public LayoutAvailableSpaceKind Kind { get; }
    public float Value { get; }
    public bool IsDefinite => Kind == LayoutAvailableSpaceKind.Definite;

    public static LayoutAvailableSpace Definite(float value) => new(LayoutAvailableSpaceKind.Definite, value);
    public static LayoutAvailableSpace MinContent => new(LayoutAvailableSpaceKind.MinContent);
    public static LayoutAvailableSpace MaxContent => new(LayoutAvailableSpaceKind.MaxContent);

    public float Resolve(float fallback)
        => Kind == LayoutAvailableSpaceKind.Definite ? Math.Max(0, Value) : Math.Max(0, fallback);
}

public readonly struct LayoutSize
{
    public LayoutSize(float Width, float Height)
    {
        this.Width = Width;
        this.Height = Height;
    }

    public float Width { get; }
    public float Height { get; }
    public static LayoutSize Zero => new(0, 0);
}

public readonly struct LayoutKnownSize
{
    public LayoutKnownSize(float? Width, float? Height)
    {
        this.Width = Width;
        this.Height = Height;
    }

    public float? Width { get; }
    public float? Height { get; }
    public bool HasWidth => Width.HasValue;
    public bool HasHeight => Height.HasValue;
}

public readonly struct LayoutAvailableSize
{
    public LayoutAvailableSize(LayoutAvailableSpace Width, LayoutAvailableSpace Height)
    {
        this.Width = Width;
        this.Height = Height;
    }

    public LayoutAvailableSpace Width { get; }
    public LayoutAvailableSpace Height { get; }
}

public readonly struct LayoutRect
{
    public LayoutRect(float Left, float Top, float Width, float Height)
    {
        this.Left = Left;
        this.Top = Top;
        this.Width = Width;
        this.Height = Height;
    }

    public float Left { get; }
    public float Top { get; }
    public float Width { get; }
    public float Height { get; }
    public static LayoutRect Empty => new(0, 0, 0, 0);
}

public readonly struct LayoutInput : IEquatable<LayoutInput>
{
    public LayoutInput(
        LayoutKnownSize KnownDimensions,
        LayoutKnownSize ParentSize,
        LayoutAvailableSize AvailableSpace,
        LayoutRunMode RunMode)
    {
        this.KnownDimensions = KnownDimensions;
        this.ParentSize = ParentSize;
        this.AvailableSpace = AvailableSpace;
        this.RunMode = RunMode;
    }

    public LayoutKnownSize KnownDimensions { get; }
    public LayoutKnownSize ParentSize { get; }
    public LayoutAvailableSize AvailableSpace { get; }
    public LayoutRunMode RunMode { get; }
    public bool PerformsLayout => RunMode != LayoutRunMode.ComputeSize;

    public static LayoutInput Definite(float width, float height, LayoutRunMode runMode = LayoutRunMode.PerformLayout)
        => new(
            new LayoutKnownSize(width, height),
            new LayoutKnownSize(width, height),
            new LayoutAvailableSize(LayoutAvailableSpace.Definite(width), LayoutAvailableSpace.Definite(height)),
            runMode);

    public bool Equals(LayoutInput other)
        => Nullable.Equals(KnownDimensions.Width, other.KnownDimensions.Width)
            && Nullable.Equals(KnownDimensions.Height, other.KnownDimensions.Height)
            && Nullable.Equals(ParentSize.Width, other.ParentSize.Width)
            && Nullable.Equals(ParentSize.Height, other.ParentSize.Height)
            && AvailableSpace.Width.Kind == other.AvailableSpace.Width.Kind
            && AvailableSpace.Width.Value.Equals(other.AvailableSpace.Width.Value)
            && AvailableSpace.Height.Kind == other.AvailableSpace.Height.Kind
            && AvailableSpace.Height.Value.Equals(other.AvailableSpace.Height.Value)
            && RunMode == other.RunMode;

    public override bool Equals(object? obj) => obj is LayoutInput other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(KnownDimensions.Width);
        hash.Add(KnownDimensions.Height);
        hash.Add(ParentSize.Width);
        hash.Add(ParentSize.Height);
        hash.Add(AvailableSpace.Width.Kind);
        hash.Add(AvailableSpace.Width.Value);
        hash.Add(AvailableSpace.Height.Kind);
        hash.Add(AvailableSpace.Height.Value);
        hash.Add(RunMode);
        return hash.ToHashCode();
    }
}

public readonly struct LayoutOutput
{
    public LayoutOutput(LayoutSize Size, LayoutSize ContentSize, LayoutRect VisualOverflow)
    {
        this.Size = Size;
        this.ContentSize = ContentSize;
        this.VisualOverflow = VisualOverflow;
    }

    public LayoutSize Size { get; }
    public LayoutSize ContentSize { get; }
    public LayoutRect VisualOverflow { get; }
}

public readonly struct LayoutBoxEdges
{
    public LayoutBoxEdges(float Left, float Top, float Right, float Bottom)
    {
        this.Left = Left;
        this.Top = Top;
        this.Right = Right;
        this.Bottom = Bottom;
    }

    public float Left { get; }
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public static LayoutBoxEdges Zero => new(0, 0, 0, 0);

    public static LayoutBoxEdges ReplaceSidesWithReservedGutter(LayoutBoxEdges padding, LayoutBoxEdges reservedGutter)
        => new(
            reservedGutter.Left > 0 ? reservedGutter.Left : padding.Left,
            reservedGutter.Top > 0 ? reservedGutter.Top : padding.Top,
            reservedGutter.Right > 0 ? reservedGutter.Right : padding.Right,
            reservedGutter.Bottom > 0 ? reservedGutter.Bottom : padding.Bottom);
}

public readonly struct LayoutContainerStyle : IEquatable<LayoutContainerStyle>
{
    public LayoutContainerStyle(
        FlexDirection FlexDirection = FlexDirection.Row,
        LayoutDirection Direction = LayoutDirection.Ltr,
        FlexWrap FlexWrap = FlexWrap.NoWrap,
        float RowGap = 0,
        float ColumnGap = 0,
        CrossAlignment AlignItems = CrossAlignment.Stretch,
        MainAxisJustification JustifyContent = MainAxisJustification.Start,
        LayoutBoxEdges Padding = default)
    {
        this.FlexDirection = FlexDirection;
        this.Direction = Direction;
        this.FlexWrap = FlexWrap;
        this.RowGap = RowGap;
        this.ColumnGap = ColumnGap;
        this.AlignItems = AlignItems;
        this.JustifyContent = JustifyContent;
        this.Padding = Padding;
    }

    public FlexDirection FlexDirection { get; }
    public LayoutDirection Direction { get; }
    public FlexWrap FlexWrap { get; }
    public float RowGap { get; }
    public float ColumnGap { get; }
    public CrossAlignment AlignItems { get; }
    public MainAxisJustification JustifyContent { get; }
    public LayoutBoxEdges Padding { get; }

    public float ResolveContentWidth(float outerWidth)
        => Math.Max(0, outerWidth - Padding.Left - Padding.Right);

    public float ResolveContentHeight(float outerHeight)
        => Math.Max(0, outerHeight - Padding.Top - Padding.Bottom);

    public bool Equals(LayoutContainerStyle other)
        => FlexDirection == other.FlexDirection
            && Direction == other.Direction
            && FlexWrap == other.FlexWrap
            && RowGap.Equals(other.RowGap)
            && ColumnGap.Equals(other.ColumnGap)
            && AlignItems == other.AlignItems
            && JustifyContent == other.JustifyContent
            && Padding.Left.Equals(other.Padding.Left)
            && Padding.Top.Equals(other.Padding.Top)
            && Padding.Right.Equals(other.Padding.Right)
            && Padding.Bottom.Equals(other.Padding.Bottom);

    public override bool Equals(object? obj) => obj is LayoutContainerStyle other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FlexDirection);
        hash.Add(Direction);
        hash.Add(FlexWrap);
        hash.Add(RowGap);
        hash.Add(ColumnGap);
        hash.Add(AlignItems);
        hash.Add(JustifyContent);
        hash.Add(Padding.Left);
        hash.Add(Padding.Top);
        hash.Add(Padding.Right);
        hash.Add(Padding.Bottom);
        return hash.ToHashCode();
    }
}

public readonly record struct LayoutNodeId(int Value);

public readonly struct LayoutCacheKey : IEquatable<LayoutCacheKey>
{
    public LayoutCacheKey(LayoutNodeId NodeId, uint StyleVersion, uint LayoutVersion, LayoutInput Input, LayoutContainerStyle ContainerStyle, uint ContextVersion = 0)
    {
        this.NodeId = NodeId;
        this.StyleVersion = StyleVersion;
        this.LayoutVersion = LayoutVersion;
        this.Input = Input;
        this.ContainerStyle = ContainerStyle;
        this.ContextVersion = ContextVersion;
    }

    public LayoutNodeId NodeId { get; }
    public uint StyleVersion { get; }
    public uint LayoutVersion { get; }
    public LayoutInput Input { get; }
    public LayoutContainerStyle ContainerStyle { get; }
    public uint ContextVersion { get; }

    public bool Equals(LayoutCacheKey other)
        => NodeId.Equals(other.NodeId)
            && StyleVersion == other.StyleVersion
            && LayoutVersion == other.LayoutVersion
            && Input.Equals(other.Input)
            && ContainerStyle.Equals(other.ContainerStyle)
            && ContextVersion == other.ContextVersion;

    public override bool Equals(object? obj) => obj is LayoutCacheKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(NodeId, StyleVersion, LayoutVersion, Input, ContainerStyle, ContextVersion);
}

public interface ILayoutCache
{
    bool TryGet(in LayoutCacheKey key, out LayoutOutput output);
    void Store(in LayoutCacheKey key, in LayoutOutput output);
    void InvalidateNode(LayoutNodeId nodeId);
    void Clear();
}

public sealed class LayoutOutputCache : ILayoutCache
{
    private const int MaxEntriesPerNode = 24;
    private readonly Dictionary<LayoutCacheKey, LayoutOutput> entries = [];
    private readonly Dictionary<LayoutNodeId, List<LayoutCacheKey>> entriesByNode = [];

    public bool TryGet(in LayoutCacheKey key, out LayoutOutput output)
        => entries.TryGetValue(key, out output);

    public void Store(in LayoutCacheKey key, in LayoutOutput output)
    {
        if (entries.ContainsKey(key))
        {
            entries[key] = output;
            return;
        }

        entries[key] = output;
        if (!entriesByNode.TryGetValue(key.NodeId, out var nodeEntries))
        {
            nodeEntries = [];
            entriesByNode[key.NodeId] = nodeEntries;
        }

        if (nodeEntries.Count >= MaxEntriesPerNode)
        {
            var evicted = nodeEntries[0];
            nodeEntries.RemoveAt(0);
            entries.Remove(evicted);
        }

        nodeEntries.Add(key);
    }

    public void InvalidateNode(LayoutNodeId nodeId)
    {
        if (!entriesByNode.Remove(nodeId, out var nodeEntries))
            return;

        foreach (var key in nodeEntries)
            entries.Remove(key);
    }

    public void InvalidateNodes(IReadOnlySet<LayoutNodeId> nodeIds)
    {
        foreach (var nodeId in nodeIds)
            InvalidateNode(nodeId);
    }

    public void Clear()
    {
        entries.Clear();
        entriesByNode.Clear();
    }
}

public enum CrossAlignment : byte
{
    Auto,
    Start,
    Center,
    End,
    Stretch
}

public enum MainAxisJustification : byte
{
    Start,
    Center,
    End,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

[Flags]
public enum LayoutAutoMarginFlags : byte
{
    None = 0,
    Left = 1 << 0,
    Top = 1 << 1,
    Right = 1 << 2,
    Bottom = 1 << 3
}

[Flags]
public enum LayoutValueUnitFlags : ushort
{
    None = 0,
    LeftPercent = 1 << 0,
    TopPercent = 1 << 1,
    RightPercent = 1 << 2,
    BottomPercent = 1 << 3,
    WidthPercent = 1 << 4,
    HeightPercent = 1 << 5,
    MinWidthPercent = 1 << 6,
    MaxWidthPercent = 1 << 7,
    MinHeightPercent = 1 << 8,
    MaxHeightPercent = 1 << 9,
    FlexBasisPercent = 1 << 10
}

public static class LayoutValue
{
    public const float Unset = float.NaN;

    public static bool IsSet(float value) => !float.IsNaN(value);

    public static float Resolve(float value, bool isPercent, float relativeTo)
    {
        if (!IsSet(value))
            return Unset;

        return isPercent
            ? relativeTo * (value * 0.01f)
            : value;
    }
}

public static class LayoutBoxModel
{
    public static float ResolveOuterSize(
        float value,
        bool isPercent,
        float availableSize,
        float contentInset,
        BoxSizingMode boxSizing)
    {
        if (!LayoutValue.IsSet(value))
            return value;

        var resolved = LayoutValue.Resolve(value, isPercent, availableSize);
        if (!LayoutValue.IsSet(resolved))
            return resolved;

        if (boxSizing == BoxSizingMode.ContentBox)
            return Math.Max(0, resolved) + contentInset;

        return Math.Max(Math.Max(0, resolved), contentInset);
    }

    public static float ClampOuterSize(
        float value,
        float minValue,
        bool minIsPercent,
        float maxValue,
        bool maxIsPercent,
        float availableSize,
        float contentInset,
        BoxSizingMode boxSizing)
    {
        var result = float.IsFinite(value) ? Math.Max(value, contentInset) : contentInset;
        var resolvedMin = ResolveOuterSize(minValue, minIsPercent, availableSize, contentInset, boxSizing);
        var resolvedMax = ResolveOuterSize(maxValue, maxIsPercent, availableSize, contentInset, boxSizing);
        if (LayoutValue.IsSet(resolvedMin))
            result = Math.Max(result, Math.Max(0, resolvedMin));

        if (LayoutValue.IsSet(resolvedMax))
            result = Math.Min(result, Math.Max(LayoutValue.IsSet(resolvedMin) ? resolvedMin : contentInset, resolvedMax));

        return Math.Max(0, result);
    }
}

public static class FlexLayout
{
    public static LayoutAxis ResolveAxis(FlexDirection direction)
    {
        return direction is FlexDirection.Row or FlexDirection.RowReverse
            ? LayoutAxis.Row
            : LayoutAxis.Column;
    }

    public static bool IsMainAxisReversed(FlexDirection direction, LayoutDirection layoutDirection)
    {
        return direction switch
        {
            FlexDirection.Row => layoutDirection == LayoutDirection.Rtl,
            FlexDirection.RowReverse => layoutDirection != LayoutDirection.Rtl,
            FlexDirection.Column => false,
            FlexDirection.ColumnReverse => true,
            _ => false
        };
    }

    public static bool IsCrossAxisReversed(FlexDirection direction, LayoutDirection layoutDirection)
    {
        return ResolveAxis(direction) == LayoutAxis.Column && layoutDirection == LayoutDirection.Rtl;
    }
}

public readonly struct LayoutChildRequest
{
    public LayoutChildRequest(
        LayoutChildKind Kind,
        float Left = float.NaN,
        float Top = float.NaN,
        float Right = float.NaN,
        float Bottom = float.NaN,
        float Width = float.NaN,
        float Height = float.NaN,
        float MinWidth = float.NaN,
        float MaxWidth = float.NaN,
        float MinHeight = float.NaN,
        float MaxHeight = float.NaN,
        float MarginLeft = 0,
        float MarginTop = 0,
        float MarginRight = 0,
        float MarginBottom = 0,
        string? Text = null,
        float FontSize = 18,
        string? FontFamily = null,
        int FontWeight = 400,
        bool Wrap = false,
        CrossAlignment AlignSelf = CrossAlignment.Auto,
        float Length = 0,
        float Thickness = 1,
        bool Vertical = true,
        float Size = 0,
        float FlexGrow = 0,
        float FlexShrink = 0,
        float FlexBasis = float.NaN,
        LayoutValueUnitFlags Units = LayoutValueUnitFlags.None,
        bool Italic = false,
        SceneFont? Font = null,
        LayoutAutoMarginFlags AutoMargins = LayoutAutoMarginFlags.None,
        BoxSizingMode BoxSizing = BoxSizingMode.BorderBox,
        float PaddingLeft = 0,
        float PaddingTop = 0,
        float PaddingRight = 0,
        float PaddingBottom = 0,
        float BorderLeft = 0,
        float BorderTop = 0,
        float BorderRight = 0,
        float BorderBottom = 0)
    {
        this.Kind = Kind;
        this.Left = Left;
        this.Top = Top;
        this.Right = Right;
        this.Bottom = Bottom;
        this.Width = Width;
        this.Height = Height;
        this.MinWidth = MinWidth;
        this.MaxWidth = MaxWidth;
        this.MinHeight = MinHeight;
        this.MaxHeight = MaxHeight;
        this.MarginLeft = MarginLeft;
        this.MarginTop = MarginTop;
        this.MarginRight = MarginRight;
        this.MarginBottom = MarginBottom;
        this.Text = Text;
        this.Font = Font ?? new SceneFont(FontSize, FontFamily, FontWeight, Italic);
        this.FontSize = this.Font.Size;
        this.FontFamily = this.Font.Family;
        this.FontWeight = this.Font.Weight;
        this.Italic = this.Font.Italic;
        this.Wrap = Wrap;
        this.AlignSelf = AlignSelf;
        this.Length = Length;
        this.Thickness = Thickness;
        this.Vertical = Vertical;
        this.Size = Size;
        this.FlexGrow = FlexGrow;
        this.FlexShrink = FlexShrink;
        this.FlexBasis = FlexBasis;
        this.Units = Units;
        this.AutoMargins = AutoMargins;
        this.BoxSizing = BoxSizing;
        this.Padding = new LayoutBoxEdges(PaddingLeft, PaddingTop, PaddingRight, PaddingBottom);
        this.Border = new LayoutBoxEdges(BorderLeft, BorderTop, BorderRight, BorderBottom);
    }

    public LayoutChildKind Kind { get; }
    public float Left { get; }
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }
    public float Width { get; }
    public float Height { get; }
    public float MinWidth { get; }
    public float MaxWidth { get; }
    public float MinHeight { get; }
    public float MaxHeight { get; }
    public float MarginLeft { get; }
    public float MarginTop { get; }
    public float MarginRight { get; }
    public float MarginBottom { get; }
    public string? Text { get; }
    public float FontSize { get; }
    public string? FontFamily { get; }
    public int FontWeight { get; }
    public bool Italic { get; }
    public SceneFont Font { get; }
    public bool Wrap { get; }
    public CrossAlignment AlignSelf { get; }
    public float Length { get; }
    public float Thickness { get; }
    public bool Vertical { get; }
    public float Size { get; }
    public float FlexGrow { get; }
    public float FlexShrink { get; }
    public float FlexBasis { get; }
    public LayoutValueUnitFlags Units { get; }
    public LayoutAutoMarginFlags AutoMargins { get; }
    public BoxSizingMode BoxSizing { get; }
    public LayoutBoxEdges Padding { get; }
    public LayoutBoxEdges Border { get; }
    public float ContentInsetLeft => Padding.Left + Border.Left;
    public float ContentInsetTop => Padding.Top + Border.Top;
    public float ContentInsetRight => Padding.Right + Border.Right;
    public float ContentInsetBottom => Padding.Bottom + Border.Bottom;
    public float HorizontalContentInset => ContentInsetLeft + ContentInsetRight;
    public float VerticalContentInset => ContentInsetTop + ContentInsetBottom;

    public bool HasLeft => LayoutValue.IsSet(Left);
    public bool HasTop => LayoutValue.IsSet(Top);
    public bool HasRight => LayoutValue.IsSet(Right);
    public bool HasBottom => LayoutValue.IsSet(Bottom);
    public bool HasWidth => LayoutValue.IsSet(Width);
    public bool HasHeight => LayoutValue.IsSet(Height);
    public bool HasMinWidth => LayoutValue.IsSet(MinWidth);
    public bool HasMaxWidth => LayoutValue.IsSet(MaxWidth);
    public bool HasMinHeight => LayoutValue.IsSet(MinHeight);
    public bool HasMaxHeight => LayoutValue.IsSet(MaxHeight);
    public bool IsLeftPercent => (Units & LayoutValueUnitFlags.LeftPercent) != 0;
    public bool IsTopPercent => (Units & LayoutValueUnitFlags.TopPercent) != 0;
    public bool IsRightPercent => (Units & LayoutValueUnitFlags.RightPercent) != 0;
    public bool IsBottomPercent => (Units & LayoutValueUnitFlags.BottomPercent) != 0;
    public bool IsWidthPercent => (Units & LayoutValueUnitFlags.WidthPercent) != 0;
    public bool IsHeightPercent => (Units & LayoutValueUnitFlags.HeightPercent) != 0;
    public bool IsMinWidthPercent => (Units & LayoutValueUnitFlags.MinWidthPercent) != 0;
    public bool IsMaxWidthPercent => (Units & LayoutValueUnitFlags.MaxWidthPercent) != 0;
    public bool IsMinHeightPercent => (Units & LayoutValueUnitFlags.MinHeightPercent) != 0;
    public bool IsMaxHeightPercent => (Units & LayoutValueUnitFlags.MaxHeightPercent) != 0;
    public bool HasFlexBasis => LayoutValue.IsSet(FlexBasis);
    public bool IsFlexBasisPercent => (Units & LayoutValueUnitFlags.FlexBasisPercent) != 0;
    public bool IsMarginLeftAuto => (AutoMargins & LayoutAutoMarginFlags.Left) != 0;
    public bool IsMarginTopAuto => (AutoMargins & LayoutAutoMarginFlags.Top) != 0;
    public bool IsMarginRightAuto => (AutoMargins & LayoutAutoMarginFlags.Right) != 0;
    public bool IsMarginBottomAuto => (AutoMargins & LayoutAutoMarginFlags.Bottom) != 0;

    public static LayoutChildRequest Invalid => new(LayoutChildKind.Invalid);
}

public readonly struct LayoutMeasurement
{
    public LayoutMeasurement(float MainSize, float CrossSize)
    {
        this.MainSize = MainSize;
        this.CrossSize = CrossSize;
    }

    public float MainSize { get; }
    public float CrossSize { get; }
}

public readonly struct LayoutFrameData
{
    public LayoutFrameData(float Left, float Top, float Width, float Height)
    {
        this.Left = Left;
        this.Top = Top;
        this.Width = Width;
        this.Height = Height;
    }

    public float Left { get; }
    public float Top { get; }
    public float Width { get; }
    public float Height { get; }
}
