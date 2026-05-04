namespace Enaga.Scene;

public enum SceneNodeKind
{
    View,
    Text,
    ScrollView,
    TextInput,
    Image
}

public enum SceneTextAlign
{
    Left,
    Center,
    Right
}

public enum SceneBoxSizing
{
    ContentBox,
    BorderBox
}

public enum SceneBorderStyle
{
    None,
    Solid,
    Dotted
}

public enum SceneControlKind
{
    None,
    TextInput,
    TextArea,
    Button,
    Select
}

public sealed record SceneViewport(int Width, int Height);

public readonly record struct SceneGraphNode(
    SceneNodeKind NodeKind,
    SceneNodeId? ParentId,
    SceneNodeId[] Children,
    string? Label = null);

public sealed record SceneFont(
    float Size,
    string? Family = null,
    int Weight = 400,
    bool Italic = false,
    string? Source = null,
    string? Identity = null)
{
    public string CacheIdentity => Identity ?? Source ?? Family ?? string.Empty;
}

public sealed record SceneTextStyle
{
    public SceneTextStyle(
        float FontSize,
        string? Color = null,
        string? FontFamily = null,
        int FontWeight = 400,
        SceneTextAlign TextAlign = SceneTextAlign.Left,
        bool WrapText = false,
        bool Underline = false,
        bool Italic = false,
        bool TextOverflowEllipsis = false,
        SceneFont? Font = null,
        SceneBoxShadow[]? TextShadows = null)
    {
        this.Font = Font ?? new SceneFont(FontSize, FontFamily, FontWeight, Italic);
        this.Color = Color;
        this.TextAlign = TextAlign;
        this.WrapText = WrapText;
        this.Underline = Underline;
        this.TextOverflowEllipsis = TextOverflowEllipsis;
        this.TextShadows = TextShadows;
    }

    public SceneFont Font { get; init; }
    public float FontSize => Font.Size;
    public string? Color { get; init; }
    public string? FontFamily => Font.Family;
    public int FontWeight => Font.Weight;
    public SceneTextAlign TextAlign { get; init; }
    public bool WrapText { get; init; }
    public bool Underline { get; init; }
    public bool Italic => Font.Italic;
    public bool TextOverflowEllipsis { get; init; }
    public SceneBoxShadow[]? TextShadows { get; init; }
}

public readonly record struct SceneBoxGeometry(
    float AbsLeft,
    float AbsTop,
    float Width,
    float Height,
    float PaddingLeft,
    float PaddingTop,
    float PaddingRight,
    float PaddingBottom,
    bool IsPositioned);

public readonly record struct SceneBoxPaint(
    string? BackgroundColor,
    string? BorderColor,
    float BorderWidth,
    float BorderRadius,
    SceneBoxSizing BoxSizing,
    SceneBorderStyle BorderStyle,
    SceneGradient? BackgroundGradient,
    SceneRuntimeShader? BackgroundShader,
    SceneBoxShadow[]? BackgroundShadows,
    string? BackgroundImageSource,
    string? BackgroundImageFit,
    SceneBoxBorder? Border);

public readonly record struct ScenePaintOverride(
    string? BackgroundColor = null,
    string? BorderColor = null,
    string? TextColor = null)
{
    public bool IsEmpty =>
        BackgroundColor is null &&
        BorderColor is null &&
        TextColor is null;
}

public readonly record struct SceneTextPayload(
    string TextContent,
    SceneTextStyle? TextStyle,
    float LineHeight);

public readonly record struct SceneTextInputPayload(
    string TextContent,
    SceneTextStyle? TextStyle,
    string PlaceholderText,
    string? PlaceholderColor,
    bool Multiline,
    float LineHeight,
    int CaretIndex,
    int SelectionStart,
    int SelectionEnd,
    bool IsFocused,
    string CompositionText,
    int CompositionStart,
    int CompositionCursorOffset,
    int CompositionSelectionStart,
    int CompositionSelectionLength,
    string? CompositionUnderlineColor,
    string? CompositionSelectionUnderlineColor,
    bool ImeOpen,
    string? ImeIndicator);

public readonly record struct SceneScrollPayload(
    float ScrollX,
    float ScrollY,
    bool IsScrollContainer,
    bool ClipContent,
    float ContentWidth,
    float ContentHeight,
    bool HorizontalScrollEnabled,
    float ScrollBarWidth,
    string? ScrollBarTrackColor,
    string? ScrollBarThumbColor);

public readonly record struct SceneImagePayload(
    string ImageSource,
    string? ImagePlaceholderSource,
    string? ImageFit);

public readonly record struct SceneInteractionPayload(
    string? LinkHref,
    SceneControlKind ControlKind);

