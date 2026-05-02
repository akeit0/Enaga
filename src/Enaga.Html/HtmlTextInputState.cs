using Enaga.Scene;

namespace Enaga.Html;

public sealed class HtmlTextInputState(string id)
{
    public string Id { get; } = id;

    public float Left { get; set; }

    public float Top { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    public string Text { get; set; } = string.Empty;

    public string PlaceholderText { get; set; } = string.Empty;

    public float FontSize { get; set; } = 16;

    public string? Color { get; set; }

    public int FontWeight { get; set; } = 400;

    public string? FontFamily { get; set; }

    public SceneTextAlign TextAlign { get; set; } = SceneTextAlign.Left;

    public float PaddingLeft { get; set; }

    public float PaddingTop { get; set; }

    public float PaddingRight { get; set; }

    public float PaddingBottom { get; set; }

    public bool Multiline { get; set; }

    public float LineHeight { get; set; } = 20;

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

    public bool IsTextCompositionActive { get; set; }

    public bool PendingCompositionCommit { get; set; }

    public bool CompositionReplacedSelection { get; set; }

    public string? CompositionRestoreText { get; set; }

    public string? PendingHostText { get; set; }

    public string LastKnownExternalText { get; set; } = string.Empty;

    public bool ImeOpen { get; set; }

    public string ImeIndicator { get; set; } = string.Empty;
}
