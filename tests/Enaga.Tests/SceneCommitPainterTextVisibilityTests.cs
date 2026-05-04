using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Enaga.Scene;
using SkiaSharp;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneCommitPainterTextVisibilityTests
{
    private static readonly SceneNodeId Root = new(1);
    private static readonly SceneNodeId Text = new(2);
    private static readonly SceneNodeId One = new(3);
    private static readonly SceneNodeId Two = new(4);
    private static readonly SceneNodeId Visible = new(5);
    private static readonly SceneNodeId Offscreen = new(6);
    private static readonly SceneNodeId Title = new(7);
    private static readonly SceneNodeId Box = new(8);

    [Fact]
    public void ShouldDrawTextLine_AllowsPartialVisibilityForClippedSingleLineText()
    {
        var shouldDraw = SceneCommitPainter.ShouldDrawTextLine(
            wrapText: false,
            contentTop: 0,
            contentBottom: 14,
            lineIndex: 0,
            lineHeight: 16.2f);

        Assert.True(shouldDraw);
    }

    [Fact]
    public void ShouldDrawTextLine_AllowsPartialVisibilityForWrappedLine()
    {
        var shouldDraw = SceneCommitPainter.ShouldDrawTextLine(
            wrapText: true,
            contentTop: 0,
            contentBottom: 14,
            lineIndex: 0,
            lineHeight: 16.2f);

        Assert.True(shouldDraw);
    }

    [Fact]
    public void ShouldDrawTextLine_HidesNonIntersectingClippedLine()
    {
        var shouldDraw = SceneCommitPainter.ShouldDrawTextLine(
            wrapText: false,
            contentTop: 0,
            contentBottom: 14,
            lineIndex: 1,
            lineHeight: 16.2f);

        Assert.False(shouldDraw);
    }

    [Fact]
    public void ShouldDrawTextCaret_AllowsPartialVisibility()
    {
        var shouldDraw = SceneCommitPainter.ShouldDrawTextCaret(
            contentTop: 10,
            contentBottom: 30,
            caretTop: 24,
            caretHeight: 12);

        Assert.True(shouldDraw);
    }

    [Fact]
    public void ShouldDrawTextCaret_HidesNonIntersectingCaret()
    {
        var shouldDraw = SceneCommitPainter.ShouldDrawTextCaret(
            contentTop: 10,
            contentBottom: 30,
            caretTop: 31,
            caretHeight: 12);

        Assert.False(shouldDraw);
    }

    [Fact]
    public void ResolveTextInputTextColor_UsesMutedFallbackForPlaceholder()
    {
        var color = SceneCommitPainter.ResolveTextInputTextColor(
            showPlaceholder: true,
            placeholderColor: null,
            textColor: "#f8fafc");

        Assert.Equal(new SKColor(0x47, 0x55, 0x69), color);
    }

    [Fact]
    public void ResolveTextInputTextColor_UsesTextColorForNonPlaceholder()
    {
        var color = SceneCommitPainter.ResolveTextInputTextColor(
            showPlaceholder: false,
            placeholderColor: "#475569",
            textColor: "#f8fafc");

        Assert.Equal(new SKColor(0xF8, 0xFA, 0xFC), color);
    }

    [Fact]
    public void ResolveTextInputTextColor_UsesDarkFallbackForLightBackgroundWhenTextColorMissing()
    {
        var color = SceneCommitPainter.ResolveTextInputTextColor(
            showPlaceholder: false,
            placeholderColor: "#475569",
            textColor: null,
            backgroundColor: "#ffffff");

        Assert.Equal(new SKColor(0x11, 0x18, 0x27, 0xFF), color);
    }

    [Fact]
    public void ResolveTextInputTextColor_UsesCssRgbaHexOrdering()
    {
        var color = SceneCommitPainter.ResolveTextInputTextColor(
            showPlaceholder: false,
            placeholderColor: "#475569",
            textColor: "#18131fff");

        Assert.Equal(new SKColor(0x18, 0x13, 0x1F, 0xFF), color);
    }

    [Fact]
    public void TryParseCssColor_UsesCssShortRgbaOrdering()
    {
        var success = SceneCommitPainter.TryParseCssColor("#abcd", out var color);

        Assert.True(success);
        Assert.Equal(new SKColor(0xAA, 0xBB, 0xCC, 0xDD), color);
    }

    [Fact]
    public void TryParseCssColor_ParsesRgbFunctions()
    {
        var success = SceneCommitPainter.TryParseCssColor("rgb(0, 110, 255)", out var color);

        Assert.True(success);
        Assert.Equal(new SKColor(0x00, 0x6E, 0xFF, 0xFF), color);
    }

    [Fact]
    public void TryParseCssColor_ParsesRgbaFunctions()
    {
        var success = SceneCommitPainter.TryParseCssColor("rgba(85, 255, 6, 0.8627451)", out var color);

        Assert.True(success);
        Assert.Equal(new SKColor(0x55, 0xFF, 0x06, 0xDC), color);
    }

    [Fact]
    public void Paint_DoesNotDrawBorderWhenBorderStyleIsNone()
    {
        using var bitmap = new SKBitmap(64, 40, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = CreateSingleBoxCommit(SceneBorderStyle.None);

        painter.Paint(canvas, commit, TimeSpan.Zero);

        Assert.All(SampleTopBorder(bitmap), pixel => Assert.Equal(0, pixel.Alpha));
    }

    [Fact]
    public void Paint_DrawsDottedBorderWithVisibleGaps()
    {
        using var bitmap = new SKBitmap(64, 40, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = CreateSingleBoxCommit(SceneBorderStyle.Dotted);

        painter.Paint(canvas, commit, TimeSpan.Zero);
        var samples = SampleTopBorder(bitmap).ToArray();

        Assert.Contains(samples, pixel => pixel.Alpha > 0);
        Assert.Contains(samples, pixel => pixel.Alpha == 0);
    }

    [Fact]
    public void Paint_AppliesScenePaintOverrideBackground()
    {
        using var bitmap = new SKBitmap(64, 40, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = CreateSingleBoxCommit(SceneBorderStyle.None) with
        {
            PaintOverrides = new Dictionary<SceneNodeId, ScenePaintOverride>
            {
                [Root] = new(BackgroundColor: "#00ff00")
            }
        };

        painter.Paint(canvas, commit, TimeSpan.Zero);

        Assert.Equal(new SKColor(0, 255, 0, 255), bitmap.GetPixel(16, 16));
    }

    [Fact]
    public void Paint_DoesNotDrawGenericPlaceholderForPendingImage()
    {
        using var bitmap = new SKBitmap(80, 60, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = CreateImageCommit($"https://example.invalid/{Guid.NewGuid():N}.svg");

        painter.Paint(canvas, commit, TimeSpan.Zero);

        Assert.Equal(0, bitmap.GetPixel(20, 20).Alpha);
    }

    [Fact]
    public void Paint_DrawsErrorPlaceholderForFailedImage()
    {
        using var bitmap = new SKBitmap(80, 60, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = CreateImageCommit(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.svg"));

        painter.Paint(canvas, commit, TimeSpan.Zero);

        Assert.True(bitmap.GetPixel(20, 20).Alpha > 0);
    }

    [Fact]
    public void Paint_DrawsExplicitPlaceholderForPendingImage()
    {
        var placeholderPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        using (var placeholder = new SKBitmap(10, 10))
        using (var placeholderCanvas = new SKCanvas(placeholder))
        {
            placeholderCanvas.Clear(SKColors.CornflowerBlue);
            using var image = SKImage.FromBitmap(placeholder);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(placeholderPath);
            data.SaveTo(stream);
        }

        try
        {
            using var bitmap = new SKBitmap(80, 60, true);
            using var canvas = new SKCanvas(bitmap);
            using var painter = new SceneCommitPainter();
            var commit = CreateImageCommit(
                $"https://example.invalid/{Guid.NewGuid():N}.svg",
                placeholderPath);

            painter.Paint(canvas, commit, TimeSpan.Zero);
            var completed = SpinWait.SpinUntil(
                () => SkiaImageAssetCache.Resolve(placeholderPath).State == SkiaImageAssetState.Ready,
                TimeSpan.FromSeconds(5));
            Assert.True(completed);
            painter.Paint(canvas, commit, TimeSpan.FromMilliseconds(16));

            Assert.True(bitmap.GetPixel(20, 20).Alpha > 0);
        }
        finally
        {
            File.Delete(placeholderPath);
        }
    }

    [Fact]
    public void Paint_InvalidatesScrollContentPictureForPaintOverride()
    {
        using var bitmap = new SKBitmap(160, 100, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var firstCommit = CreateScrollBackgroundCommit();
        var secondCommit = firstCommit with
        {
            PaintOverrides = new Dictionary<SceneNodeId, ScenePaintOverride>
            {
                [One] = new(BackgroundColor: "#00ff00")
            }
        };

        painter.Paint(canvas, firstCommit, TimeSpan.Zero);
        painter.Paint(canvas, secondCommit, TimeSpan.FromMilliseconds(16), [new SceneDamageRect(0, 0, 160, 100)]);

        Assert.Equal(new SKColor(0, 255, 0, 255), bitmap.GetPixel(16, 16));
    }

    [Fact]
    public void Paint_RoundsUniformSideBorder()
    {
        using var bitmap = new SKBitmap(64, 40, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = new SceneLayoutCommit(
            Root,
            new SceneViewport(64, 40),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.View, null, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(
                    SceneNodeKind.View,
                    8,
                    8,
                    44,
                    20,
                    BorderWidth: 2,
                    BorderRadius: 8,
                    Border: new SceneBoxBorder(
                        2,
                        2,
                        2,
                        2,
                        SceneBorderStyle.Solid,
                        SceneBorderStyle.Solid,
                        SceneBorderStyle.Solid,
                        SceneBorderStyle.Solid,
                        "#006eff",
                        "#006eff",
                        "#006eff",
                        "#006eff"))
            },
            []);

        painter.Paint(canvas, commit, TimeSpan.Zero);

        Assert.Equal(0, bitmap.GetPixel(8, 8).Alpha);
        Assert.True(bitmap.GetPixel(16, 9).Alpha > 0);
    }

    [Fact]
    public void Paint_KeepsSquareCornerForNonUniformSideBorder()
    {
        using var bitmap = new SKBitmap(64, 40, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = new SceneLayoutCommit(
            Root,
            new SceneViewport(64, 40),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.View, null, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(
                    SceneNodeKind.View,
                    8,
                    8,
                    44,
                    20,
                    BorderWidth: 2,
                    BorderRadius: 8,
                    Border: new SceneBoxBorder(
                        2,
                        2,
                        2,
                        2,
                        SceneBorderStyle.Solid,
                        SceneBorderStyle.Solid,
                        SceneBorderStyle.Solid,
                        SceneBorderStyle.Solid,
                        "#006eff",
                        "#ff0000",
                        "#006eff",
                        "#006eff"))
            },
            []);

        painter.Paint(canvas, commit, TimeSpan.Zero);

        Assert.True(bitmap.GetPixel(9, 9).Alpha > 0);
    }

    [Fact]
    public void Paint_DoesNotFillTransparentBoxInteriorWithOuterShadow()
    {
        using var bitmap = new SKBitmap(80, 60, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = new SceneLayoutCommit(
            Root,
            new SceneViewport(80, 60),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.View, null, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(
                    SceneNodeKind.View,
                    10,
                    10,
                    50,
                    30,
                    BackgroundShadows: [new SceneBoxShadow("#777777", 0, 5, 4, -4)])
            },
            []);

        painter.Paint(canvas, commit, TimeSpan.Zero);

        Assert.Equal(0, bitmap.GetPixel(35, 25).Alpha);
    }

    [Fact]
    public void Paint_DrawsPositionedChildrenAboveNormalFlowSiblings()
    {
        using var bitmap = new SKBitmap(120, 60, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = new SceneLayoutCommit(
            Root,
            new SceneViewport(120, 60),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.View, null, [Title, Box]),
                [Title] = new(SceneNodeKind.View, Root, []),
                [Box] = new(SceneNodeKind.View, Root, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(SceneNodeKind.View, 0, 0, 120, 60),
                [Title] = new(SceneNodeKind.View, 0, 10, 80, 20, BackgroundColor: "#ffffff", IsPositioned: true),
                [Box] = new(SceneNodeKind.View, 0, 20, 100, 30, BorderColor: "#000066", BorderWidth: 1)
            },
            []);

        painter.Paint(canvas, commit, TimeSpan.Zero);

        Assert.Equal(SKColors.White, bitmap.GetPixel(20, 20));
    }

    [Fact]
    public void Paint_ReusesTextBlobAcrossEquivalentCommitRecordings()
    {
        using var bitmap = new SKBitmap(220, 60, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var firstCommit = CreateSingleTextCommit("cached text blob");
        var secondCommit = CreateSingleTextCommit("cached text blob");

        painter.Paint(canvas, firstCommit, TimeSpan.Zero);
        var missCount = painter.TextBlobCacheMisses;
        var hitCount = painter.TextBlobCacheHits;
        var cacheCount = painter.TextBlobCacheCount;

        painter.Paint(canvas, secondCommit, TimeSpan.FromMilliseconds(16));

        Assert.True(cacheCount > 0);
        Assert.Equal(cacheCount, painter.TextBlobCacheCount);
        Assert.Equal(missCount, painter.TextBlobCacheMisses);
        Assert.True(painter.TextBlobCacheHits > hitCount);
    }

    [Fact]
    public void Paint_ReusesEllipsizedLineAcrossEquivalentCommitRecordings()
    {
        using var bitmap = new SKBitmap(140, 60, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var text = "this sentence is intentionally too long";
        var firstCommit = CreateSingleTextCommit(text, width: 72, textOverflowEllipsis: true);
        var secondCommit = CreateSingleTextCommit(text, width: 72, textOverflowEllipsis: true);

        painter.Paint(canvas, firstCommit, TimeSpan.Zero);
        var missCount = painter.EllipsizedLineCacheMisses;
        var hitCount = painter.EllipsizedLineCacheHits;
        var cacheCount = painter.EllipsizedLineCacheCount;

        painter.Paint(canvas, secondCommit, TimeSpan.FromMilliseconds(16));

        Assert.True(cacheCount > 0);
        Assert.Equal(cacheCount, painter.EllipsizedLineCacheCount);
        Assert.Equal(missCount, painter.EllipsizedLineCacheMisses);
        Assert.True(painter.EllipsizedLineCacheHits > hitCount);
    }

    [Fact]
    public void Paint_CullsOffscreenTextDuringPictureRecording()
    {
        using var bitmap = new SKBitmap(120, 60, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = new SceneLayoutCommit(
            Root,
            new SceneViewport(120, 60),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.View, null, [Visible, Offscreen]),
                [Visible] = new(SceneNodeKind.Text, Root, []),
                [Offscreen] = new(SceneNodeKind.Text, Root, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(SceneNodeKind.View, 0, 0, 120, 60),
                [Visible] = new(
                    SceneNodeKind.Text,
                    8,
                    8,
                    80,
                    24,
                    TextContent: "visible",
                    TextStyle: new SceneTextStyle(16, "#101820")),
                [Offscreen] = new(
                    SceneNodeKind.Text,
                    8,
                    800,
                    80,
                    24,
                    TextContent: "offscreen",
                    TextStyle: new SceneTextStyle(16, "#101820"))
            },
            []);

        painter.Paint(canvas, commit, TimeSpan.Zero);

        Assert.Equal(3, painter.LastRecordedNodeCount);
        Assert.True(painter.LastCulledNodePaintCount >= 1);
        Assert.Equal(1, painter.TextBlobCacheMisses);
    }

    [Fact]
    public void Paint_DirectlyPaintsDirtyRectsWhenCommitChanged()
    {
        using var bitmap = new SKBitmap(120, 60, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var firstCommit = CreateSingleTextCommit("first", width: 80);
        var secondCommit = CreateSingleTextCommit("second", width: 80);

        painter.Paint(canvas, firstCommit, TimeSpan.Zero);
        painter.Paint(canvas, secondCommit, TimeSpan.FromMilliseconds(16), [new SceneDamageRect(0, 0, 120, 60)]);

        Assert.True(painter.LastDirectDirtyPaintUsed);
        Assert.False(painter.LastPictureReused);
        Assert.True(painter.LastRecordedNodeCount > 0);
    }

    [Fact]
    public void Paint_ReusesScrollContentPictureWhenOnlyScrollOffsetChanges()
    {
        using var bitmap = new SKBitmap(160, 100, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var firstCommit = CreateScrollCommit(scrollY: 0, contentHeight: 180);
        var secondCommit = CreateScrollCommit(scrollY: 24, contentHeight: 180);

        painter.Paint(canvas, firstCommit, TimeSpan.Zero);
        var missCount = painter.ScrollContentPictureCacheMisses;
        var hitCount = painter.ScrollContentPictureCacheHits;
        var cacheCount = painter.ScrollContentPictureCacheCount;

        painter.Paint(canvas, secondCommit, TimeSpan.FromMilliseconds(16), [new SceneDamageRect(0, 0, 160, 100)]);

        Assert.True(painter.LastDirectDirtyPaintUsed);
        Assert.True(cacheCount > 0);
        Assert.Equal(cacheCount, painter.ScrollContentPictureCacheCount);
        Assert.Equal(missCount, painter.ScrollContentPictureCacheMisses);
        Assert.True(painter.ScrollContentPictureCacheHits > hitCount);
    }

    [Fact]
    public void Paint_ReplacesLargeScrollContentPictureWhenViewportMoves()
    {
        using var bitmap = new SKBitmap(160, 100, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var firstCommit = CreateScrollCommit(scrollY: 0, contentHeight: 420);
        var secondCommit = CreateScrollCommit(scrollY: 120, contentHeight: 420);

        painter.Paint(canvas, firstCommit, TimeSpan.Zero);
        var cacheCount = painter.ScrollContentPictureCacheCount;
        var missCount = painter.ScrollContentPictureCacheMisses;

        painter.Paint(canvas, secondCommit, TimeSpan.FromMilliseconds(16), [new SceneDamageRect(0, 0, 160, 100)]);

        Assert.Equal(1, cacheCount);
        Assert.Equal(cacheCount, painter.ScrollContentPictureCacheCount);
        Assert.True(painter.ScrollContentPictureCacheMisses > missCount);
    }

    [Fact]
    public void Paint_ReplacesScrollContentPictureWhenSameScrollViewContentChanges()
    {
        using var bitmap = new SKBitmap(160, 100, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var firstCommit = CreateScrollCommit(scrollY: 0, secondText: "second row");
        var secondCommit = CreateScrollCommit(scrollY: 0, secondText: "updated row");

        painter.Paint(canvas, firstCommit, TimeSpan.Zero);
        var cacheCount = painter.ScrollContentPictureCacheCount;
        var missCount = painter.ScrollContentPictureCacheMisses;

        painter.Paint(canvas, secondCommit, TimeSpan.FromMilliseconds(16), [new SceneDamageRect(0, 0, 160, 100)]);

        Assert.Equal(1, cacheCount);
        Assert.Equal(cacheCount, painter.ScrollContentPictureCacheCount);
        Assert.True(painter.ScrollContentPictureCacheMisses > missCount);
    }

    [Fact]
    public void Paint_BypassesScrollContentCacheForSmallDirtyPaintChanges()
    {
        using var bitmap = new SKBitmap(160, 100, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var firstCommit = CreateScrollCommit(scrollY: 0, firstText: "first row", contentHeight: 420);
        var secondCommit = CreateScrollCommit(scrollY: 0, firstText: "hovered row", contentHeight: 420);

        painter.Paint(canvas, firstCommit, TimeSpan.Zero);
        var cacheCount = painter.ScrollContentPictureCacheCount;
        var missCount = painter.ScrollContentPictureCacheMisses;

        painter.Paint(canvas, secondCommit, TimeSpan.FromMilliseconds(16), [new SceneDamageRect(0, 0, 160, 20)]);

        Assert.True(painter.LastDirectDirtyPaintUsed);
        Assert.True(cacheCount > 0);
        Assert.Equal(0, painter.ScrollContentPictureCacheCount);
        Assert.Equal(missCount, painter.ScrollContentPictureCacheMisses);
        Assert.True(painter.LastRecordedNodeCount <= 2);
    }

    private static SceneLayoutCommit CreateSingleBoxCommit(SceneBorderStyle borderStyle)
    {
        return new SceneLayoutCommit(
            Root,
            new SceneViewport(64, 40),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.View, null, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(
                    SceneNodeKind.View,
                    8,
                    8,
                    44,
                    20,
                    BorderColor: "#006eff",
                    BorderWidth: 2,
                    BorderStyle: borderStyle)
            },
            []);
    }

    private static SceneLayoutCommit CreateSingleTextCommit(string text, float width = 180, bool textOverflowEllipsis = false)
    {
        return new SceneLayoutCommit(
            Root,
            new SceneViewport(220, 60),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.View, null, [Text]),
                [Text] = new(SceneNodeKind.Text, Root, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(SceneNodeKind.View, 0, 0, 220, 60),
                [Text] = new(
                    SceneNodeKind.Text,
                    8,
                    8,
                    width,
                    32,
                    TextContent: text,
                    TextStyle: new SceneTextStyle(16, "#101820", TextOverflowEllipsis: textOverflowEllipsis))
            },
            []);
    }

    private static SceneLayoutCommit CreateImageCommit(string imageSource, string? placeholderSource = null)
    {
        return new SceneLayoutCommit(
            Root,
            new SceneViewport(80, 60),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.View, null, [One]),
                [One] = new(SceneNodeKind.Image, Root, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(SceneNodeKind.View, 0, 0, 80, 60),
                [One] = new(
                    SceneNodeKind.Image,
                    8,
                    8,
                    48,
                    32,
                    ImageSource: imageSource,
                    ImagePlaceholderSource: placeholderSource)
            },
            []);
    }

    private static SceneLayoutCommit CreateScrollCommit(float scrollY, string firstText = "first row", string secondText = "second row", float contentHeight = 220)
    {
        return new SceneLayoutCommit(
            Root,
            new SceneViewport(160, 100),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.ScrollView, null, [One, Two]),
                [One] = new(SceneNodeKind.Text, Root, []),
                [Two] = new(SceneNodeKind.Text, Root, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(
                    SceneNodeKind.ScrollView,
                    0,
                    0,
                    160,
                    100,
                    IsScrollContainer: true,
                    ClipContent: true,
                    ContentHeight: contentHeight,
                    ScrollY: scrollY),
                [One] = new(
                    SceneNodeKind.Text,
                    8,
                    8,
                    120,
                    24,
                    TextContent: firstText,
                    TextStyle: new SceneTextStyle(16, "#101820")),
                [Two] = new(
                    SceneNodeKind.Text,
                    8,
                    120,
                    120,
                    24,
                    TextContent: secondText,
                    TextStyle: new SceneTextStyle(16, "#101820"))
            },
            []); 
    }

    private static SceneLayoutCommit CreateScrollBackgroundCommit()
    {
        return new SceneLayoutCommit(
            Root,
            new SceneViewport(160, 100),
            new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [Root] = new(SceneNodeKind.ScrollView, null, [One, Two]),
                [One] = new(SceneNodeKind.View, Root, []),
                [Two] = new(SceneNodeKind.View, Root, [])
            },
            new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [Root] = new(
                    SceneNodeKind.ScrollView,
                    0,
                    0,
                    160,
                    100,
                    IsScrollContainer: true,
                    ClipContent: true,
                    ContentHeight: 260,
                    ScrollY: 0),
                [One] = new(SceneNodeKind.View, 8, 8, 120, 24, BackgroundColor: "#ff0000"),
                [Two] = new(SceneNodeKind.View, 8, 220, 120, 24, BackgroundColor: "#101820")
            },
            []);
    }

    private static IEnumerable<SKColor> SampleTopBorder(SKBitmap bitmap)
    {
        for (var x = 12; x <= 48; x += 4)
            yield return bitmap.GetPixel(x, 9);
    }
}
