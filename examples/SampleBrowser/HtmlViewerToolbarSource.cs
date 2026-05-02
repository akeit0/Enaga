using Enaga.Html;
using Enaga.Input;
using Enaga.Rendering;
using Enaga.Scene;
using System.Drawing;

namespace SampleBrowser;

internal sealed class SampleBrowserToolbarSource : ISceneFrameSource, IInputSink, IPointerCursorSource, ITextCompositionRangeSink, IRenderWakeSource
{
    public const int Height = 30;
    private const string RootId = "toolbar-root";
    private const string BackId = "toolbar-back";
    private const string BackIconId = "toolbar-back-icon";
    private const string ForwardId = "toolbar-forward";
    private const string ForwardIconId = "toolbar-forward-icon";
    private const string RefreshId = "toolbar-refresh";
    private const string RefreshIconId = "toolbar-refresh-icon";
    private const string UrlInputId = "toolbar-url";
    private const int LeftMouseButtonMask = 1;
    private const double DoubleClickThresholdMs = 150;
    private const float DoubleClickThresholdPx = 8;
    private static readonly SceneTextStyle UrlInputTextStyle = new(13, "#111827", FontFamily: "Arial");

    private readonly object sync = new();
    private readonly IRuntimeTextServices textServices;
    private readonly HtmlTextInputController textInputController;
    private readonly TimeProvider timeProvider;
    private readonly HtmlTextInputState urlInputState = new(UrlInputId);
    private bool canGoBack;
    private bool canGoForward;
    private bool pointerOverInput;
    private bool pointerOverButton;
    private float lastPointerX;
    private float lastPointerY;
    private string? lastPrimaryClickInputId;
    private float lastPrimaryClickX;
    private float lastPrimaryClickY;
    private long lastPrimaryClickTimestamp;
    private SceneLayoutCommit? cachedCommit;
    private int cachedWidth = -1;
    private bool dirty = true;

    public SampleBrowserToolbarSource(string documentSource, IRuntimeTextServices textServices, TimeProvider? timeProvider = null)
    {
        this.textServices = textServices ?? throw new ArgumentNullException(nameof(textServices));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        textInputController = new HtmlTextInputController(this.textServices, Invalidate, MoveFocus, SetFocus);
        urlInputState.Text = documentSource;
        urlInputState.CaretIndex = documentSource.Length;
        urlInputState.LastKnownExternalText = documentSource;
        textInputController.ClearSelection(urlInputState);
    }

    public string? LastError => null;

    public PointerCursorKind CurrentCursor
        => pointerOverInput ? PointerCursorKind.Text :
            pointerOverButton ? PointerCursorKind.Pointer :
            PointerCursorKind.Default;

    public event Action? BackRequested;
    public event Action? ForwardRequested;
    public event Action? RefreshRequested;
    public event Action<string>? UrlSubmitted;
    public event Action? RenderWakeRequested;

    public void SetState(string documentSource, bool canGoBack, bool canGoForward, string? message = null)
    {
        lock (sync)
        {
            HtmlTextInputStateLogic.ApplyExternalValue(urlInputState, textServices, documentSource);
            this.canGoBack = canGoBack;
            this.canGoForward = canGoForward;
            Invalidate();
        }
    }

    public void ClearUrlFocus()
    {
        lock (sync)
        {
            if (!urlInputState.IsFocused && !urlInputState.IsSelectingWithMouse && !HtmlTextInputStateLogic.HasSelection(urlInputState))
                return;

            SetFocused(false);
        }
    }

    public SceneFrameResult RenderFrame(int width, int height, TimeSpan elapsed)
    {
        lock (sync)
        {
            var resolvedWidth = Math.Max(1, width);
            if (!dirty && cachedCommit is not null && cachedWidth == resolvedWidth)
                return SceneFrameResult.NoDamage(cachedCommit);

            cachedWidth = resolvedWidth;
            cachedCommit = BuildCommit(resolvedWidth);
            dirty = false;
            return SceneFrameResult.FullFrame(cachedCommit, resolvedWidth, Height, SceneDamageReason.RuntimeReload);
        }
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic)
    {
        lock (sync)
        {
            lastPointerX = x;
            lastPointerY = y;
            var oldInput = pointerOverInput;
            var oldButton = pointerOverButton;
            pointerOverInput = HitTestInput(x, y);
            pointerOverButton = HitTestEnabledButton(x, y);

            if ((buttons & LeftMouseButtonMask) != 0 && urlInputState.IsSelectingWithMouse)
            {
                textInputController.SetSelection(urlInputState, urlInputState.SelectionAnchorIndex, HitTestCaretIndex(x, y));
                Invalidate();
                return;
            }

            if (oldInput != pointerOverInput || oldButton != pointerOverButton)
                Invalidate();
        }
    }

