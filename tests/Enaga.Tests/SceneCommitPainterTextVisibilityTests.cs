using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Enaga.Scene;
using SkiaSharp;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneCommitPainterTextVisibilityTests
{
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
    public void Paint_RoundsUniformSideBorder()
    {
        using var bitmap = new SKBitmap(64, 40, true);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SceneCommitPainter();
        var commit = new SceneLayoutCommit(
            "root",
            new SceneViewport(64, 40),
            new Dictionary<string, SceneGraphNode>
            {
                ["root"] = new(SceneNodeKind.View, null, [])
            },
            new Dictionary<string, SceneLayoutBox>
            {
                ["root"] = new(
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
            "root",
            new SceneViewport(64, 40),
            new Dictionary<string, SceneGraphNode>
            {
                ["root"] = new(SceneNodeKind.View, null, [])
            },
            new Dictionary<string, SceneLayoutBox>
            {
                ["root"] = new(
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
            "root",
            new SceneViewport(80, 60),
            new Dictionary<string, SceneGraphNode>
            {
                ["root"] = new(SceneNodeKind.View, null, Array.Empty<string>())
            },
            new Dictionary<string, SceneLayoutBox>
            {
                ["root"] = new(
                    SceneNodeKind.View,
                    10,
                    10,
                    50,
                    30,
                    BackgroundShadows: [new SceneBoxShadow("#777777", 0, 5, 4, -4)])
            },
            Array.Empty<string>());

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
            "root",
            new SceneViewport(120, 60),
            new Dictionary<string, SceneGraphNode>
            {
                ["root"] = new(SceneNodeKind.View, null, ["title", "box"]),
                ["title"] = new(SceneNodeKind.View, "root", []),
                ["box"] = new(SceneNodeKind.View, "root", [])
            },
            new Dictionary<string, SceneLayoutBox>
            {
                ["root"] = new(SceneNodeKind.View, 0, 0, 120, 60),
                ["title"] = new(SceneNodeKind.View, 0, 10, 80, 20, BackgroundColor: "#ffffff", IsPositioned: true),
                ["box"] = new(SceneNodeKind.View, 0, 20, 100, 30, BorderColor: "#000066", BorderWidth: 1)
            },
            Array.Empty<string>());

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
            "root",
            new SceneViewport(120, 60),
            new Dictionary<string, SceneGraphNode>
            {
                ["root"] = new(SceneNodeKind.View, null, ["visible", "offscreen"]),
                ["visible"] = new(SceneNodeKind.Text, "root", []),
                ["offscreen"] = new(SceneNodeKind.Text, "root", [])
            },
            new Dictionary<string, SceneLayoutBox>
            {
                ["root"] = new(SceneNodeKind.View, 0, 0, 120, 60),
                ["visible"] = new(
                    SceneNodeKind.Text,
                    8,
                    8,
                    80,
                    24,
                    TextContent: "visible",
                    TextStyle: new SceneTextStyle(16, "#101820")),
                ["offscreen"] = new(
                    SceneNodeKind.Text,
                    8,
                    800,
                    80,
                    24,
                    TextContent: "offscreen",
                    TextStyle: new SceneTextStyle(16, "#101820"))
            },
            Array.Empty<string>());

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
            "root",
            new SceneViewport(64, 40),
            new Dictionary<string, SceneGraphNode>
            {
                ["root"] = new(SceneNodeKind.View, null, Array.Empty<string>())
            },
            new Dictionary<string, SceneLayoutBox>
            {
                ["root"] = new(
                    SceneNodeKind.View,
                    8,
                    8,
                    44,
                    20,
                    BorderColor: "#006eff",
                    BorderWidth: 2,
                    BorderStyle: borderStyle)
            },
            Array.Empty<string>());
    }

    private static SceneLayoutCommit CreateSingleTextCommit(string text, float width = 180, bool textOverflowEllipsis = false)
    {
        return new SceneLayoutCommit(
            "root",
            new SceneViewport(220, 60),
            new Dictionary<string, SceneGraphNode>
            {
                ["root"] = new(SceneNodeKind.View, null, ["text"]),
                ["text"] = new(SceneNodeKind.Text, "root", [])
            },
            new Dictionary<string, SceneLayoutBox>
            {
                ["root"] = new(SceneNodeKind.View, 0, 0, 220, 60),
                ["text"] = new(
                    SceneNodeKind.Text,
                    8,
                    8,
                    width,
                    32,
                    TextContent: text,
                    TextStyle: new SceneTextStyle(16, "#101820", TextOverflowEllipsis: textOverflowEllipsis))
            },
            Array.Empty<string>());
    }

    private static SceneLayoutCommit CreateScrollCommit(float scrollY, string firstText = "first row", string secondText = "second row", float contentHeight = 220)
    {
        return new SceneLayoutCommit(
            "root",
            new SceneViewport(160, 100),
            new Dictionary<string, SceneGraphNode>
            {
                ["root"] = new(SceneNodeKind.ScrollView, null, ["one", "two"]),
                ["one"] = new(SceneNodeKind.Text, "root", []),
                ["two"] = new(SceneNodeKind.Text, "root", [])
            },
            new Dictionary<string, SceneLayoutBox>
            {
                ["root"] = new(
                    SceneNodeKind.ScrollView,
                    0,
                    0,
                    160,
                    100,
                    IsScrollContainer: true,
                    ClipContent: true,
                    ContentHeight: contentHeight,
                    ScrollY: scrollY),
                ["one"] = new(
                    SceneNodeKind.Text,
                    8,
                    8,
                    120,
                    24,
                    TextContent: firstText,
                    TextStyle: new SceneTextStyle(16, "#101820")),
                ["two"] = new(
                    SceneNodeKind.Text,
                    8,
                    120,
                    120,
                    24,
                    TextContent: secondText,
                    TextStyle: new SceneTextStyle(16, "#101820"))
            },
            Array.Empty<string>());
    }

    private static IEnumerable<SKColor> SampleTopBorder(SKBitmap bitmap)
    {
        for (var x = 12; x <= 48; x += 4)
            yield return bitmap.GetPixel(x, 9);
    }
}
