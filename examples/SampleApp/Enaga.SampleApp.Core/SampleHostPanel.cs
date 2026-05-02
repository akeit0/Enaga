using Okojo.Annotations;
using Enaga.Rendering;
using SkiaSharp;
using Enaga.Rendering.Skia;

namespace Enaga.SampleApp;

[GenerateJsObject]
public sealed partial class SampleHostPanel : ILowLevelSkiaLayer
{
    private const int WordLength = 5;
    private const int MaxGuesses = 6;
    private static readonly string[] AnswerWords = ["REACT", "PIXEL", "FRAME", "PANEL", "STATE", "INPUT"];

    private readonly List<GuessRow> guesses = [];
    private SceneDamageRect effectivePanelRect;
    private SceneDamageRect effectiveVisibleRect;
    private SceneDamageRect panelRect;
    private int answerIndex;
    private string answer = AnswerWords[0];
    private string currentGuess = string.Empty;
    private string? hostNodeRuntimeId;
    private SKPicture? cachedPicture;
    private bool pictureDirty = true;
    private Func<string, SceneDamageRect?>? resolveBounds;
    private Func<string, SceneDamageRect?>? resolveVisibleBounds;
    private bool isRoundComplete;
    private bool isSolved;
    private Action<LowLevelRepaintRequest>? requestRepaint;

    public SampleHostPanel()
    {
        StatusText = "Type a five-letter word in React, then press Guess.";
    }

    [JsMember]
    public string CurrentGuess
    {
        get => currentGuess;
        set => currentGuess = NormalizeGuess(value);
    }

    [JsMember]
    public string StatusText { get; private set; }

    [JsMember]
    public string BoardSummary
    {
        get
        {
            if (isSolved)
                return $"Solved in {AttemptsUsed}/{MaxGuesses}";
            if (isRoundComplete)
                return $"Round over · {answer}";

            return $"Attempt {AttemptsUsed + 1}/{MaxGuesses} · {RemainingGuesses} left";
        }
    }

    [JsMember]
    public int AttemptsUsed => guesses.Count;

    [JsMember]
    public int RemainingGuesses => Math.Max(0, MaxGuesses - guesses.Count);

    [JsMember]
    public bool IsSolved => isSolved;

    [JsMember]
    public bool IsRoundComplete => isRoundComplete;

    [JsMember]
    public string RectSummary => HasPanelRect
        ? $"{effectivePanelRect.Width}x{effectivePanelRect.Height} @ {effectivePanelRect.X},{effectivePanelRect.Y}"
        : "not set";

    internal void AttachRepaintRequester(Action<LowLevelRepaintRequest> repaintRequester)
    {
        requestRepaint = repaintRequester ?? throw new ArgumentNullException(nameof(repaintRequester));
    }

    internal void AttachBoundsResolver(Func<string, SceneDamageRect?> boundsResolver)
    {
        resolveBounds = boundsResolver ?? throw new ArgumentNullException(nameof(boundsResolver));
    }

    internal void AttachVisibleBoundsResolver(Func<string, SceneDamageRect?> boundsResolver)
    {
        resolveVisibleBounds = boundsResolver ?? throw new ArgumentNullException(nameof(boundsResolver));
    }

    [JsMember]
    public void SetRect(float left, float top, float width, float height)
    {
        var nextRect = NormalizeRect(left, top, width, height);
        if (nextRect.Equals(panelRect))
            return;

        panelRect = nextRect;
        SyncEffectivePanelRect();
        RequestPanelRepaint();
    }

    [JsMember]
    public void AttachHostNode(string runtimeId)
    {
        var normalized = string.IsNullOrWhiteSpace(runtimeId) ? null : runtimeId.Trim();
        if (string.Equals(hostNodeRuntimeId, normalized, StringComparison.Ordinal))
            return;

        hostNodeRuntimeId = normalized;
        SyncEffectivePanelRect();
        RequestPanelRepaint();
    }

    [JsMember]
    public void SubmitGuess()
    {
        if (isRoundComplete)
        {
            StatusText = isSolved
                ? "Round already solved. Press New Round to play again."
                : $"Round finished. The answer was {answer}.";
            RequestPanelRepaint();
            return;
        }

        var guess = NormalizeGuess(currentGuess);
        if (guess.Length != WordLength)
        {
            StatusText = "Guess needs exactly five letters.";
            RequestPanelRepaint();
            return;
        }

        var nextRow = EvaluateGuess(guess);
        guesses.Add(nextRow);
        currentGuess = string.Empty;

        if (nextRow.IsSolved)
        {
            isSolved = true;
            isRoundComplete = true;
            StatusText = $"Solved! {guess} in {AttemptsUsed}/{MaxGuesses}.";
        }
        else if (guesses.Count >= MaxGuesses)
        {
            isRoundComplete = true;
            StatusText = $"No more guesses. The answer was {answer}.";
        }
        else
        {
            StatusText = $"{nextRow.EvaluationSummary} · Try another word.";
        }

        RequestPanelRepaint();
    }