    public void PointerDown(int button, int buttons, bool synthetic)
    {
        if (button != 0)
            return;

        Action? action = null;
        lock (sync)
        {
            if (HitTestInput(lastPointerX, lastPointerY))
            {
                SetFocused(true);
                var caretIndex = HitTestCaretIndex(lastPointerX, lastPointerY);
                if (IsDoubleClick(lastPointerX, lastPointerY))
                {
                    urlInputState.IsSelectingWithMouse = false;
                    textInputController.SelectWordAt(urlInputState, caretIndex);
                }
                else
                {
                    urlInputState.IsSelectingWithMouse = true;
                    textInputController.SetSelection(urlInputState, caretIndex, caretIndex);
                }

                RememberPrimaryClick(lastPointerX, lastPointerY);
                Invalidate();
                return;
            }

            SetFocused(false);
            var toolbarLayout = ResolveToolbarLayout(cachedWidth > 0 ? cachedWidth : 980);
            if (canGoBack && Contains(toolbarLayout.BackLeft, 3, 24, 24, lastPointerX, lastPointerY))
                action = BackRequested;
            else if (canGoForward && Contains(toolbarLayout.ForwardLeft, 3, 24, 24, lastPointerX, lastPointerY))
                action = ForwardRequested;
            else if (Contains(toolbarLayout.RefreshLeft, 3, 24, 24, lastPointerX, lastPointerY))
                action = RefreshRequested;

            Invalidate();
        }

        action?.Invoke();
    }

    public void PointerUp(int button, int buttons, bool synthetic)
    {
        if (button != 0)
            return;

        lock (sync)
        {
            if (!urlInputState.IsSelectingWithMouse)
                return;

            textInputController.SetSelection(urlInputState, urlInputState.SelectionAnchorIndex, HitTestCaretIndex(lastPointerX, lastPointerY));
            urlInputState.IsSelectingWithMouse = false;
            Invalidate();
        }
    }

