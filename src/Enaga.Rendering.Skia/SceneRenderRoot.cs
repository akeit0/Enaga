using Enaga.Input;
using Enaga.Scene;
using SkiaSharp;

namespace Enaga.Rendering.Skia;

public sealed class SceneRenderRoot
    : IRenderRoot,
        IRenderGpuContextSink,
        IRenderSurfaceInvalidationSink,
        IInputSink,
        IPointerCursorSource,
        ITextCompositionRangeSink,
        IRenderDiagnosticsProvider,
        IRenderDirtyRectSource,
        IRenderWakeSource,
        IOverlayInputHitTestSource,
        IDisposable
{
    private const double ScaleOverlayDurationSeconds = 2.5;
    private const double ScaleOverlayFadeDurationSeconds = 0.25;
    private readonly SceneCommitPainter painter;
    private readonly ISceneFrameSource source;
    private readonly TimeProvider timeProvider;
    private readonly bool diagnosticsEnabled;
    private readonly bool requiresFullFramePresentation;
    private readonly bool viewCounter;
    private readonly SceneDamageRectBufferWriter sceneDirtyRectBuffer = new(16);
    private readonly SceneDamageRectBufferWriter sceneDirtyRectScratchBuffer = new(16);
    private readonly SceneDamageRectBufferWriter presentationSceneDirtyRectBuffer = new(16);
    private readonly SceneDamageRectBufferWriter mergedDirtyRectBuffer = new(16);
    private readonly SceneDamageRectBufferWriter lastDirtyRectBuffer = new(16);
    private SceneLayoutCommit? lastCommit;
    private string? lastErrorMessage;
    private RenderRootDiagnosticsSnapshot lastDiagnostics;
    private float lastViewportScale = 1f;
    private float lastScaleOverlayOpacity;
    private int lastPresentationWidth;
    private int lastPresentationHeight;
    private long scaleOverlayUntilTimestamp;
    private bool hasRenderedFrame;
    private int imageCacheDirty;
    private int presentationSurfaceInvalidated;
    private float lastPointerX;
    private float lastPointerY;
    private event Action? renderWakeRequested;

    public SceneRenderRoot(
        ISceneFrameSource source,
        bool diagnosticsEnabled = false,
        bool requiresFullFramePresentation = false,
        bool viewCounter = false
    )
        : this(
            source,
            new SceneRenderRootOptions
            {
                DiagnosticsEnabled = diagnosticsEnabled,
                RequiresFullFramePresentation = requiresFullFramePresentation,
                ViewCounter = viewCounter,
            }
        ) { }

    public SceneRenderRoot(ISceneFrameSource source, SceneRenderRootOptions? options)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;
        options ??= new SceneRenderRootOptions();
        diagnosticsEnabled = options.DiagnosticsEnabled;
        requiresFullFramePresentation = options.RequiresFullFramePresentation;
        viewCounter = options.ViewCounter;
        timeProvider =
            options.TimeProvider ?? throw new ArgumentNullException(nameof(options.TimeProvider));
        painter = CreatePainter(source, timeProvider);
        WebImageCache.ImageChanged += OnImageCacheChanged;
        SkiaImageAssetCache.AssetChanged += OnImageCacheChanged;
    }

    private static SceneCommitPainter CreatePainter(
        ISceneFrameSource source,
        TimeProvider timeProvider
    )
    {
        if (
            source is IRuntimeBackendServicesSource backendServicesSource
            && backendServicesSource.BackendServices.Text
                is SkiaRuntimeTextServices skiaTextServices
        )
        {
            return new SceneCommitPainter(skiaTextServices.TextResources, timeProvider);
        }

        return new SceneCommitPainter(timeProvider);
    }

    private float ViewportScale =>
        source is IRenderViewportScaleSource scaleSource
            ? Math.Clamp(scaleSource.ViewportScale, 0.25f, 5f)
            : 1f;

    public event Action? RenderWakeRequested
    {
        add
        {
            renderWakeRequested += value;
            if (source is IRenderWakeSource wakeSource)
                wakeSource.RenderWakeRequested += value;
        }
        remove
        {
            renderWakeRequested -= value;
            if (source is IRenderWakeSource wakeSource)
                wakeSource.RenderWakeRequested -= value;
        }
    }

    public void Dispose()
    {
        painter.Dispose();
        sceneDirtyRectBuffer.Dispose();
        sceneDirtyRectScratchBuffer.Dispose();
        presentationSceneDirtyRectBuffer.Dispose();
        mergedDirtyRectBuffer.Dispose();
        lastDirtyRectBuffer.Dispose();
        WebImageCache.ImageChanged -= OnImageCacheChanged;
        SkiaImageAssetCache.AssetChanged -= OnImageCacheChanged;
        if (source is IDisposable disposable)
            disposable.Dispose();
    }

    private void OnImageCacheChanged()
    {
        if (source is IRenderResourceInvalidationSink invalidationSink)
            invalidationSink.InvalidateRenderResources();
        Interlocked.Exchange(ref imageCacheDirty, 1);
        renderWakeRequested?.Invoke();
    }

    public void InvalidatePresentationSurface()
    {
        Interlocked.Exchange(ref presentationSurfaceInvalidated, 1);
        renderWakeRequested?.Invoke();
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic)
    {
        lastPointerX = x;
        lastPointerY = y;
        if (HitTestScaleOverlayInput(x, y))
            return;

        if (source is IInputSink inputSink)
        {
            var scale = ViewportScale;
            inputSink.PointerMove(x / scale, y / scale, buttons, synthetic);
            RequestInputRenderWake();
        }
    }

    public PointerCursorKind CurrentCursor =>
        source is IPointerCursorSource cursorSource
            ? cursorSource.CurrentCursor
            : PointerCursorKind.Default;

    public bool HitTestOverlayInput(float x, float y)
    {
        if (HitTestScaleOverlayInput(x, y))
            return true;

        var scale = ViewportScale;
        return source is IOverlayInputHitTestSource hitTestSource
            && hitTestSource.HitTestOverlayInput(x / scale, y / scale);
    }

    public void PointerDown(int button, int buttons, bool synthetic)
    {
        if (button == 0 && TryHandleScaleOverlayPointerDown())
            return;

        if (HitTestScaleOverlayInput())
            return;

        if (source is IInputSink inputSink)
        {
            inputSink.PointerDown(button, buttons, synthetic);
            RequestInputRenderWake();
        }
    }

    public void PointerUp(int button, int buttons, bool synthetic)
    {
        if (HitTestScaleOverlayInput())
            return;

        if (source is IInputSink inputSink)
        {
            inputSink.PointerUp(button, buttons, synthetic);
            RequestInputRenderWake();
        }
    }

    public void Wheel(float deltaX, float deltaY, bool synthetic)
    {
        if (source is IInputSink inputSink)
        {
            inputSink.Wheel(deltaX, deltaY, synthetic);
            RequestInputRenderWake();
        }
    }

    public void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0)
    {
        if (source is IInputSink inputSink)
        {
            inputSink.Wheel(deltaX, deltaY, synthetic, modifiers);
            RequestInputRenderWake();
        }
    }

    public void KeyDown(string key, int modifiers, bool repeat, bool synthetic)
    {
        if (source is IInputSink inputSink)
        {
            inputSink.KeyDown(key, modifiers, repeat, synthetic);
            RequestInputRenderWake();
        }
    }

    public void KeyUp(string key, int modifiers, bool synthetic)
    {
        if (source is IInputSink inputSink)
        {
            inputSink.KeyUp(key, modifiers, synthetic);
            RequestInputRenderWake();
        }
    }

    public void TextInput(string text, bool synthetic)
    {
        if (source is IInputSink inputSink)
        {
            inputSink.TextInput(text, synthetic);
            RequestInputRenderWake();
        }
    }

    public void StartTextComposition()
    {
        if (source is ITextCompositionSink compositionSink)
        {
            compositionSink.StartTextComposition();
            RequestInputRenderWake();
        }
    }

    public void StartTextComposition(int startIndex)
    {
        if (source is ITextCompositionRangeSink rangeSink)
        {
            rangeSink.StartTextComposition(startIndex);
            RequestInputRenderWake();
        }
        else if (source is ITextCompositionSink compositionSink)
        {
            compositionSink.StartTextComposition();
            RequestInputRenderWake();
        }
    }

    public void UpdateTextComposition(string text, int cursorPosition)
    {
        if (source is ITextCompositionSink compositionSink)
        {
            compositionSink.UpdateTextComposition(text, cursorPosition);
            RequestInputRenderWake();
        }
    }

    public void UpdateTextComposition(
        string text,
        int cursorPosition,
        int selectionStart,
        int selectionLength
    )
    {
        if (source is ITextCompositionSink compositionSink)
        {
            compositionSink.UpdateTextComposition(
                text,
                cursorPosition,
                selectionStart,
                selectionLength
            );
            RequestInputRenderWake();
        }
    }

    public void EndTextComposition()
    {
        if (source is ITextCompositionSink compositionSink)
        {
            compositionSink.EndTextComposition();
            RequestInputRenderWake();
        }
    }

    public void PrepareTextCompositionCommit()
    {
        if (source is ITextCompositionSink compositionSink)
        {
            compositionSink.PrepareTextCompositionCommit();
            RequestInputRenderWake();
        }
    }

    public void UpdateImeState(bool isOpen, string indicator)
    {
        if (source is ITextCompositionSink compositionSink)
        {
            compositionSink.UpdateImeState(isOpen, indicator);
            RequestInputRenderWake();
        }
    }

    public bool TryGetTextCompositionCursor(out TextCompositionCursor cursor)
    {
        if (source is ITextCompositionSink compositionSink)
        {
            if (!compositionSink.TryGetTextCompositionCursor(out cursor))
                return false;

            var scale = ViewportScale;
            cursor = new TextCompositionCursor(
                cursor.X * scale,
                cursor.Y * scale,
                cursor.Width * scale,
                cursor.Height * scale
            );
            return true;
        }

        cursor = default;
        return false;
    }

    public void Render(SKCanvas canvas, int width, int height, TimeSpan elapsed)
    {
        var sourceStartTimestamp = timeProvider.GetTimestamp();
        var viewportScale = ViewportScale;
        var viewportScaleChanged =
            hasRenderedFrame && Math.Abs(viewportScale - lastViewportScale) > 0.001f;
        var presentationSizeChanged =
            hasRenderedFrame
            && (width != lastPresentationWidth || height != lastPresentationHeight);
        var nowTimestamp = timeProvider.GetTimestamp();
        if (viewportScaleChanged)
            scaleOverlayUntilTimestamp =
                nowTimestamp
                + (long)(timeProvider.TimestampFrequency * ScaleOverlayDurationSeconds);
        var scaleOverlayOpacity = ResolveScaleOverlayOpacity(nowTimestamp);
        var scaleOverlayVisible = scaleOverlayOpacity > 0.001f;
        var hadScaleOverlay = lastScaleOverlayOpacity > 0.001f;
        var imageReady = Interlocked.Exchange(ref imageCacheDirty, 0) != 0;
        var surfaceInvalidated = Interlocked.Exchange(ref presentationSurfaceInvalidated, 0) != 0;
        var logicalWidth = Math.Max(1, (int)MathF.Ceiling(width / viewportScale));
        var logicalHeight = Math.Max(1, (int)MathF.Ceiling(height / viewportScale));
        var frameResult = source.RenderFrame(logicalWidth, logicalHeight, elapsed);
        var commit = frameResult.Commit;
        var previousCommit = lastCommit;
        var sourceFrameMs = timeProvider.GetElapsedTime(sourceStartTimestamp).TotalMilliseconds;
        var commitReused = ReferenceEquals(previousCommit, commit);
        var errorMessage = source.LastError;
        var errorChanged = !string.Equals(lastErrorMessage, errorMessage, StringComparison.Ordinal);
        var effectiveDamageReasons = errorChanged
            ? frameResult.DamageReasons | SceneDamageReason.ErrorOverlay
            : frameResult.DamageReasons;
        if (viewportScaleChanged)
            effectiveDamageReasons |= SceneDamageReason.Resize;
        if (presentationSizeChanged)
            effectiveDamageReasons |= SceneDamageReason.Resize;
        if (surfaceInvalidated)
            effectiveDamageReasons |= SceneDamageReason.Resize;
        if (imageReady)
            effectiveDamageReasons |= SceneDamageReason.ImageReady;
        var lowLevelSkiaRenderer = source as ILowLevelSkiaRenderer;
        ReadOnlySpan<SceneDamageRect> lowLevelDirtyRects = [];
        if (lowLevelSkiaRenderer is not null)
            lowLevelDirtyRects = lowLevelSkiaRenderer.ConsumeLowLevelDirtyRects(width, height);
        if (lowLevelDirtyRects.Length > 0)
            effectiveDamageReasons |= SceneDamageReason.LowLevelDraw;

        ReadOnlySpan<SceneDamageRect> sceneDirtyRects;
        var hasSourceDamage =
            effectiveDamageReasons != SceneDamageReason.None
            || frameResult.DirtyRects.Length > 0
            || lowLevelDirtyRects.Length > 0
            || errorChanged
            || viewportScaleChanged
            || presentationSizeChanged
            || surfaceInvalidated
            || scaleOverlayVisible
            || hadScaleOverlay
            || imageReady;
        if (
            (requiresFullFramePresentation && hasSourceDamage)
            || viewportScaleChanged
            || surfaceInvalidated
            || imageReady
        )
        {
            sceneDirtyRectBuffer.Clear();
            sceneDirtyRectBuffer.Add(
                new SceneDamageRect(0, 0, Math.Max(1, logicalWidth), Math.Max(1, logicalHeight))
            );
            sceneDirtyRects = sceneDirtyRectBuffer.WrittenSpan;
        }
        else if (requiresFullFramePresentation)
        {
            sceneDirtyRects = ReadOnlySpan<SceneDamageRect>.Empty;
        }
        else
        {
            var estimatorDamageReasons = presentationSizeChanged
                ? effectiveDamageReasons & ~SceneDamageReason.Resize
                : effectiveDamageReasons;
            sceneDirtyRects = SceneDamageEstimator.Resolve(
                previousCommit,
                commit,
                frameResult.DirtyRects,
                estimatorDamageReasons,
                logicalWidth,
                logicalHeight,
                errorChanged,
                sceneDirtyRectBuffer,
                sceneDirtyRectScratchBuffer
            );
            if (presentationSizeChanged && previousCommit is not null)
            {
                sceneDirtyRects = MergeDirtyRects(
                    sceneDirtyRects,
                    ResolveResizeExposureDirtyRects(
                        previousCommit.Viewport.Width,
                        previousCommit.Viewport.Height,
                        logicalWidth,
                        logicalHeight,
                        sceneDirtyRectScratchBuffer
                    ),
                    logicalWidth,
                    logicalHeight
                );
            }
        }
        var presentationSceneDirtyRects = ScaleSceneDirtyRectsToViewport(
            sceneDirtyRects,
            viewportScale,
            width,
            height
        );
        var effectivePresentationDirtyRects = MergeDirtyRects(
            presentationSceneDirtyRects,
            lowLevelDirtyRects,
            width,
            height
        );
        if (scaleOverlayVisible || hadScaleOverlay)
            effectivePresentationDirtyRects = AddScaleOverlayDirtyRect(
                effectivePresentationDirtyRects,
                width,
                height
            );
        CaptureDirtyRects(effectivePresentationDirtyRects);
        lastCommit = commit;
        lastErrorMessage = errorMessage;
        lastViewportScale = viewportScale;
        lastScaleOverlayOpacity = scaleOverlayOpacity;
        lastPresentationWidth = width;
        lastPresentationHeight = height;
        hasRenderedFrame = true;

        var shouldPaint =
            effectivePresentationDirtyRects.Length > 0 || !string.IsNullOrWhiteSpace(errorMessage);
        if (shouldPaint)
        {
            canvas.Save();
            if (Math.Abs(viewportScale - 1f) > 0.001f)
                canvas.Scale(viewportScale);
            painter.Paint(
                canvas,
                commit,
                elapsed,
                sceneDirtyRects.Length > 0 ? sceneDirtyRects : lowLevelDirtyRects
            );
            canvas.Restore();
            painter.PaintScrollBars(canvas, commit, viewportScale, width, height);
            lowLevelSkiaRenderer?.RenderLowLevelSkia(
                canvas,
                width,
                height,
                elapsed,
                effectivePresentationDirtyRects
            );
            if (scaleOverlayVisible)
                DrawScaleOverlay(canvas, viewportScale, width, scaleOverlayOpacity);
        }
        else
        {
            painter.SkipPaint(commitReused);
        }

        var runtimeState = source is IRenderRuntimeStateSource runtimeStateSource
            ? runtimeStateSource.GetRenderRuntimeStateSnapshot()
            : default;
        var viewCallCount = runtimeState.ViewCallCount;

        if (viewCounter && viewCallCount > 0)
        {
            Console.WriteLine($"View call count: {viewCallCount}");
        }
        long dirtyPixels = 0;
        foreach (var rect in effectivePresentationDirtyRects)
            dirtyPixels += rect.PixelCount;

        lastDiagnostics = new RenderRootDiagnosticsSnapshot(
            diagnosticsEnabled,
            sourceFrameMs,
            painter.LastPaintDurationMs,
            shouldPaint,
            commitReused,
            painter.LastPictureReused,
            diagnosticsEnabled ? lastDirtyRectBuffer.WrittenSpan.ToArray() : null,
            effectivePresentationDirtyRects.Length,
            dirtyPixels,
            effectiveDamageReasons,
            width,
            height,
            runtimeState
        );
        if (scaleOverlayVisible)
        {
            renderWakeRequested?.Invoke();
        }
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            SceneOverlayPainter.DrawOverlayMessage(canvas, errorMessage!);
        }
    }

    public void SetRenderGpuContext(GRContext? context) => painter.SetRenderGpuContext(context);

    private void RequestInputRenderWake() => renderWakeRequested?.Invoke();

    public RenderRootDiagnosticsSnapshot GetRenderRootDiagnosticsSnapshot()
    {
        return lastDiagnostics;
    }

    public ReadOnlySpan<SceneDamageRect> GetLastDirtyRects()
    {
        return lastDirtyRectBuffer.WrittenSpan;
    }

    private ReadOnlySpan<SceneDamageRect> MergeDirtyRects(
        ReadOnlySpan<SceneDamageRect> sceneDirtyRects,
        ReadOnlySpan<SceneDamageRect> lowLevelDirtyRects,
        int width,
        int height
    )
    {
        if (sceneDirtyRects.Length == 0)
            return lowLevelDirtyRects;

        if (lowLevelDirtyRects.Length == 0)
            return sceneDirtyRects;

        if (
            ContainsFullFrameRect(sceneDirtyRects, width, height)
            || ContainsFullFrameRect(lowLevelDirtyRects, width, height)
        )
        {
            mergedDirtyRectBuffer.Clear();
            mergedDirtyRectBuffer.Add(
                new SceneDamageRect(0, 0, Math.Max(1, width), Math.Max(1, height))
            );
            return mergedDirtyRectBuffer.WrittenSpan;
        }

        mergedDirtyRectBuffer.Clear();
        foreach (var rect in sceneDirtyRects)
            mergedDirtyRectBuffer.Add(rect);
        foreach (var rect in lowLevelDirtyRects)
            mergedDirtyRectBuffer.Add(rect);
        return mergedDirtyRectBuffer.WrittenSpan;
    }

    private ReadOnlySpan<SceneDamageRect> AddScaleOverlayDirtyRect(
        ReadOnlySpan<SceneDamageRect> dirtyRects,
        int width,
        int height
    )
    {
        if (ContainsFullFrameRect(dirtyRects, width, height))
            return dirtyRects;

        mergedDirtyRectBuffer.Clear();
        foreach (var rect in dirtyRects)
            mergedDirtyRectBuffer.Add(rect);
        mergedDirtyRectBuffer.Add(ResolveScaleOverlayRect(width, height));
        return mergedDirtyRectBuffer.WrittenSpan;
    }

    private static ReadOnlySpan<SceneDamageRect> ResolveResizeExposureDirtyRects(
        int previousWidth,
        int previousHeight,
        int width,
        int height,
        SceneDamageRectBufferWriter buffer
    )
    {
        buffer.Clear();
        var safePreviousWidth = Math.Max(1, previousWidth);
        var safePreviousHeight = Math.Max(1, previousHeight);
        var safeWidth = Math.Max(1, width);
        var safeHeight = Math.Max(1, height);

        if (safeWidth > safePreviousWidth)
            buffer.Add(
                new SceneDamageRect(safePreviousWidth, 0, safeWidth - safePreviousWidth, safeHeight)
            );

        if (safeHeight > safePreviousHeight)
            buffer.Add(
                new SceneDamageRect(
                    0,
                    safePreviousHeight,
                    Math.Min(safePreviousWidth, safeWidth),
                    safeHeight - safePreviousHeight
                )
            );

        return buffer.WrittenSpan;
    }

    private bool HitTestScaleOverlayInput() =>
        lastDiagnostics.Width > 0 && HitTestScaleOverlayInput(lastPointerX, lastPointerY);

    private bool HitTestScaleOverlayInput(float x, float y) =>
        IsScaleOverlayActive()
        && Contains(
            ResolveScaleOverlayGeometry(lastDiagnostics.Width, lastDiagnostics.Height).Rect,
            x,
            y
        );

    private bool TryHandleScaleOverlayPointerDown()
    {
        if (!IsScaleOverlayActive() || lastDiagnostics.Width <= 0)
            return false;

        var geometry = ResolveScaleOverlayGeometry(lastDiagnostics.Width, lastDiagnostics.Height);
        if (!Contains(geometry.Rect, lastPointerX, lastPointerY))
            return false;

        if (source is not IRenderViewportScaleController controller)
            return true;

        if (Contains(geometry.DecreaseRect, lastPointerX, lastPointerY))
            controller.TryStepViewportScale(-1);
        else if (Contains(geometry.IncreaseRect, lastPointerX, lastPointerY))
            controller.TryStepViewportScale(1);
        else if (Contains(geometry.ResetRect, lastPointerX, lastPointerY))
            controller.TryResetViewportScale();

        return true;
    }

    private bool IsScaleOverlayActive() =>
        ResolveScaleOverlayOpacity(timeProvider.GetTimestamp()) > 0.001f;

    private float ResolveScaleOverlayOpacity(long nowTimestamp)
    {
        var remainingTicks = scaleOverlayUntilTimestamp - nowTimestamp;
        if (remainingTicks <= 0)
            return 0f;

        var fadeTicks = timeProvider.TimestampFrequency * ScaleOverlayFadeDurationSeconds;
        if (fadeTicks <= 0 || remainingTicks >= fadeTicks)
            return 1f;

        return Math.Clamp((float)(remainingTicks / fadeTicks), 0f, 1f);
    }

    private static bool Contains(SceneDamageRect rect, float x, float y) =>
        x >= rect.X && x <= rect.X + rect.Width && y >= rect.Y && y <= rect.Y + rect.Height;

    private static SceneDamageRect ResolveScaleOverlayRect(int width, int height) =>
        ResolveScaleOverlayGeometry(width, height).Rect;

    private static ScaleOverlayGeometry ResolveScaleOverlayGeometry(int width, int height)
    {
        var overlayWidth = Math.Min(Math.Max(1, width), 330);
        var overlayHeight = Math.Min(Math.Max(1, height), 48);
        var rect = new SceneDamageRect(
            Math.Max(0, (width - overlayWidth) / 2),
            0,
            overlayWidth,
            overlayHeight
        );
        var right = rect.X + rect.Width;
        return new ScaleOverlayGeometry(
            rect,
            new SceneDamageRect(right - 148, rect.Y, 36, rect.Height),
            new SceneDamageRect(right - 112, rect.Y, 34, rect.Height),
            new SceneDamageRect(right - 74, rect.Y + 7, 64, Math.Max(1, rect.Height - 14))
        );
    }

    private static void DrawScaleOverlay(
        SKCanvas canvas,
        float viewportScale,
        int width,
        float opacity
    )
    {
        var rect = ResolveScaleOverlayGeometry(width, 48).Rect;
        var skRect = new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
        using var overlayOpacityPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(
                (byte)Math.Clamp((int)MathF.Round(opacity * 255f), 0, 255)
            ),
        };
        canvas.SaveLayer(skRect, overlayOpacityPaint);

        using var background = new SKPaint
        {
            Color = new SKColor(31, 31, 31, 244),
            IsAntialias = true,
        };
        canvas.DrawRoundRect(skRect, 13, 13, background);

        using var boldTypeface = SKTypeface.FromFamilyName(
            null,
            SKFontStyleWeight.Bold,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright
        );
        using var regularTypeface = SKTypeface.FromFamilyName(
            null,
            SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright
        );
        using var percentFont = new SKFont(boldTypeface, 14);
        using var percentPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(
            $"{MathF.Round(viewportScale * 100)}%",
            skRect.Left + 14,
            skRect.Top + 29,
            SKTextAlign.Left,
            percentFont,
            percentPaint
        );

        using var actionFont = new SKFont(regularTypeface, 28);
        using var actionPaint = new SKPaint
        {
            Color = new SKColor(210, 210, 210),
            IsAntialias = true,
        };
        canvas.DrawText(
            "-",
            skRect.Right - 130,
            skRect.Top + 29,
            SKTextAlign.Center,
            actionFont,
            actionPaint
        );
        canvas.DrawText(
            "+",
            skRect.Right - 96,
            skRect.Top + 29,
            SKTextAlign.Center,
            actionFont,
            actionPaint
        );

        var resetRect = new SKRect(
            skRect.Right - 74,
            skRect.Top + 7,
            skRect.Right - 10,
            skRect.Bottom - 7
        );
        using var resetFill = new SKPaint
        {
            Color = new SKColor(68, 68, 68, 255),
            IsAntialias = true,
        };
        using var resetStroke = new SKPaint
        {
            Color = new SKColor(110, 110, 110, 255),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
        };
        canvas.DrawRoundRect(resetRect, 8, 8, resetFill);
        canvas.DrawRoundRect(resetRect, 8, 8, resetStroke);
        using var resetFont = new SKFont(boldTypeface, 13);
        using var resetPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(
            "reset",
            resetRect.MidX,
            resetRect.Top + 22,
            SKTextAlign.Center,
            resetFont,
            resetPaint
        );
        canvas.Restore();
    }

    private readonly record struct ScaleOverlayGeometry(
        SceneDamageRect Rect,
        SceneDamageRect DecreaseRect,
        SceneDamageRect IncreaseRect,
        SceneDamageRect ResetRect
    );

    private ReadOnlySpan<SceneDamageRect> ScaleSceneDirtyRectsToViewport(
        ReadOnlySpan<SceneDamageRect> dirtyRects,
        float viewportScale,
        int width,
        int height
    )
    {
        if (dirtyRects.Length == 0 || Math.Abs(viewportScale - 1f) <= 0.001f)
            return dirtyRects;

        var safeWidth = Math.Max(1, width);
        var safeHeight = Math.Max(1, height);
        presentationSceneDirtyRectBuffer.Clear();
        foreach (var dirtyRect in dirtyRects)
        {
            var left = Math.Clamp((int)MathF.Floor(dirtyRect.X * viewportScale), 0, safeWidth);
            var top = Math.Clamp((int)MathF.Floor(dirtyRect.Y * viewportScale), 0, safeHeight);
            var right = Math.Clamp(
                (int)MathF.Ceiling((dirtyRect.X + dirtyRect.Width) * viewportScale),
                0,
                safeWidth
            );
            var bottom = Math.Clamp(
                (int)MathF.Ceiling((dirtyRect.Y + dirtyRect.Height) * viewportScale),
                0,
                safeHeight
            );
            var scaledWidth = right - left;
            var scaledHeight = bottom - top;
            if (scaledWidth > 0 && scaledHeight > 0)
                presentationSceneDirtyRectBuffer.Add(
                    new SceneDamageRect(left, top, scaledWidth, scaledHeight)
                );
        }

        return presentationSceneDirtyRectBuffer.WrittenSpan;
    }

    private static bool ContainsFullFrameRect(
        ReadOnlySpan<SceneDamageRect> dirtyRects,
        int width,
        int height
    )
    {
        foreach (var dirtyRect in dirtyRects)
        {
            if (
                dirtyRect.X <= 0
                && dirtyRect.Y <= 0
                && dirtyRect.Width >= width
                && dirtyRect.Height >= height
            )
            {
                return true;
            }
        }

        return false;
    }

    private void CaptureDirtyRects(ReadOnlySpan<SceneDamageRect> dirtyRects)
    {
        lastDirtyRectBuffer.Clear();
        foreach (var dirtyRect in dirtyRects)
            lastDirtyRectBuffer.Add(dirtyRect);
    }
}
