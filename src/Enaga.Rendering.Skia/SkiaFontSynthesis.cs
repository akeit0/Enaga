using Enaga.Scene;
using SkiaSharp;

namespace Enaga.Rendering.Skia;

internal static class SkiaFontSynthesis
{
    public static SKFont CreateFont(SKTypeface typeface, SceneFont font)
    {
        var skFont = new SKFont(typeface, font.Size);
        Apply(skFont, font);
        return skFont;
    }

    public static SKFont CreateFont(SKTypeface typeface, float size, int fontWeight, bool italic)
    {
        var skFont = new SKFont(typeface, size);
        Apply(skFont, fontWeight, italic);
        return skFont;
    }

    public static void Apply(SKFont skFont, SceneFont font)
    {
        Apply(skFont, font.Weight, font.Italic);
    }

    public static void Apply(SKFont skFont, int fontWeight, bool italic)
    {
        var typefaceStyle = skFont.Typeface?.FontStyle;
        if (
            fontWeight >= 500
            && (typefaceStyle?.Weight ?? (int)SKFontStyleWeight.Normal)
                < (int)SKFontStyleWeight.Medium
        )
            skFont.Embolden = true;

        if (italic && typefaceStyle?.Slant != SKFontStyleSlant.Italic)
            skFont.SkewX = -0.25f;
    }
}
