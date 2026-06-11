using SkiaSharp;

namespace Enaga.Rendering.Skia;

public static class SceneOverlayPainter
{
    public static void DrawOverlayMessage(SKCanvas canvas, string message)
    {
        using var panelPaint = new SKPaint
        {
            Color = new SKColor(69, 10, 10, 230),
            IsAntialias = true,
        };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var textFont = new SKFont(SKTypeface.Default, 18);

        canvas.DrawRoundRect(new SKRoundRect(new SKRect(24, 24, 24 + 920, 96), 18, 18), panelPaint);
        canvas.DrawText(message, 42, 70, textFont, textPaint);
    }
}
