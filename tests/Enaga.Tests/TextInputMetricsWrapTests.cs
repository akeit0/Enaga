using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Enaga.Scene;
using Xunit;

namespace Enaga.Tests;

public sealed class TextInputMetricsWrapTests
{
    [Fact]
    public void CreateLayout_WrapsPlainTextWhenWidthIsConstrained()
    {
        using var textResources = new SkiaTextResources();
        var metrics = textResources.InputMetrics;
        var style = new SceneTextStyle(16, WrapText: true);
        using var paint = metrics.CreatePaint();
        using var font = metrics.CreateFont(style);
        var maxWidth = font.MeasureText("aaa", paint) + 0.1f;

        var layout = metrics.CreateLayout(style, paint, "aaaaaa", 22, maxWidth);

        Assert.True(layout.Lines.Count >= 2);
    }

    [Fact]
    public void GetCaretPosition_UsesNextLineAtSoftWrapBoundary()
    {
        using var textResources = new SkiaTextResources();
        var metrics = textResources.InputMetrics;
        var style = new SceneTextStyle(16, WrapText: true);
        using var paint = metrics.CreatePaint();
        using var font = metrics.CreateFont(style);
        var maxWidth = font.MeasureText("aaa", paint) + 0.1f;

        var layout = metrics.CreateLayout(style, paint, "aaaaaa", 22, maxWidth);
        var caret = metrics.GetCaretPosition(layout, 3);

        Assert.Equal(1, caret.LineIndex);
        Assert.Equal(0, caret.X);
    }

    [Fact]
    public void CreateLayout_ReusesCachedWrappedLayoutForSameStyleTextAndWidth()
    {
        using var textResources = new SkiaTextResources();
        var metrics = textResources.InputMetrics;
        var style = new SceneTextStyle(16, WrapText: true);
        using var paint = metrics.CreatePaint();
        using var font = metrics.CreateFont(style);
        var maxWidth = font.MeasureText("wrapped text ", paint) + 0.1f;

        var first = metrics.CreateLayout(
            style,
            paint,
            "wrapped text measurement cache",
            22,
            maxWidth
        );
        var second = metrics.CreateLayout(
            style,
            paint,
            "wrapped text measurement cache",
            22,
            maxWidth
        );

        Assert.Same(first, second);
    }

    [Fact]
    public void CreateLayout_SeparatesCachedWrappedLayoutByWidth()
    {
        using var textResources = new SkiaTextResources();
        var metrics = textResources.InputMetrics;
        var style = new SceneTextStyle(16, WrapText: true);
        using var paint = metrics.CreatePaint();
        using var font = metrics.CreateFont(style);
        var narrowWidth = font.MeasureText("wrapped ", paint) + 0.1f;
        var wideWidth = font.MeasureText("wrapped text measurement cache", paint) + 0.1f;

        var narrow = metrics.CreateLayout(
            style,
            paint,
            "wrapped text measurement cache",
            22,
            narrowWidth
        );
        var wide = metrics.CreateLayout(
            style,
            paint,
            "wrapped text measurement cache",
            22,
            wideWidth
        );

        Assert.NotSame(narrow, wide);
    }

    [Fact]
    public void SkiaRuntimeTextServices_MeasuresAndBreaksSpanText()
    {
        var services = new SkiaRuntimeTextServices();
        var style = new SceneTextStyle(16);
        var text = "alpha beta";
        var fullWidth = services.MeasureTextWidth(text.AsSpan(), style);
        var halfWidth = fullWidth * 0.5f;

        var count = services.BreakText(text.AsSpan(), halfWidth, style, out var measuredWidth);

        Assert.InRange(count, 1, text.Length - 1);
        Assert.True(
            measuredWidth <= halfWidth + 0.5f,
            $"measuredWidth={measuredWidth} halfWidth={halfWidth}"
        );
    }

    [Fact]
    public void SkiaRuntimeTextServices_MeasureTextHeight_CountsExplicitLineBreaks()
    {
        var services = new SkiaRuntimeTextServices();
        var style = new SceneTextStyle(16);
        var lineHeight = services.MeasureLineHeight(style.Font);

        var height = services.MeasureTextHeight("first\r\nsecond\n", width: 400, style);

        Assert.Equal(lineHeight * 3, height, precision: 3);
    }

