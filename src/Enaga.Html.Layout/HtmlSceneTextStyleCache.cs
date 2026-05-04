using Enaga.Scene;

namespace Enaga.Html;

internal sealed class HtmlSceneTextStyleCache
{
    private const int maxFontEntries = 512;
    private const int maxTextStyleEntries = 2048;
    private readonly Dictionary<FontKey, SceneFont> fonts = new();
    private readonly Dictionary<TextStyleKey, SceneTextStyle> textStyles = new();

    public SceneFont GetFont(HtmlComputedStyle style, float defaultSize = 16, int defaultWeight = 400)
    {
        var key = FontKey.From(style, defaultSize, defaultWeight);
        if (fonts.TryGetValue(key, out var font))
            return font;

        if (fonts.Count >= maxFontEntries)
            fonts.Clear();

        font = new SceneFont(key.Size, key.Family, key.Weight, key.Italic);
        fonts[key] = font;
        return font;
    }

    public SceneTextStyle GetInlineMeasureStyle(HtmlComputedStyle style)
        => GetTextStyle(style, wrapText: false, textOverflowEllipsis: false, textShadows: null);

    public SceneTextStyle GetTextStyle(HtmlComputedStyle style)
        => GetTextStyle(style, style.WrapText, style.TextOverflowEllipsis, style.TextShadows);

    public SceneTextStyle GetTextStyle(
        HtmlComputedStyle style,
        bool wrapText,
        bool textOverflowEllipsis,
        SceneBoxShadow[]? textShadows)
    {
        var fontKey = FontKey.From(style, 16, 400);
        var key = new TextStyleKey(
            fontKey,
            style.Color,
            style.TextAlign,
            wrapText,
            style.Underline,
            textOverflowEllipsis,
            textShadows);
        if (textStyles.TryGetValue(key, out var textStyle))
            return textStyle;

        if (textStyles.Count >= maxTextStyleEntries)
            textStyles.Clear();

        textStyle = new SceneTextStyle(
            fontKey.Size,
            style.Color,
            TextAlign: style.TextAlign,
            WrapText: wrapText,
            Underline: style.Underline,
            TextOverflowEllipsis: textOverflowEllipsis,
            Font: GetFont(style, 16, 400),
            TextShadows: textShadows);
        textStyles[key] = textStyle;
        return textStyle;
    }

    private readonly record struct FontKey(float Size, string? Family, int Weight, bool Italic)
    {
        public static FontKey From(HtmlComputedStyle style, float defaultSize, int defaultWeight)
            => new(
                style.FontSize > 0 ? style.FontSize : defaultSize,
                style.FontFamily,
                style.FontWeight > 0 ? style.FontWeight : defaultWeight,
                style.Italic);
    }

    private readonly record struct TextStyleKey(
        FontKey Font,
        string? Color,
        SceneTextAlign TextAlign,
        bool WrapText,
        bool Underline,
        bool TextOverflowEllipsis,
        SceneBoxShadow[]? TextShadows);
}