public sealed record SceneLayoutBox(
    SceneNodeKind NodeKind,
    float AbsLeft,
    float AbsTop,
    float Width,
    float Height,
    string? BackgroundColor = null,
    string? BorderColor = null,
    float BorderWidth = 0,
    float BorderRadius = 0,
    SceneBoxSizing BoxSizing = SceneBoxSizing.ContentBox,
    string? TextContent = null,
    SceneTextStyle? TextStyle = null,
    string? PlaceholderText = null,
    string? PlaceholderColor = null,
    float PaddingLeft = 0,
    float PaddingTop = 0,
    float PaddingRight = 0,
    float PaddingBottom = 0,
    bool Multiline = false,
    float LineHeight = 0,
    int CaretIndex = 0,
    int SelectionStart = 0,
    int SelectionEnd = 0,
    bool IsFocused = false,
    float ScrollY = 0,
    bool IsScrollContainer = false,
    bool ClipContent = false,
    float ContentHeight = 0,
    string? ImageSource = null,
    string? ImagePlaceholderSource = null,
    string? ImageFit = null,
    SceneGradient? BackgroundGradient = null,
    SceneRuntimeShader? BackgroundShader = null,
    SceneBoxShadow[]? BackgroundShadows = null,
    string? CompositionText = null,
    int CompositionStart = 0,
    int CompositionCursorOffset = 0,
    int CompositionSelectionStart = 0,
    int CompositionSelectionLength = 0,
    string? CompositionUnderlineColor = null,
    string? CompositionSelectionUnderlineColor = null,
    bool ImeOpen = false,
    string? ImeIndicator = null,
    float ScrollX = 0,
    float ContentWidth = 0,
    bool HorizontalScrollEnabled = false,
    SceneBorderStyle BorderStyle = SceneBorderStyle.Solid,
    string? LinkHref = null,
    string? BackgroundImageSource = null,
    string? BackgroundImageFit = null,
    SceneBoxBorder? Border = null,
    float ScrollBarWidth = 12,
    string? ScrollBarTrackColor = null,
    string? ScrollBarThumbColor = null,
    bool IsPositioned = false,
    SceneControlKind ControlKind = SceneControlKind.None)
{
    public SceneBoxGeometry Geometry => new(
        AbsLeft,
        AbsTop,
        Width,
        Height,
        PaddingLeft,
        PaddingTop,
        PaddingRight,
        PaddingBottom,
        IsPositioned);

    public SceneBoxPaint Paint => new(
        BackgroundColor,
        BorderColor,
        BorderWidth,
        BorderRadius,
        BoxSizing,
        BorderStyle,
        BackgroundGradient,
        BackgroundShader,
        BackgroundShadows,
        BackgroundImageSource,
        BackgroundImageFit,
        Border);

    public SceneTextPayload? Text => NodeKind == SceneNodeKind.Text && TextContent is { } textContent
        ? new SceneTextPayload(textContent, TextStyle, LineHeight)
        : null;

    public SceneTextInputPayload? TextInput => NodeKind == SceneNodeKind.TextInput
        ? new SceneTextInputPayload(
            TextContent ?? string.Empty,
            TextStyle,
            PlaceholderText ?? string.Empty,
            PlaceholderColor,
            Multiline,
            LineHeight,
            CaretIndex,
            SelectionStart,
            SelectionEnd,
            IsFocused,
            CompositionText ?? string.Empty,
            CompositionStart,
            CompositionCursorOffset,
            CompositionSelectionStart,
            CompositionSelectionLength,
            CompositionUnderlineColor,
            CompositionSelectionUnderlineColor,
            ImeOpen,
            ImeIndicator)
        : null;

    public SceneScrollPayload? Scroll => NodeKind == SceneNodeKind.ScrollView || IsScrollContainer || ClipContent || ContentWidth > 0 || ContentHeight > 0
        ? new SceneScrollPayload(
            ScrollX,
            ScrollY,
            IsScrollContainer,
            ClipContent,
            ContentWidth,
            ContentHeight,
            HorizontalScrollEnabled,
            ScrollBarWidth,
            ScrollBarTrackColor,
            ScrollBarThumbColor)
        : null;

    public SceneImagePayload? Image => NodeKind == SceneNodeKind.Image && ImageSource is { } imageSource
        ? new SceneImagePayload(imageSource, ImagePlaceholderSource, ImageFit)
        : null;

    public SceneInteractionPayload Interaction => new(LinkHref, ControlKind);
}

public sealed record SceneBoxBorder(
    float LeftWidth,
    float TopWidth,
    float RightWidth,
    float BottomWidth,
    SceneBorderStyle LeftStyle,
    SceneBorderStyle TopStyle,
    SceneBorderStyle RightStyle,
    SceneBorderStyle BottomStyle,
    string? LeftColor,
    string? TopColor,
    string? RightColor,
    string? BottomColor);

public sealed record SceneLayoutCommit(
    SceneNodeId RootId,
    SceneViewport Viewport,
    SceneNodeMap<SceneGraphNode> Nodes,
    SceneNodeMap<SceneLayoutBox> Layout,
    SceneNodeId[] HostAnimatedShaderRootIds)
{
    public SceneLayoutCommit(
        SceneNodeId rootId,
        SceneViewport viewport,
        IReadOnlyDictionary<SceneNodeId, SceneGraphNode> nodes,
        IReadOnlyDictionary<SceneNodeId, SceneLayoutBox> layout,
        SceneNodeId[] hostAnimatedShaderRootIds)
        : this(
            rootId,
            viewport,
            new SceneNodeMap<SceneGraphNode>(nodes),
            new SceneNodeMap<SceneLayoutBox>(layout),
            hostAnimatedShaderRootIds)
    {
    }

    public SceneNodeId[] PaintOrderIds { get; init; } = [];

    private static readonly IReadOnlyDictionary<SceneNodeId, ScenePaintOverride> EmptyPaintOverrides =
        new Dictionary<SceneNodeId, ScenePaintOverride>();

    public IReadOnlyDictionary<SceneNodeId, ScenePaintOverride> PaintOverrides { get; init; } =
        EmptyPaintOverrides;

    public bool TryGetPaintOverride(SceneNodeId id, out ScenePaintOverride paintOverride)
        => PaintOverrides.TryGetValue(id, out paintOverride) && !paintOverride.IsEmpty;

    public SceneNodeId[] DynamicOverlayRootIds { get; init; } = [];
}