    [Fact]
    public void SkiaRuntimeTextServices_MeasureLineHeight_UsesBrowserNormalMinimum()
    {
        var services = new SkiaRuntimeTextServices();
        var style = new SceneTextStyle(16);

        var lineHeight = services.MeasureLineHeight(style.Font);

        Assert.True(
            lineHeight >= MathF.Ceiling(style.Font.Size * 1.35f),
            $"lineHeight={lineHeight}"
        );
    }

    [Fact]
    public void SkiaRuntimeTextServices_MeasureTextHeight_UsesWrappedLineCount()
    {
        var services = new SkiaRuntimeTextServices();
        var style = new SceneTextStyle(16, WrapText: true);
        var text = "alpha beta gamma";
        var wideHeight = services.MeasureTextHeight(text, width: 1000, style);
        var narrowHeight = services.MeasureTextHeight(
            text,
            width: services.MeasureTextWidth("alpha ", style) + 0.1f,
            style
        );

        Assert.True(
            narrowHeight > wideHeight,
            $"narrowHeight={narrowHeight} wideHeight={wideHeight}"
        );
    }

    [Fact]
    public void SceneTextStyle_NormalizesLegacyFontFieldsIntoFontDescriptor()
    {
        var style = new SceneTextStyle(18, FontFamily: "Arial", FontWeight: 700, Italic: true);

        Assert.Equal(18, style.Font.Size);
        Assert.Equal("Arial", style.Font.Family);
        Assert.Equal(700, style.Font.Weight);
        Assert.True(style.Font.Italic);
    }

    [Fact]
    public void SceneTextStyle_UsesExplicitFontDescriptorAsSourceOfTruth()
    {
        var font = new SceneFont(20, "Arial", 600, Italic: true, Identity: "registered-font");
        var style = new SceneTextStyle(12, FontFamily: "ignored", FontWeight: 400, Font: font);

        Assert.Equal(font, style.Font);
        Assert.Equal(20, style.FontSize);
        Assert.Equal("Arial", style.FontFamily);
        Assert.Equal(600, style.FontWeight);
        Assert.True(style.Italic);
    }

    [Fact]
    public void SkiaFontCollection_ReusesFontDataForSameDescriptor()
    {
        using var fontCatalog = new TextFontCatalog();
        using var fontCollection = new SkiaFontCollection(fontCatalog);
        var font = new SceneFont(16, Identity: "cache-test");

        using var first = fontCollection.Get(font);
        using var second = fontCollection.Get(font);

        Assert.Same(first.Data, second.Data);
        Assert.Same(first.Data.Font, second.Data.Font);
    }

    [Fact]
    public void SkiaFontSynthesis_EmboldensJapaneseFallbackWhenBoldFaceIsUnavailable()
    {
        using var fontCatalog = new TextFontCatalog();
        var sceneFont = new SceneFont(24, Weight: 700);
        var typeface = fontCatalog.ResolveTypefaceForText(sceneFont, "日本語");

        using var font = SkiaFontSynthesis.CreateFont(typeface, sceneFont);

        Assert.True(
            font.Embolden || font.Typeface.FontStyle.Weight >= 600,
            $"Expected bold fallback or synthetic emboldening, family={font.Typeface.FamilyName}, weight={font.Typeface.FontStyle.Weight}"
        );
    }

    [Fact]
    public void TextFontCatalog_ReusesSystemFallbackBucketByCodePoint()
    {
        using var fontCatalog = new TextFontCatalog();
        var sceneFont = new SceneFont(16, "DefinitelyMissingFontFamilyForFallback");

        fontCatalog.ResolveTypefaceForText(sceneFont, "\U0001F600");
        var bucketCount = fontCatalog.SystemFallbackCacheBucketCount;
        var entryCount = fontCatalog.SystemFallbackCacheEntryCount;

        fontCatalog.ResolveTypefaceForText(sceneFont, "\U0001F600");

        Assert.True(bucketCount <= 1);
        Assert.Equal(bucketCount, fontCatalog.SystemFallbackCacheBucketCount);
        Assert.Equal(entryCount, fontCatalog.SystemFallbackCacheEntryCount);
    }
}
