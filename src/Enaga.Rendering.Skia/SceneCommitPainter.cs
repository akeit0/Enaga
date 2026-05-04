using System.Globalization;
using Enaga.Input;
using Enaga.Scene;
using SkiaSharp;

namespace Enaga.Rendering.Skia;


internal sealed class SceneCommitPainter : IDisposable
{
    private const int textBlobCacheLimit = 512;
    private const int textBlobCacheMaxRunLength = 256;
    private const int ellipsizedLineCacheLimit = 1024;
    private const int fontMetricsCacheLimit = 1024;
    private const int scrollContentPictureCacheLimit = 128;
    private const float viewportScrollPictureThreshold = 2f;
    private const float smallDirtyRectCacheBypassAreaRatio = 0.25f;
    private static readonly SceneTextStyle DefaultTextStyle = new(16, "#ffffff");
    private static readonly SceneTextStyle DefaultTextInputStyle = new(16, "#f8fafc");
    private readonly Dictionary<string, SKColor> colorCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeShaderTemplate?> runtimeShaderTemplateCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SKRuntimeEffect?> shaderEffectCache = new(StringComparer.Ordinal);
    private readonly Dictionary<float, SKMaskFilter> textShadowBlurFilterCache = new();
    private readonly Dictionary<float, SKImageFilter> boxShadowBlurFilterCache = new();
    private readonly Dictionary<TextBlobCacheKey, SKTextBlob> textBlobCache = new();
    private readonly Queue<TextBlobCacheKey> textBlobCacheOrder = new();
    private readonly Dictionary<EllipsizedLineCacheKey, TextInputMetrics.TextLineSpan> ellipsizedLineCache = new();
    private readonly Queue<EllipsizedLineCacheKey> ellipsizedLineCacheOrder = new();
    private readonly Dictionary<FontMetricsCacheKey, SKFontMetrics> fontMetricsCache = new();
    private readonly Dictionary<ScrollContentPictureCacheKey, SKPicture> scrollContentPictureCache = new();
    private readonly Queue<ScrollContentPictureCacheKey> scrollContentPictureCacheOrder = new();
    private readonly Dictionary<SceneNodeId, ScrollContentPictureCacheKey> scrollContentPictureKeyByNode = new();
    private readonly Dictionary<ScrollContentPictureCacheKey, SKImage> scrollContentRasterCache = new();
    private readonly Queue<ScrollContentPictureCacheKey> scrollContentRasterCacheOrder = new();
    private readonly Dictionary<SceneNodeId, ScrollContentPictureCacheKey> scrollContentRasterKeyByNode = new();
    private readonly SKPaint fillPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };
    private readonly SKPaint strokePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke
    };
    private readonly SKPaint clearPaint = new()
    {
        BlendMode = SKBlendMode.Clear
    };
    private readonly SKPaint textPaint = new()
    {
        IsAntialias = true
    };
    private readonly SKPaint shadowPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };
    private readonly TimeProvider timeProvider;
    private readonly SkiaTextResources textResources;
    private readonly bool ownsTextResources;
    private GRContext? renderGpuContext;
    private int lastFontCatalogVersion = -1;
    private bool recordingContainsPendingImages;
    private bool bypassScrollContentCacheForCurrentPaint;
    private int recordingCulledNodePaints;
    private int recordingVisitedNodes;

    public SceneCommitPainter()
        : this(new SkiaTextResources(), TimeProvider.System, ownsTextResources: true)
    {
    }

    public SceneCommitPainter(TimeProvider? timeProvider)
        : this(new SkiaTextResources(), timeProvider ?? TimeProvider.System, ownsTextResources: true)
    {
    }

    internal SceneCommitPainter(SkiaTextResources textResources, TimeProvider? timeProvider = null)
        : this(textResources, timeProvider ?? TimeProvider.System, ownsTextResources: false)
    {
    }

    private SceneCommitPainter(SkiaTextResources textResources, TimeProvider timeProvider, bool ownsTextResources)
    {
        this.textResources = textResources ?? throw new ArgumentNullException(nameof(textResources));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.ownsTextResources = ownsTextResources;
    }

    public double LastPaintDurationMs { get; private set; }

    public bool LastPictureReused { get; private set; }

    internal bool LastDirectDirtyPaintUsed { get; private set; }

    internal int LastRecordedNodeCount { get; private set; }

    internal int LastCulledNodePaintCount { get; private set; }

    internal int TextBlobCacheCount => textBlobCache.Count;

    internal long TextBlobCacheHits { get; private set; }

    internal long TextBlobCacheMisses { get; private set; }

    internal int EllipsizedLineCacheCount => ellipsizedLineCache.Count;

    internal long EllipsizedLineCacheHits { get; private set; }

    internal long EllipsizedLineCacheMisses { get; private set; }

    internal int ScrollContentPictureCacheCount => scrollContentPictureCache.Count + scrollContentRasterCache.Count;

    internal long ScrollContentPictureCacheHits { get; private set; }

    internal long ScrollContentPictureCacheMisses { get; private set; }

    public void Paint(SKCanvas canvas, SceneLayoutCommit commit, TimeSpan elapsed, ReadOnlySpan<SceneDamageRect> dirtyRects = default)
    {
        var startTimestamp = timeProvider.GetTimestamp();
        var fontCatalogVersion = textResources.FontCatalog.CurrentVersion;
        if (fontCatalogVersion != lastFontCatalogVersion)
        {
            InvalidateFontCaches();
            lastFontCatalogVersion = fontCatalogVersion;
        }

        if (dirtyRects.Length == 0)
        {
            LastDirectDirtyPaintUsed = false;
            canvas.Clear(SKColors.Transparent);
            BeginRecordingStats();
            PaintNode(canvas, commit, commit.RootId);
            CompleteRecordingStats();
            PaintAnimatedShaderNodes(canvas, commit, (float)elapsed.TotalSeconds);
        }
        else
        {
            LastDirectDirtyPaintUsed = true;
            PaintDirtyRectsDirect(canvas, commit, elapsed, dirtyRects);
        }

        LastPaintDurationMs = timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
    }

    public void SkipPaint(bool pictureReused)
    {
        LastPaintDurationMs = 0;
        LastPictureReused = pictureReused;
    }

    public void SetRenderGpuContext(GRContext? context)
    {
        if (!ReferenceEquals(renderGpuContext, context) && renderGpuContext is not null)
            ClearScrollContentRasterCache();
        renderGpuContext = context;
    }

    public void Dispose()
    {
        ClearTextBlobCache();
        ClearEllipsizedLineCache();
        ClearScrollContentPictureCache();
        foreach (var effect in shaderEffectCache.Values)
            effect?.Dispose();
        foreach (var filter in textShadowBlurFilterCache.Values)
            filter.Dispose();
        foreach (var filter in boxShadowBlurFilterCache.Values)
            filter.Dispose();
        fillPaint.Dispose();
        strokePaint.Dispose();
        clearPaint.Dispose();
        textPaint.Dispose();
        shadowPaint.Dispose();
        if (ownsTextResources)
            textResources.Dispose();
        recordingContainsPendingImages = false;
        runtimeShaderTemplateCache.Clear();
        shaderEffectCache.Clear();
        textShadowBlurFilterCache.Clear();
        boxShadowBlurFilterCache.Clear();
    }

    private void PaintDirtyRectsDirect(SKCanvas canvas, SceneLayoutCommit commit, TimeSpan elapsed, ReadOnlySpan<SceneDamageRect> dirtyRects)
    {
        LastPictureReused = false;

        BeginRecordingStats();
        bypassScrollContentCacheForCurrentPaint = ShouldBypassScrollContentCacheForDirtyPaint(commit, dirtyRects);
        try
        {
            foreach (var dirtyRect in dirtyRects)
            {
                var clipRect = SKRect.Create(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height);
                canvas.Save();
                canvas.ClipRect(clipRect);
                canvas.DrawRect(clipRect, clearPaint);
                PaintNode(canvas, commit, commit.RootId);
                PaintAnimatedShaderNodes(canvas, commit, (float)elapsed.TotalSeconds);
                canvas.Restore();
            }
        }
        finally
        {
            bypassScrollContentCacheForCurrentPaint = false;
        }

        CompleteRecordingStats();
    }

    private static bool ShouldBypassScrollContentCacheForDirtyPaint(SceneLayoutCommit commit, ReadOnlySpan<SceneDamageRect> dirtyRects)
    {
        if (dirtyRects.IsEmpty || dirtyRects.Length > 8)
            return false;

        var viewportArea = Math.Max(1L, (long)Math.Max(1, commit.Viewport.Width) * Math.Max(1, commit.Viewport.Height));
        var dirtyArea = 0L;
        for (var index = 0; index < dirtyRects.Length; index++)
            dirtyArea += dirtyRects[index].PixelCount;

        return dirtyArea > 0 && dirtyArea <= viewportArea * smallDirtyRectCacheBypassAreaRatio;
    }

    private void BeginRecordingStats()
    {
        recordingContainsPendingImages = false;
        recordingVisitedNodes = 0;
        recordingCulledNodePaints = 0;
    }

    private void CompleteRecordingStats()
    {
        LastRecordedNodeCount = recordingVisitedNodes;
        LastCulledNodePaintCount = recordingCulledNodePaints;
    }

    private void PaintNode(SKCanvas canvas, SceneLayoutCommit commit, SceneNodeId id)
    {
        if (!commit.Layout.TryGetValue(id, out var box) || !commit.Nodes.TryGetValue(id, out var node))
            return;

        recordingVisitedNodes++;
        if (IsHostAnimatedRuntimeShader(box))
            return;

        var paintBox = ApplyPaintOverride(commit, id, box);
        var selfPaintRejected = ShouldCullSelfPaint(canvas, box);
        if (selfPaintRejected)
            recordingCulledNodePaints++;

        if (!selfPaintRejected)
            DrawBox(canvas, paintBox);

        var clipPushed = false;
        if (ShouldClipChildren(box))
        {
            if (selfPaintRejected)
                return;

            canvas.Save();
            clipPushed = true;
            ClipBox(canvas, box);
        }

        if (!selfPaintRejected)
        {
            if (box.Text is not null)
                DrawText(canvas, paintBox);
            else if (box.TextInput is not null)
                DrawTextInput(canvas, paintBox);
            else if (box.Image is not null)
                DrawImage(canvas, box);
        }

        if (box.Scroll is { IsScrollContainer: true })
        {
            PaintScrollViewChildren(canvas, commit, id, node, box);
            if (clipPushed)
            {
                canvas.Restore();
                clipPushed = false;
            }

        }
        else
        {
            PaintChildren(canvas, commit, node.Children);
        }

        if (clipPushed)
            canvas.Restore();
    }

    private void PaintScrollViewChildren(SKCanvas canvas, SceneLayoutCommit commit, SceneNodeId id, SceneGraphNode node, SceneLayoutBox box)
    {
        canvas.Save();
        canvas.Translate(-box.ScrollX, -box.ScrollY);
        if (bypassScrollContentCacheForCurrentPaint)
        {
            RemoveScrollContentPictureForNode(id);
            RemoveScrollContentRasterForNode(id);
            PaintChildren(canvas, commit, node.Children, canvas.LocalClipBounds);
        }
        else if (ShouldUseScrollContentRasterTile(canvas))
        {
            if (TryGetOrCreateScrollContentRasterTile(commit, id, node, box, out var rasterImage, out var rasterRect))
                canvas.DrawImage(rasterImage, rasterRect);
            else if (GetOrCreateScrollContentPicture(commit, id, node, box) is { } picture)
                canvas.DrawPicture(picture);
            else
                PaintChildren(canvas, commit, node.Children, canvas.LocalClipBounds);
        }
        else if (GetOrCreateScrollContentPicture(commit, id, node, box) is { } picture)
        {
            RemoveScrollContentRasterForNode(id);
            canvas.DrawPicture(picture);
        }
        else
        {
            PaintChildren(canvas, commit, node.Children, canvas.LocalClipBounds);
        }

        canvas.Restore();
    }

    private static bool ShouldUseScrollContentRasterTile(SKCanvas canvas)
    {
        var matrix = canvas.TotalMatrix;
        return Math.Abs(matrix.ScaleX - 1f) <= 0.001f &&
               Math.Abs(matrix.ScaleY - 1f) <= 0.001f &&
               Math.Abs(matrix.SkewX) <= 0.001f &&
               Math.Abs(matrix.SkewY) <= 0.001f;
    }

    private bool TryGetOrCreateScrollContentRasterTile(
        SceneLayoutCommit commit,
        SceneNodeId id,
        SceneGraphNode node,
        SceneLayoutBox box,
        out SKImage image,
        out SKRect destinationRect)
    {
        image = null!;
        destinationRect = default;
        if (node.Children.Length == 0 || IsHostAnimatedRuntimeShader(box) || HasPositionedChild(commit, node))
            return false;

        var contentWidth = Math.Max(box.Width, box.ContentWidth);
        var contentHeight = Math.Max(box.Height, box.ContentHeight);
        if (!ShouldUseViewportScrollContentPicture(box, contentWidth, contentHeight))
            return false;

        var tileRect = ResolveScrollContentTileRect(box, contentWidth, contentHeight);
        var key = new ScrollContentPictureCacheKey(
            id,
            ComputeScrollContentSignature(commit, node),
            QuantizePixel(tileRect.Left),
            QuantizePixel(tileRect.Top),
            QuantizePixel(tileRect.Width),
            QuantizePixel(tileRect.Height));
        if (scrollContentRasterCache.TryGetValue(key, out var cached))
        {
            ScrollContentPictureCacheHits++;
            image = cached;
            destinationRect = tileRect;
            return true;
        }

        ScrollContentPictureCacheMisses++;
        var tileWidth = Math.Max(1, (int)MathF.Ceiling(tileRect.Width));
        var tileHeight = Math.Max(1, (int)MathF.Ceiling(tileRect.Height));
        using var recorder = new SKPictureRecorder();
        var recordingCanvas = recorder.BeginRecording(SKRect.Create(0, 0, tileWidth, tileHeight));
        recordingCanvas.ClipRect(SKRect.Create(0, 0, tileWidth, tileHeight));
        recordingCanvas.Translate(-tileRect.Left, -tileRect.Top);
        var previousPending = recordingContainsPendingImages;
        recordingContainsPendingImages = false;
        PaintChildren(recordingCanvas, commit, node.Children, recordingCanvas.LocalClipBounds);
        var containsPending = recordingContainsPendingImages;
        recordingContainsPendingImages = previousPending || containsPending;
        using var picture = recorder.EndRecording();
        if (containsPending || picture is null)
            return false;

        using var surface = CreateScrollContentTileSurface(tileWidth, tileHeight);
        if (surface is null)
            return false;

        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawPicture(picture);
        var snapshot = surface.Snapshot();
        if (snapshot is null)
            return false;

        RemoveScrollContentRasterForNode(id);
        scrollContentRasterCache[key] = snapshot;
        scrollContentRasterKeyByNode[id] = key;
        scrollContentRasterCacheOrder.Enqueue(key);
        CompactScrollContentRasterCacheOrderIfNeeded();
        TrimScrollContentRasterCache();
        image = snapshot;
        destinationRect = tileRect;
        return true;
    }

    private SKSurface? CreateScrollContentTileSurface(int tileWidth, int tileHeight)
    {
        var imageInfo = new SKImageInfo(tileWidth, tileHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        return renderGpuContext is not null
            ? SKSurface.Create(renderGpuContext, budgeted: true, imageInfo)
            : SKSurface.Create(imageInfo);
    }

    private SKPicture? GetOrCreateScrollContentPicture(SceneLayoutCommit commit, SceneNodeId id, SceneGraphNode node, SceneLayoutBox box)
    {
        if (node.Children.Length == 0 || IsHostAnimatedRuntimeShader(box) || HasPositionedChild(commit, node))
            return null;

        var contentWidth = Math.Max(box.Width, box.ContentWidth);
        var contentHeight = Math.Max(box.Height, box.ContentHeight);
        var viewportPicture = ShouldUseViewportScrollContentPicture(box, contentWidth, contentHeight);
        var visibleContentRect = ResolveVisibleScrollContentRect(box, contentWidth, contentHeight, viewportPicture);
        var key = new ScrollContentPictureCacheKey(
            id,
            ComputeScrollContentSignature(commit, node),
            viewportPicture ? QuantizePixel(visibleContentRect.Left) : 0,
            viewportPicture ? QuantizePixel(visibleContentRect.Top) : 0,
            viewportPicture ? QuantizePixel(visibleContentRect.Width) : 0,
            viewportPicture ? QuantizePixel(visibleContentRect.Height) : 0);
        if (scrollContentPictureCache.TryGetValue(key, out var cached))
        {
            ScrollContentPictureCacheHits++;
            return cached;
        }

        ScrollContentPictureCacheMisses++;
        using var recorder = new SKPictureRecorder();
        var recordingRect = viewportPicture
            ? visibleContentRect
            : SKRect.Create(box.AbsLeft, box.AbsTop, Math.Max(1, contentWidth), Math.Max(1, contentHeight));
        var recordingCanvas = recorder.BeginRecording(recordingRect);
        if (viewportPicture)
            recordingCanvas.ClipRect(visibleContentRect);
        var previousPending = recordingContainsPendingImages;
        recordingContainsPendingImages = false;
        PaintChildren(recordingCanvas, commit, node.Children, viewportPicture ? recordingCanvas.LocalClipBounds : null);
        var containsPending = recordingContainsPendingImages;
        recordingContainsPendingImages = previousPending || containsPending;
        var picture = recorder.EndRecording();
        if (containsPending)
        {
            picture?.Dispose();
            return null;
        }

        if (picture is null)
            return null;

        RemoveScrollContentPictureForNode(id);
        scrollContentPictureCache[key] = picture;
        scrollContentPictureKeyByNode[id] = key;
        scrollContentPictureCacheOrder.Enqueue(key);
        CompactScrollContentPictureCacheOrderIfNeeded();
        TrimScrollContentPictureCache();
        return picture;
    }

    private static int ComputeScrollContentSignature(SceneLayoutCommit commit, SceneGraphNode scrollNode)
    {
        var hash = new HashCode();
        AddNodeListToSignature(commit, scrollNode.Children, ref hash);
        return hash.ToHashCode();
    }

    private static bool ShouldUseViewportScrollContentPicture(SceneLayoutBox box, float contentWidth, float contentHeight)
        => contentWidth > box.Width * viewportScrollPictureThreshold ||
           contentHeight > box.Height * viewportScrollPictureThreshold;

    private static SKRect ResolveVisibleScrollContentRect(SceneLayoutBox box, float contentWidth, float contentHeight, bool viewportPicture)
    {
        if (!viewportPicture)
            return SKRect.Create(box.AbsLeft, box.AbsTop, Math.Max(1, contentWidth), Math.Max(1, contentHeight));

        var left = box.AbsLeft + Math.Clamp(box.ScrollX, 0, Math.Max(0, contentWidth - box.Width));
        var top = box.AbsTop + Math.Clamp(box.ScrollY, 0, Math.Max(0, contentHeight - box.Height));
        return SKRect.Create(left, top, Math.Max(1, box.Width), Math.Max(1, box.Height));
    }

    private static SKRect ResolveScrollContentTileRect(SceneLayoutBox box, float contentWidth, float contentHeight)
    {
        var tileWidth = Math.Max(1, Math.Min(contentWidth, Math.Max(box.Width, box.Width * 2f)));
        var tileHeight = Math.Max(1, Math.Min(contentHeight, Math.Max(box.Height, box.Height * 3f)));
        var stepX = Math.Max(1, box.Width);
        var stepY = Math.Max(1, box.Height);
        var maxContentLeft = Math.Max(0, contentWidth - tileWidth);
        var maxContentTop = Math.Max(0, contentHeight - tileHeight);
        var contentLeft = Math.Clamp(MathF.Floor(Math.Max(0, box.ScrollX) / stepX) * stepX, 0, maxContentLeft);
        var contentTop = Math.Clamp(MathF.Floor(Math.Max(0, box.ScrollY) / stepY) * stepY, 0, maxContentTop);
        return SKRect.Create(box.AbsLeft + contentLeft, box.AbsTop + contentTop, tileWidth, tileHeight);
    }

    private static void AddNodeListToSignature(SceneLayoutCommit commit, ReadOnlySpan<SceneNodeId> ids, ref HashCode hash)
    {
        hash.Add(ids.Length);
        for (var index = 0; index < ids.Length; index++)
            AddNodeToSignature(commit, ids[index], ref hash);
    }

    private static void AddNodeToSignature(SceneLayoutCommit commit, SceneNodeId id, ref HashCode hash)
    {
        hash.Add(id);
        if (!commit.Nodes.TryGetValue(id, out var node) || !commit.Layout.TryGetValue(id, out var box))
            return;

        hash.Add(node);
        hash.Add(box);
        if (commit.TryGetPaintOverride(id, out var paintOverride))
            hash.Add(paintOverride);
        AddNodeListToSignature(commit, node.Children, ref hash);
    }

    private void PaintChildren(SKCanvas canvas, SceneLayoutCommit commit, ReadOnlySpan<SceneNodeId> childIds)
        => PaintChildren(canvas, commit, childIds, null);

    private void PaintChildren(SKCanvas canvas, SceneLayoutCommit commit, ReadOnlySpan<SceneNodeId> childIds, SKRect? subtreeCullRect)
    {
        for (var index = 0; index < childIds.Length; index++)
        {
            var childId = childIds[index];
            if (commit.Layout.TryGetValue(childId, out var childBox) && childBox.IsPositioned)
                continue;

            if (ShouldCullChildSubtree(commit, childId, childBox, subtreeCullRect))
                continue;

            PaintNode(canvas, commit, childId);
        }

        for (var index = 0; index < childIds.Length; index++)
        {
            var childId = childIds[index];
            if (!commit.Layout.TryGetValue(childId, out var childBox) || !childBox.IsPositioned)
                continue;

            if (ShouldCullChildSubtree(commit, childId, childBox, subtreeCullRect))
                continue;

            PaintNode(canvas, commit, childId);
        }
    }

    private static bool ShouldCullChildSubtree(SceneLayoutCommit commit, SceneNodeId childId, SceneLayoutBox? childBox, SKRect? subtreeCullRect)
    {
        if (childBox is null || subtreeCullRect is null || subtreeCullRect.Value.IsEmpty)
            return false;

        if (!commit.Nodes.TryGetValue(childId, out var node))
            return false;

        if (node.Children.Length > 0 && !CanCullDescendantsByOwnBounds(commit, node))
            return false;

        return !ResolveSelfPaintCullRect(childBox).IntersectsWith(subtreeCullRect.Value);
    }

    private static bool CanCullDescendantsByOwnBounds(SceneLayoutCommit commit, SceneGraphNode node)
    {
        for (var index = 0; index < node.Children.Length; index++)
        {
            var childId = node.Children[index];
            if (commit.Layout.TryGetValue(childId, out var childBox) && childBox.IsPositioned)
                return false;
        }

        return true;
    }

    private static bool HasPositionedChild(SceneLayoutCommit commit, SceneGraphNode node)
    {
        for (var index = 0; index < node.Children.Length; index++)
        {
            var childId = node.Children[index];
            if (commit.Layout.TryGetValue(childId, out var childBox) && childBox.IsPositioned)
                return true;
        }

        return false;
    }

    private void DrawBox(SKCanvas canvas, SceneLayoutBox box)
    {
        var geometry = ResolveBoxPaintGeometry(box);
        var hostAnimatedShader = IsHostAnimatedRuntimeShader(box);
        if (!hostAnimatedShader)
            DrawShadows(canvas, geometry, box);

        var usedFill = false;
        if (!hostAnimatedShader &&
            box.BackgroundShader is not null &&
            TryCreateRuntimeShader(box, out var runtimeShader))
        {
            fillPaint.Shader = runtimeShader;
            fillPaint.Color = SKColors.White;
            DrawRect(canvas, geometry.FillRect, geometry.FillRadius, fillPaint);
            fillPaint.Shader = null;
            usedFill = true;
        }
        else if (TryCreateGradientShader(box, out var gradientShader))
        {
            fillPaint.Shader = gradientShader;
            fillPaint.Color = SKColors.White;
            DrawRect(canvas, geometry.FillRect, geometry.FillRadius, fillPaint);
            fillPaint.Shader = null;
            usedFill = true;
        }

        if (!hostAnimatedShader && !usedFill && !string.IsNullOrWhiteSpace(box.BackgroundColor))
        {
            fillPaint.Shader = null;
            fillPaint.Color = ResolveColor(box.BackgroundColor, new SKColor(20, 26, 41, 255));
            DrawRect(canvas, geometry.FillRect, geometry.FillRadius, fillPaint);
        }

        if (!hostAnimatedShader && !string.IsNullOrWhiteSpace(box.BackgroundImageSource))
            DrawBackgroundImage(canvas, box, geometry);

        DrawBorder(canvas, box, geometry.BorderRect, geometry.BorderRadius);
    }

    private static SceneLayoutBox ApplyPaintOverride(SceneLayoutCommit commit, SceneNodeId id, SceneLayoutBox box)
    {
        if (!commit.TryGetPaintOverride(id, out var paintOverride))
            return box;

        var textStyle = box.TextStyle;
        if (paintOverride.TextColor is not null && textStyle is not null)
            textStyle = textStyle with { Color = paintOverride.TextColor };

        return box with
        {
            BackgroundColor = paintOverride.BackgroundColor ?? box.BackgroundColor,
            BorderColor = paintOverride.BorderColor ?? box.BorderColor,
            TextStyle = textStyle
        };
    }

    private void PaintAnimatedShaderNodes(SKCanvas canvas, SceneLayoutCommit commit, float elapsedSeconds)
    {
        foreach (var id in commit.HostAnimatedShaderRootIds)
            PaintAnimatedShaderRoot(canvas, commit, id, elapsedSeconds);
    }

    private void PaintAnimatedShaderRoot(SKCanvas canvas, SceneLayoutCommit commit, SceneNodeId id, float elapsedSeconds)
    {
        if (!commit.Nodes.TryGetValue(id, out var node))
            return;

        var ancestorIds = new List<SceneNodeId>();
        var parentId = node.ParentId;
        while (parentId is { } resolvedParentId)
        {
            ancestorIds.Add(resolvedParentId);
            if (!commit.Nodes.TryGetValue(resolvedParentId, out var parentNode))
                break;

            parentId = parentNode.ParentId;
        }

        ancestorIds.Reverse();
        var restoreCount = 0;
        foreach (var ancestorId in ancestorIds)
        {
            if (!commit.Layout.TryGetValue(ancestorId, out var ancestorBox))
                continue;

            if (ShouldClipChildren(ancestorBox))
            {
                canvas.Save();
                ClipBox(canvas, ancestorBox);
                restoreCount++;
            }

            if (ancestorBox.NodeKind == SceneNodeKind.ScrollView)
            {
                canvas.Save();
                canvas.Translate(-ancestorBox.ScrollX, -ancestorBox.ScrollY);
                restoreCount++;
            }
        }

        PaintAnimatedShaderSubtree(canvas, commit, id, elapsedSeconds);

        for (var index = 0; index < restoreCount; index++)
            canvas.Restore();
    }

    private void PaintAnimatedShaderSubtree(SKCanvas canvas, SceneLayoutCommit commit, SceneNodeId id, float elapsedSeconds)
    {
        if (!commit.Layout.TryGetValue(id, out var box) || !commit.Nodes.TryGetValue(id, out var node))
            return;

        var paintBox = ApplyPaintOverride(commit, id, box);
        var hostAnimatedShader = IsHostAnimatedRuntimeShader(box);
        if (hostAnimatedShader)
            DrawAnimatedShaderBox(canvas, box, elapsedSeconds);
        else
            DrawBox(canvas, paintBox);

        var clipPushed = false;
        if (ShouldClipChildren(box))
        {
            canvas.Save();
            clipPushed = true;
            ClipBox(canvas, box);
        }

        if (box.NodeKind == SceneNodeKind.Text && !string.IsNullOrEmpty(box.TextContent))
            DrawText(canvas, paintBox);
        else if (box.NodeKind == SceneNodeKind.TextInput)
            DrawTextInput(canvas, paintBox);
        else if (box.NodeKind == SceneNodeKind.Image)
            DrawImage(canvas, box);

        if (box.NodeKind == SceneNodeKind.ScrollView)
        {
            canvas.Save();
            canvas.Translate(-box.ScrollX, -box.ScrollY);
            PaintAnimatedShaderChildren(canvas, commit, node.Children, elapsedSeconds);
            canvas.Restore();
            if (clipPushed)
            {
                canvas.Restore();
                clipPushed = false;
            }

        }
        else
        {
            PaintAnimatedShaderChildren(canvas, commit, node.Children, elapsedSeconds);
        }

        if (clipPushed)
            canvas.Restore();
    }

    private void PaintAnimatedShaderChildren(SKCanvas canvas, SceneLayoutCommit commit, ReadOnlySpan<SceneNodeId> childIds, float elapsedSeconds)
    {
        for (var index = 0; index < childIds.Length; index++)
        {
            var childId = childIds[index];
            if (commit.Layout.TryGetValue(childId, out var childBox) && childBox.IsPositioned)
                continue;

            PaintAnimatedShaderSubtree(canvas, commit, childId, elapsedSeconds);
        }

        for (var index = 0; index < childIds.Length; index++)
        {
            var childId = childIds[index];
            if (!commit.Layout.TryGetValue(childId, out var childBox) || !childBox.IsPositioned)
                continue;

            PaintAnimatedShaderSubtree(canvas, commit, childId, elapsedSeconds);
        }
    }

    private void DrawAnimatedShaderBox(SKCanvas canvas, SceneLayoutBox box, float elapsedSeconds)
    {
        var geometry = ResolveBoxPaintGeometry(box);
        DrawShadows(canvas, geometry, box);
        if (!TryCreateRuntimeShader(box, out var runtimeShader, elapsedSeconds))
            return;

        fillPaint.Shader = runtimeShader;
        fillPaint.Color = SKColors.White;
        DrawRect(canvas, geometry.FillRect, geometry.FillRadius, fillPaint);
        fillPaint.Shader = null;
    }

    private bool TryCreateGradientShader(SceneLayoutBox box, out SKShader? shader)
    {
        shader = null;
        var spec = box.BackgroundGradient;
        if (spec is null || spec.Colors is null || spec.Colors.Length < 2)
            return false;

        var colors = new SKColor[spec.Colors.Length];
        for (var index = 0; index < spec.Colors.Length; index++)
            colors[index] = ResolveColor(spec.Colors[index], SKColors.Transparent);

        var stops = spec.Stops is { Length: > 0 } && spec.Stops.Length == colors.Length
            ? spec.Stops
            : null;

        if (spec.Kind == SceneGradientKind.Radial)
        {
            var center = new SKPoint(
                box.AbsLeft + box.Width * spec.CenterX,
                box.AbsTop + box.Height * spec.CenterY);
            var radius = Math.Max(1, Math.Min(box.Width, box.Height) * spec.Radius);
            shader = SKShader.CreateRadialGradient(center, radius, colors, stops, SKShaderTileMode.Clamp);
            return shader is not null;
        }

        var start = new SKPoint(
            box.AbsLeft + box.Width * spec.StartX,
            box.AbsTop + box.Height * spec.StartY);
        var end = new SKPoint(
            box.AbsLeft + box.Width * spec.EndX,
            box.AbsTop + box.Height * spec.EndY);
        shader = SKShader.CreateLinearGradient(start, end, colors, stops, SKShaderTileMode.Clamp);
        return shader is not null;
    }

    private bool TryCreateRuntimeShader(SceneLayoutBox box, out SKShader? shader, float elapsedSeconds = 0)
    {
        shader = null;
        var template = ResolveRuntimeShaderTemplate(box);
        if (template is null)
            return false;

        using var uniforms = new SKRuntimeEffectUniforms(template.Effect);
        if (template.HasResolution)
            uniforms.Add("resolution", new SKPoint(box.Width, box.Height));
        if (template.HasOrigin)
            uniforms.Add("origin", new SKPoint(box.AbsLeft, box.AbsTop));
        if (template.UsesHostTime)
            uniforms.Add("time", elapsedSeconds);
        foreach (var binding in template.UniformBindings)
        {
            AddCachedUniform(uniforms, binding);
        }

        shader = template.Effect.ToShader(uniforms);
        return shader is not null;
    }

    private RuntimeShaderTemplate? ResolveRuntimeShaderTemplate(SceneLayoutBox box)
    {
        var shader = box.BackgroundShader;
        if (shader is null || string.IsNullOrWhiteSpace(shader.Source))
            return null;

        var templateCacheKey = GetRuntimeShaderTemplateCacheKey(shader);
        if (string.IsNullOrWhiteSpace(templateCacheKey))
            return null;

        if (runtimeShaderTemplateCache.TryGetValue(templateCacheKey, out var cached))
            return cached;

        var effect = ResolveRuntimeEffect(shader.Source);
        if (effect is null)
        {
            runtimeShaderTemplateCache[templateCacheKey] = null;
            return null;
        }

        var template = BuildRuntimeShaderTemplate(effect, shader);

        runtimeShaderTemplateCache[templateCacheKey] = template;
        return template;
    }

    private RuntimeShaderTemplate BuildRuntimeShaderTemplate(SKRuntimeEffect effect, SceneRuntimeShader shader)
    {
        RuntimeShaderUniformBinding[] uniformBindings;
        if (shader.Uniforms is null || shader.Uniforms.Length == 0)
        {
            uniformBindings = [];
        }
        else
        {
            var bindings = new List<RuntimeShaderUniformBinding>(shader.Uniforms.Length);
            foreach (var uniform in shader.Uniforms)
            {
                if (shader.HostTime && string.Equals(uniform.Name, "time", StringComparison.Ordinal))
                    continue;

                var binding = CreateUniformBinding(effect, uniform);
                if (binding is not null)
                    bindings.Add(binding);
            }

            uniformBindings = bindings.Count == 0 ? [] : [.. bindings];
        }

        return new RuntimeShaderTemplate(
            effect,
            HasUniform(effect, "resolution"),
            HasUniform(effect, "origin"),
            shader.HostTime && HasUniform(effect, "time"),
            uniformBindings);
    }

    private static string GetRuntimeShaderTemplateCacheKey(SceneRuntimeShader shader)
    {
        var sourceKey = !string.IsNullOrWhiteSpace(shader.SourceId)
            ? "id:" + shader.SourceId
            : "src:" + shader.Source;
        if (shader.Uniforms is not { Length: > 0 })
            return sourceKey + "|hostTime:" + shader.HostTime;

        var builder = new System.Text.StringBuilder(sourceKey.Length + 64);
        builder.Append(sourceKey).Append("|hostTime:").Append(shader.HostTime ? '1' : '0');
        foreach (var uniform in shader.Uniforms)
        {
            builder.Append('|').Append(uniform.Name).Append(':').Append((int)uniform.Kind).Append('=');
            switch (uniform.Kind)
            {
                case SceneRuntimeShaderUniformKind.Int:
                    builder.Append(uniform.IntValue);
                    break;
                case SceneRuntimeShaderUniformKind.Float:
                    builder.Append(uniform.FloatValue);
                    break;
                case SceneRuntimeShaderUniformKind.Color:
                    builder.Append(uniform.ColorValue);
                    break;
                case SceneRuntimeShaderUniformKind.FloatArray:
                    if (uniform.FloatArrayValue is not null)
                    {
                        for (var index = 0; index < uniform.FloatArrayValue.Length; index++)
                        {
                            if (index > 0)
                                builder.Append(',');
                            builder.Append(uniform.FloatArrayValue[index]);
                        }
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    private bool IsHostAnimatedRuntimeShader(SceneLayoutBox box)
    {
        return ResolveRuntimeShaderTemplate(box) is { UsesHostTime: true };
    }

    private RuntimeShaderUniformBinding? CreateUniformBinding(SKRuntimeEffect effect, SceneRuntimeShaderUniform value)
    {
        if (!HasUniform(effect, value.Name))
            return null;

        switch (value.Kind)
        {
            case SceneRuntimeShaderUniformKind.Int:
                return RuntimeShaderUniformBinding.CreateInt(value.Name, value.IntValue);
            case SceneRuntimeShaderUniformKind.Float:
                return RuntimeShaderUniformBinding.CreateFloat(value.Name, value.FloatValue);
            case SceneRuntimeShaderUniformKind.Color:
                return RuntimeShaderUniformBinding.CreateColor(value.Name, ToColorF(ResolveColor(value.ColorValue, SKColors.White)));
            case SceneRuntimeShaderUniformKind.FloatArray:
                return CreateNumericArrayBinding(value.Name, value.FloatArrayValue);
            default:
                return null;
        }
    }

    private static RuntimeShaderUniformBinding? CreateNumericArrayBinding(string name, float[]? values)
    {
        if (values is null || values.Length == 0)
            return null;

        switch (values.Length)
        {
            case 2:
                return RuntimeShaderUniformBinding.CreatePoint(name, new SKPoint(values[0], values[1]));
            case 3:
                return RuntimeShaderUniformBinding.CreatePoint3(name, new SKPoint3(values[0], values[1], values[2]));
            default:
                return RuntimeShaderUniformBinding.CreateFloatArray(name, [.. values]);
        }
    }

    private static void AddCachedUniform(SKRuntimeEffectUniforms uniforms, RuntimeShaderUniformBinding binding)
    {
        switch (binding.Kind)
        {
            case RuntimeShaderUniformKind.Int:
                uniforms.Add(binding.Name, binding.IntValue);
                break;
            case RuntimeShaderUniformKind.Float:
                uniforms.Add(binding.Name, binding.FloatValue);
                break;
            case RuntimeShaderUniformKind.Color:
                uniforms.Add(binding.Name, binding.ColorValue);
                break;
            case RuntimeShaderUniformKind.Point:
                uniforms.Add(binding.Name, binding.PointValue);
                break;
            case RuntimeShaderUniformKind.Point3:
                uniforms.Add(binding.Name, binding.Point3Value);
                break;
            case RuntimeShaderUniformKind.FloatArray:
                uniforms.Add(binding.Name, binding.FloatArrayValue!);
                break;
        }
    }

    private SKRuntimeEffect? ResolveRuntimeEffect(string source)
    {
        if (shaderEffectCache.TryGetValue(source, out var cached))
            return cached;

        var compiled = SKRuntimeEffect.CreateShader(source, out _);
        shaderEffectCache[source] = compiled;
        return compiled;
    }

    private static bool HasUniform(SKRuntimeEffect effect, string name)
    {
        foreach (var uniformName in effect.Uniforms)
        {
            if (string.Equals(uniformName, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void DrawShadows(SKCanvas canvas, BoxPaintGeometry geometry, SceneLayoutBox box)
    {
        var shadows = box.BackgroundShadows;
        if (shadows is null || shadows.Length == 0)
            return;

        foreach (var shadow in shadows)
        {
            canvas.Save();
            ClipShadowInnerBox(canvas, geometry);

            shadowPaint.Color = ResolveColor(shadow.Color, new SKColor(15, 23, 42, 110));
            shadowPaint.ImageFilter = shadow.Blur > 0 ? GetBoxShadowBlurFilter(shadow.Blur) : null;

            var rect = geometry.BorderRect;
            rect.Offset(shadow.OffsetX, shadow.OffsetY);
            rect.Inflate(shadow.Spread, shadow.Spread);
            var radius = Math.Max(0, geometry.BorderRadius + shadow.Spread);
            if (radius > 0)
                canvas.DrawRoundRect(rect, radius, radius, shadowPaint);
            else
                canvas.DrawRect(rect, shadowPaint);

            canvas.Restore();
            shadowPaint.ImageFilter = null;
        }
    }

    private SKImageFilter GetBoxShadowBlurFilter(float blur)
    {
        var sigma = MathF.Max(0.001f, blur * 0.5f);
        if (boxShadowBlurFilterCache.TryGetValue(sigma, out var cached))
            return cached;

        var filter = SKImageFilter.CreateBlur(sigma, sigma);
        boxShadowBlurFilterCache[sigma] = filter;
        return filter;
    }

    private static void ClipShadowInnerBox(SKCanvas canvas, BoxPaintGeometry geometry)
    {
        if (geometry.BorderRadius > 0)
        {
            using var path = new SKPath();
            path.AddRoundRect(geometry.BorderRect, geometry.BorderRadius, geometry.BorderRadius);
            canvas.ClipPath(path, SKClipOperation.Difference, antialias: true);
            return;
        }

        canvas.ClipRect(geometry.BorderRect, SKClipOperation.Difference, antialias: true);
    }

    private void DrawText(SKCanvas canvas, SceneLayoutBox box)
    {
        if (box.Text is not { } text)
            return;

        var geometry = box.Geometry;
        var textStyle = text.TextStyle ?? DefaultTextStyle;
        textPaint.Color = ResolveColor(textStyle.Color, SKColors.White);
        var textAlign = MapTextAlign(textStyle.TextAlign);
        var lineHeight = text.LineHeight > 0 ? text.LineHeight : textResources.TextMeasurer.MeasureLineHeight(textStyle.Font);
        var textWidth = Math.Max(0, geometry.Width - geometry.PaddingLeft - geometry.PaddingRight);
        var layout = textResources.InputMetrics.CreateLayout(textStyle, textPaint, text.TextContent, lineHeight, textWidth);
        var contentTop = geometry.AbsTop + geometry.PaddingTop;
        var contentBottom = geometry.AbsTop + geometry.Height - geometry.PaddingBottom;
        var textX = geometry.AbsLeft + geometry.PaddingLeft;

        for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
        {
            if (!ShouldDrawTextLine(textStyle.WrapText, contentTop, contentBottom, lineIndex, lineHeight))
                break;

            var line = layout.Lines[lineIndex];
            var lineToDraw = textStyle.TextOverflowEllipsis && !textStyle.WrapText && lineIndex == 0 && line.Width > textWidth
                ? CreateEllipsizedLine(textStyle, line, textWidth, lineHeight)
                : line;
            var drawX = AlignTextX(textX, textWidth, lineToDraw.Width, textAlign);
            var underlineWidth = textStyle.Underline && !textStyle.WrapText
                ? Math.Max(lineToDraw.Width, textWidth)
                : lineToDraw.Width;
            DrawTextLine(canvas, lineToDraw, drawX, geometry.AbsTop + geometry.PaddingTop + ResolveTextBaselineOffset(lineToDraw, textStyle, lineHeight) + lineIndex * lineHeight, textStyle, underlineWidth);
            if (textStyle.TextOverflowEllipsis && !textStyle.WrapText)
                break;
        }
    }

    private TextInputMetrics.TextLineSpan CreateEllipsizedLine(
        SceneTextStyle textStyle,
        TextInputMetrics.TextLineSpan line,
        float textWidth,
        float lineHeight)
    {
        const string ellipsis = "\u2026";
        if (textWidth <= 0 || line.Text.Length == 0)
            return line;

        var cacheKey = CreateEllipsizedLineCacheKey(textStyle, line.Text, textWidth, lineHeight);
        if (ellipsizedLineCache.TryGetValue(cacheKey, out var cached))
        {
            EllipsizedLineCacheHits++;
            return cached;
        }

        EllipsizedLineCacheMisses++;
        var ellipsisLayout = textResources.InputMetrics.CreateLayout(textStyle with { TextOverflowEllipsis = false }, textPaint, ellipsis, lineHeight, float.PositiveInfinity);
        var ellipsisWidth = ellipsisLayout.Lines[0].Width;
        if (ellipsisWidth >= textWidth)
            return StoreEllipsizedLine(cacheKey, ellipsisLayout.Lines[0]);

        var allowedWidth = textWidth - ellipsisWidth;
        var cutoff = 0;
        for (var index = 0; index < line.CaretOffsets.Length; index++)
        {
            if (line.CaretOffsets[index] > allowedWidth)
                break;
            cutoff = line.CaretIndices[index];
        }

        var text = cutoff <= 0
            ? ellipsis
            : line.Text[..Math.Min(cutoff, line.Text.Length)].TrimEnd() + ellipsis;
        var ellipsizedLine = textResources.InputMetrics.CreateLayout(textStyle with { TextOverflowEllipsis = false }, textPaint, text, lineHeight, float.PositiveInfinity).Lines[0];
        return StoreEllipsizedLine(cacheKey, ellipsizedLine);
    }

    private float ResolveTextBaselineOffset(TextInputMetrics.TextLineSpan line, SceneTextStyle textStyle, float lineHeight)
    {
        if (line.Runs.Count == 0)
            return textStyle.FontSize;

        var ascent = 0f;
        var descent = 0f;
        for (var index = 0; index < line.Runs.Count; index++)
        {
            var metrics = GetFontMetrics(line.Runs[index].Typeface, textStyle.Font);
            if (!float.IsFinite(metrics.Ascent) || !float.IsFinite(metrics.Descent))
                continue;

            ascent = Math.Min(ascent, metrics.Ascent);
            descent = Math.Max(descent, metrics.Descent);
        }

        var glyphHeight = descent - ascent;
        if (!float.IsFinite(glyphHeight) || glyphHeight <= 0)
            return textStyle.FontSize;

        var leading = Math.Max(0, lineHeight - glyphHeight) * 0.5f;
        return leading - ascent;
    }

    private EllipsizedLineCacheKey CreateEllipsizedLineCacheKey(SceneTextStyle textStyle, string text, float textWidth, float lineHeight)
        => new(
            textResources.FontCatalog.CurrentVersion,
            text,
            QuantizePixel(textWidth),
            QuantizePixel(lineHeight),
            QuantizePixel(textStyle.FontSize),
            textStyle.Font.CacheIdentity,
            textStyle.Font.Weight,
            textStyle.Font.Italic);

    private TextInputMetrics.TextLineSpan StoreEllipsizedLine(EllipsizedLineCacheKey key, TextInputMetrics.TextLineSpan line)
    {
        if (ellipsizedLineCache.TryAdd(key, line))
        {
            ellipsizedLineCacheOrder.Enqueue(key);
            TrimEllipsizedLineCache();
        }

        return line;
    }

    private void TrimEllipsizedLineCache()
    {
        while (ellipsizedLineCache.Count > ellipsizedLineCacheLimit && ellipsizedLineCacheOrder.Count > 0)
        {
            var oldestKey = ellipsizedLineCacheOrder.Dequeue();
            ellipsizedLineCache.Remove(oldestKey);
        }
    }

    private void DrawTextInput(SKCanvas canvas, SceneLayoutBox box)
    {
        if (box.TextInput is not { } input)
            return;

        var geometry = box.Geometry;
        var paint = box.Paint;
        var interaction = box.Interaction;
        var textStyle = input.TextStyle ?? DefaultTextInputStyle;
        var isSelect = interaction.ControlKind == SceneControlKind.Select;
        var value = input.TextContent;
        var compositionText = input.CompositionText;
        var hasComposition = compositionText.Length > 0;
        var composedValue = hasComposition
            ? value.Insert(Math.Clamp(input.CompositionStart, 0, value.Length), compositionText)
            : value;
        var showPlaceholder = value.Length == 0 && !hasComposition && !string.IsNullOrEmpty(input.PlaceholderText);
        var displayText = showPlaceholder ? input.PlaceholderText : composedValue;
        var lineHeight = input.LineHeight > 0 ? input.LineHeight : textStyle.FontSize * 1.35f;

        var resolvedInputTextColor = ResolveTextInputTextColor(showPlaceholder, input.PlaceholderColor, textStyle.Color, paint.BackgroundColor);
        textPaint.Color = resolvedInputTextColor;
        var textWidth = Math.Max(0, geometry.Width - geometry.PaddingLeft - geometry.PaddingRight);
        var displayLayout = textResources.InputMetrics.CreateLayout(textStyle, textPaint, displayText, lineHeight, textWidth);
        var valueLayout = showPlaceholder ? null : textResources.InputMetrics.CreateLayout(textStyle, textPaint, value, lineHeight, textWidth);

        var textX = geometry.AbsLeft + geometry.PaddingLeft;
        var contentTop = geometry.AbsTop + geometry.PaddingTop;
        var contentBottom = geometry.AbsTop + geometry.Height - geometry.PaddingBottom;
        canvas.Save();
        ClipBox(canvas, box);
        var selectionStart = Math.Clamp(Math.Min(input.SelectionStart, input.SelectionEnd), 0, value.Length);
        var selectionEnd = Math.Clamp(Math.Max(input.SelectionStart, input.SelectionEnd), 0, value.Length);
        if (!showPlaceholder && selectionStart != selectionEnd && valueLayout is not null)
        {
            fillPaint.Color = new SKColor(96, 165, 250, 96);
            foreach (var rect in textResources.InputMetrics.GetSelectionRects(valueLayout, selectionStart, selectionEnd))
            {
                canvas.DrawRect(
                    textX + rect.Left,
                    geometry.AbsTop + geometry.PaddingTop + rect.Top,
                    Math.Max(1, rect.Right - rect.Left),
                    Math.Max(textStyle.FontSize + 4, lineHeight),
                     fillPaint);
            }
        }

        if (!showPlaceholder && hasComposition)
        {
            fillPaint.Color = ResolveColor(input.CompositionUnderlineColor, resolvedInputTextColor.WithAlpha(190));
            var compositionStart = Math.Clamp(input.CompositionStart, 0, composedValue.Length);
            var compositionEnd = Math.Clamp(input.CompositionStart + compositionText.Length, 0, composedValue.Length);
            foreach (var rect in textResources.InputMetrics.GetSelectionRects(
                         displayLayout,
                         compositionStart,
                         compositionEnd))
            {
                var underlineY = geometry.AbsTop + geometry.PaddingTop + rect.Top + Math.Max(textStyle.FontSize + 4, lineHeight) - 3;
                DrawUnderline(canvas, textX + rect.Left, textX + rect.Right, underlineY, 1, fillPaint);
            }

            var selectedStart = compositionStart + Math.Clamp(input.CompositionSelectionStart, 0, compositionText.Length);
            var selectedLength = Math.Clamp(
                input.CompositionSelectionLength,
                0,
                compositionText.Length - Math.Clamp(input.CompositionSelectionStart, 0, compositionText.Length));
            if (selectedLength > 0)
            {
                var selectedEnd = Math.Clamp(selectedStart + selectedLength, compositionStart, compositionEnd);
                fillPaint.Color = ResolveColor(input.CompositionSelectionUnderlineColor, resolvedInputTextColor);
                foreach (var rect in textResources.InputMetrics.GetSelectionRects(displayLayout, selectedStart, selectedEnd))
                {
                    var underlineY = geometry.AbsTop + geometry.PaddingTop + rect.Top + Math.Max(textStyle.FontSize + 4, lineHeight) - 2;
                    DrawUnderline(canvas, textX + rect.Left, textX + rect.Right, underlineY, 2f, fillPaint);
                }
            }
        }

        for (var lineIndex = 0; lineIndex < displayLayout.Lines.Count; lineIndex++)
        {
            if (!ShouldDrawTextLine(input.Multiline, contentTop, contentBottom, lineIndex, lineHeight))
                break;

            var line = displayLayout.Lines[lineIndex];
            DrawTextLine(canvas, line, textX, geometry.AbsTop + geometry.PaddingTop + textStyle.FontSize + lineIndex * lineHeight, textStyle, line.Width);
        }

        if (isSelect)
            DrawSelectArrow(canvas, box, resolvedInputTextColor, textStyle.FontSize);

        if (!input.IsFocused || isSelect)
        {
            canvas.Restore();
            return;
        }

        var caretLayout = showPlaceholder
            ? textResources.InputMetrics.CreateLayout(textStyle, textPaint, value, lineHeight, textWidth)
            : displayLayout;
        var caretIndex = hasComposition
            ? Math.Clamp(input.CompositionStart + Math.Clamp(input.CompositionCursorOffset, 0, compositionText.Length), 0, composedValue.Length)
            : input.CaretIndex;
        if (!hasComposition || input.CompositionSelectionLength == 0)
        {
            var caret = textResources.InputMetrics.GetCaretPosition(caretLayout, caretIndex);
            fillPaint.Color = resolvedInputTextColor;
            var caretTop = geometry.AbsTop + geometry.PaddingTop + caret.Y;
            var caretHeight = Math.Max(textStyle.FontSize + 4, lineHeight);
            if (ShouldDrawTextCaret(contentTop, contentBottom, caretTop, caretHeight))
                canvas.DrawRect(textX + caret.X, caretTop, 1.5f, caretHeight, fillPaint);
        }

        canvas.Restore();
        //DrawImeIndicator(canvas, box);
    }

    private void DrawSelectArrow(SKCanvas canvas, SceneLayoutBox box, SKColor color, float fontSize)
    {
        var arrowInset = Math.Max(12, Math.Min(18, box.PaddingRight * 0.5f));
        var centerX = box.AbsLeft + box.Width - arrowInset;
        var centerY = box.AbsTop + box.Height * 0.5f;
        var size = Math.Clamp(fontSize * 0.28f, 4f, 7f);

        strokePaint.Color = color;
        strokePaint.Style = SKPaintStyle.Stroke;
        strokePaint.StrokeWidth = Math.Clamp(fontSize / 12f, 1.25f, 2f);
        strokePaint.StrokeCap = SKStrokeCap.Round;
        strokePaint.IsAntialias = true;

        canvas.DrawLine(centerX - size, centerY - size * 0.45f, centerX, centerY + size * 0.45f, strokePaint);
        canvas.DrawLine(centerX, centerY + size * 0.45f, centerX + size, centerY - size * 0.45f, strokePaint);
    }

    private void DrawImeIndicator(SKCanvas canvas, SceneLayoutBox box)
    {
        if (!box.IsFocused)
            return;

        var indicator = string.IsNullOrWhiteSpace(box.ImeIndicator) ? "A" : box.ImeIndicator;
        var trimmedIndicator = indicator.Trim();
        var indicatorTypeface = textResources.FontCatalog.ResolveTypefaceForText(null, 600, trimmedIndicator);
        using var indicatorFont = SkiaFontSynthesis.CreateFont(indicatorTypeface, 12, 600, italic: false);
        using var indicatorPaint = new SKPaint
        {
            IsAntialias = true,
            Color = box.ImeOpen ? SKColors.White : new SKColor(148, 163, 184, 255)
        };
        var badgePaddingX = 8f;
        var textWidth = indicatorFont.MeasureText(trimmedIndicator, indicatorPaint);
        var badgeWidth = textWidth + badgePaddingX * 2;
        var badgeHeight = 20f;
        var badgeLeft = box.AbsLeft + box.Width - badgeWidth - 8;
        var badgeTop = box.AbsTop - badgeHeight - 6;
        if (badgeTop < box.AbsTop + 4)
            badgeTop = box.AbsTop + 6;
        using var badgePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = box.ImeOpen ? new SKColor(37, 99, 235, 255) : new SKColor(15, 23, 42, 235)
        };
        canvas.DrawRoundRect(SKRect.Create(badgeLeft, badgeTop, badgeWidth, badgeHeight), 10, 10, badgePaint);
        canvas.DrawText(trimmedIndicator, badgeLeft + badgePaddingX, badgeTop + 14.5f, indicatorFont, indicatorPaint);
    }

    private static void DrawUnderline(SKCanvas canvas, float left, float right, float y, float thickness, SKPaint paint)
    {
        if (right <= left)
            return;

        canvas.DrawRect(left, y, Math.Max(1, right - left), thickness, paint);
    }

    private void DrawImage(SKCanvas canvas, SceneLayoutBox box)
    {
        if (box.Width <= 0 || box.Height <= 0)
            return;

        var geometry = ResolveBoxPaintGeometry(box);
        DrawShadows(canvas, geometry, box);
        var destinationRect = geometry.FillRect;
        var imageState = WebImageCache.Resolve(box.ImageSource ?? string.Empty);
        if (imageState.State != WebImageCacheState.Ready)
        {
            if (imageState.State == WebImageCacheState.Pending)
                recordingContainsPendingImages = true;
            if (imageState.State == WebImageCacheState.Pending && TryDrawPlaceholderImage(canvas, box, destinationRect))
                return;
            DrawImagePlaceholder(canvas, box, destinationRect, imageState.State == WebImageCacheState.Failed);
            return;
        }

        var image = SkiaImageAssetCache.Resolve(imageState.LocalPath);
        if (image.State != SkiaImageAssetState.Ready || image.Asset is null)
        {
            if (image.State == SkiaImageAssetState.Pending)
                recordingContainsPendingImages = true;
            if (image.State == SkiaImageAssetState.Pending && TryDrawPlaceholderImage(canvas, box, destinationRect))
                return;
            DrawImagePlaceholder(canvas, box, destinationRect, isError: image.State == SkiaImageAssetState.Failed);
            return;
        }

        DrawResolvedImage(canvas, image.Asset, destinationRect, box.ImageFit, geometry.FillRadius);
    }

    private void DrawBackgroundImage(SKCanvas canvas, SceneLayoutBox box, BoxPaintGeometry geometry)
    {
        if (geometry.FillRect.Width <= 0 || geometry.FillRect.Height <= 0)
            return;

        var imageState = WebImageCache.Resolve(box.BackgroundImageSource ?? string.Empty);
        if (imageState.State != WebImageCacheState.Ready)
        {
            if (imageState.State == WebImageCacheState.Pending)
                recordingContainsPendingImages = true;
            return;
        }

        var image = SkiaImageAssetCache.Resolve(imageState.LocalPath);
        if (image.State != SkiaImageAssetState.Ready || image.Asset is null)
        {
            if (image.State == SkiaImageAssetState.Pending)
                recordingContainsPendingImages = true;
            return;
        }

        if (string.Equals(box.BackgroundImageFit, "repeat", StringComparison.OrdinalIgnoreCase))
            DrawRepeatedImage(canvas, image.Asset, geometry.FillRect, geometry.FillRadius, box.ScrollX, box.ScrollY);
        else
            DrawResolvedImage(canvas, image.Asset, geometry.FillRect, box.BackgroundImageFit ?? "cover", geometry.FillRadius);
    }

    private void DrawImagePlaceholder(SKCanvas canvas, SceneLayoutBox box, SKRect destinationRect, bool isError)
    {
        fillPaint.Color = ResolveColor(
            box.BackgroundColor,
            isError ? new SKColor(69, 10, 10, 255) : new SKColor(15, 23, 42, 255));
        var geometry = ResolveBoxPaintGeometry(box);
        DrawRect(canvas, geometry.FillRect, geometry.FillRadius, fillPaint);

        strokePaint.Color = ResolveColor(
            box.BorderColor,
            isError ? new SKColor(248, 113, 113, 255) : new SKColor(71, 85, 105, 255));
        strokePaint.StrokeWidth = Math.Max(1, box.BorderWidth > 0 ? box.BorderWidth : 1.5f);
        DrawRect(canvas, geometry.BorderRect, geometry.BorderRadius, strokePaint);

        if (isError)
        {
            canvas.DrawLine(destinationRect.Left + 12, destinationRect.Top + 12, destinationRect.Right - 12, destinationRect.Bottom - 12, strokePaint);
            canvas.DrawLine(destinationRect.Right - 12, destinationRect.Top + 12, destinationRect.Left + 12, destinationRect.Bottom - 12, strokePaint);
        }
        else
        {
            fillPaint.Color = new SKColor(148, 163, 184, 64);
            var insetRect = destinationRect;
            insetRect.Inflate(-Math.Min(24, destinationRect.Width * 0.14f), -Math.Min(24, destinationRect.Height * 0.14f));
            canvas.DrawRoundRect(insetRect, Math.Min(14, box.BorderRadius + 6), Math.Min(14, box.BorderRadius + 6), fillPaint);
        }
    }

    private bool TryDrawPlaceholderImage(SKCanvas canvas, SceneLayoutBox box, SKRect destinationRect)
    {
        if (string.IsNullOrWhiteSpace(box.ImagePlaceholderSource))
            return false;

        var placeholderPath = WebImageCache.Resolve(box.ImagePlaceholderSource);
        if (placeholderPath.State != WebImageCacheState.Ready)
            return false;

        var placeholderImage = SkiaImageAssetCache.Resolve(placeholderPath.LocalPath);
        if (placeholderImage.State != SkiaImageAssetState.Ready || placeholderImage.Asset is null)
            return false;

        var geometry = ResolveBoxPaintGeometry(box);
        DrawResolvedImage(canvas, placeholderImage.Asset, geometry.FillRect, box.ImageFit, geometry.FillRadius);
        return true;
    }

    private static void DrawResolvedImage(SKCanvas canvas, SkiaImageAsset asset, SKRect destinationRect, string? fit, float borderRadius)
    {
        var sourceRect = asset.SourceRect;
        var fittedRect = ExpandImageRectToAvoidSamplingGaps(CalculateImageRect(sourceRect, destinationRect, fit), destinationRect);

        canvas.Save();
        if (borderRadius > 0)
            canvas.ClipRoundRect(new SKRoundRect(destinationRect, borderRadius, borderRadius), antialias: true);
        else
            canvas.ClipRect(destinationRect);

        if (asset.RasterImage is not null)
        {
            canvas.DrawImage(asset.RasterImage, sourceRect, fittedRect);
        }
        else if (asset.VectorPicture is not null)
        {
            DrawVectorImage(canvas, asset.VectorPicture, sourceRect, fittedRect);
        }

        canvas.Restore();
    }

    private static void DrawRepeatedImage(SKCanvas canvas, SkiaImageAsset asset, SKRect destinationRect, float borderRadius, float scrollX = 0, float scrollY = 0)
    {
        var sourceRect = asset.SourceRect;
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
            return;

        canvas.Save();
        if (borderRadius > 0)
            canvas.ClipRoundRect(new SKRoundRect(destinationRect, borderRadius, borderRadius), antialias: true);
        else
            canvas.ClipRect(destinationRect);

        if (asset.RasterImage is not null)
        {
            var matrix = SKMatrix.CreateTranslation(-scrollX, -scrollY);
            using var shader = asset.RasterImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, matrix);
            using var paint = new SKPaint
            {
                IsAntialias = false,
                Shader = shader,
                BlendMode = SKBlendMode.Src
            };
            canvas.DrawRect(destinationRect, paint);
        }
        else
        {
            var startX = destinationRect.Left - PositiveModulo(scrollX, sourceRect.Width);
            var startY = destinationRect.Top - PositiveModulo(scrollY, sourceRect.Height);
            for (var y = startY; y < destinationRect.Bottom; y += sourceRect.Height)
            {
                for (var x = startX; x < destinationRect.Right; x += sourceRect.Width)
                {
                    var tileRect = SKRect.Create(x, y, sourceRect.Width, sourceRect.Height);
                    if (asset.VectorPicture is not null)
                        DrawVectorImage(canvas, asset.VectorPicture, sourceRect, tileRect);
                }
            }
        }

        canvas.Restore();
    }

    private static float PositiveModulo(float value, float divisor)
    {
        if (divisor <= 0)
            return 0;
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private void DrawVerticalScrollBar(SKCanvas canvas, SceneLayoutBox box)
    {
        var metrics = ResolveVerticalScrollBar(box);
        if (metrics is null)
            return;

        fillPaint.Shader = null;
        fillPaint.Color = ResolveColor(box.ScrollBarTrackColor, new SKColor(31, 31, 31, 255));
        canvas.DrawRect(box.AbsLeft + box.Width - box.ScrollBarWidth, box.AbsTop, box.ScrollBarWidth, box.Height, fillPaint);
        fillPaint.Color = ResolveColor(box.ScrollBarThumbColor, new SKColor(180, 180, 180, 255));
        canvas.DrawRoundRect(metrics.Value.ThumbRect, metrics.Value.Radius, metrics.Value.Radius, fillPaint);
    }

    public void PaintScrollBars(SKCanvas canvas, SceneLayoutCommit commit, float viewportScale, int presentationWidth, int presentationHeight)
    {
        var scale = Math.Max(0.001f, viewportScale);
        canvas.Save();
        if (Math.Abs(scale - 1f) > 0.001f)
            canvas.Scale(scale);
        PaintNestedScrollBars(canvas, commit, commit.RootId, hasScrollAncestor: false);
        canvas.Restore();

        PaintTopLevelScrollBars(canvas, commit, commit.RootId, scale, presentationWidth, presentationHeight, hasScrollAncestor: false);
    }

    private void PaintNestedScrollBars(SKCanvas canvas, SceneLayoutCommit commit, SceneNodeId id, bool hasScrollAncestor)
    {
        if (!commit.Layout.TryGetValue(id, out var box) || !commit.Nodes.TryGetValue(id, out var node))
            return;

        if (box.NodeKind == SceneNodeKind.ScrollView)
        {
            canvas.Save();
            ClipBox(canvas, box);
            canvas.Translate(-box.ScrollX, -box.ScrollY);
            for (var index = 0; index < node.Children.Length; index++)
                PaintNestedScrollBars(canvas, commit, node.Children[index], hasScrollAncestor: true);
            canvas.Restore();

            if (hasScrollAncestor)
            {
                DrawVerticalScrollBar(canvas, box);
                DrawHorizontalScrollBar(canvas, box);
            }

            return;
        }

        for (var index = 0; index < node.Children.Length; index++)
            PaintNestedScrollBars(canvas, commit, node.Children[index], hasScrollAncestor);
    }

    private void PaintTopLevelScrollBars(SKCanvas canvas, SceneLayoutCommit commit, SceneNodeId id, float viewportScale, int presentationWidth, int presentationHeight, bool hasScrollAncestor)
    {
        if (!commit.Layout.TryGetValue(id, out var box) || !commit.Nodes.TryGetValue(id, out var node))
            return;

        var isScrollView = box.NodeKind == SceneNodeKind.ScrollView;
        if (isScrollView && !hasScrollAncestor)
        {
            DrawPresentationVerticalScrollBar(canvas, box, viewportScale, presentationWidth, presentationHeight);
            DrawPresentationHorizontalScrollBar(canvas, box, viewportScale, presentationWidth, presentationHeight);
        }

        for (var index = 0; index < node.Children.Length; index++)
            PaintTopLevelScrollBars(canvas, commit, node.Children[index], viewportScale, presentationWidth, presentationHeight, hasScrollAncestor || isScrollView);
    }

    private void DrawPresentationVerticalScrollBar(SKCanvas canvas, SceneLayoutBox box, float viewportScale, int presentationWidth, int presentationHeight)
    {
        var metrics = ResolvePresentationVerticalScrollBar(box, viewportScale, presentationWidth, presentationHeight);
        if (metrics is null)
            return;

        fillPaint.Shader = null;
        fillPaint.Color = ResolveColor(box.ScrollBarTrackColor, new SKColor(31, 31, 31, 255));
        canvas.DrawRect(metrics.Value.GutterRect, fillPaint);
        fillPaint.Color = ResolveColor(box.ScrollBarThumbColor, new SKColor(180, 180, 180, 255));
        canvas.DrawRoundRect(metrics.Value.ThumbRect, metrics.Value.Radius, metrics.Value.Radius, fillPaint);
    }

    private void DrawPresentationHorizontalScrollBar(SKCanvas canvas, SceneLayoutBox box, float viewportScale, int presentationWidth, int presentationHeight)
    {
        var metrics = ResolvePresentationHorizontalScrollBar(box, viewportScale, presentationWidth, presentationHeight);
        if (metrics is null)
            return;

        fillPaint.Shader = null;
        fillPaint.Color = ResolveColor(box.ScrollBarTrackColor, new SKColor(31, 31, 31, 255));
        canvas.DrawRect(metrics.Value.GutterRect, fillPaint);
        fillPaint.Color = ResolveColor(box.ScrollBarThumbColor, new SKColor(180, 180, 180, 255));
        canvas.DrawRoundRect(metrics.Value.ThumbRect, metrics.Value.Radius, metrics.Value.Radius, fillPaint);
    }

    private void DrawHorizontalScrollBar(SKCanvas canvas, SceneLayoutBox box)
    {
        var metrics = ResolveHorizontalScrollBar(box);
        if (metrics is null)
            return;

        fillPaint.Shader = null;
        fillPaint.Color = ResolveColor(box.ScrollBarTrackColor, new SKColor(31, 31, 31, 255));
        canvas.DrawRect(box.AbsLeft, box.AbsTop + box.Height - box.ScrollBarWidth, box.Width, box.ScrollBarWidth, fillPaint);
        fillPaint.Color = ResolveColor(box.ScrollBarThumbColor, new SKColor(180, 180, 180, 255));
        canvas.DrawRoundRect(metrics.Value.ThumbRect, metrics.Value.Radius, metrics.Value.Radius, fillPaint);
    }

    internal static BoxPaintGeometry ResolveBoxPaintGeometry(SceneLayoutBox box)
    {
        var outerRect = new SKRect(box.AbsLeft, box.AbsTop, box.AbsLeft + box.Width, box.AbsTop + box.Height);
        var borderWidth = Math.Max(0, box.BorderWidth);
        var fillRect = outerRect;
        fillRect.Inflate(-borderWidth, -borderWidth);
        if (fillRect.Right < fillRect.Left)
            fillRect.Right = fillRect.Left;
        if (fillRect.Bottom < fillRect.Top)
            fillRect.Bottom = fillRect.Top;

        var borderRect = outerRect;
        borderRect.Inflate(-borderWidth * 0.5f, -borderWidth * 0.5f);
        if (borderRect.Right < borderRect.Left)
            borderRect.Right = borderRect.Left;
        if (borderRect.Bottom < borderRect.Top)
            borderRect.Bottom = borderRect.Top;

        return new BoxPaintGeometry(
            fillRect,
            borderRect,
            Math.Max(0, box.BorderRadius - borderWidth),
            Math.Max(0, box.BorderRadius - borderWidth * 0.5f));
    }

    private static void DrawRect(SKCanvas canvas, SKRect rect, float radius, SKPaint paint)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        if (radius > 0)
        {
            radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) * 0.5f);
            canvas.DrawRoundRect(rect, radius, radius, paint);
            return;
        }

        canvas.DrawRect(rect, paint);
    }

    private void DrawBorder(SKCanvas canvas, SceneLayoutBox box, SKRect rect, float radius)
    {
        if (box.Border is { } border)
        {
            if (radius > 0 && TryResolveUniformBorder(border, out var uniformWidth, out var uniformStyle, out var uniformColor))
            {
                DrawUniformBorder(canvas, rect, radius, uniformWidth, uniformStyle, uniformColor);
                return;
            }

            DrawBorderSide(canvas, rect.Left, rect.Top, rect.Left, rect.Bottom, border.LeftWidth, border.LeftStyle, border.LeftColor);
            DrawBorderSide(canvas, rect.Left, rect.Top, rect.Right, rect.Top, border.TopWidth, border.TopStyle, border.TopColor);
            DrawBorderSide(canvas, rect.Right, rect.Top, rect.Right, rect.Bottom, border.RightWidth, border.RightStyle, border.RightColor);
            DrawBorderSide(canvas, rect.Left, rect.Bottom, rect.Right, rect.Bottom, border.BottomWidth, border.BottomStyle, border.BottomColor);
            return;
        }

        if (box.BorderWidth <= 0 ||
            box.BorderStyle == SceneBorderStyle.None ||
            string.IsNullOrWhiteSpace(box.BorderColor))
        {
            return;
        }

        strokePaint.Color = ResolveColor(box.BorderColor, new SKColor(59, 130, 246, 255));
        strokePaint.StrokeWidth = box.BorderWidth;
        strokePaint.PathEffect = null;
        strokePaint.StrokeCap = SKStrokeCap.Butt;

        if (box.BorderStyle == SceneBorderStyle.Dotted)
        {
            using var dottedEffect = SKPathEffect.CreateDash(
                [Math.Max(1f, box.BorderWidth), Math.Max(2f, box.BorderWidth * 2.2f)],
                0);
            strokePaint.PathEffect = dottedEffect;
            strokePaint.StrokeCap = SKStrokeCap.Round;
            DrawRect(canvas, rect, radius, strokePaint);
            strokePaint.PathEffect = null;
            strokePaint.StrokeCap = SKStrokeCap.Butt;
            return;
        }

        DrawRect(canvas, rect, radius, strokePaint);
    }

    private void DrawUniformBorder(
        SKCanvas canvas,
        SKRect rect,
        float radius,
        float borderWidth,
        SceneBorderStyle borderStyle,
        string? borderColor)
    {
        if (borderWidth <= 0 ||
            borderStyle == SceneBorderStyle.None ||
            string.IsNullOrWhiteSpace(borderColor))
        {
            return;
        }

        strokePaint.Color = ResolveColor(borderColor, new SKColor(59, 130, 246, 255));
        strokePaint.StrokeWidth = borderWidth;
        strokePaint.PathEffect = null;
        strokePaint.StrokeCap = SKStrokeCap.Butt;

        if (borderStyle == SceneBorderStyle.Dotted)
        {
            using var dottedEffect = SKPathEffect.CreateDash(
                [Math.Max(1f, borderWidth), Math.Max(2f, borderWidth * 2.2f)],
                0);
            strokePaint.PathEffect = dottedEffect;
            strokePaint.StrokeCap = SKStrokeCap.Round;
            DrawRect(canvas, rect, radius, strokePaint);
            strokePaint.PathEffect = null;
            strokePaint.StrokeCap = SKStrokeCap.Butt;
            return;
        }

        DrawRect(canvas, rect, radius, strokePaint);
    }

    private static bool TryResolveUniformBorder(
        SceneBoxBorder border,
        out float width,
        out SceneBorderStyle style,
        out string? color)
    {
        width = border.LeftWidth;
        style = border.LeftStyle;
        color = border.LeftColor;

        return SameBorderValue(border.LeftWidth, border.TopWidth) &&
               SameBorderValue(border.LeftWidth, border.RightWidth) &&
               SameBorderValue(border.LeftWidth, border.BottomWidth) &&
               border.LeftStyle == border.TopStyle &&
               border.LeftStyle == border.RightStyle &&
               border.LeftStyle == border.BottomStyle &&
               border.LeftColor == border.TopColor &&
               border.LeftColor == border.RightColor &&
               border.LeftColor == border.BottomColor;
    }

    private static bool SameBorderValue(float left, float right)
        => Math.Abs(left - right) <= 0.001f;

    private void DrawBorderSide(
        SKCanvas canvas,
        float x0,
        float y0,
        float x1,
        float y1,
        float width,
        SceneBorderStyle style,
        string? color)
    {
        if (width <= 0 ||
            style == SceneBorderStyle.None ||
            string.IsNullOrWhiteSpace(color))
        {
            return;
        }

        strokePaint.Color = ResolveColor(color, new SKColor(59, 130, 246, 255));
        strokePaint.StrokeWidth = width;
        strokePaint.PathEffect = null;
        strokePaint.StrokeCap = SKStrokeCap.Butt;

        if (style == SceneBorderStyle.Dotted)
        {
            using var dottedEffect = SKPathEffect.CreateDash(
                [Math.Max(1f, width), Math.Max(2f, width * 2.2f)],
                0);
            strokePaint.PathEffect = dottedEffect;
            strokePaint.StrokeCap = SKStrokeCap.Round;
            canvas.DrawLine(x0, y0, x1, y1, strokePaint);
            strokePaint.PathEffect = null;
            strokePaint.StrokeCap = SKStrokeCap.Butt;
            return;
        }

        canvas.DrawLine(x0, y0, x1, y1, strokePaint);
    }

    private static void ClipBox(SKCanvas canvas, SceneLayoutBox box)
    {
        var geometry = ResolveBoxPaintGeometry(box);
        var rect = geometry.FillRect;
        if (box.NodeKind == SceneNodeKind.ScrollView && SceneScrollBarLayout.ResolveVerticalScrollBar(box) is not null)
            rect.Right = Math.Max(rect.Left, rect.Right - Math.Max(0, box.ScrollBarWidth));
        if (box.NodeKind == SceneNodeKind.ScrollView && SceneScrollBarLayout.ResolveHorizontalScrollBar(box) is not null)
            rect.Bottom = Math.Max(rect.Top, rect.Bottom - Math.Max(0, box.ScrollBarWidth));
        if (geometry.FillRadius > 0)
        {
            canvas.ClipRoundRect(new SKRoundRect(rect, geometry.FillRadius, geometry.FillRadius), antialias: true);
            return;
        }

        canvas.ClipRect(rect);
    }

    private static bool ShouldClipChildren(SceneLayoutBox box)
    {
        return box.IsScrollContainer || box.ClipContent;
    }

    private static bool ShouldCullSelfPaint(SKCanvas canvas, SceneLayoutBox box)
    {
        if (box.Width <= 0 || box.Height <= 0)
            return true;

        return canvas.QuickReject(ResolveSelfPaintCullRect(box));
    }

    private static SKRect ResolveSelfPaintCullRect(SceneLayoutBox box)
    {
        var left = box.AbsLeft;
        var top = box.AbsTop;
        var right = box.AbsLeft + box.Width;
        var bottom = box.AbsTop + box.Height;
        var padding = Math.Max(4f, box.BorderWidth);
        if (box.BackgroundShadows is { Length: > 0 })
            padding += 32f;
        if (box.TextStyle?.TextShadows is { Length: > 0 })
            padding += 32f;

        return new SKRect(left - padding, top - padding, right + padding, bottom + padding);
    }

    private SKColor ResolveColor(string? color, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
            return fallback;

        if (colorCache.TryGetValue(color, out var cached))
            return cached;

        var resolved = ParseColorOrFallback(color, fallback);
        colorCache[color] = resolved;
        return resolved;
    }

    private static SKTextAlign MapTextAlign(SceneTextAlign value)
    {
        return value switch
        {
            SceneTextAlign.Center => SKTextAlign.Center,
            SceneTextAlign.Right => SKTextAlign.Right,
            _ => SKTextAlign.Left
        };
    }

    private void DrawTextLine(SKCanvas canvas, TextInputMetrics.TextLineSpan line, float x, float baselineY, SceneTextStyle textStyle, float underlineWidth)
    {
        if (textStyle.TextShadows is { Length: > 0 } shadows)
            DrawTextLineShadows(canvas, line, x, baselineY, textStyle, shadows);

        var cursorX = x;
        var textColor = textPaint.Color;
        foreach (var run in line.Runs)
        {
            var blob = GetOrCreateTextBlob(run, textStyle, textColor, out var disposeBlob);
            try
            {
                if (blob is not null)
                    canvas.DrawText(blob, cursorX, baselineY, textPaint);
            }
            finally
            {
                if (disposeBlob)
                    blob?.Dispose();
            }

            cursorX += run.Width;
        }

        if (textStyle.Underline && underlineWidth > 0)
        {
            var underline = ResolveUnderlineStroke(line, textStyle);
            strokePaint.Shader = null;
            strokePaint.Color = textPaint.Color;
            strokePaint.StrokeWidth = underline.Thickness;
            strokePaint.IsAntialias = true;
            var underlineY = baselineY + underline.Offset;
            canvas.DrawLine(x, underlineY, x + underlineWidth, underlineY, strokePaint);
        }
    }

    private UnderlineStroke ResolveUnderlineStroke(TextInputMetrics.TextLineSpan line, SceneTextStyle textStyle)
    {
        var fallbackThickness = Math.Max(1, textStyle.FontSize / 16f);
        var fallbackOffset = Math.Max(2, textStyle.FontSize * 0.14f);
        var offset = 0f;
        var thickness = 0f;

        foreach (var run in line.Runs)
        {
            var metrics = GetFontMetrics(run.Typeface, textStyle.Font);
            var runThickness = metrics.UnderlineThickness ?? 0f;
            var runOffset = metrics.UnderlinePosition ?? 0f;
            if (runThickness <= 0 || runOffset <= 0)
                continue;

            thickness = Math.Max(thickness, runThickness);
            offset = Math.Max(offset, runOffset + runThickness * 0.5f);
        }

        return new UnderlineStroke(
            Math.Max(offset, fallbackOffset),
            Math.Max(thickness, fallbackThickness));
    }

    private SKFontMetrics GetFontMetrics(SKTypeface typeface, SceneFont font)
    {
        var key = new FontMetricsCacheKey(
            textResources.FontCatalog.CurrentVersion,
            typeface,
            QuantizePixel(font.Size),
            font.CacheIdentity,
            font.Weight,
            font.Italic);
        if (fontMetricsCache.TryGetValue(key, out var cached))
            return cached;

        using var skFont = SkiaFontSynthesis.CreateFont(typeface, font);
        var metrics = skFont.Metrics;
        if (fontMetricsCache.Count >= fontMetricsCacheLimit)
            fontMetricsCache.Clear();
        fontMetricsCache[key] = metrics;
        return metrics;
    }

    private void DrawTextLineShadows(SKCanvas canvas, TextInputMetrics.TextLineSpan line, float x, float baselineY, SceneTextStyle textStyle, SceneBoxShadow[] shadows)
    {
        var originalColor = textPaint.Color;
        var originalMaskFilter = textPaint.MaskFilter;
        for (var shadowIndex = 0; shadowIndex < shadows.Length; shadowIndex++)
        {
            var shadow = shadows[shadowIndex];
            textPaint.Color = ResolveColor(shadow.Color, new SKColor(0, 0, 0, 128));
            textPaint.MaskFilter = shadow.Blur > 0 ? GetTextShadowBlurFilter(shadow.Blur) : null;

            var cursorX = x + shadow.OffsetX;
            var shadowBaselineY = baselineY + shadow.OffsetY;
            foreach (var run in line.Runs)
            {
                var blob = GetOrCreateTextBlob(run, textStyle, textPaint.Color, out var disposeBlob);
                try
                {
                    if (blob is not null)
                        canvas.DrawText(blob, cursorX, shadowBaselineY, textPaint);
                }
                finally
                {
                    if (disposeBlob)
                        blob?.Dispose();
                }

                cursorX += run.Width;
            }
        }

        textPaint.MaskFilter = originalMaskFilter;
        textPaint.Color = originalColor;
    }

    private SKMaskFilter GetTextShadowBlurFilter(float blur)
    {
        var sigma = MathF.Max(0.001f, blur * 0.5f);
        if (textShadowBlurFilterCache.TryGetValue(sigma, out var cached))
            return cached;

        var filter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
        textShadowBlurFilterCache[sigma] = filter;
        return filter;
    }

    private SKTextBlob? GetOrCreateTextBlob(TextInputMetrics.TextRunSpan run, SceneTextStyle textStyle, SKColor textColor, out bool disposeBlob)
    {
        disposeBlob = false;
        if (string.IsNullOrEmpty(run.Text))
            return null;

        if (run.Text.Length > textBlobCacheMaxRunLength)
        {
            using var uncachedFont = SkiaFontSynthesis.CreateFont(run.Typeface, textStyle.Font);
            disposeBlob = true;
            return SKTextBlob.Create(run.Text, uncachedFont, SKPoint.Empty);
        }

        var key = CreateTextBlobCacheKey(run, textStyle, textColor);
        if (textBlobCache.TryGetValue(key, out var cached))
        {
            TextBlobCacheHits++;
            return cached;
        }

        TextBlobCacheMisses++;
        using var font = SkiaFontSynthesis.CreateFont(run.Typeface, textStyle.Font);
        var blob = SKTextBlob.Create(run.Text, font, SKPoint.Empty);
        if (blob is null)
            return null;

        textBlobCache[key] = blob;
        textBlobCacheOrder.Enqueue(key);
        TrimTextBlobCache();
        return blob;
    }

    private TextBlobCacheKey CreateTextBlobCacheKey(TextInputMetrics.TextRunSpan run, SceneTextStyle textStyle, SKColor textColor)
        => new(
            textResources.FontCatalog.CurrentVersion,
            run.Text,
            run.Typeface,
            QuantizePixel(textStyle.FontSize),
            textStyle.Font.CacheIdentity,
            textStyle.Font.Weight,
            textStyle.Font.Italic,
            textStyle.Underline,
            textColor,
            DeviceScaleQuarterPx: 4);

    private void TrimTextBlobCache()
    {
        while (textBlobCache.Count > textBlobCacheLimit && textBlobCacheOrder.Count > 0)
        {
            var oldestKey = textBlobCacheOrder.Dequeue();
            if (textBlobCache.Remove(oldestKey, out var oldest))
                oldest.Dispose();
        }
    }

    private void ClearTextBlobCache()
    {
        foreach (var blob in textBlobCache.Values)
            blob.Dispose();

        textBlobCache.Clear();
        textBlobCacheOrder.Clear();
    }

    private void ClearEllipsizedLineCache()
    {
        ellipsizedLineCache.Clear();
        ellipsizedLineCacheOrder.Clear();
        fontMetricsCache.Clear();
    }

    private void TrimScrollContentPictureCache()
    {
        while (scrollContentPictureCache.Count > scrollContentPictureCacheLimit && scrollContentPictureCacheOrder.Count > 0)
        {
            var oldestKey = scrollContentPictureCacheOrder.Dequeue();
            if (scrollContentPictureCache.Remove(oldestKey, out var oldest))
            {
                oldest.Dispose();
                if (scrollContentPictureKeyByNode.TryGetValue(oldestKey.NodeId, out var currentKey) &&
                    currentKey.Equals(oldestKey))
                {
                    scrollContentPictureKeyByNode.Remove(oldestKey.NodeId);
                }
            }
        }
    }

    private void TrimScrollContentRasterCache()
    {
        while (scrollContentRasterCache.Count > scrollContentPictureCacheLimit && scrollContentRasterCacheOrder.Count > 0)
        {
            var oldestKey = scrollContentRasterCacheOrder.Dequeue();
            if (scrollContentRasterCache.Remove(oldestKey, out var oldest))
            {
                oldest.Dispose();
                if (scrollContentRasterKeyByNode.TryGetValue(oldestKey.NodeId, out var currentKey) &&
                    currentKey.Equals(oldestKey))
                {
                    scrollContentRasterKeyByNode.Remove(oldestKey.NodeId);
                }
            }
        }
    }

    private void CompactScrollContentPictureCacheOrderIfNeeded()
    {
        if (scrollContentPictureCacheOrder.Count <= Math.Max(16, scrollContentPictureCacheLimit * 2))
            return;

        scrollContentPictureCacheOrder.Clear();
        foreach (var key in scrollContentPictureKeyByNode.Values)
            scrollContentPictureCacheOrder.Enqueue(key);
    }

    private void CompactScrollContentRasterCacheOrderIfNeeded()
    {
        if (scrollContentRasterCacheOrder.Count <= Math.Max(16, scrollContentPictureCacheLimit * 2))
            return;

        scrollContentRasterCacheOrder.Clear();
        foreach (var key in scrollContentRasterKeyByNode.Values)
            scrollContentRasterCacheOrder.Enqueue(key);
    }

    private void RemoveScrollContentPictureForNode(SceneNodeId nodeId)
    {
        if (!scrollContentPictureKeyByNode.Remove(nodeId, out var oldKey))
            return;

        if (scrollContentPictureCache.Remove(oldKey, out var oldPicture))
            oldPicture.Dispose();
    }

    private void RemoveScrollContentRasterForNode(SceneNodeId nodeId)
    {
        if (!scrollContentRasterKeyByNode.Remove(nodeId, out var oldKey))
            return;

        if (scrollContentRasterCache.Remove(oldKey, out var oldImage))
            oldImage.Dispose();
    }

    private void ClearScrollContentPictureCache()
    {
        foreach (var picture in scrollContentPictureCache.Values)
            picture.Dispose();
        ClearScrollContentRasterCache();

        scrollContentPictureCache.Clear();
        scrollContentPictureCacheOrder.Clear();
        scrollContentPictureKeyByNode.Clear();
    }

    private void ClearScrollContentRasterCache()
    {
        foreach (var image in scrollContentRasterCache.Values)
            image.Dispose();

        scrollContentRasterCache.Clear();
        scrollContentRasterCacheOrder.Clear();
        scrollContentRasterKeyByNode.Clear();
    }

    private static int QuantizePixel(float value)
    {
        if (float.IsPositiveInfinity(value))
            return int.MaxValue;

        if (float.IsNegativeInfinity(value))
            return int.MinValue;

        if (float.IsNaN(value))
            return 0;

        return (int)MathF.Round(value * 4f);
    }

    private static SKRect ExpandImageRectToAvoidSamplingGaps(SKRect fittedRect, SKRect destinationRect)
    {
        var expanded = fittedRect;
        if (Math.Abs(fittedRect.Left - destinationRect.Left) <= 0.5f)
            expanded.Left = destinationRect.Left - 0.5f;
        if (Math.Abs(fittedRect.Top - destinationRect.Top) <= 0.5f)
            expanded.Top = destinationRect.Top - 0.5f;
        if (Math.Abs(fittedRect.Right - destinationRect.Right) <= 0.5f)
            expanded.Right = destinationRect.Right + 0.5f;
        if (Math.Abs(fittedRect.Bottom - destinationRect.Bottom) <= 0.5f)
            expanded.Bottom = destinationRect.Bottom + 0.5f;
        return expanded;
    }

    private static float AlignTextX(float x, float width, float measuredWidth, SKTextAlign align)
    {
        return align switch
        {
            SKTextAlign.Center => x + (width - measuredWidth) * 0.5f,
            SKTextAlign.Right => x + width - measuredWidth,
            _ => x
        };
    }

    private static bool IsLineFullyVisible(float contentTop, float contentBottom, int lineIndex, float lineHeight)
    {
        var lineTop = contentTop + lineIndex * lineHeight;
        var lineBottom = lineTop + lineHeight;
        return lineTop >= contentTop && lineBottom <= contentBottom;
    }

    internal static bool ShouldDrawTextLine(bool wrapText, float contentTop, float contentBottom, int lineIndex, float lineHeight)
    {
        var lineTop = contentTop + lineIndex * lineHeight;
        var lineBottom = lineTop + lineHeight;
        return lineTop < contentBottom && lineBottom > contentTop;
    }

    internal static bool ShouldDrawTextCaret(float contentTop, float contentBottom, float caretTop, float caretHeight)
    {
        var caretBottom = caretTop + caretHeight;
        return caretTop < contentBottom && caretBottom > contentTop;
    }

    internal static SKColor ResolveTextInputTextColor(bool showPlaceholder, string? placeholderColor, string? textColor, string? backgroundColor = null)
    {
        return showPlaceholder
            ? ParseColorOrFallback(placeholderColor, new SKColor(0x47, 0x55, 0x69))
            : ParseColorOrFallback(textColor, ResolveTextInputFallbackColor(backgroundColor));
    }

    internal static SKColor ResolveTextInputFallbackColor(string? backgroundColor)
    {
        var background = ParseColorOrFallback(backgroundColor, new SKColor(0x0F, 0x17, 0x2A, 0xFF));
        var luminance = ((0.2126f * background.Red) + (0.7152f * background.Green) + (0.0722f * background.Blue)) / 255f;
        return luminance >= 0.62f
            ? new SKColor(0x11, 0x18, 0x27, 0xFF)
            : SKColors.White;
    }

    private static SKColor ParseColorOrFallback(string? color, SKColor fallback)
    {
        if (TryParseCssColor(color, out var parsed))
            return parsed;

        return SKColor.TryParse(color, out parsed) ? parsed : fallback;
    }

    internal static bool TryParseCssColor(string? color, out SKColor parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(color))
            return false;

        if (color[0] == '#')
        {
            var hex = color.AsSpan(1);
            return hex.Length switch
            {
                3 => TryParseShortRgb(hex, out parsed),
                4 => TryParseShortRgba(hex, out parsed),
                6 => TryParseRgb(hex, out parsed),
                8 => TryParseRgba(hex, out parsed),
                _ => false
            };
        }

        if (color.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
            return TryParseRgbFunction(color, out parsed);

        if (color.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
            return TryParseRgbaFunction(color, out parsed);

        if (color.StartsWith("argb(", StringComparison.OrdinalIgnoreCase))
            return TryParseArgbFunction(color, out parsed);

        return false;
    }

    private static bool TryParseRgbFunction(string color, out SKColor parsed)
    {
        parsed = default;
        Span<Range> components = stackalloc Range[3];
        if (!TryParseFunctionArguments(color, "rgb", components, out var arguments))
            return false;

        return TryParseColorByte(arguments[components[0]], out var red) &&
               TryParseColorByte(arguments[components[1]], out var green) &&
               TryParseColorByte(arguments[components[2]], out var blue) &&
               CreateColor(red, green, blue, 255, out parsed);
    }

    private static bool TryParseRgbaFunction(string color, out SKColor parsed)
    {
        parsed = default;
        Span<Range> components = stackalloc Range[4];
        if (!TryParseFunctionArguments(color, "rgba", components, out var arguments))
            return false;

        return TryParseColorByte(arguments[components[0]], out var red) &&
               TryParseColorByte(arguments[components[1]], out var green) &&
               TryParseColorByte(arguments[components[2]], out var blue) &&
               TryParseAlphaByte(arguments[components[3]], out var alpha) &&
               CreateColor(red, green, blue, alpha, out parsed);
    }

    private static bool TryParseArgbFunction(string color, out SKColor parsed)
    {
        parsed = default;
        Span<Range> components = stackalloc Range[4];
        if (!TryParseFunctionArguments(color, "argb", components, out var arguments))
            return false;

        return TryParseAlphaByte(arguments[components[0]], out var alpha) &&
               TryParseColorByte(arguments[components[1]], out var red) &&
               TryParseColorByte(arguments[components[2]], out var green) &&
               TryParseColorByte(arguments[components[3]], out var blue) &&
               CreateColor(red, green, blue, alpha, out parsed);
    }

    private static bool TryParseFunctionArguments(string color, string functionName, Span<Range> components, out ReadOnlySpan<char> arguments)
    {
        arguments = default;
        var prefixLength = functionName.Length + 1;
        if (!color.EndsWith(')') || color.Length <= prefixLength)
            return false;

        arguments = color.AsSpan(prefixLength, color.Length - prefixLength - 1);
        var componentCount = 0;
        var componentStart = 0;
        for (var index = 0; index <= arguments.Length; index++)
        {
            if (index < arguments.Length && arguments[index] != ',')
                continue;

            if (componentCount >= components.Length)
                return false;

            var start = componentStart;
            var end = index;
            while (start < end && char.IsWhiteSpace(arguments[start]))
                start++;
            while (end > start && char.IsWhiteSpace(arguments[end - 1]))
                end--;
            if (start == end)
                return false;

            components[componentCount++] = start..end;
            componentStart = index + 1;
        }

        return componentCount == components.Length;
    }

    private static bool TryParseColorByte(ReadOnlySpan<char> component, out byte value)
    {
        value = 0;
        if (component.Length > 0 &&
            component[^1] == '%' &&
            float.TryParse(component[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            percent = Math.Clamp(percent, 0f, 100f);
            value = (byte)Math.Round(percent * 255f / 100f);
            return true;
        }

        if (!float.TryParse(component, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
            return false;

        numeric = Math.Clamp(numeric, 0f, 255f);
        value = (byte)Math.Round(numeric);
        return true;
    }

    private static bool TryParseAlphaByte(ReadOnlySpan<char> component, out byte value)
    {
        value = 255;
        if (component.Length > 0 &&
            component[^1] == '%' &&
            float.TryParse(component[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            percent = Math.Clamp(percent, 0f, 100f);
            value = (byte)Math.Round(percent * 255f / 100f);
            return true;
        }

        if (!float.TryParse(component, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
            return false;

        numeric = numeric <= 1f ? numeric * 255f : numeric;
        numeric = Math.Clamp(numeric, 0f, 255f);
        value = (byte)Math.Round(numeric);
        return true;
    }

    private static bool CreateColor(byte red, byte green, byte blue, byte alpha, out SKColor parsed)
    {
        parsed = new SKColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseShortRgb(ReadOnlySpan<char> hex, out SKColor parsed)
    {
        parsed = default;
        if (!TryParseHexByte(hex[0], hex[0], out var red) ||
            !TryParseHexByte(hex[1], hex[1], out var green) ||
            !TryParseHexByte(hex[2], hex[2], out var blue))
        {
            return false;
        }

        parsed = new SKColor(red, green, blue, 255);
        return true;
    }

    private static bool TryParseShortRgba(ReadOnlySpan<char> hex, out SKColor parsed)
    {
        parsed = default;
        if (!TryParseHexByte(hex[0], hex[0], out var red) ||
            !TryParseHexByte(hex[1], hex[1], out var green) ||
            !TryParseHexByte(hex[2], hex[2], out var blue) ||
            !TryParseHexByte(hex[3], hex[3], out var alpha))
        {
            return false;
        }

        parsed = new SKColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseRgb(ReadOnlySpan<char> hex, out SKColor parsed)
    {
        parsed = default;
        if (!TryParseHexByte(hex[0], hex[1], out var red) ||
            !TryParseHexByte(hex[2], hex[3], out var green) ||
            !TryParseHexByte(hex[4], hex[5], out var blue))
        {
            return false;
        }

        parsed = new SKColor(red, green, blue, 255);
        return true;
    }

    private static bool TryParseRgba(ReadOnlySpan<char> hex, out SKColor parsed)
    {
        parsed = default;
        if (!TryParseHexByte(hex[0], hex[1], out var red) ||
            !TryParseHexByte(hex[2], hex[3], out var green) ||
            !TryParseHexByte(hex[4], hex[5], out var blue) ||
            !TryParseHexByte(hex[6], hex[7], out var alpha))
        {
            return false;
        }

        parsed = new SKColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseHexByte(char high, char low, out byte value)
    {
        value = default;
        Span<char> buffer = stackalloc char[] { high, low };
        return byte.TryParse(buffer, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    internal static VerticalScrollBarMetrics? ResolveVerticalScrollBar(SceneLayoutBox box)
    {
        var metrics = SceneScrollBarLayout.ResolveVerticalScrollBar(box);
        if (metrics is not { } scrollBar)
            return null;

        return new VerticalScrollBarMetrics(
            SKRect.Create(scrollBar.TrackRect.Left, scrollBar.TrackRect.Top, scrollBar.TrackRect.Width, scrollBar.TrackRect.Height),
            SKRect.Create(scrollBar.ThumbRect.Left, scrollBar.ThumbRect.Top, scrollBar.ThumbRect.Width, scrollBar.ThumbRect.Height),
            scrollBar.Radius);
    }

    internal static HorizontalScrollBarMetrics? ResolveHorizontalScrollBar(SceneLayoutBox box)
    {
        var metrics = SceneScrollBarLayout.ResolveHorizontalScrollBar(box);
        if (metrics is not { } scrollBar)
            return null;

        return new HorizontalScrollBarMetrics(
            SKRect.Create(scrollBar.TrackRect.Left, scrollBar.TrackRect.Top, scrollBar.TrackRect.Width, scrollBar.TrackRect.Height),
            SKRect.Create(scrollBar.ThumbRect.Left, scrollBar.ThumbRect.Top, scrollBar.ThumbRect.Width, scrollBar.ThumbRect.Height),
            scrollBar.Radius);
    }

    internal static PresentationScrollBarMetrics? ResolvePresentationVerticalScrollBar(SceneLayoutBox box, float viewportScale, int presentationWidth, int presentationHeight)
    {
        if (box.NodeKind != SceneNodeKind.ScrollView)
            return null;

        var scale = Math.Max(0.001f, viewportScale);
        var viewportHeight = Math.Max(0, box.Height);
        var contentHeight = Math.Max(viewportHeight, box.ContentHeight);
        var maxScroll = Math.Max(0, contentHeight - viewportHeight);
        if (viewportHeight <= 0 || maxScroll <= 0)
            return null;

        var right = Math.Min(Math.Max(0, presentationWidth), Snap((box.AbsLeft + box.Width) * scale));
        var left = right - ResolvePresentationGutterSize(box, scale);
        var top = Math.Clamp(Snap(box.AbsTop * scale), 0, Math.Max(0, presentationHeight));
        var bottom = Math.Clamp(Snap((box.AbsTop + box.Height) * scale), 0, Math.Max(0, presentationHeight));
        if (right <= left || bottom <= top)
            return null;

        var gutterRect = new SKRect(left, top, right, bottom);
        var margin = ResolvePresentationScrollBarMargin(gutterRect.Width);
        var barWidth = Math.Max(1, gutterRect.Width - margin * 2);
        var trackHeight = Math.Max(0, gutterRect.Height - margin * 2);
        if (trackHeight <= 0)
            return null;

        var thumbHeight = Math.Min(
            trackHeight,
            Math.Max(Math.Min(24, trackHeight), trackHeight * Math.Min(1, viewportHeight / contentHeight)));
        var thumbTravel = Math.Max(0, trackHeight - thumbHeight);
        var progress = maxScroll > 0 ? Math.Clamp(box.ScrollY / maxScroll, 0, 1) : 0;
        var thumbTop = gutterRect.Top + margin + thumbTravel * progress;
        var thumbRect = new SKRect(gutterRect.Left + margin, thumbTop, gutterRect.Left + margin + barWidth, thumbTop + thumbHeight);
        return new PresentationScrollBarMetrics(gutterRect, thumbRect, barWidth * 0.5f);
    }

    internal static PresentationScrollBarMetrics? ResolvePresentationHorizontalScrollBar(SceneLayoutBox box, float viewportScale, int presentationWidth, int presentationHeight)
    {
        if (box.NodeKind != SceneNodeKind.ScrollView || !box.HorizontalScrollEnabled)
            return null;

        var scale = Math.Max(0.001f, viewportScale);
        var viewportWidth = Math.Max(0, box.Width);
        var contentWidth = Math.Max(viewportWidth, box.ContentWidth);
        var maxScroll = Math.Max(0, contentWidth - viewportWidth);
        if (viewportWidth <= 0 || maxScroll <= 0)
            return null;

        var left = Math.Clamp(Snap(box.AbsLeft * scale), 0, Math.Max(0, presentationWidth));
        var right = Math.Clamp(Snap((box.AbsLeft + box.Width) * scale), 0, Math.Max(0, presentationWidth));
        var bottom = Math.Min(Math.Max(0, presentationHeight), Snap((box.AbsTop + box.Height) * scale));
        var top = bottom - ResolvePresentationGutterSize(box, scale);
        if (right <= left || bottom <= top)
            return null;

        var gutterRect = new SKRect(left, top, right, bottom);
        var margin = ResolvePresentationScrollBarMargin(gutterRect.Height);
        var barHeight = Math.Max(1, gutterRect.Height - margin * 2);
        var trackWidth = Math.Max(0, gutterRect.Width - margin * 2);
        if (trackWidth <= 0)
            return null;

        var thumbWidth = Math.Min(
            trackWidth,
            Math.Max(Math.Min(24, trackWidth), trackWidth * Math.Min(1, viewportWidth / contentWidth)));
        var thumbTravel = Math.Max(0, trackWidth - thumbWidth);
        var progress = maxScroll > 0 ? Math.Clamp(box.ScrollX / maxScroll, 0, 1) : 0;
        var thumbLeft = gutterRect.Left + margin + thumbTravel * progress;
        var thumbRect = new SKRect(thumbLeft, gutterRect.Top + margin, thumbLeft + thumbWidth, gutterRect.Top + margin + barHeight);
        return new PresentationScrollBarMetrics(gutterRect, thumbRect, barHeight * 0.5f);
    }

    private static float ResolvePresentationGutterSize(SceneLayoutBox box, float viewportScale)
        => Math.Max(1, Snap(Math.Max(0, box.ScrollBarWidth) * viewportScale));

    private static float ResolvePresentationScrollBarMargin(float gutterSize)
        => Math.Min(2, Math.Max(0, gutterSize) / 6);

    private static float Snap(float value)
        => MathF.Round(value, MidpointRounding.AwayFromZero);

    internal static bool TryHitVerticalScrollBarThumb(SceneLayoutBox box, float x, float y, out VerticalScrollBarMetrics metrics, out float grabOffsetY)
    {
        var resolved = SceneScrollBarLayout.TryHitVerticalScrollBarThumb(box, x, y, out var scrollBar, out grabOffsetY);
        if (!resolved)
        {
            metrics = default;
            return false;
        }

        metrics = new VerticalScrollBarMetrics(
            SKRect.Create(scrollBar.TrackRect.Left, scrollBar.TrackRect.Top, scrollBar.TrackRect.Width, scrollBar.TrackRect.Height),
            SKRect.Create(scrollBar.ThumbRect.Left, scrollBar.ThumbRect.Top, scrollBar.ThumbRect.Width, scrollBar.ThumbRect.Height),
            scrollBar.Radius);
        return true;
    }

    internal static float ResolveVerticalScrollOffsetFromThumbTop(SceneLayoutBox box, float thumbTop)
    {
        return SceneScrollBarLayout.ResolveVerticalScrollOffsetFromThumbTop(box, thumbTop);
    }

    internal static bool TryHitHorizontalScrollBarThumb(SceneLayoutBox box, float x, float y, out HorizontalScrollBarMetrics metrics, out float grabOffsetX)
    {
        var resolved = SceneScrollBarLayout.TryHitHorizontalScrollBarThumb(box, x, y, out var scrollBar, out grabOffsetX);
        if (!resolved)
        {
            metrics = default;
            return false;
        }

        metrics = new HorizontalScrollBarMetrics(
            SKRect.Create(scrollBar.TrackRect.Left, scrollBar.TrackRect.Top, scrollBar.TrackRect.Width, scrollBar.TrackRect.Height),
            SKRect.Create(scrollBar.ThumbRect.Left, scrollBar.ThumbRect.Top, scrollBar.ThumbRect.Width, scrollBar.ThumbRect.Height),
            scrollBar.Radius);
        return true;
    }

    internal static float ResolveHorizontalScrollOffsetFromThumbLeft(SceneLayoutBox box, float thumbLeft)
    {
        return SceneScrollBarLayout.ResolveHorizontalScrollOffsetFromThumbLeft(box, thumbLeft);
    }

    internal readonly record struct BoxPaintGeometry(SKRect FillRect, SKRect BorderRect, float FillRadius, float BorderRadius);

    internal readonly record struct VerticalScrollBarMetrics(SKRect TrackRect, SKRect ThumbRect, float Radius);

    internal readonly record struct HorizontalScrollBarMetrics(SKRect TrackRect, SKRect ThumbRect, float Radius);

    internal readonly record struct PresentationScrollBarMetrics(SKRect GutterRect, SKRect ThumbRect, float Radius);

    private readonly record struct UnderlineStroke(float Offset, float Thickness);

    private readonly record struct TextBlobCacheKey(
        int FontCatalogVersion,
        string Text,
        SKTypeface Typeface,
        int FontSizeQuarterPx,
        string FontIdentity,
        int FontWeight,
        bool Italic,
        bool Underline,
        SKColor Color,
        int DeviceScaleQuarterPx);

    private readonly record struct EllipsizedLineCacheKey(
        int FontCatalogVersion,
        string Text,
        int TextWidthQuarterPx,
        int LineHeightQuarterPx,
        int FontSizeQuarterPx,
        string FontIdentity,
        int FontWeight,
        bool Italic);

    private readonly record struct FontMetricsCacheKey(
        int FontCatalogVersion,
        SKTypeface Typeface,
        int FontSizeQuarterPx,
        string FontIdentity,
        int FontWeight,
        bool Italic);

    private readonly record struct ScrollContentPictureCacheKey(
        SceneNodeId NodeId,
        int ContentSignature,
        int ViewportLeftQuarterPx,
        int ViewportTopQuarterPx,
        int ViewportWidthQuarterPx,
        int ViewportHeightQuarterPx);

    private void InvalidateFontCaches()
    {
        ClearTextBlobCache();
        ClearEllipsizedLineCache();
        ClearScrollContentPictureCache();
        recordingContainsPendingImages = false;
    }

    private static void DrawVectorImage(SKCanvas canvas, SKPicture picture, SKRect sourceRect, SKRect destinationRect)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || destinationRect.Width <= 0 || destinationRect.Height <= 0)
            return;

        var scaleX = destinationRect.Width / sourceRect.Width;
        var scaleY = destinationRect.Height / sourceRect.Height;
        canvas.Save();
        canvas.Translate(
            destinationRect.Left - sourceRect.Left * scaleX,
            destinationRect.Top - sourceRect.Top * scaleY);
        canvas.Scale(scaleX, scaleY);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static SKRect CalculateImageRect(SKRect sourceRect, SKRect destinationRect, string? fit)
    {
        var fitMode = fit?.Trim().ToLowerInvariant();
        if (fitMode == "fill" || sourceRect.Width <= 0 || sourceRect.Height <= 0)
            return destinationRect;

        var scaleX = destinationRect.Width / sourceRect.Width;
        var scaleY = destinationRect.Height / sourceRect.Height;
        var scale = fitMode == "cover" ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        var width = sourceRect.Width * scale;
        var height = sourceRect.Height * scale;
        var left = destinationRect.Left + (destinationRect.Width - width) * 0.5f;
        var top = destinationRect.Top + (destinationRect.Height - height) * 0.5f;
        return new SKRect(left, top, left + width, top + height);
    }

    private sealed record RuntimeShaderTemplate(
        SKRuntimeEffect Effect,
        bool HasResolution,
        bool HasOrigin,
        bool UsesHostTime,
        RuntimeShaderUniformBinding[] UniformBindings);

    private enum RuntimeShaderUniformKind
    {
        Int,
        Float,
        Color,
        Point,
        Point3,
        FloatArray
    }

    private sealed record RuntimeShaderUniformBinding(
        string Name,
        RuntimeShaderUniformKind Kind,
        int IntValue = 0,
        float FloatValue = 0,
        SKColorF ColorValue = default,
        SKPoint PointValue = default,
        SKPoint3 Point3Value = default,
        float[]? FloatArrayValue = null)
    {
        public static RuntimeShaderUniformBinding CreateInt(string name, int value) => new(name, RuntimeShaderUniformKind.Int, IntValue: value);
        public static RuntimeShaderUniformBinding CreateFloat(string name, float value) => new(name, RuntimeShaderUniformKind.Float, FloatValue: value);
        public static RuntimeShaderUniformBinding CreateColor(string name, SKColorF value) => new(name, RuntimeShaderUniformKind.Color, ColorValue: value);
        public static RuntimeShaderUniformBinding CreatePoint(string name, SKPoint value) => new(name, RuntimeShaderUniformKind.Point, PointValue: value);
        public static RuntimeShaderUniformBinding CreatePoint3(string name, SKPoint3 value) => new(name, RuntimeShaderUniformKind.Point3, Point3Value: value);
        public static RuntimeShaderUniformBinding CreateFloatArray(string name, float[] value) => new(name, RuntimeShaderUniformKind.FloatArray, FloatArrayValue: value);
    }

    private static SKColorF ToColorF(SKColor color)
    {
        return new SKColorF(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);
    }
}
