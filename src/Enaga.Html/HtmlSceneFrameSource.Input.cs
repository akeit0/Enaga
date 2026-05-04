using Enaga.Html.Dom;
using Enaga.Input;
using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

public sealed partial class HtmlSceneFrameSource : IInputSink, IPointerCursorSource, ITextCompositionRangeSink
{
    private const int ShiftModifier = 1;
    private const int ControlModifier = 2;
    private const int LeftMouseButtonMask = 1;
    private const double DoubleClickThresholdMs = 150;
    private const float DoubleClickThresholdPx = 8;
    private const float WheelScrollFactor = 52;
    private static readonly float[] ViewportScaleSteps =
    [
        0.25f,
        0.33f,
        0.50f,
        0.67f,
        0.75f,
        0.80f,
        0.90f,
        1.00f,
        1.10f,
        1.25f,
        1.50f,
        1.75f,
        2.50f,
        3.00f,
        4.00f,
        5.00f
    ];
    private readonly Dictionary<SceneNodeId, HtmlTextInputState> inputStates = new();
    private readonly Dictionary<HtmlNodeId, HtmlSelectState> selectStates = [];
    private readonly Dictionary<SceneNodeId, HtmlScrollViewState> scrollStates = new();
    private readonly Dictionary<SceneNodeId, ScrollScaleAnchor> pendingScrollScaleAnchors = new();
    private readonly List<SceneDamageRect> hoverPaintDirtyRects = new();
    private readonly List<SceneNodeId> staleInputIdScratch = new();
    private readonly List<SceneNodeId> staleScrollIdScratch = new();
    private readonly SceneWheelScrollTargetLatch<SceneNodeId> wheelScrollTargetLatch = new();
    private readonly SceneNodeIdentityMap<string> overlaySceneNodeIds;
    private readonly HtmlTextInputController textInputController;
    private readonly IRuntimeTextServices textServices;
    private SceneNodeId? focusedInputId;
    private float lastPointerX;
    private float lastPointerY;
    private SceneNodeId? lastPrimaryClickInputId;
    private float lastPrimaryClickX;
    private float lastPrimaryClickY;
    private long lastPrimaryClickTimestamp;
    private readonly SceneScrollBarDragState<SceneNodeId> activeScrollBarDrag = new();
    private SceneNodeId? activeLinkPressNodeId;
    private HtmlNodeId? activeClickDomNodeId;
    private HtmlNodeId? openSelectDomNodeId;
    private float activeClickX;
    private float activeClickY;
    private PointerCursorKind currentCursor = PointerCursorKind.Default;

    public string? LastActivatedLinkHref { get; private set; }

    public PointerCursorKind CurrentCursor => currentCursor;

    public event Action<string>? LinkActivated;

    public event Action<HtmlDomElement>? ElementClicked;

    public event Action<string, string>? TextInputSubmitted;

    public bool TryGetTextInputValueByElementId(string elementId, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(elementId))
            return false;

        lock (sync)
        {
            if (cachedCommit is null)
                return false;

            foreach (var (sceneNodeId, box) in cachedCommit.Layout)
            {
                if (box.NodeKind != SceneNodeKind.TextInput ||
                    !cachedCommit.Nodes.TryGetValue(sceneNodeId, out var node) ||
                    !string.Equals(node.Label, elementId, StringComparison.Ordinal))
                {
                    continue;
                }

                value = inputStates.TryGetValue(sceneNodeId, out var state)
                    ? state.Text
                    : box.TextContent ?? string.Empty;
                return true;
            }
        }