    public void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0)
    {
    }

    public void KeyDown(string key, int modifiers, bool repeat, bool synthetic)
    {
        Action<string>? submit = null;
        string? submitted = null;
        lock (sync)
        {
            if (!urlInputState.IsFocused)
                return;

            if (key is "Enter" or "KeypadEnter" or "NumpadEnter")
            {
                submit = UrlSubmitted;
                submitted = urlInputState.Text;
            }
            else
            {
                textInputController.HandleKey(urlInputState, key, modifiers);
            }
        }

        if (submit is not null && submitted is not null)
            submit(submitted);
    }

    public void KeyUp(string key, int modifiers, bool synthetic)
    {
    }

    public void TextInput(string text, bool synthetic)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (sync)
        {
            if (!urlInputState.IsFocused)
                return;

            var normalizedText = RemoveLineBreaks(text);
            if (normalizedText.Length == 0)
                return;

            textInputController.ApplyTextInput(urlInputState, normalizedText);
        }
    }

    public void StartTextComposition()
    {
        StartTextComposition(startIndex: null);
    }

    public void StartTextComposition(int startIndex)
    {
        StartTextComposition((int?)startIndex);
    }

    private void StartTextComposition(int? startIndex)
    {
        lock (sync)
        {
            if (!urlInputState.IsFocused)
                return;

            HtmlTextInputStateLogic.StartComposition(urlInputState, startIndex);
            Invalidate();
        }
    }

    public void UpdateTextComposition(string text, int cursorPosition)
    {
        UpdateTextComposition(text, cursorPosition, 0, text?.Length ?? 0);
    }

    public void UpdateTextComposition(string text, int cursorPosition, int selectionStart, int selectionLength)
    {
        lock (sync)
        {
            if (!urlInputState.IsFocused)
                return;

            HtmlTextInputStateLogic.UpdateComposition(urlInputState, text, cursorPosition, selectionStart, selectionLength);
            Invalidate();
        }
    }

    public void EndTextComposition()
    {
        lock (sync)
        {
            if (!urlInputState.IsFocused)
                return;

            HtmlTextInputStateLogic.EndComposition(urlInputState, textServices);
            Invalidate();
        }
    }

    public void PrepareTextCompositionCommit()
    {
        lock (sync)
        {
            if (urlInputState.IsFocused)
                HtmlTextInputStateLogic.PrepareCompositionCommit(urlInputState);
        }
    }

    public void UpdateImeState(bool isOpen, string indicator)
    {
        lock (sync)
        {
            if (!urlInputState.IsFocused)
                return;

            urlInputState.ImeOpen = isOpen;
            urlInputState.ImeIndicator = indicator ?? string.Empty;
            Invalidate();
        }
    }

    public bool TryGetTextCompositionCursor(out TextCompositionCursor cursor)
    {
        lock (sync)
        {
            cursor = default;
            if (!urlInputState.IsFocused)
                return false;

            var composedValue = urlInputState.CompositionText.Length > 0
                ? urlInputState.Text.Insert(Math.Clamp(urlInputState.CompositionStartIndex, 0, urlInputState.Text.Length), urlInputState.CompositionText)
                : urlInputState.Text;
            var caretIndex = urlInputState.CompositionText.Length > 0
                ? urlInputState.CompositionStartIndex + Math.Clamp(urlInputState.CompositionCursorOffset, 0, urlInputState.CompositionText.Length)
                : urlInputState.CaretIndex;
            var caret = textServices.GetCaretPosition(
                CreateTextInputTextStyle(),
                composedValue,
                urlInputState.LineHeight,
                Math.Max(0, urlInputState.Width - urlInputState.PaddingLeft - urlInputState.PaddingRight),
                caretIndex);
            cursor = new TextCompositionCursor(
                urlInputState.Left + urlInputState.PaddingLeft + caret.X,
                urlInputState.Top + urlInputState.PaddingTop + caret.Y,
                2,
                Math.Max(urlInputState.FontSize + 4, urlInputState.LineHeight));
            return true;
        }
    }

    private SceneLayoutCommit BuildCommit(int width)
    {
        var toolbarLayout = ResolveToolbarLayout(width);
        SyncInputLayout(toolbarLayout.InputLeft, toolbarLayout.InputWidth);
        var children = new List<string>();
        var nodes = new Dictionary<string, SceneGraphNode>(StringComparer.Ordinal);
        var layout = new Dictionary<string, SceneLayoutBox>(StringComparer.Ordinal)
        {
            [RootId] = new(SceneNodeKind.View, 0, 0, width, Height, BackgroundColor: "#f8fafc", BorderColor: "#d6dbe6", BorderWidth: 1)
        };


        {
            children.Add(BackId);
            children.Add(BackIconId);
            nodes[BackId] = new(SceneNodeKind.View, RootId, [], "Back");
            nodes[BackIconId] = new(SceneNodeKind.Image, RootId, [], "Back icon");
            layout[BackId] = CreateButtonBox(toolbarLayout.BackLeft, canGoBack ? "#e2e8f0" : "#f0f0f0");
            layout[BackIconId] = CreateIconBox(toolbarLayout.BackLeft + 4, canGoBack ? "arrow_back_black64.png" : "arrow_back_disabled64.png");
        }

        if (canGoForward)
        {
            children.Add(ForwardId);
            children.Add(ForwardIconId);
            nodes[ForwardId] = new(SceneNodeKind.View, RootId, [], "Next");
            nodes[ForwardIconId] = new(SceneNodeKind.Image, RootId, [], "Next icon");
            layout[ForwardId] = CreateButtonBox(toolbarLayout.ForwardLeft);
            layout[ForwardIconId] = CreateIconBox(toolbarLayout.ForwardLeft + 4, "arrow_forward_black64.png");
        }

        children.Add(RefreshId);
        children.Add(RefreshIconId);
        children.Add(UrlInputId);
        nodes[RefreshId] = new(SceneNodeKind.View, RootId, [], "Refresh");
        nodes[RefreshIconId] = new(SceneNodeKind.Image, RootId, [], "Refresh icon");
        nodes[UrlInputId] = new(SceneNodeKind.TextInput, RootId, [], "URL");
        nodes[RootId] = new(SceneNodeKind.View, null, children);
        layout[RefreshId] = CreateButtonBox(toolbarLayout.RefreshLeft);
        layout[RefreshIconId] = CreateIconBox(toolbarLayout.RefreshLeft + 4, "refresh_black64.png");
        layout[UrlInputId] = new(
                SceneNodeKind.TextInput,
                toolbarLayout.InputLeft,
                3,
                toolbarLayout.InputWidth,
                24,
                BackgroundColor: "#ffffff",
                BorderColor: urlInputState.IsFocused ? "#2563eb" : "#aeb7c5",
                BorderWidth: 1,
                BorderRadius: 4,
                TextContent: urlInputState.Text,
                TextStyle: UrlInputTextStyle,
                PlaceholderText: "https://example.com/",
                PlaceholderColor: "#64748b",
                PaddingLeft: urlInputState.PaddingLeft,
                PaddingTop: urlInputState.PaddingTop,
                PaddingRight: urlInputState.PaddingRight,
                PaddingBottom: urlInputState.PaddingBottom,
                LineHeight: urlInputState.LineHeight,
                CaretIndex: urlInputState.CaretIndex,
                SelectionStart: urlInputState.SelectionStart,
                SelectionEnd: urlInputState.SelectionEnd,
                IsFocused: urlInputState.IsFocused,
                CompositionText: urlInputState.CompositionText,
                CompositionStart: urlInputState.CompositionStartIndex,
                CompositionCursorOffset: urlInputState.CompositionCursorOffset,
                CompositionSelectionStart: urlInputState.CompositionSelectionStart,
                CompositionSelectionLength: urlInputState.CompositionSelectionLength,
                ImeOpen: urlInputState.ImeOpen,
                ImeIndicator: urlInputState.ImeIndicator);

        return SceneLayoutCommitFactory.Create(RootId, new SceneViewport(width, Height), nodes, layout);
    }

    private void SyncInputLayout(float inputLeft, float inputWidth)
    {
        urlInputState.Left = inputLeft;
        urlInputState.Top = 3;
        urlInputState.Width = inputWidth;
        urlInputState.Height = 24;
        urlInputState.FontSize = UrlInputTextStyle.FontSize;
        urlInputState.Color = UrlInputTextStyle.Color;
        urlInputState.FontFamily = UrlInputTextStyle.FontFamily;
        urlInputState.FontWeight = UrlInputTextStyle.FontWeight;
        urlInputState.TextAlign = UrlInputTextStyle.TextAlign;
        urlInputState.PaddingLeft = 6;
        urlInputState.PaddingTop = 3;
        urlInputState.PaddingRight = 6;
        urlInputState.PaddingBottom = 3;
        urlInputState.LineHeight = 18;
        urlInputState.Multiline = false;
        urlInputState.PlaceholderText = "https://example.com/";
        urlInputState.CaretIndex = textServices.SnapCaretIndex(urlInputState.Text, Math.Clamp(urlInputState.CaretIndex, 0, urlInputState.Text.Length));
    }

    private static SceneLayoutBox CreateButtonBox(float left, string? color = null)
        => new(
            SceneNodeKind.View,
            left,
            3,
            24,
            24,
            BackgroundColor: color ?? "#e2e8f0",
            BorderRadius: 4);

    private static SceneLayoutBox CreateIconBox(float left, string fileName)
        => new(
            SceneNodeKind.Image,
            left,
            7,
            16,
            16,
            ImageSource: ResolveAssetUri(fileName));

    private static float ResolveInputWidth(int width, float inputLeft)
        => Math.Max(120, width - inputLeft - 6);

    private bool HitTestInput(float x, float y)
    {
        var toolbarLayout = ResolveToolbarLayout(cachedWidth > 0 ? cachedWidth : 980);
        return Contains(toolbarLayout.InputLeft, 3, toolbarLayout.InputWidth, 24, x, y);
    }

    private int HitTestCaretIndex(float pointerX, float pointerY)
    {
        var localX = Math.Max(0, pointerX - (urlInputState.Left + urlInputState.PaddingLeft));
        var localY = Math.Max(0, pointerY - (urlInputState.Top + urlInputState.PaddingTop));
        return textServices.HitTestCaretIndex(
            CreateTextInputTextStyle(),
            urlInputState.Text,
            urlInputState.LineHeight,
            Math.Max(0, urlInputState.Width - urlInputState.PaddingLeft - urlInputState.PaddingRight),
            localX,
            localY);
    }

    private bool HitTestEnabledButton(float x, float y)
    {
        var toolbarLayout = ResolveToolbarLayout(cachedWidth > 0 ? cachedWidth : 980);
        return canGoBack && Contains(toolbarLayout.BackLeft, 3, 24, 24, x, y) ||
               canGoForward && Contains(toolbarLayout.ForwardLeft, 3, 24, 24, x, y) ||
               Contains(toolbarLayout.RefreshLeft, 3, 24, 24, x, y);
    }

    private ToolbarLayout ResolveToolbarLayout(int width)
    {
        var nextLeft = 6f;
        var backLeft = nextLeft;
        //if (canGoBack)
        nextLeft += 30;

        var forwardLeft = nextLeft;
        if (canGoForward)
            nextLeft += 30;

        var refreshLeft = nextLeft;
        var inputLeft = refreshLeft + 30;
        return new ToolbarLayout(backLeft, forwardLeft, refreshLeft, inputLeft, ResolveInputWidth(width, inputLeft));
    }

    private bool IsDoubleClick(float x, float y)
    {
        var elapsedMs = timeProvider.GetElapsedTime(lastPrimaryClickTimestamp).TotalMilliseconds;
        var dx = x - lastPrimaryClickX;
        var dy = y - lastPrimaryClickY;
        return string.Equals(lastPrimaryClickInputId, UrlInputId, StringComparison.Ordinal) &&
               elapsedMs <= DoubleClickThresholdMs &&
               dx * dx + dy * dy <= DoubleClickThresholdPx * DoubleClickThresholdPx;
    }

    private void RememberPrimaryClick(float x, float y)
    {
        lastPrimaryClickInputId = UrlInputId;
        lastPrimaryClickX = x;
        lastPrimaryClickY = y;
        lastPrimaryClickTimestamp = timeProvider.GetTimestamp();
    }

    private bool MoveFocus(bool forward)
    {
        SetFocused(false);
        return true;
    }

    private void SetFocus(string? inputId)
        => SetFocused(string.Equals(inputId, UrlInputId, StringComparison.Ordinal));

    private void SetFocused(bool focused)
    {
        var shouldClearTransientState =
            !focused &&
            (urlInputState.IsSelectingWithMouse ||
             urlInputState.IsTextCompositionActive ||
             urlInputState.CompositionText.Length > 0 ||
             urlInputState.ImeOpen ||
             HtmlTextInputStateLogic.HasSelection(urlInputState));
        if (urlInputState.IsFocused == focused && !shouldClearTransientState)
            return;

        urlInputState.IsFocused = focused;
        urlInputState.IsSelectingWithMouse = false;
        urlInputState.IsTextCompositionActive = false;
        urlInputState.PendingCompositionCommit = false;
        urlInputState.CompositionReplacedSelection = false;
        urlInputState.CompositionText = string.Empty;
        urlInputState.CompositionStartIndex = urlInputState.CaretIndex;
        urlInputState.CompositionCursorOffset = 0;
        urlInputState.ImeOpen = false;
        urlInputState.ImeIndicator = string.Empty;
        if (focused)
            urlInputState.CaretIndex = Math.Min(urlInputState.CaretIndex, urlInputState.Text.Length);
        textInputController.ClearSelection(urlInputState);
        Invalidate();
    }

    private void Invalidate()
    {
        dirty = true;
        RenderWakeRequested?.Invoke();
    }

    private static string RemoveLineBreaks(string text)
        => text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);

    private static SceneTextStyle CreateTextInputTextStyle()
        => UrlInputTextStyle;

    private static bool Contains(float left, float top, float width, float height, float x, float y)
        => x >= left && x <= left + width && y >= top && y <= top + height;

    private static string ResolveAssetUri(string fileName)
    {
        var outputCandidate = Path.Combine(AppContext.BaseDirectory, "assets", fileName);
        if (File.Exists(outputCandidate))
            return new Uri(outputCandidate).AbsoluteUri;

        var sourceCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "assets", fileName));
        return new Uri(sourceCandidate).AbsoluteUri;
    }

    private readonly record struct ToolbarLayout(float BackLeft, float ForwardLeft, float RefreshLeft, float InputLeft, float InputWidth);
}
