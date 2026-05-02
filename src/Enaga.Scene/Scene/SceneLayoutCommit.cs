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

public sealed record SceneGraphNode(
    SceneNodeKind NodeKind,
    string? ParentId,
    IReadOnlyList<string> Children,
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
    SceneControlKind ControlKind = SceneControlKind.None);

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
    string RootId,
    SceneViewport Viewport,
    IReadOnlyDictionary<string, SceneGraphNode> Nodes,
    IReadOnlyDictionary<string, SceneLayoutBox> Layout,
    string[] HostAnimatedShaderRootIds)
{
    public string[] PaintOrderIds { get; init; } = [];
}