        return false;
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic)
    {
        lock (sync)
        {
            lastPointerX = x;
            lastPointerY = y;

            if ((buttons & LeftMouseButtonMask) != 0 && UpdateActiveScrollBarDrag(x, y))
            {
                UpdateHoveredNodeId(x, y);
                RequestInteractiveUpdate();
                return;
            }

            if (UpdateOpenSelectHover(x, y))
            {
                UpdateHoveredNodeId(x, y);
                UpdatePointerCursor();
                RequestInteractiveUpdate();
                return;
            }

            UpdateHoveredNodeId(x, y);
            UpdatePointerCursor();

            if ((buttons & LeftMouseButtonMask) != 0 &&
                focusedInputId is { } focusedId &&
                inputStates.TryGetValue(focusedId, out var state) &&
                state.IsSelectingWithMouse)
            {
                textInputController.SetSelection(state, state.SelectionAnchorIndex, HitTestCaretIndex(state, x, y));
                RequestInteractiveUpdate();
                return;
            }
        }
    }

    public void PointerDown(int button, int buttons, bool synthetic)
    {
        if (button != 0)
            return;

        lock (sync)
        {
            if (cachedCommit is null)
                return;

            activeLinkPressNodeId = null;
            activeClickDomNodeId = null;
            if (TryHitOpenSelectOption(lastPointerX, lastPointerY, out var optionSelectState, out var optionIndex))
            {
                optionSelectState.Select(optionIndex);
                openSelectDomNodeId = null;
                SetFocusedTextInput(null);
                activeDomNodeId = null;
                RequestInteractiveUpdate();
                return;
            }

            var oldActiveDomNodeId = activeDomNodeId;
            if (TryHitTestDomNode(cachedCommit, lastPointerX, lastPointerY, out var pressedDomNodeIds, includeAncestors: false))
            {
                activeDomNodeId = pressedDomNodeIds?.FirstOrDefault(static id => id.IsValid);
                activeClickDomNodeId = activeDomNodeId;
                activeClickX = lastPointerX;
                activeClickY = lastPointerY;
            }
            else
            {
                activeDomNodeId = null;
            }
            builder.ApplyActiveSnapshot(oldActiveDomNodeId, activeDomNodeId);
            if (TryBeginScrollBarDrag(lastPointerX, lastPointerY))
            {
                SetFocusedTextInput(null);
                openSelectDomNodeId = null;
                RequestInteractiveUpdate();
                return;
            }

            if (TryHitTestSelect(cachedCommit, lastPointerX, lastPointerY, out var selectDomNodeId, out _))
            {
                openSelectDomNodeId = openSelectDomNodeId == selectDomNodeId ? null : selectDomNodeId;
                SetFocusedTextInput(null);
                RequestInteractiveUpdate();
                return;
            }

            openSelectDomNodeId = null;
            if (TryHitTestTextInput(cachedCommit, lastPointerX, lastPointerY, out var inputId, out var inputBox))
            {
                var state = EnsureInputState(inputId, inputBox);
                SetFocusedTextInput(inputId);
                var caretIndex = HitTestCaretIndex(state, lastPointerX, lastPointerY);
                if (IsDoubleClick(inputId, lastPointerX, lastPointerY))
                {
                    state.IsSelectingWithMouse = false;
                    textInputController.SelectWordAt(state, caretIndex);
                }
                else
                {
                    state.IsSelectingWithMouse = true;
                    textInputController.SetSelection(state, caretIndex, caretIndex);
                }

                RememberPrimaryClick(inputId, lastPointerX, lastPointerY);
            }
            else
            {
                SetFocusedTextInput(null);
                if (TryHitTestLink(cachedCommit, lastPointerX, lastPointerY, out var linkNodeId, out _))
                    activeLinkPressNodeId = linkNodeId;
            }

            RequestInteractiveUpdate();
        }
    }

    public void PointerUp(int button, int buttons, bool synthetic)
    {
        if (button != 0)
            return;

        string? linkToActivate = null;
        HtmlDomElement? clickedDomElement = null;
        lock (sync)
        {
            var oldActiveDomNodeId = activeDomNodeId;
            activeDomNodeId = null;
            builder.ApplyActiveSnapshot(oldActiveDomNodeId, activeDomNodeId);
            if (EndActiveScrollBarDrag())
            {
                RequestInteractiveUpdate();
                return;
            }

            if (activeLinkPressNodeId is { } linkNodeId &&
                cachedCommit is not null &&
                TryHitTestLink(cachedCommit, lastPointerX, lastPointerY, out var releasedNodeId, out var href) &&
                linkNodeId == releasedNodeId)
            {
                LastActivatedLinkHref = href;
                activeLinkPressNodeId = null;
                linkToActivate = href;
                RequestInteractiveUpdate();
            }
            else
            {
                activeLinkPressNodeId = null;
                if (focusedInputId is { } focusedId && inputStates.TryGetValue(focusedId, out var state))
                {
                    if (state.IsSelectingWithMouse)
                        textInputController.SetSelection(state, state.SelectionAnchorIndex, HitTestCaretIndex(state, lastPointerX, lastPointerY));
                    state.IsSelectingWithMouse = false;
                    RequestInteractiveUpdate();
                }
            }

            if (activeClickDomNodeId is { } pressedDomNodeId &&
                cachedCommit is not null &&
                IsDomClickRelease(cachedCommit, pressedDomNodeId, lastPointerX, lastPointerY))
            {
                TryResolveDomElement(pressedDomNodeId, out clickedDomElement);
            }

            activeClickDomNodeId = null;
        }

        if (clickedDomElement is not null)
            ElementClicked?.Invoke(clickedDomElement);
        if (linkToActivate is not null)
            LinkActivated?.Invoke(linkToActivate);
    }

    private bool IsDomClickRelease(SceneLayoutCommit commit, HtmlNodeId pressedDomNodeId, float x, float y)
    {
        if (TryHitTestDomNode(commit, x, y, out var releasedDomNodeIds, includeAncestors: true) &&
            releasedDomNodeIds?.Contains(pressedDomNodeId) == true)
        {
            return true;
        }

        var deltaX = x - activeClickX;
        var deltaY = y - activeClickY;
        return deltaX * deltaX + deltaY * deltaY <= DoubleClickThresholdPx * DoubleClickThresholdPx;
    }

    private bool TryResolveDomElement(HtmlNodeId nodeId, out HtmlDomElement element)
    {
        return cachedDomElements.TryGetValue(nodeId, out element!);
    }

    public void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0)
    {
        lock (sync)
        {
            if ((modifiers & ControlModifier) != 0)
            {
                TryStepViewportScale(deltaY > 0 ? 1 : -1);
                return;
            }

            if (cachedCommit is null ||
                ResolveWheelScrollView(cachedCommit, lastPointerX, lastPointerY, deltaX, deltaY) is not { } scrollViewId ||
                !cachedCommit.Layout.TryGetValue(scrollViewId, out var scrollBox))
            {
                return;
            }

            var state = EnsureScrollState(scrollViewId, scrollBox);
            if (SceneSmoothScrollController.ApplyWheelTarget(state, scrollBox, deltaX, deltaY, WheelScrollFactor))
            {
                dirtyScrollViewIds.Add(scrollViewId);
                hoverRefreshDeferred = true;
                RequestInteractiveUpdate();
            }
        }
    }

    public bool TryStepViewportScale(int direction)
    {
        var oldScale = viewportScale;
        viewportScale = ResolveNextViewportScale(oldScale, direction);
        return ApplyViewportScaleChange(oldScale);
    }

    public bool TryResetViewportScale()
    {
        var oldScale = viewportScale;
        viewportScale = 1f;
        return ApplyViewportScaleChange(oldScale);
    }

    private bool ApplyViewportScaleChange(float oldScale)
    {
        if (Math.Abs(viewportScale - oldScale) < 0.001f)
            return false;

        CaptureScrollScaleAnchors();
        cachedBaseCommit = null;
        cachedCommit = null;
        cachedWidth = -1;
        cachedHeight = -1;
        previousRenderElapsed = null;
        Invalidate(BaseCommitInvalidation | HtmlPipelineInvalidation.Interactive | HtmlPipelineInvalidation.HitTest, HtmlRenderDamageBits.FullFrame | HtmlRenderDamageBits.Resize);
        dirtyScrollViewIds.Clear();
        RenderWakeRequested?.Invoke();
        return true;
    }

    private void CaptureScrollScaleAnchors()
    {
        pendingScrollScaleAnchors.Clear();
        if (cachedCommit is null)
            return;

        foreach (var (scrollId, state) in scrollStates)
        {
            if (!cachedCommit.Layout.TryGetValue(scrollId, out var scrollBox))
                continue;

            var screenScrollBox = SceneScreenGeometry.ResolveScreenBox(cachedCommit, cachedCommit.Layout, scrollId, scrollBox) with
            {
                ScrollX = SceneScrollMetrics.ClampScrollX(scrollBox, state.ScrollX),
                ScrollY = SceneScrollMetrics.ClampScrollY(scrollBox, state.ScrollY)
            };
            if (TryFindScrollScaleAnchor(cachedCommit, scrollId, screenScrollBox, out var anchor))
                pendingScrollScaleAnchors[scrollId] = anchor;
        }
    }

    private static bool TryFindScrollScaleAnchor(SceneLayoutCommit commit, SceneNodeId scrollId, SceneLayoutBox scrollBox, out ScrollScaleAnchor anchor)
    {
        anchor = default;
        var bestId = default(SceneNodeId);
        var bestTop = 0f;
        var bestScore = float.PositiveInfinity;
        if (commit.PaintOrderIds.Length > 0)
        {
            for (var index = 0; index < commit.PaintOrderIds.Length; index++)
                ConsiderAnchor(commit.PaintOrderIds[index]);
        }
        else
        {
            foreach (var (id, _) in commit.Layout)
                ConsiderAnchor(id);
        }

        if (!bestId.IsValid)
            return false;

        anchor = new ScrollScaleAnchor(bestId, bestTop);
        return true;

        void ConsiderAnchor(SceneNodeId id)
        {
            if (id == scrollId || !IsDescendantOf(commit, id, scrollId) || !commit.Layout.TryGetValue(id, out var box))
                return;

            if (box.Width <= 0 || box.Height <= 0)
                return;

            var screenBox = SceneScreenGeometry.ResolveScreenBox(commit, commit.Layout, id, box);
            var intersects =
                screenBox.AbsTop + screenBox.Height > scrollBox.AbsTop &&
                screenBox.AbsTop < scrollBox.AbsTop + scrollBox.Height &&
                screenBox.AbsLeft + screenBox.Width > scrollBox.AbsLeft &&
                screenBox.AbsLeft < scrollBox.AbsLeft + scrollBox.Width;
            if (!intersects)
                return;

            var distance = Math.Abs(screenBox.AbsTop - scrollBox.AbsTop);
            var isContentAnchor =
                box.NodeKind is SceneNodeKind.Text or SceneNodeKind.Image or SceneNodeKind.TextInput ||
                !commit.Nodes.TryGetValue(id, out var graphNode) ||
                graphNode.Children.Length == 0;
            var isOversized = screenBox.Height > scrollBox.Height * 0.9f;
            var score = distance + (isContentAnchor ? 0 : 10_000) + (isOversized ? 5_000 : 0);
            if (score >= bestScore)
                return;

            bestScore = score;
            bestId = id;
            bestTop = screenBox.AbsTop;
        }
    }

    private static bool IsDescendantOf(SceneLayoutCommit commit, SceneNodeId id, SceneNodeId ancestorId)
    {
        var current = id;
        while (commit.Nodes.TryGetValue(current, out var node) && node.ParentId is { } parentId)
        {
            if (parentId == ancestorId)
                return true;

            current = parentId;
        }

        return false;
    }

    private static float ResolveNextViewportScale(float currentScale, int direction)
    {
        if (direction > 0)
        {
            for (var index = 0; index < ViewportScaleSteps.Length; index++)
            {
                if (ViewportScaleSteps[index] > currentScale + 0.001f)
                    return ViewportScaleSteps[index];
            }

            return ViewportScaleSteps[^1];
        }

        if (direction < 0)
        {
            for (var index = ViewportScaleSteps.Length - 1; index >= 0; index--)
            {
                if (ViewportScaleSteps[index] < currentScale - 0.001f)
                    return ViewportScaleSteps[index];
            }

            return ViewportScaleSteps[0];
        }

        return currentScale;
    }

    public void KeyDown(string key, int modifiers, bool repeat, bool synthetic)
    {
        (string ElementId, string Value)? submittedInput = null;
        lock (sync)
        {
            if (!TryGetFocusedTextInput(out var state))
                return;

            if (!state.Multiline &&
                string.Equals(key, "Enter", StringComparison.Ordinal) &&
                focusedInputId is { } focusedId &&
                cachedCommit is not null &&
                cachedCommit.Nodes.TryGetValue(focusedId, out var node) &&
                !string.IsNullOrWhiteSpace(node.Label))
            {
                submittedInput = (node.Label, state.Text);
            }

            textInputController.HandleKey(state, key, modifiers);
        }

        if (submittedInput is { } submitted)
            TextInputSubmitted?.Invoke(submitted.ElementId, submitted.Value);
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
            if (!TryGetFocusedTextInput(out var state))
                return;

            var normalizedText = state.Multiline ? text : RemoveLineBreaks(text);
            if (normalizedText.Length == 0)
                return;

            textInputController.ApplyTextInput(state, normalizedText);
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
            if (!TryGetFocusedTextInput(out var state))
                return;

            HtmlTextInputStateLogic.StartComposition(state, startIndex);
            RequestInteractiveUpdate();
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
            if (!TryGetFocusedTextInput(out var state))
                return;

            HtmlTextInputStateLogic.UpdateComposition(state, text, cursorPosition, selectionStart, selectionLength);
            RequestInteractiveUpdate();
        }
    }

    public void EndTextComposition()
    {
        lock (sync)
        {
            if (!TryGetFocusedTextInput(out var state))
                return;

            HtmlTextInputStateLogic.EndComposition(state, textServices);
            RequestInteractiveUpdate();
        }
    }

    public void PrepareTextCompositionCommit()
    {
        lock (sync)
        {
            if (!TryGetFocusedTextInput(out var state))
                return;

            HtmlTextInputStateLogic.PrepareCompositionCommit(state);
        }
    }

    public void UpdateImeState(bool isOpen, string indicator)
    {
        lock (sync)
        {
            if (!TryGetFocusedTextInput(out var state))
                return;

            state.ImeOpen = isOpen;
            state.ImeIndicator = indicator ?? string.Empty;
            RequestInteractiveUpdate();
        }
    }

    public bool TryGetTextCompositionCursor(out TextCompositionCursor cursor)
    {
        lock (sync)
        {
            cursor = default;
            if (!TryGetFocusedTextInput(out var state))
                return false;

            var composedValue = state.CompositionText.Length > 0
                ? state.Text.Insert(Math.Clamp(state.CompositionStartIndex, 0, state.Text.Length), state.CompositionText)
                : state.Text;
            var caretIndex = state.CompositionText.Length > 0
                ? state.CompositionStartIndex + Math.Clamp(state.CompositionCursorOffset, 0, state.CompositionText.Length)
                : state.CaretIndex;
            var caret = textServices.GetCaretPosition(
                CreateTextInputTextStyle(state),
                composedValue,
                state.LineHeight,
                Math.Max(0, state.Width - state.PaddingLeft - state.PaddingRight),
                caretIndex);
            cursor = new TextCompositionCursor(
                state.Left + state.PaddingLeft + caret.X,
                state.Top + state.PaddingTop + caret.Y,
                2,
                Math.Max(state.FontSize + 4, state.LineHeight));
            return true;
        }
    }

    private void ResetInteractiveState()
    {
        inputStates.Clear();
        selectStates.Clear();
        scrollStates.Clear();
        focusedInputId = null;
        openSelectDomNodeId = null;
        hoveredDomNodeIds = null;
        activeDomNodeId = null;
        activeScrollBarDrag.Clear();
        activeLinkPressNodeId = null;
        currentCursor = PointerCursorKind.Default;
        LastActivatedLinkHref = null;
        previousRenderElapsed = null;
        hoverRefreshDeferred = false;
        wheelScrollTargetLatch.Clear();
        dirtyScrollViewIds.Clear();
        if (cachedCommit is not null)
            Invalidate(HtmlPipelineInvalidation.Interactive | HtmlPipelineInvalidation.HitTest, HtmlRenderDamageBits.Interactive);
        lastPrimaryClickInputId = null;
    }

    private SceneLayoutCommit ApplyInteractiveState(
        SceneLayoutCommit commit,
        double scrollDeltaSeconds,
        out bool hasPendingScrollAnimation,
        out bool hitTestGeometryChanged)
    {
        SceneNodeMap<SceneLayoutBox>? updatedLayout = null;
        SceneNodeMap<SceneGraphNode>? updatedNodes = null;
        activeInputIdScratch.Clear();
        activeScrollIdScratch.Clear();
        hasPendingScrollAnimation = false;
        hitTestGeometryChanged = false;
        foreach (var (id, box) in commit.Layout)
        {
            if (box.NodeKind == SceneNodeKind.ScrollView)
            {
                activeScrollIdScratch.Add(id);
                var scrollScreenBox = SceneScreenGeometry.ResolveScreenBox(commit, commit.Layout, id, box);
                var scrollState = EnsureScrollState(id, scrollScreenBox);
                if (pendingScrollScaleAnchors.Remove(id, out var anchor) &&
                    commit.Layout.TryGetValue(anchor.NodeId, out var anchorBox))
                {
                    scrollState.ScrollY = SceneScrollMetrics.ClampScrollY(box, anchorBox.AbsTop - anchor.ScreenTop);
                    scrollState.TargetScrollY = scrollState.ScrollY;
                    scrollState.TargetScrollX = scrollState.ScrollX;
                }

                var scrollAnimating = SceneSmoothScrollController.Advance(scrollState, box, scrollDeltaSeconds);
                hasPendingScrollAnimation |= scrollAnimating;
                if (scrollAnimating)
                    dirtyScrollViewIds.Add(id);
                var nextScrollBox = box with
                {
                    ScrollX = SceneScrollMetrics.ClampScrollX(box, scrollState.ScrollX),
                    ScrollY = SceneScrollMetrics.ClampScrollY(box, scrollState.ScrollY)
                };

                if (nextScrollBox != box)
                {
                    hitTestGeometryChanged = true;
                    updatedLayout ??= SceneNodeMap<SceneLayoutBox>.CreateOverlay(commit.Layout, Math.Max(4, dirtyScrollViewIds.Count));
                    updatedLayout[id] = nextScrollBox;
                }
            }

            if (box.NodeKind != SceneNodeKind.TextInput)
                continue;

            if (TryResolveNearestDomNodeId(commit, id, out var inputDomNodeId) &&
                TryResolveDomElement(inputDomNodeId, out var inputElement) &&
                IsSelectElement(inputElement))
            {
                var selectState = EnsureSelectState(inputElement);
                var nextSelectBox = (updatedLayout is not null && updatedLayout.TryGetValue(id, out var currentSelectBox) ? currentSelectBox : box) with
                {
                    TextContent = selectState.SelectedText,
                    CaretIndex = 0,
                    SelectionStart = 0,
                    SelectionEnd = 0,
                    IsFocused = false
                };

                if (nextSelectBox != box)
                {
                    updatedLayout ??= SceneNodeMap<SceneLayoutBox>.CreateOverlay(commit.Layout, Math.Max(4, inputStates.Count));
                    updatedLayout[id] = nextSelectBox;
                }

                continue;
            }

            activeInputIdScratch.Add(id);
            var layout = updatedLayout ?? commit.Layout;
            var inputScreenBox = SceneScreenGeometry.ResolveScreenBox(commit, layout, id, layout[id]);
            var state = EnsureInputState(id, inputScreenBox);
            SyncStateFromLayoutBox(state, inputScreenBox);
            var nextInputBox = (updatedLayout is not null && updatedLayout.TryGetValue(id, out var currentBox) ? currentBox : box) with
            {
                TextContent = state.Text,
                CaretIndex = state.CaretIndex,
                SelectionStart = state.SelectionStart,
                SelectionEnd = state.SelectionEnd,
                CompositionText = state.CompositionText,
                CompositionStart = state.CompositionStartIndex,
                CompositionCursorOffset = state.CompositionCursorOffset,
                CompositionSelectionStart = state.CompositionSelectionStart,
                CompositionSelectionLength = state.CompositionSelectionLength,
                ImeOpen = state.ImeOpen,
                ImeIndicator = state.ImeIndicator,
                IsFocused = id == focusedInputId
            };

            if (nextInputBox == box)
                continue;

            updatedLayout ??= SceneNodeMap<SceneLayoutBox>.CreateOverlay(commit.Layout, Math.Max(4, inputStates.Count));
            updatedLayout[id] = nextInputBox;
        }

        RemoveStaleInputStates(activeInputIdScratch);
        RemoveStaleScrollStates(activeScrollIdScratch);
        var layoutForOverlay = updatedLayout ?? commit.Layout;
        var commitWithLayout = updatedLayout is null
            ? commit
            : commit with { Layout = updatedLayout };
        if (TryAddSelectPopupOverlay(commitWithLayout, layoutForOverlay, ref updatedNodes, ref updatedLayout))
        {
            hitTestGeometryChanged = true;
        }

        if (updatedLayout is null && updatedNodes is null)
            return commit;

        return commit with
        {
            Nodes = updatedNodes ?? commit.Nodes,
            Layout = updatedLayout ?? commit.Layout
        };
    }

    private HtmlTextInputState EnsureInputState(SceneNodeId inputId, SceneLayoutBox box)
    {
        if (inputStates.TryGetValue(inputId, out var state))
            return state;

        state = new HtmlTextInputState(inputId.ToString());
        inputStates[inputId] = state;
        SyncStateFromLayoutBox(state, box);
        state.Text = box.TextContent ?? string.Empty;
        state.CaretIndex = state.Text.Length;
        textInputController.ClearSelection(state);
        return state;
    }

    private HtmlSelectState EnsureSelectState(HtmlDomElement element)
    {
        if (!selectStates.TryGetValue(element.NodeId, out var state))
        {
            state = new HtmlSelectState(element.NodeId);
            selectStates[element.NodeId] = state;
        }

        state.Refresh(element);
        return state;
    }

    private bool TryHitTestSelect(SceneLayoutCommit commit, float x, float y, out HtmlNodeId selectDomNodeId, out SceneLayoutBox selectBox)
    {
        var hitTestIndex = GetHitTestIndex(commit);
        var candidateIndexes = hitTestIndex.Query(x, y, HtmlHitTestChannel.TextInput);
        for (var candidateIndex = candidateIndexes.Count - 1; candidateIndex >= 0; candidateIndex--)
        {
            var entry = hitTestIndex.Entries[candidateIndexes[candidateIndex]];
            if (entry.Box.NodeKind != SceneNodeKind.TextInput ||
                !entry.ScreenRect.Contains(x, y) ||
                !IsPointVisibleThroughScrollAncestors(commit, entry.SceneNodeId, x, y) ||
                !TryResolveNearestDomNodeId(commit, entry.SceneNodeId, out var domNodeId) ||
                !TryResolveDomElement(domNodeId, out var element) ||
                !IsSelectElement(element))
            {
                continue;
            }

            selectDomNodeId = domNodeId;
            selectBox = WithScreenRect(entry.Box, entry.ScreenRect);
            return true;
        }

        selectDomNodeId = default;
        selectBox = default!;
        return false;
    }

    private bool TryHitOpenSelectOption(float x, float y, out HtmlSelectState state, out int optionIndex)
    {
        state = null!;
        optionIndex = -1;
        if (openSelectDomNodeId is not { } selectDomNodeId ||
            !selectStates.TryGetValue(selectDomNodeId, out var resolvedState))
        {
            return false;
        }

        state = resolvedState;
        return state.TryHitOption(x, y, out optionIndex);
    }

    private bool UpdateOpenSelectHover(float x, float y)
    {
        if (openSelectDomNodeId is not { } selectDomNodeId ||
            !selectStates.TryGetValue(selectDomNodeId, out var state))
        {
            return false;
        }

        var nextHoveredIndex = state.TryHitOption(x, y, out var optionIndex) ? optionIndex : -1;
        return state.SetHoveredIndex(nextHoveredIndex);
    }

    private bool TryAddSelectPopupOverlay(
        SceneLayoutCommit commit,
        SceneNodeMap<SceneLayoutBox> layout,
        ref SceneNodeMap<SceneGraphNode>? updatedNodes,
        ref SceneNodeMap<SceneLayoutBox>? updatedLayout)
    {
        if (openSelectDomNodeId is not { } selectDomNodeId ||
            parsedDocument is null ||
            !TryResolveDomElement(selectDomNodeId, out var selectElement) ||
            EnsureSelectState(selectElement) is not { Options.Count: > 0 } selectState ||
            !TryFindSelectSceneNode(commit, layout, selectDomNodeId, out var selectSceneNodeId, out var selectBox))
        {
            return false;
        }

        var rootScrollX = 0f;
        var rootScrollY = 0f;
        if (layout.TryGetValue(commit.RootId, out var rootBox) && rootBox.NodeKind == SceneNodeKind.ScrollView)
        {
            rootScrollX = rootBox.ScrollX;
            rootScrollY = rootBox.ScrollY;
        }

        var visibleCount = Math.Min(selectState.Options.Count, 8);
        var rowHeight = Math.Max(24, selectBox.Height);
        var popupWidth = Math.Max(selectBox.Width, 80);
        const float popupBorderWidth = 1;
        var popupHeight = rowHeight * visibleCount + popupBorderWidth * 2;
        var popupScreenLeft = Math.Clamp(selectBox.AbsLeft, 0, Math.Max(0, commit.Viewport.Width - popupWidth));
        var popupScreenTop = selectBox.AbsTop + selectBox.Height;
        if (popupScreenTop + popupHeight > commit.Viewport.Height && selectBox.AbsTop - popupHeight >= 0)
            popupScreenTop = selectBox.AbsTop - popupHeight;

        var popupLeft = popupScreenLeft + rootScrollX;
        var popupTop = popupScreenTop + rootScrollY;
        var popupKey = $"__html-select-popup:{selectDomNodeId.Value}";
        var popupId = overlaySceneNodeIds.GetOrCreate(popupKey);
        var optionIds = new SceneNodeId[visibleCount];
        updatedNodes ??= SceneNodeMap<SceneGraphNode>.CreateOverlay(commit.Nodes, visibleCount * 2 + 1);
        updatedLayout ??= SceneNodeMap<SceneLayoutBox>.CreateOverlay(commit.Layout, visibleCount * 2 + 1);
        selectState.BeginPopupLayout();

        for (var index = 0; index < visibleCount; index++)
        {
            var option = selectState.Options[index];
            var rowKey = $"{popupKey}:option:{index}";
            var rowId = overlaySceneNodeIds.GetOrCreate(rowKey);
            var textId = overlaySceneNodeIds.GetOrCreate($"{rowKey}:text");
            optionIds[index] = rowId;
            var rowLeft = popupLeft + popupBorderWidth;
            var rowTop = popupTop + popupBorderWidth + rowHeight * index;
            var rowWidth = Math.Max(0, popupWidth - popupBorderWidth * 2);
            var rowScreenTop = popupScreenTop + popupBorderWidth + rowHeight * index;
            var background = index == selectState.HoveredIndex
                ? "#e5e7eb"
                : index == selectState.SelectedIndex
                    ? "#dbeafe"
                    : "#ffffff";
            updatedNodes[rowId] = new SceneGraphNode(SceneNodeKind.View, popupId, [textId]);
            updatedNodes[textId] = new SceneGraphNode(SceneNodeKind.Text, rowId, []);
            updatedLayout[rowId] = new SceneLayoutBox(
                SceneNodeKind.View,
                rowLeft,
                rowTop,
                rowWidth,
                rowHeight,
                BackgroundColor: background,
                IsPositioned: false);
            updatedLayout[textId] = new SceneLayoutBox(
                SceneNodeKind.Text,
                rowLeft + 8,
                rowTop + 4,
                Math.Max(0, rowWidth - 16),
                Math.Max(0, rowHeight - 8),
                TextContent: option.Text,
                TextStyle: selectBox.TextStyle ?? new SceneTextStyle(16, "#111827"),
                LineHeight: selectBox.LineHeight,
                IsPositioned: false);
            selectState.SetPopupOptionRect(index, popupScreenLeft + popupBorderWidth, rowScreenTop, rowWidth, rowHeight);
        }

        updatedNodes[popupId] = new SceneGraphNode(SceneNodeKind.View, commit.RootId, optionIds);
        updatedLayout[popupId] = new SceneLayoutBox(
            SceneNodeKind.View,
            popupLeft,
            popupTop,
            popupWidth,
            popupHeight,
            BackgroundColor: "#ffffff",
            BorderColor: "#9ca3af",
            BorderWidth: popupBorderWidth,
            BorderStyle: SceneBorderStyle.Solid,
            IsPositioned: true);

        if (updatedNodes.TryGetValue(commit.RootId, out var updatedRootNode))
        {
            if (Array.IndexOf(updatedRootNode.Children, popupId) < 0)
                updatedNodes[commit.RootId] = updatedRootNode with { Children = AppendChild(updatedRootNode.Children, popupId) };
        }
        else if (commit.Nodes.TryGetValue(commit.RootId, out var rootNode))
        {
            updatedNodes[commit.RootId] = rootNode with { Children = AppendChild(rootNode.Children, popupId) };
        }

        return true;
    }

    private static SceneNodeId[] AppendChild(SceneNodeId[] children, SceneNodeId childId)
    {
        var appended = new SceneNodeId[children.Length + 1];
        children.AsSpan().CopyTo(appended);
        appended[^1] = childId;
        return appended;
    }

    private bool TryFindSelectSceneNode(
        SceneLayoutCommit commit,
        SceneNodeMap<SceneLayoutBox> layout,
        HtmlNodeId selectDomNodeId,
        out SceneNodeId sceneNodeId,
        out SceneLayoutBox screenBox)
    {
        foreach (var (id, box) in layout)
        {
            if (box.NodeKind != SceneNodeKind.TextInput ||
                !TryResolveNearestDomNodeId(commit, id, out var domNodeId) ||
                domNodeId != selectDomNodeId)
            {
                continue;
            }

            sceneNodeId = id;
            screenBox = SceneScreenGeometry.ResolveScreenBox(commit, layout, id, box);
            return true;
        }

        sceneNodeId = default;
        screenBox = default!;
        return false;
    }

    private void SyncStateFromLayoutBox(HtmlTextInputState state, SceneLayoutBox box)
    {
        var textStyle = box.TextStyle;
        state.Left = box.AbsLeft;
        state.Top = box.AbsTop;
        state.Width = box.Width;
        state.Height = box.Height;
        state.PaddingLeft = box.PaddingLeft;
        state.PaddingTop = box.PaddingTop;
        state.PaddingRight = box.PaddingRight;
        state.PaddingBottom = box.PaddingBottom;
        state.Multiline = box.Multiline;
        state.LineHeight = box.LineHeight;
        state.FontSize = textStyle?.FontSize ?? state.FontSize;
        state.Color = textStyle?.Color;
        state.FontWeight = textStyle?.FontWeight ?? state.FontWeight;
        state.FontFamily = textStyle?.FontFamily;
        state.TextAlign = textStyle?.TextAlign ?? SceneTextAlign.Left;
        state.PlaceholderText = box.PlaceholderText ?? string.Empty;
        HtmlTextInputStateLogic.ApplyExternalValue(state, textServices, box.TextContent ?? string.Empty);
    }

    private bool TryGetFocusedTextInput(out HtmlTextInputState state)
    {
        state = default!;
        if (focusedInputId is not { } inputId ||
            cachedCommit is null ||
            !cachedCommit.Layout.TryGetValue(inputId, out var box) ||
            box.NodeKind != SceneNodeKind.TextInput)
        {
            return false;
        }

        state = EnsureInputState(inputId, SceneScreenGeometry.ResolveScreenBox(cachedCommit, cachedCommit.Layout, inputId, box));
        return true;
    }

    private bool TryHitTestTextInput(SceneLayoutCommit commit, float x, float y, out SceneNodeId inputId, out SceneLayoutBox inputBox)
    {
        var hitTestIndex = GetHitTestIndex(commit);
        var candidateIndexes = hitTestIndex.Query(x, y, HtmlHitTestChannel.TextInput);
        for (var candidateIndex = candidateIndexes.Count - 1; candidateIndex >= 0; candidateIndex--)
        {
            var entry = hitTestIndex.Entries[candidateIndexes[candidateIndex]];
            if (entry.Box.NodeKind != SceneNodeKind.TextInput)
                continue;

            if (!entry.ScreenRect.Contains(x, y) ||
                !IsPointVisibleThroughScrollAncestors(commit, entry.SceneNodeId, x, y) ||
                IsSelectSceneNode(commit, entry.SceneNodeId))
            {
                continue;
            }

            inputId = entry.SceneNodeId;
            inputBox = WithScreenRect(entry.Box, entry.ScreenRect);
            return true;
        }

        inputId = default;
        inputBox = default!;
        return false;
    }

    private void SetFocusedTextInput(SceneNodeId? inputId)
    {
        if (focusedInputId == inputId)
            return;

        if (focusedInputId is { } previousId && inputStates.TryGetValue(previousId, out var previous))
        {
            previous.IsFocused = false;
            previous.IsSelectingWithMouse = false;
            previous.IsTextCompositionActive = false;
            previous.PendingCompositionCommit = false;
            previous.CompositionReplacedSelection = false;
            previous.CompositionText = string.Empty;
            previous.CompositionCursorOffset = 0;
            previous.ImeOpen = false;
            previous.ImeIndicator = string.Empty;
            textInputController.ClearSelection(previous);
        }

        focusedInputId = inputId;

        if (focusedInputId is { } nextId && inputStates.TryGetValue(nextId, out var next))
        {
            next.IsFocused = true;
            next.CaretIndex = Math.Min(next.CaretIndex, next.Text.Length);
            next.PreferredCaretX = null;
            next.IsTextCompositionActive = false;
            next.PendingCompositionCommit = false;
            next.CompositionReplacedSelection = false;
            next.CompositionText = string.Empty;
            next.CompositionStartIndex = next.CaretIndex;
            next.CompositionCursorOffset = 0;
            next.ImeOpen = false;
            next.ImeIndicator = string.Empty;
            textInputController.ClearSelection(next);
        }
    }

    private bool MoveFocus(bool forward)
    {
        if (cachedCommit is null)
            return false;

        var focusOrder = GetFocusableInputIds(cachedCommit);
        if (focusOrder.Count == 0)
            return false;

        var index = focusedInputId is null ? -1 : focusOrder.IndexOf(focusedInputId.Value);
        index = forward
            ? (index + 1 + focusOrder.Count) % focusOrder.Count
            : (index - 1 + focusOrder.Count) % focusOrder.Count;
        SetFocusedTextInput(focusOrder[index]);
        return true;
    }

    private List<SceneNodeId> GetFocusableInputIds(SceneLayoutCommit commit)
    {
        var ordered = new List<SceneNodeId>();
        TraverseNode(commit.RootId);
        return ordered;

        void TraverseNode(SceneNodeId id)
        {
            if (commit.Layout.TryGetValue(id, out var box) &&
                box.NodeKind == SceneNodeKind.TextInput &&
                !IsSelectSceneNode(commit, id))
            {
                ordered.Add(id);
            }
            if (!commit.Nodes.TryGetValue(id, out var node))
                return;
            for (var index = 0; index < node.Children.Length; index++)
                TraverseNode(node.Children[index]);
        }
    }

    private int HitTestCaretIndex(HtmlTextInputState state, float pointerX, float pointerY)
    {
        var localX = Math.Max(0, pointerX - (state.Left + state.PaddingLeft));
        var localY = Math.Max(0, pointerY - (state.Top + state.PaddingTop));
        return textServices.HitTestCaretIndex(
            CreateTextInputTextStyle(state),
            state.Text,
            state.LineHeight,
            Math.Max(0, state.Width - state.PaddingLeft - state.PaddingRight),
            localX,
            localY);
    }

    private void RemoveStaleInputStates(HashSet<SceneNodeId> activeInputIds)
    {
        staleInputIdScratch.Clear();
        foreach (var inputId in inputStates.Keys)
        {
            if (activeInputIds.Contains(inputId))
                continue;

            staleInputIdScratch.Add(inputId);
        }

        if (staleInputIdScratch.Count > 0)
        {
            for (var index = 0; index < staleInputIdScratch.Count; index++)
                inputStates.Remove(staleInputIdScratch[index]);
        }

        if (focusedInputId is { } focusedId && !activeInputIds.Contains(focusedId))
            focusedInputId = null;
    }

    private void RemoveStaleScrollStates(HashSet<SceneNodeId> activeScrollIds)
    {
        staleScrollIdScratch.Clear();
        foreach (var scrollId in scrollStates.Keys)
        {
            if (activeScrollIds.Contains(scrollId))
                continue;

            staleScrollIdScratch.Add(scrollId);
        }

        if (staleScrollIdScratch.Count == 0)
            return;

        for (var index = 0; index < staleScrollIdScratch.Count; index++)
            scrollStates.Remove(staleScrollIdScratch[index]);
    }

    private HtmlScrollViewState EnsureScrollState(SceneNodeId scrollViewId, SceneLayoutBox box)
    {
        if (scrollStates.TryGetValue(scrollViewId, out var state))
            return state;

        state = new HtmlScrollViewState(box.ScrollX, box.ScrollY);
        scrollStates[scrollViewId] = state;
        return state;
    }

    private SceneNodeId? ResolveWheelScrollView(SceneLayoutCommit commit, float x, float y, float deltaX, float deltaY)
    {
        if (wheelScrollTargetLatch.TryUseActive(CurrentElapsedMs(), out var activeId) &&
            commit.Layout.ContainsKey(activeId))
        {
            return activeId;
        }

        if (FindScrollViewAtPoint(commit, x, y, deltaX, deltaY) is { } foundId)
            return wheelScrollTargetLatch.SetActive(foundId);

        return wheelScrollTargetLatch.ClearActiveTarget();
    }

    private SceneNodeId? FindScrollViewAtPoint(SceneLayoutCommit commit, float x, float y, float deltaX, float deltaY)
    {
        var hitTestIndex = GetHitTestIndex(commit);
        var candidateIndexes = hitTestIndex.Query(x, y, HtmlHitTestChannel.ScrollView);
        for (var candidateIndex = candidateIndexes.Count - 1; candidateIndex >= 0; candidateIndex--)
        {
            var entry = hitTestIndex.Entries[candidateIndexes[candidateIndex]];
            if (entry.Box.NodeKind != SceneNodeKind.ScrollView)
                continue;

            if (!entry.ScreenRect.Contains(x, y))
                continue;

            var state = EnsureScrollState(entry.SceneNodeId, WithScreenRect(entry.Box, entry.ScreenRect));
            if (!SceneScrollMetrics.CanScrollBy(
                state.TargetScrollX,
                state.TargetScrollY,
                entry.Box.Width,
                entry.Box.Height,
                entry.Box.ContentWidth,
                entry.Box.ContentHeight,
                entry.Box.HorizontalScrollEnabled,
                deltaX,
                deltaY,
                WheelScrollFactor))
                continue;

            return entry.SceneNodeId;
        }

        return null;
    }

    private bool TryBeginScrollBarDrag(float x, float y)
    {
        if (cachedCommit is null)
            return false;

        var hitTestIndex = GetHitTestIndex(cachedCommit);
        var candidateIndexes = hitTestIndex.Query(x, y, HtmlHitTestChannel.ScrollView);
        for (var candidateIndex = candidateIndexes.Count - 1; candidateIndex >= 0; candidateIndex--)
        {
            var entry = hitTestIndex.Entries[candidateIndexes[candidateIndex]];
            if (entry.Box.NodeKind != SceneNodeKind.ScrollView)
                continue;

            var screenBox = WithScreenRect(entry.Box, entry.ScreenRect);
            var currentState = EnsureScrollState(entry.SceneNodeId, screenBox);
            screenBox = screenBox with
            {
                ScrollX = SceneScrollMetrics.ClampScrollX(screenBox, currentState.ScrollX),
                ScrollY = SceneScrollMetrics.ClampScrollY(screenBox, currentState.ScrollY)
            };

            if (SceneScrollBarDragController.TryHitThumb(screenBox, x, y, out var axis, out var grabOffset))
            {
                activeScrollBarDrag.Begin(entry.SceneNodeId, axis, grabOffset);
                return true;
            }
        }

        return false;
    }

    private bool UpdateActiveScrollBarDrag(float pointerX, float pointerY)
    {
        if (!activeScrollBarDrag.HasScrollViewId ||
            cachedCommit is null ||
            !cachedCommit.Layout.TryGetValue(activeScrollBarDrag.ScrollViewId, out var box))
        {
            return false;
        }

        var scrollViewId = activeScrollBarDrag.ScrollViewId;
        var screenBox = SceneScreenGeometry.ResolveScreenBox(cachedCommit, cachedCommit.Layout, scrollViewId, box);
        var state = EnsureScrollState(scrollViewId, screenBox);
        screenBox = screenBox with
        {
            ScrollX = SceneScrollMetrics.ClampScrollX(screenBox, state.ScrollX),
            ScrollY = SceneScrollMetrics.ClampScrollY(screenBox, state.ScrollY)
        };

        if (SceneScrollBarDragController.TryUpdate(activeScrollBarDrag, screenBox, state, pointerX, pointerY))
        {
            dirtyScrollViewIds.Add(scrollViewId);
            return true;
        }

        return false;
    }

    private bool EndActiveScrollBarDrag()
    {
        return activeScrollBarDrag.Clear();
    }

    private void UpdateHoveredNodeId(float x, float y)
    {
        if (cachedCommit is not null)
            UpdateHoveredNodeId(cachedCommit, x, y, requestUpdate: true);
    }

    private bool UpdateHoveredNodeId(SceneLayoutCommit commit, float x, float y, bool requestUpdate)
    {
        if (hoverRefreshDeferred)
        {
            cachedHitTestCommit = null;
            cachedHitTestGeometryVersion = ulong.MaxValue;
        }

        TryHitTestDomNode(commit, x, y, out var nextHoveredDomNodeIds);
        if (SameDomNodeSet(hoveredDomNodeIds, nextHoveredDomNodeIds))
        {
            if (!hoverRefreshDeferred &&
                !CanHoverAffectRendering(cachedHoveredDomNodeIds, nextHoveredDomNodeIds))
            {
                return false;
            }

            builder.ApplyHoverSnapshot(cachedHoveredDomNodeIds, nextHoveredDomNodeIds);
            if (requestUpdate)
                RequestHoverUpdate();
            hoveredDomNodeIds = nextHoveredDomNodeIds;
            hoverRefreshDeferred = false;
            return true;
        }

        var oldHoveredDomNodeIds = hoveredDomNodeIds;
        hoveredDomNodeIds = nextHoveredDomNodeIds;
        if (!CanHoverAffectRendering(oldHoveredDomNodeIds, hoveredDomNodeIds))
        {
            cachedHoveredDomNodeIds = hoveredDomNodeIds;
            hoverRefreshDeferred = false;
            return false;
        }

        builder.ApplyHoverSnapshot(oldHoveredDomNodeIds, hoveredDomNodeIds);
        hoverRefreshDeferred = false;
        if (requestUpdate)
            RequestHoverUpdate();
        return true;
    }

    private bool CanHoverAffectRendering(IReadOnlySet<HtmlNodeId>? oldHoveredNodeIds, IReadOnlySet<HtmlNodeId>? newHoveredNodeIds)
    {
        if (parsedDocument is null)
            return true;

        var oldHasRenderAffectingHover = ContainsRenderAffectingHoverNode(oldHoveredNodeIds, parsedDocument);
        var newHasRenderAffectingHover = ContainsRenderAffectingHoverNode(newHoveredNodeIds, parsedDocument);
        if (oldHasRenderAffectingHover || newHasRenderAffectingHover)
        {
            return !SameRenderAffectingHoverSet(oldHoveredNodeIds, newHoveredNodeIds, parsedDocument) ||
                   !SameRenderAffectingHoverSet(cachedHoveredDomNodeIds, newHoveredNodeIds, parsedDocument);
        }

        return parsedDocument.HasHoverDependencies || DocumentHasDefaultHoverControls();
    }

    private bool SameRenderAffectingHoverSet(
        IReadOnlySet<HtmlNodeId>? oldHoveredNodeIds,
        IReadOnlySet<HtmlNodeId>? newHoveredNodeIds,
        HtmlParsedDocument parsed)
    {
        if (oldHoveredNodeIds is null || oldHoveredNodeIds.Count == 0)
            return !ContainsRenderAffectingHoverNode(newHoveredNodeIds, parsed);
        if (newHoveredNodeIds is null || newHoveredNodeIds.Count == 0)
            return !ContainsRenderAffectingHoverNode(oldHoveredNodeIds, parsed);

        foreach (var nodeId in oldHoveredNodeIds)
        {
            if (!IsRenderAffectingHoverNode(nodeId, parsed))
                continue;

            if (!newHoveredNodeIds.Contains(nodeId))
                return false;
        }

        foreach (var nodeId in newHoveredNodeIds)
        {
            if (!IsRenderAffectingHoverNode(nodeId, parsed))
                continue;

            if (!oldHoveredNodeIds.Contains(nodeId))
                return false;
        }

        return true;
    }

    private bool ContainsRenderAffectingHoverNode(IReadOnlySet<HtmlNodeId>? nodeIds, HtmlParsedDocument parsed)
    {
        if (nodeIds is null)
            return false;

        foreach (var nodeId in nodeIds)
            if (IsRenderAffectingHoverNode(nodeId, parsed))
                return true;

        return false;
    }

    private bool IsRenderAffectingHoverNode(HtmlNodeId nodeId, HtmlParsedDocument parsed)
    {
        if (!cachedDomElements.TryGetValue(nodeId, out var element))
            return true;

        return string.Equals(element.LocalName, "button", StringComparison.OrdinalIgnoreCase) ||
               parsed.CanHoverAffectElement(element);
    }

    private bool DocumentHasDefaultHoverControls()
    {
        foreach (var element in cachedDomElements.Values)
            if (string.Equals(element.LocalName, "button", StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private IReadOnlySet<HtmlNodeId>? ResolveDomNodePath(HtmlNodeId domNodeId, bool includeAncestors = true)
    {
        if (!domNodeId.IsValid)
            return null;

        var ids = new HashSet<HtmlNodeId> { domNodeId };
        if (!includeAncestors)
            return ids;

        var currentDomNodeId = domNodeId;
        while (cachedDomNodeParentIds.TryGetValue(currentDomNodeId, out var parentDomNodeId) &&
               parentDomNodeId.IsValid)
        {
            if (!ids.Add(parentDomNodeId))
                break;

            currentDomNodeId = parentDomNodeId;
        }

        return ids;
    }

    private bool TryApplyHoverPaintOverlay(
        HtmlParsedDocument parsed,
        SceneLayoutCommit commit,
        int width,
        int height,
        out SceneLayoutCommit overlayCommit)
    {
        overlayCommit = commit;
        hoverPaintDirtyRects.Clear();

        var changedHoverLinks = CollectChangedHoverLinks(parsed, width, height);
        var updatedLayout = SceneNodeMap<SceneLayoutBox>.CreateOverlay(commit.Layout, Math.Min(64, changedHoverLinks.Count * 4));
        var changed = false;
        foreach (var pair in commit.Layout)
        {
            var sceneNodeId = pair.Key;
            var box = pair.Value;
            if (box.NodeKind != SceneNodeKind.Text ||
                box.TextStyle is null ||
                string.IsNullOrWhiteSpace(box.LinkHref) ||
                !TryResolveNearestDomNodeId(commit, sceneNodeId, out var domNodeId) ||
                !TryFindHoverLinkTarget(domNodeId, box.LinkHref, changedHoverLinks, out var linkNodeId, out var hoverColor))
            {
                continue;
            }

            var targetColor = hoveredDomNodeIds?.Contains(linkNodeId) == true
                ? hoverColor
                : cachedBaseCommit?.Layout.TryGetValue(sceneNodeId, out var baseBox) == true
                    ? baseBox.TextStyle?.Color
                    : box.TextStyle.Color;
            if (string.Equals(box.TextStyle.Color, targetColor, StringComparison.Ordinal))
                continue;

            var updatedTextStyle = box.TextStyle with { Color = targetColor };
            updatedLayout[sceneNodeId] = box with { TextStyle = updatedTextStyle };
            AddHoverPaintDirtyRect(commit, sceneNodeId, box);
            changed = true;
        }

        if (!TryApplyHoverBackgroundOverlay(parsed, commit, updatedLayout, width, height, ref changed))
            return false;

        if (!changed)
            return false;

        overlayCommit = commit with { Layout = updatedLayout };
        return true;
    }

    private bool TryApplyHoverBackgroundOverlay(
        HtmlParsedDocument parsed,
        SceneLayoutCommit commit,
        SceneNodeMap<SceneLayoutBox> updatedLayout,
        int width,
        int height,
        ref bool changed)
    {
        foreach (var pair in commit.Layout)
        {
            var sceneNodeId = pair.Key;
            var box = pair.Value;
            if (!TryResolveNearestDomNodeId(commit, sceneNodeId, out var domNodeId) ||
                !cachedDomElements.TryGetValue(domNodeId, out var element) ||
                !TryBuildAncestorStyleContext(domNodeId, out var ancestors, out var hoverStates))
            {
                continue;
            }

            var isHovered = hoveredDomNodeIds?.Contains(domNodeId) == true;
            if (!parsed.TryResolvePaintOnlyHoveredBackgroundColor(element, ancestors, hoverStates, isHovered, width, height, out var matched, out var hoverColor))
                return false;

            var targetColor = matched
                ? hoverColor
                : cachedBaseCommit?.Layout.TryGetValue(sceneNodeId, out var baseBox) == true
                    ? baseBox.BackgroundColor
                    : box.BackgroundColor;
            if (string.Equals(box.BackgroundColor, targetColor, StringComparison.Ordinal))
                continue;

            updatedLayout[sceneNodeId] = box with { BackgroundColor = targetColor };
            AddHoverPaintDirtyRect(commit, sceneNodeId, box);
            changed = true;
        }

        return true;
    }

    private Dictionary<HtmlNodeId, HoverLinkPaint> CollectChangedHoverLinks(HtmlParsedDocument parsed, int width, int height)
    {
        var links = new Dictionary<HtmlNodeId, HoverLinkPaint>();
        AddChangedHoverLinks(parsed, cachedHoveredDomNodeIds, hoveredDomNodeIds, width, height, links);
        AddChangedHoverLinks(parsed, hoveredDomNodeIds, cachedHoveredDomNodeIds, width, height, links);
        return links;
    }

    private void AddChangedHoverLinks(
        HtmlParsedDocument parsed,
        IReadOnlySet<HtmlNodeId>? source,
        IReadOnlySet<HtmlNodeId>? other,
        int width,
        int height,
        Dictionary<HtmlNodeId, HoverLinkPaint> links)
    {
        if (source is null)
            return;

        foreach (var nodeId in source)
        {
            if (other?.Contains(nodeId) == true ||
                links.ContainsKey(nodeId) ||
                !cachedDomElements.TryGetValue(nodeId, out var element) ||
                !string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase) ||
                !TryBuildAncestorStyleContext(nodeId, out var ancestors, out var hoverStates) ||
                !parsed.TryResolvePaintOnlyHoveredTextColor(element, ancestors, hoverStates, width, height, out var color) ||
                string.IsNullOrWhiteSpace(color))
            {
                continue;
            }

            links[nodeId] = new HoverLinkPaint(
                HtmlUrlResolver.Resolve(element.GetAttribute("href"), parsed.BasePath),
                color);
        }
    }

    private bool TryBuildAncestorStyleContext(
        HtmlNodeId nodeId,
        out List<HtmlDomElement> ancestors,
        out List<bool> hoverStates)
    {
        ancestors = [];
        hoverStates = [];
        var chain = new List<HtmlDomElement>();
        var hoverChain = new List<bool>();
        var current = nodeId;
        while (cachedDomNodeParentIds.TryGetValue(current, out var parentId) && parentId.IsValid)
        {
            if (!cachedDomElements.TryGetValue(parentId, out var parent))
                return false;

            chain.Add(parent);
            hoverChain.Add(hoveredDomNodeIds?.Contains(parentId) == true);
            current = parentId;
        }

        for (var index = chain.Count - 1; index >= 0; index--)
        {
            ancestors.Add(chain[index]);
            hoverStates.Add(hoverChain[index]);
        }

        return true;
    }

    private bool TryFindHoverLinkTarget(
        HtmlNodeId nodeId,
        string linkHref,
        IReadOnlyDictionary<HtmlNodeId, HoverLinkPaint> changedHoverLinks,
        out HtmlNodeId linkNodeId,
        out string hoverColor)
    {
        var current = nodeId;
        while (current.IsValid)
        {
            if (changedHoverLinks.TryGetValue(current, out var paint))
            {
                linkNodeId = current;
                hoverColor = paint.HoverColor;
                return true;
            }

            if (!cachedDomNodeParentIds.TryGetValue(current, out current))
                break;
        }

        foreach (var pair in changedHoverLinks)
        {
            if (!string.Equals(pair.Value.LinkHref, linkHref, StringComparison.Ordinal))
                continue;

            linkNodeId = pair.Key;
            hoverColor = pair.Value.HoverColor;
            return true;
        }

        linkNodeId = default;
        hoverColor = string.Empty;
        return false;
    }

    private void AddHoverPaintDirtyRect(SceneLayoutCommit commit, SceneNodeId sceneNodeId, SceneLayoutBox box)
    {
        var screenBox = SceneScreenGeometry.ResolveScreenBox(commit, commit.Layout, sceneNodeId, box);
        hoverPaintDirtyRects.Add(new SceneDamageRect(
            Math.Max(0, (int)MathF.Floor(screenBox.AbsLeft)),
            Math.Max(0, (int)MathF.Floor(screenBox.AbsTop)),
            Math.Max(1, (int)MathF.Ceiling(screenBox.Width)),
            Math.Max(1, (int)MathF.Ceiling(screenBox.Height))));
    }

    private readonly record struct HoverLinkPaint(string? LinkHref, string HoverColor);

    private bool TryResolveNearestDomNodeId(SceneLayoutCommit commit, SceneNodeId sceneNodeId, out HtmlNodeId domNodeId)
    {
        var currentId = sceneNodeId;
        while (currentId.IsValid)
        {
            if (cachedSceneNodeDomIds.TryGetValue(currentId, out domNodeId) && domNodeId.IsValid)
                return true;

            if (!commit.Nodes.TryGetValue(currentId, out var node) || node.ParentId is not { } parentId)
                break;

            currentId = parentId;
        }

        domNodeId = default;
        return false;
    }

    private static bool SameDomNodeSet(IReadOnlySet<HtmlNodeId>? left, IReadOnlySet<HtmlNodeId>? right)
    {
        if (left is null || left.Count == 0)
            return right is null || right.Count == 0;
        if (right is null || right.Count == 0 || left.Count != right.Count)
            return false;
        foreach (var id in left)
        {
            if (!right.Contains(id))
                return false;
        }

        return true;
    }

    private void UpdatePointerCursor()
    {
        if (cachedCommit is null)
        {
            currentCursor = PointerCursorKind.Default;
            return;
        }

        var nextCursor =
            TryHitTestLink(cachedCommit, lastPointerX, lastPointerY, out _, out _)
                ? PointerCursorKind.Pointer
                : TryHitOpenSelectOption(lastPointerX, lastPointerY, out _, out _) ||
                  TryHitTestSelect(cachedCommit, lastPointerX, lastPointerY, out _, out _)
                    ? PointerCursorKind.Pointer
                : TryHitTestTextInput(cachedCommit, lastPointerX, lastPointerY, out _, out _)
                    ? PointerCursorKind.Text
                    : PointerCursorKind.Default;
        currentCursor = nextCursor;
    }

    private bool TryHitTestDomNode(
        SceneLayoutCommit commit,
        float x,
        float y,
        out IReadOnlySet<HtmlNodeId>? domNodeIds,
        bool includeAncestors = true)
    {
        if (TryHitTestLink(commit, x, y, out var linkNodeId, out _, out _))
        {
            if (TryResolveNearestDomNodeId(commit, linkNodeId, out var linkDomNodeId))
            {
                domNodeIds = ResolveDomNodePath(linkDomNodeId, includeAncestors);
                return true;
            }
        }

        var hitTestIndex = GetHitTestIndex(commit);
        var candidateIndexes = hitTestIndex.Query(x, y, HtmlHitTestChannel.DomHover);
        HtmlNodeId bestDomNodeId = default;
        SceneNodeId bestSceneNodeId = default;
        var bestDomDepth = -1;
        SceneScreenBounds bestBounds = default;
        var bestZOrder = -1;
        for (var candidateIndex = candidateIndexes.Count - 1; candidateIndex >= 0; candidateIndex--)
        {
            var entry = hitTestIndex.Entries[candidateIndexes[candidateIndex]];
            if (!entry.DomNodeId.IsValid ||
                !entry.ScreenRect.Contains(x, y) ||
                !IsPointVisibleThroughScrollAncestors(commit, entry.SceneNodeId, x, y))
            {
                continue;
            }

            if (!bestDomNodeId.IsValid ||
                entry.DomDepth > bestDomDepth ||
                (entry.DomDepth == bestDomDepth && SceneScreenBounds.IsHigherPriority(entry.Bounds, entry.ZOrder, bestBounds, bestZOrder)))
            {
                bestDomNodeId = entry.DomNodeId;
                bestSceneNodeId = entry.SceneNodeId;
                bestDomDepth = entry.DomDepth;
                bestBounds = entry.Bounds;
                bestZOrder = entry.ZOrder;
            }
        }

        if (bestDomNodeId.IsValid)
        {
            domNodeIds = ResolveDomNodePath(commit, bestSceneNodeId, bestDomNodeId, includeAncestors);
            return true;
        }

        domNodeIds = null;
        return false;
    }

    private IReadOnlySet<HtmlNodeId>? ResolveDomNodePath(
        SceneLayoutCommit commit,
        SceneNodeId sceneNodeId,
        HtmlNodeId domNodeId,
        bool includeAncestors)
    {
        var ids = ResolveDomNodePath(domNodeId, includeAncestors);
        if (!includeAncestors || ids is null)
            return ids;

        HashSet<HtmlNodeId>? expanded = null;
        var currentSceneNodeId = sceneNodeId;
        while (commit.Nodes.TryGetValue(currentSceneNodeId, out var node) && node.ParentId is { } parentId)
        {
            if (TryResolveNearestDomNodeId(commit, parentId, out var ancestorDomNodeId) &&
                ancestorDomNodeId.IsValid &&
                !ids.Contains(ancestorDomNodeId))
            {
                expanded ??= new HashSet<HtmlNodeId>(ids);
                expanded.Add(ancestorDomNodeId);
            }

            currentSceneNodeId = parentId;
        }

        return expanded ?? ids;
    }

    private bool TryHitTestLink(SceneLayoutCommit commit, float x, float y, out SceneNodeId nodeId, out string href)
        => TryHitTestLink(commit, x, y, out nodeId, out href, out _);

    private bool TryHitTestLink(SceneLayoutCommit commit, float x, float y, out SceneNodeId nodeId, out string href, out SceneNodeId hitNodeId)
    {
        var hitTestIndex = GetHitTestIndex(commit);
        var candidateIndexes = hitTestIndex.Query(x, y, HtmlHitTestChannel.Link);
        for (var candidateIndex = candidateIndexes.Count - 1; candidateIndex >= 0; candidateIndex--)
        {
            var entry = hitTestIndex.Entries[candidateIndexes[candidateIndex]];
            if (string.IsNullOrWhiteSpace(entry.Box.LinkHref))
                continue;

            if (!entry.ScreenRect.Contains(x, y) ||
                !IsPointVisibleThroughScrollAncestors(commit, entry.SceneNodeId, x, y))
            {
                continue;
            }

            nodeId = ResolveLinkNodeId(commit, entry.SceneNodeId, entry.Box.LinkHref);
            href = entry.Box.LinkHref;
            hitNodeId = entry.SceneNodeId;
            return true;
        }

        nodeId = default;
        href = string.Empty;
        hitNodeId = default;
        return false;
    }

    private static SceneNodeId ResolveLinkNodeId(SceneLayoutCommit commit, SceneNodeId id, string href)
    {
        var currentId = id;
        while (commit.Nodes.TryGetValue(currentId, out var node) &&
               node.ParentId is { } parentId &&
               commit.Layout.TryGetValue(parentId, out var parentBox) &&
               string.Equals(parentBox.LinkHref, href, StringComparison.Ordinal))
        {
            currentId = parentId;
        }

        if (commit.Nodes.TryGetValue(currentId, out var currentNode) &&
            currentNode.ParentId is { } siblingParentId &&
            commit.Nodes.TryGetValue(siblingParentId, out var siblingParent))
        {
            var currentIndex = -1;
            for (var index = 0; index < siblingParent.Children.Length; index++)
            {
                if (siblingParent.Children[index] == currentId)
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex >= 0)
            {
                var firstLinkSiblingIndex = currentIndex;
                for (var index = currentIndex - 1; index >= 0; index--)
                {
                    var siblingId = siblingParent.Children[index];
                    if (!commit.Layout.TryGetValue(siblingId, out var siblingBox) ||
                        !string.Equals(siblingBox.LinkHref, href, StringComparison.Ordinal))
                    {
                        break;
                    }

                    firstLinkSiblingIndex = index;
                }

                return siblingParent.Children[firstLinkSiblingIndex];
            }
        }

        return currentId;
    }

    private static bool IsPointVisibleThroughScrollAncestors(SceneLayoutCommit commit, SceneNodeId nodeId, float x, float y)
    {
        var currentId = nodeId;
        while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
        {
            if (commit.Layout.TryGetValue(parentId, out var parentBox) &&
                parentBox.NodeKind == SceneNodeKind.ScrollView)
            {
                var scrollViewport = SceneScreenGeometry.ResolveScreenBox(commit, commit.Layout, parentId, parentBox);
                if (!ContainsPoint(scrollViewport, x, y))
                    return false;
            }

            currentId = parentId;
        }

        return true;
    }

    private static bool ContainsPoint(SceneLayoutBox box, float x, float y)
        => x >= box.AbsLeft &&
           x <= box.AbsLeft + box.Width &&
           y >= box.AbsTop &&
           y <= box.AbsTop + box.Height;

    private bool IsSelectSceneNode(SceneLayoutCommit commit, SceneNodeId sceneNodeId)
        => TryResolveNearestDomNodeId(commit, sceneNodeId, out var domNodeId) &&
           TryResolveDomElement(domNodeId, out var element) &&
           IsSelectElement(element);

    private static bool IsSelectElement(HtmlDomElement element)
        => string.Equals(element.LocalName, "select", StringComparison.OrdinalIgnoreCase);

    private HtmlHitTestSpatialIndex GetHitTestIndex(SceneLayoutCommit commit)
    {
        if (ReferenceEquals(cachedHitTestCommit, commit))
            return cachedHitTestIndex ?? HtmlHitTestSpatialIndex.Empty;
        if (cachedHitTestIndex is not null &&
            cachedHitTestGeometryVersion == hitTestGeometryVersion)
        {
            cachedHitTestCommit = commit;
            return cachedHitTestIndex;
        }

        hitTestEntryScratch.Clear();
        if (cachedBaseFragmentTree is { } fragmentTree)
            AddHitTestEntriesFromFragments(commit, fragmentTree, hitTestEntryScratch);
        else
            AddHitTestEntriesFromLayout(commit, hitTestEntryScratch);

        cachedHitTestCommit = commit;
        cachedHitTestIndex ??= new HtmlHitTestSpatialIndex();
        cachedHitTestIndex.Rebuild(commit.Viewport.Width, commit.Viewport.Height, hitTestEntryScratch);
        cachedHitTestGeometryVersion = hitTestGeometryVersion;
        return cachedHitTestIndex;
    }

    private void AddHitTestEntriesFromLayout(SceneLayoutCommit commit, List<HtmlHitTestEntry> entries)
    {
        if (commit.PaintOrderIds.Length > 0)
        {
            for (var index = 0; index < commit.PaintOrderIds.Length; index++)
                AddHitTestEntryFromLayout(commit, commit.PaintOrderIds[index], index, entries);
            return;
        }

        var zOrder = 0;
        foreach (var (id, _) in commit.Layout)
            AddHitTestEntryFromLayout(commit, id, zOrder++, entries);
    }

    private void AddHitTestEntryFromLayout(
        SceneLayoutCommit commit,
        SceneNodeId id,
        int zOrder,
        List<HtmlHitTestEntry> entries)
    {
        if (!commit.Layout.TryGetValue(id, out var box))
            return;

        if (!SceneScreenGeometry.TryGetNodeScreenBounds(commit, commit.Layout, id, out var bounds))
            bounds = new SceneScreenBounds(box.AbsLeft, box.AbsTop, box.AbsLeft + box.Width, box.AbsTop + box.Height, 0);

        TryResolveNearestDomNodeId(commit, id, out var domNodeId);
        var domDepth = domNodeId.IsValid && cachedDomNodeDepths.TryGetValue(domNodeId, out var depth)
            ? depth
            : -1;
        entries.Add(new HtmlHitTestEntry(id, box, new HtmlHitTestRect(bounds.Left, bounds.Top, box.Width, box.Height), bounds, domNodeId, domDepth, zOrder));
    }

    private void AddHitTestEntriesFromFragments(
        SceneLayoutCommit commit,
        HtmlFragmentTree fragmentTree,
        List<HtmlHitTestEntry> entries)
    {
        FillPaintOrderIndex(commit, hitTestPaintOrderIndexScratch);
        var fragments = fragmentTree.OrderedFragments;
        for (var fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
        {
            var fragment = fragments[fragmentIndex];
            var id = fragment.SceneNodeId;
            if (!id.IsValid)
                continue;
            if (!commit.Layout.TryGetValue(id, out var box))
                continue;

            var screenRect = ResolveFragmentScreenRect(commit, id, box, fragment.BorderBox);
            var bounds = ResolveFragmentScreenBounds(commit, id, box, fragment.VisualOverflow);
            var domNodeId = ResolveFragmentDomNodeId(commit, fragment, id);
            var domDepth = domNodeId.IsValid && cachedDomNodeDepths.TryGetValue(domNodeId, out var depth)
                ? depth
                : -1;
            var zOrder = hitTestPaintOrderIndexScratch.TryGetValue(id, out var paintOrderIndex)
                ? paintOrderIndex
                : entries.Count;
            entries.Add(new HtmlHitTestEntry(id, box, screenRect, bounds, domNodeId, domDepth, zOrder));
        }
    }

    private static void FillPaintOrderIndex(SceneLayoutCommit commit, Dictionary<SceneNodeId, int> indexes)
    {
        indexes.Clear();
        if (commit.PaintOrderIds.Length > 0)
        {
            for (var index = 0; index < commit.PaintOrderIds.Length; index++)
                indexes[commit.PaintOrderIds[index]] = index;
            return;
        }

        var zOrder = 0;
        foreach (var (id, _) in commit.Layout)
            indexes[id] = zOrder++;
    }

    private HtmlNodeId ResolveFragmentDomNodeId(SceneLayoutCommit commit, HtmlFragment fragment, SceneNodeId sceneNodeId)
    {
        if (fragment.SceneNodeId.IsValid &&
            cachedSceneNodeDomIds.TryGetValue(fragment.SceneNodeId, out var placedDomNodeId) &&
            placedDomNodeId.IsValid)
        {
            return placedDomNodeId;
        }

        return TryResolveNearestDomNodeId(commit, sceneNodeId, out var domNodeId)
            ? domNodeId
            : default;
    }

    private static HtmlHitTestRect ResolveFragmentScreenRect(
        SceneLayoutCommit commit,
        SceneNodeId sceneNodeId,
        SceneLayoutBox box,
        HtmlLayoutRect rect)
    {
        if (!SceneScreenGeometry.TryGetNodeScreenBounds(commit, commit.Layout, sceneNodeId, out var bounds))
            return new HtmlHitTestRect(rect.Left, rect.Top, rect.Width, rect.Height);

        return new HtmlHitTestRect(rect.Left + bounds.Left - box.AbsLeft, rect.Top + bounds.Top - box.AbsTop, rect.Width, rect.Height);
    }

    private static SceneScreenBounds ResolveFragmentScreenBounds(
        SceneLayoutCommit commit,
        SceneNodeId sceneNodeId,
        SceneLayoutBox box,
        HtmlLayoutRect rect)
    {
        if (!SceneScreenGeometry.TryGetNodeScreenBounds(commit, commit.Layout, sceneNodeId, out var bounds))
            bounds = new SceneScreenBounds(box.AbsLeft, box.AbsTop, box.AbsLeft + box.Width, box.AbsTop + box.Height, 0);

        var dx = bounds.Left - box.AbsLeft;
        var dy = bounds.Top - box.AbsTop;
        return new SceneScreenBounds(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy, bounds.Depth);
    }

    private static SceneLayoutBox WithScreenRect(SceneLayoutBox box, HtmlHitTestRect rect)
        => box with
        {
            AbsLeft = rect.Left,
            AbsTop = rect.Top,
            Width = rect.Width,
            Height = rect.Height
        };

    private bool IsDoubleClick(SceneNodeId inputId, float x, float y)
    {
        if (lastPrimaryClickInputId != inputId)
            return false;

        if (lastPrimaryClickTimestamp == 0)
            return false;

        var elapsed = TimeSource.GetElapsedTime(lastPrimaryClickTimestamp).TotalMilliseconds;
        if (elapsed > DoubleClickThresholdMs)
            return false;

        var dx = x - lastPrimaryClickX;
        var dy = y - lastPrimaryClickY;
        return dx * dx + dy * dy <= DoubleClickThresholdPx * DoubleClickThresholdPx;
    }

    private void RememberPrimaryClick(SceneNodeId inputId, float x, float y)
    {
        lastPrimaryClickInputId = inputId;
        lastPrimaryClickX = x;
        lastPrimaryClickY = y;
        lastPrimaryClickTimestamp = TimeSource.GetTimestamp();
    }

    private double CurrentElapsedMs()
        => TimeSource.GetElapsedTime(0).TotalMilliseconds;

    private void RequestInteractiveUpdate()
    {
        Invalidate(HtmlPipelineInvalidation.Interactive | HtmlPipelineInvalidation.HitTest, HtmlRenderDamageBits.Interactive);
        RenderWakeRequested?.Invoke();
    }

    private void RequestHoverUpdate()
    {
        Invalidate(HtmlPipelineInvalidation.Hover | HtmlPipelineInvalidation.HitTest, HtmlRenderDamageBits.DirtyRects);
        RenderWakeRequested?.Invoke();
    }

    private static SceneTextStyle CreateTextInputTextStyle(HtmlTextInputState state)
        => new(state.FontSize, state.Color, state.FontFamily, state.FontWeight, state.TextAlign, state.Multiline);

    private static string RemoveLineBreaks(string text)
    {
        var firstLineBreak = text.AsSpan().IndexOfAny('\r', '\n');
        if (firstLineBreak < 0)
            return text;

        return string.Create(text.Length - CountLineBreaks(text.AsSpan()), text, static (destination, source) =>
        {
            var writeIndex = 0;
            foreach (var ch in source)
            {
                if (ch is '\r' or '\n')
                    continue;

                destination[writeIndex++] = ch;
            }
        });
    }

    private static int CountLineBreaks(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n')
                count++;
        }

        return count;
    }

    private sealed class HtmlScrollViewState : ISceneScrollOffsetState
    {
        public HtmlScrollViewState(float scrollX, float scrollY)
        {
            ScrollX = scrollX;
            ScrollY = scrollY;
            TargetScrollX = scrollX;
            TargetScrollY = scrollY;
        }

        public float ScrollX { get; set; }
        public float ScrollY { get; set; }
        public float TargetScrollX { get; set; }
        public float TargetScrollY { get; set; }
    }

    private readonly record struct HtmlHitTestEntry(
        SceneNodeId SceneNodeId,
        SceneLayoutBox Box,
        HtmlHitTestRect ScreenRect,
        SceneScreenBounds Bounds,
        HtmlNodeId DomNodeId,
        int DomDepth,
        int ZOrder);

    private readonly record struct HtmlHitTestRect(float Left, float Top, float Width, float Height)
    {
        public float Right => Left + Width;

        public float Bottom => Top + Height;

        public bool Contains(float x, float y)
            => x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    [Flags]
    private enum HtmlHitTestChannel : byte
    {
        None = 0,
        DomHover = 1 << 0,
        Link = 1 << 1,
        TextInput = 1 << 2,
        ScrollView = 1 << 3
    }

    private sealed class HtmlHitTestSpatialIndex
    {
        private const float CellSize = 96f;
        private static readonly HtmlHitTestCell EmptyCell = new();
        private HtmlHitTestCell[] domHoverCells = [new HtmlHitTestCell()];
        private HtmlHitTestCell[] linkCells = [new HtmlHitTestCell()];
        private HtmlHitTestCell[] textInputCells = [new HtmlHitTestCell()];
        private HtmlHitTestCell[] scrollViewCells = [new HtmlHitTestCell()];
        private int columns = 1;
        private int rows = 1;
        private int viewportWidth = 1;
        private int viewportHeight = 1;

        public static HtmlHitTestSpatialIndex Empty { get; } = new();

        public HtmlHitTestEntry[] Entries { get; private set; } = [];

        public int EntryCount { get; private set; }

        public void Rebuild(int nextViewportWidth, int nextViewportHeight, List<HtmlHitTestEntry> entries)
        {
            viewportWidth = Math.Max(1, nextViewportWidth);
            viewportHeight = Math.Max(1, nextViewportHeight);
            var nextColumns = Math.Max(1, (int)MathF.Ceiling(viewportWidth / CellSize));
            var nextRows = Math.Max(1, (int)MathF.Ceiling(viewportHeight / CellSize));
            EnsureGrid(nextColumns, nextRows);
            ClearCells(domHoverCells);
            ClearCells(linkCells);
            ClearCells(textInputCells);
            ClearCells(scrollViewCells);
            EnsureEntryCapacity(entries.Count);
            EntryCount = entries.Count;
            for (var index = 0; index < entries.Count; index++)
                Entries[index] = entries[index];

            for (var entryIndex = 0; entryIndex < EntryCount; entryIndex++)
            {
                var box = entries[entryIndex].ScreenRect;
                if (box.Width <= 0 || box.Height <= 0)
                    continue;

                var left = box.Left;
                var top = box.Top;
                var right = box.Right;
                var bottom = box.Bottom;
                if (right < 0 || bottom < 0 || left > viewportWidth || top > viewportHeight)
                    continue;

                var minColumn = ClampCell(left, columns);
                var maxColumn = ClampCell(right, columns);
                var minRow = ClampCell(top, rows);
                var maxRow = ClampCell(bottom, rows);
                var channels = ResolveChannels(entries[entryIndex]);
                for (var row = minRow; row <= maxRow; row++)
                {
                    var rowOffset = row * columns;
                    for (var column = minColumn; column <= maxColumn; column++)
                    {
                        var cellIndex = rowOffset + column;
                        AddToChannel(domHoverCells, cellIndex, entryIndex, channels, HtmlHitTestChannel.DomHover);
                        AddToChannel(linkCells, cellIndex, entryIndex, channels, HtmlHitTestChannel.Link);
                        AddToChannel(textInputCells, cellIndex, entryIndex, channels, HtmlHitTestChannel.TextInput);
                        AddToChannel(scrollViewCells, cellIndex, entryIndex, channels, HtmlHitTestChannel.ScrollView);
                    }
                }
            }
        }

        public HtmlHitTestCell Query(float x, float y, HtmlHitTestChannel channel)
        {
            if (float.IsNaN(x) ||
                float.IsNaN(y) ||
                x < 0 ||
                y < 0 ||
                x > viewportWidth ||
                y > viewportHeight)
            {
                return EmptyCell;
            }

            var column = ClampCell(x, columns);
            var row = ClampCell(y, rows);
            var cellIndex = (row * columns) + column;
            return channel switch
            {
                HtmlHitTestChannel.DomHover => domHoverCells[cellIndex],
                HtmlHitTestChannel.Link => linkCells[cellIndex],
                HtmlHitTestChannel.TextInput => textInputCells[cellIndex],
                HtmlHitTestChannel.ScrollView => scrollViewCells[cellIndex],
                _ => EmptyCell
            };
        }

        private void EnsureGrid(int nextColumns, int nextRows)
        {
            var nextCellCount = nextColumns * nextRows;
            if (columns == nextColumns &&
                rows == nextRows &&
                domHoverCells.Length == nextCellCount)
            {
                return;
            }

            columns = nextColumns;
            rows = nextRows;
            domHoverCells = CreateCells(nextCellCount);
            linkCells = CreateCells(nextCellCount);
            textInputCells = CreateCells(nextCellCount);
            scrollViewCells = CreateCells(nextCellCount);
        }

        private void EnsureEntryCapacity(int count)
        {
            if (Entries.Length >= count)
                return;

            var capacity = Math.Max(count, Entries.Length == 0 ? 64 : Entries.Length * 2);
            var entries = Entries;
            Array.Resize(ref entries, capacity);
            Entries = entries;
        }

        private static HtmlHitTestCell[] CreateCells(int count)
        {
            var cells = new HtmlHitTestCell[count];
            for (var index = 0; index < cells.Length; index++)
                cells[index] = new HtmlHitTestCell();
            return cells;
        }

        private static void ClearCells(HtmlHitTestCell[] cells)
        {
            for (var index = 0; index < cells.Length; index++)
                cells[index].Clear();
        }

        private static HtmlHitTestChannel ResolveChannels(HtmlHitTestEntry entry)
        {
            var channels = entry.DomNodeId.IsValid
                ? HtmlHitTestChannel.DomHover
                : HtmlHitTestChannel.None;
            if (!string.IsNullOrWhiteSpace(entry.Box.LinkHref))
                channels |= HtmlHitTestChannel.Link;
            if (entry.Box.NodeKind == SceneNodeKind.TextInput)
                channels |= HtmlHitTestChannel.TextInput;
            if (entry.Box.NodeKind == SceneNodeKind.ScrollView)
                channels |= HtmlHitTestChannel.ScrollView;
            return channels;
        }

        private static void AddToChannel(
            HtmlHitTestCell[] cells,
            int cellIndex,
            int entryIndex,
            HtmlHitTestChannel entryChannels,
            HtmlHitTestChannel channel)
        {
            if ((entryChannels & channel) == 0)
                return;

            cells[cellIndex].Add(entryIndex);
        }

        private static int ClampCell(float value, int count)
            => Math.Clamp((int)MathF.Floor(value / CellSize), 0, count - 1);
    }

    private sealed class HtmlHitTestCell
    {
        private int[] indexes = [];

        public int Count { get; private set; }

        public int this[int index] => indexes[index];

        public void Clear() => Count = 0;

        public void Add(int entryIndex)
        {
            if (Count == indexes.Length)
            {
                var capacity = indexes.Length == 0 ? 4 : indexes.Length * 2;
                Array.Resize(ref indexes, capacity);
            }

            indexes[Count++] = entryIndex;
        }
    }

    private sealed class HtmlSelectState(HtmlNodeId nodeId)
    {
        private readonly List<HtmlSelectOption> options = [];
        private HtmlSelectOptionRect[] optionRects = [];

        public HtmlNodeId NodeId { get; } = nodeId;

        public IReadOnlyList<HtmlSelectOption> Options => options;

        public int SelectedIndex { get; private set; }

        public int HoveredIndex { get; private set; } = -1;

        public string SelectedText => options.Count == 0 ? string.Empty : options[Math.Clamp(SelectedIndex, 0, options.Count - 1)].Text;

        public void Refresh(HtmlDomElement element)
        {
            var previousValue = options.Count == 0 ? null : options[Math.Clamp(SelectedIndex, 0, options.Count - 1)].Value;
            options.Clear();
            CollectOptions(element, options);
            if (options.Count == 0)
            {
                SelectedIndex = 0;
                HoveredIndex = -1;
                optionRects = [];
                return;
            }

            var selected = -1;
            for (var index = 0; index < options.Count; index++)
            {
                if (options[index].Selected)
                {
                    selected = index;
                    break;
                }
            }

            if (selected < 0 && previousValue is not null)
            {
                for (var index = 0; index < options.Count; index++)
                {
                    if (string.Equals(options[index].Value, previousValue, StringComparison.Ordinal))
                    {
                        selected = index;
                        break;
                    }
                }
            }

            SelectedIndex = selected >= 0 ? selected : Math.Clamp(SelectedIndex, 0, options.Count - 1);
            if (HoveredIndex >= options.Count)
                HoveredIndex = -1;
        }

        public void Select(int index)
        {
            if (options.Count == 0)
            {
                SelectedIndex = 0;
                return;
            }

            SelectedIndex = Math.Clamp(index, 0, options.Count - 1);
            HoveredIndex = -1;
        }

        public bool SetHoveredIndex(int index)
        {
            var next = options.Count == 0 ? -1 : Math.Clamp(index, -1, options.Count - 1);
            if (HoveredIndex == next)
                return false;

            HoveredIndex = next;
            return true;
        }

        public void BeginPopupLayout()
        {
            if (optionRects.Length < options.Count)
                optionRects = new HtmlSelectOptionRect[options.Count];
            Array.Clear(optionRects, 0, optionRects.Length);
        }

        public void SetPopupOptionRect(int index, float left, float top, float width, float height)
        {
            if ((uint)index >= (uint)optionRects.Length)
                return;

            optionRects[index] = new HtmlSelectOptionRect(left, top, width, height);
        }

        public bool TryHitOption(float x, float y, out int index)
        {
            for (var i = 0; i < Math.Min(options.Count, optionRects.Length); i++)
            {
                if (optionRects[i].Contains(x, y))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private static void CollectOptions(HtmlDomElement element, List<HtmlSelectOption> result)
        {
            for (var index = 0; index < element.Children.Count; index++)
            {
                var child = element.Children[index];
                if (child is not HtmlDomElement childElement)
                    continue;

                if (string.Equals(childElement.LocalName, "option", StringComparison.OrdinalIgnoreCase))
                {
                    var text = childElement.InnerText;
                    result.Add(new HtmlSelectOption(
                        string.IsNullOrEmpty(text) ? childElement.TextContent : text,
                        childElement.GetAttribute("value") ?? text,
                        childElement.Attributes.ContainsKey("selected")));
                    continue;
                }

                CollectOptions(childElement, result);
            }
        }
    }

    private readonly record struct HtmlSelectOption(string Text, string Value, bool Selected);

    private readonly record struct HtmlSelectOptionRect(float Left, float Top, float Width, float Height)
    {
        public bool Contains(float x, float y)
            => x >= Left && x <= Left + Width && y >= Top && y <= Top + Height;
    }

    private readonly record struct ScrollScaleAnchor(SceneNodeId NodeId, float ScreenTop);

}

