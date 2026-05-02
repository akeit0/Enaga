using Enaga.Scene;

namespace Enaga.React.OkojoRuntime;

internal sealed class NativeTextInputState(string id)
{
    public string Id { get; } = id;

    public string ParentId { get; set; } = "root";

    public float Left { get; set; }

    public float Top { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    public string Text { get; set; } = string.Empty;

    public string PlaceholderText { get; set; } = string.Empty;

    public float FontSize { get; set; } = 16;

    public string? Color { get; set; } = "#f8fafc";

    public int FontWeight { get; set; } = 400;

    public string? FontFamily { get; set; }

    public SceneTextAlign TextAlign { get; set; } = SceneTextAlign.Left;

    public float PaddingLeft { get; set; } = 12;

    public float PaddingTop { get; set; } = 10;

    public float PaddingRight { get; set; } = 12;

    public float PaddingBottom { get; set; } = 10;

    public bool Multiline { get; set; }

    public float LineHeight { get; set; } = 22;

    public string? BackgroundColor { get; set; } = "#0b1220";

    public SceneGradient? BackgroundGradient { get; set; }

    public SceneRuntimeShader? BackgroundShader { get; set; }

    public SceneBoxShadow[]? BackgroundShadows { get; set; }

    public string? BorderColor { get; set; } = "#334155";

    public string? ActiveBorderColor { get; set; } = "#60a5fa";

    public string? PlaceholderColor { get; set; } = "#475569";

    public float BorderRadius { get; set; } = 12;

    public SceneBoxSizing BoxSizing { get; set; } = SceneBoxSizing.ContentBox;

    public int CaretIndex { get; set; }

    public int SelectionStart { get; set; }

    public int SelectionEnd { get; set; }

    public int SelectionAnchorIndex { get; set; }

    public bool IsSelectingWithMouse { get; set; }

    public bool IsFocused { get; set; }

    public float? PreferredCaretX { get; set; }

    public string CompositionText { get; set; } = string.Empty;

    public int CompositionStartIndex { get; set; }

    public int CompositionCursorOffset { get; set; }

    public int CompositionSelectionStart { get; set; }

    public int CompositionSelectionLength { get; set; }

    public string? CompositionUnderlineColor { get; set; }

    public string? CompositionSelectionUnderlineColor { get; set; }

    public bool IsTextCompositionActive { get; set; }

    public bool PendingCompositionCommit { get; set; }

    public bool CompositionReplacedSelection { get; set; }

    public string? CompositionRestoreText { get; set; }

    public string? PendingHostText { get; set; }

    public string LastKnownExternalText { get; set; } = string.Empty;

    public bool ImeOpen { get; set; }

    public string ImeIndicator { get; set; } = string.Empty;

    public int ZOrder { get; set; }

    public int Generation { get; set; }
}