    [JsMember]
    public void ResetGame()
    {
        answerIndex = (answerIndex + 1) % AnswerWords.Length;
        answer = AnswerWords[answerIndex];
        guesses.Clear();
        currentGuess = string.Empty;
        isSolved = false;
        isRoundComplete = false;
        StatusText = "New round ready. Type a five-letter word in React.";
        RequestPanelRepaint();
    }

    public bool TryGetRenderBounds(out SceneDamageRect bounds)
    {
        SyncEffectivePanelRect();
        bounds = effectiveVisibleRect;
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public void RenderLowLevelSkia(SKCanvas canvas, int width, int height, TimeSpan elapsed, ReadOnlySpan<SceneDamageRect> dirtyRects)
    {
        SyncEffectivePanelRect();
        if (effectiveVisibleRect.Width <= 0 || effectiveVisibleRect.Height <= 0 || !IntersectsAnyDirtyRect(effectiveVisibleRect, dirtyRects))
            return;

        var bounds = new SKRect(effectivePanelRect.X, effectivePanelRect.Y, effectivePanelRect.X + effectivePanelRect.Width, effectivePanelRect.Y + effectivePanelRect.Height);
        var visibleBounds = new SKRect(
            effectiveVisibleRect.X,
            effectiveVisibleRect.Y,
            effectiveVisibleRect.X + effectiveVisibleRect.Width,
            effectiveVisibleRect.Y + effectiveVisibleRect.Height);
        if (pictureDirty || cachedPicture is null)
        {
            cachedPicture?.Dispose();
            cachedPicture = RecordPanelPicture(bounds);
            pictureDirty = false;
        }

        using var restore = new SKAutoCanvasRestore(canvas, true);
        canvas.ClipRect(visibleBounds);
        canvas.DrawPicture(cachedPicture);
    }

    private bool HasPanelRect => effectivePanelRect.Width > 0 && effectivePanelRect.Height > 0;

    private SceneDamageRect SyncEffectivePanelRect()
    {
        var resolvedRect = panelRect;
        if (hostNodeRuntimeId is { Length: > 0 } nodeId &&
            resolveBounds?.Invoke(nodeId) is { } resolved &&
            resolved.Width > 0 &&
            resolved.Height > 0)
        {
            resolvedRect = resolved;
        }

        var visibleRect = resolvedRect;
        if (hostNodeRuntimeId is { Length: > 0 } visibleNodeId &&
            resolveVisibleBounds?.Invoke(visibleNodeId) is { } resolvedVisible &&
            resolvedVisible.Width > 0 &&
            resolvedVisible.Height > 0)
        {
            visibleRect = resolvedVisible;
        }

        if (!resolvedRect.Equals(effectivePanelRect))
        {
            effectivePanelRect = resolvedRect;
            pictureDirty = true;
        }

        effectiveVisibleRect = visibleRect;
        return effectivePanelRect;
    }

    private SKPicture RecordPanelPicture(SKRect bounds)
    {
        using var recorder = new SKPictureRecorder();
        var recordingCanvas = recorder.BeginRecording(bounds);
        DrawPanel(recordingCanvas, bounds);
        return recorder.EndRecording();
    }

    private void DrawPanel(SKCanvas canvas, SKRect bounds)
    {
        using var backgroundPaint = new SKPaint { Color = new SKColor(7, 16, 28), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var borderPaint = new SKPaint { Color = new SKColor(51, 65, 85), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        using var headerPaint = new SKPaint { Color = new SKColor(15, 23, 42), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var accentPaint = new SKPaint { Color = new SKColor(96, 165, 250, 44), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var titlePaint = new SKPaint { Color = new SKColor(241, 245, 249), IsAntialias = true };
        using var bodyPaint = new SKPaint { Color = new SKColor(148, 163, 184), IsAntialias = true };
        using var badgeTextPaint = new SKPaint { Color = new SKColor(191, 219, 254), IsAntialias = true };
        using var tileLetterPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var footerPaint = new SKPaint { Color = new SKColor(125, 140, 160), IsAntialias = true };
        using var titleTypeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
        using var titleFont = new SKFont(titleTypeface, 18);
        using var subtitleFont = new SKFont(SKTypeface.Default, 13);
        using var badgeFont = new SKFont(titleTypeface, 12);
        using var tileFont = new SKFont(titleTypeface, 22);
        using var footerFont = new SKFont(SKTypeface.Default, 12);

        canvas.DrawRoundRect(bounds, 18, 18, backgroundPaint);
        canvas.DrawRoundRect(bounds, 18, 18, borderPaint);

        var inner = new SKRect(bounds.Left + 18, bounds.Top + 18, bounds.Right - 18, bounds.Bottom - 18);
        var headerRect = new SKRect(inner.Left, inner.Top, inner.Right, inner.Top + 44);
        canvas.DrawRoundRect(headerRect, 12, 12, headerPaint);
        canvas.DrawRoundRect(headerRect, 12, 12, borderPaint);

        canvas.DrawText("Word bridge", headerRect.Left + 14, headerRect.Top + 18, titleFont, titlePaint);
        canvas.DrawText("React sends guesses, C# scores them, Skia draws the board.", headerRect.Left + 14, headerRect.Top + 36, subtitleFont, bodyPaint);

        var badgeWidth = MeasureTextWidth(BoardSummary, badgeFont, badgeTextPaint) + 22;
        var badgeRect = new SKRect(headerRect.Right - badgeWidth - 12, headerRect.Top + 8, headerRect.Right - 12, headerRect.Top + 32);
        canvas.DrawRoundRect(badgeRect, 999, 999, accentPaint);
        canvas.DrawText(BoardSummary, badgeRect.Left + 11, badgeRect.Top + 16, badgeFont, badgeTextPaint);

        const float tileGap = 8;
        var footerHeight = 58f;
        var boardTop = headerRect.Bottom + 18;
        var boardBottom = inner.Bottom - footerHeight;
        var boardHeight = Math.Max(120, boardBottom - boardTop);
        var tileSize = MathF.Min(
            (inner.Width - tileGap * (WordLength - 1)) / WordLength,
            (boardHeight - tileGap * (MaxGuesses - 1)) / MaxGuesses);
        var boardWidth = tileSize * WordLength + tileGap * (WordLength - 1);
        var boardLeft = inner.Left + MathF.Max(0, (inner.Width - boardWidth) * 0.5f);

        for (var row = 0; row < MaxGuesses; row++)
        {
            GuessRow? guessRow = row < guesses.Count ? guesses[row] : null;
            for (var column = 0; column < WordLength; column++)
            {
                var tileRect = new SKRect(
                    boardLeft + column * (tileSize + tileGap),
                    boardTop + row * (tileSize + tileGap),
                    boardLeft + column * (tileSize + tileGap) + tileSize,
                    boardTop + row * (tileSize + tileGap) + tileSize);

                var letter = guessRow is null
                    ? GetPreviewLetter(row, column)
                    : guessRow.Value.Word[column].ToString();
                var state = guessRow?.States[column] ?? ResolvePreviewState(row, column);
                DrawTile(canvas, tileRect, letter, state, tileFont, tileLetterPaint, borderPaint);
            }
        }

        var footerTop = inner.Bottom - footerHeight + 10;
        canvas.DrawText(StatusText, inner.Left, footerTop + 12, subtitleFont, titlePaint);
        canvas.DrawText("Wordle-style scoring stays in C#. React just edits currentGuess and triggers submitGuess().", inner.Left, footerTop + 32, footerFont, footerPaint);
    }

    private void DrawTile(
        SKCanvas canvas,
        SKRect tileRect,
        string letter,
        LetterState state,
        SKFont tileFont,
        SKPaint textPaint,
        SKPaint borderPaint)
    {
        var (fill, stroke) = state switch
        {
            LetterState.Correct => (new SKColor(34, 197, 94), new SKColor(22, 163, 74)),
            LetterState.Present => (new SKColor(234, 179, 8), new SKColor(202, 138, 4)),
            LetterState.Absent => (new SKColor(51, 65, 85), new SKColor(71, 85, 105)),
            LetterState.Active => (new SKColor(15, 23, 42), new SKColor(96, 165, 250)),
            _ => (new SKColor(15, 23, 42), new SKColor(51, 65, 85))
        };

        using var fillPaint = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var strokePaint = new SKPaint { Color = stroke, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = state == LetterState.Active ? 2f : borderPaint.StrokeWidth };
        canvas.DrawRoundRect(tileRect, 10, 10, fillPaint);
        canvas.DrawRoundRect(tileRect, 10, 10, strokePaint);
        if (string.IsNullOrEmpty(letter))
            return;

        var textWidth = tileFont.MeasureText(letter, textPaint);
        var metrics = tileFont.Metrics;
        var baselineX = tileRect.MidX - textWidth * 0.5f;
        var baselineY = tileRect.MidY - (metrics.Ascent + metrics.Descent) * 0.5f;
        canvas.DrawText(letter, baselineX, baselineY, tileFont, textPaint);
    }

    private string GetPreviewLetter(int row, int column)
    {
        if (isRoundComplete || row != guesses.Count || column >= currentGuess.Length)
            return string.Empty;

        return currentGuess[column].ToString();
    }

    private LetterState ResolvePreviewState(int row, int column)
    {
        if (isRoundComplete || row != guesses.Count)
            return LetterState.Empty;

        return column < currentGuess.Length ? LetterState.Active : LetterState.Empty;
    }

    private GuessRow EvaluateGuess(string guess)
    {
        var states = new LetterState[WordLength];
        var remaining = new Dictionary<char, int>();
        for (var index = 0; index < WordLength; index++)
        {
            if (guess[index] == answer[index])
            {
                states[index] = LetterState.Correct;
                continue;
            }

            remaining.TryGetValue(answer[index], out var count);
            remaining[answer[index]] = count + 1;
        }

        for (var index = 0; index < WordLength; index++)
        {
            if (states[index] == LetterState.Correct)
                continue;

            if (remaining.TryGetValue(guess[index], out var count) && count > 0)
            {
                states[index] = LetterState.Present;
                remaining[guess[index]] = count - 1;
            }
            else
            {
                states[index] = LetterState.Absent;
            }
        }

        var summary = string.Create(WordLength, states, static (buffer, nextStates) =>
        {
            for (var index = 0; index < nextStates.Length; index++)
            {
                buffer[index] = nextStates[index] switch
                {
                    LetterState.Correct => 'G',
                    LetterState.Present => 'Y',
                    _ => 'X'
                };
            }
        });
        return new GuessRow(guess, states, summary, summary == "GGGGG");
    }

    private void RequestPanelRepaint()
    {
        SyncEffectivePanelRect();
        pictureDirty = true;
        if (effectiveVisibleRect.Width > 0 && effectiveVisibleRect.Height > 0)
            requestRepaint?.Invoke(new LowLevelRepaintRequest(LowLevelRepaintRequestKind.NextFrame, effectiveVisibleRect));
    }

    private static SceneDamageRect NormalizeRect(float left, float top, float width, float height)
    {
        var roundedWidth = (int)MathF.Round(width);
        var roundedHeight = (int)MathF.Round(height);
        if (roundedWidth <= 0 || roundedHeight <= 0)
            return default;

        return new SceneDamageRect(
            (int)MathF.Round(left),
            (int)MathF.Round(top),
            roundedWidth,
            roundedHeight);
    }

    private static string NormalizeGuess(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var letters = new char[WordLength];
        var length = 0;
        foreach (var character in value)
        {
            if (!char.IsLetter(character))
                continue;

            letters[length++] = char.ToUpperInvariant(character);
            if (length == WordLength)
                break;
        }

        return new string(letters, 0, length);
    }

    private static float MeasureTextWidth(string text, SKFont font, SKPaint paint)
    {
        return string.IsNullOrEmpty(text) ? 0 : font.MeasureText(text, paint);
    }

    private static bool IntersectsAnyDirtyRect(SceneDamageRect bounds, ReadOnlySpan<SceneDamageRect> dirtyRects)
    {
        if (dirtyRects.IsEmpty)
            return false;

        foreach (var dirtyRect in dirtyRects)
        {
            if (dirtyRect.X < bounds.X + bounds.Width &&
                dirtyRect.X + dirtyRect.Width > bounds.X &&
                dirtyRect.Y < bounds.Y + bounds.Height &&
                dirtyRect.Y + dirtyRect.Height > bounds.Y)
            {
                return true;
            }
        }

        return false;
    }

    private enum LetterState
    {
        Empty,
        Active,
        Absent,
        Present,
        Correct
    }

    private readonly record struct GuessRow(string Word, LetterState[] States, string EvaluationSummary, bool IsSolved);
}
