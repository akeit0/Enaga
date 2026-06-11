using Enaga.Rendering;
using Silk.NET.Maths;

namespace Enaga.Hosting;

internal static class FramebufferDirtyRectScaler
{
    public static ReadOnlySpan<SceneDamageRect> Scale(
        ReadOnlySpan<SceneDamageRect> dirtyRects,
        Vector2D<int> logicalSize,
        Vector2D<int> framebufferSize,
        SceneDamageRectBufferWriter buffer
    )
    {
        if (dirtyRects.IsEmpty)
            return ReadOnlySpan<SceneDamageRect>.Empty;

        var safeLogicalSize = new Vector2D<int>(
            Math.Max(1, logicalSize.X),
            Math.Max(1, logicalSize.Y)
        );
        var safeFramebufferSize = new Vector2D<int>(
            Math.Max(1, framebufferSize.X),
            Math.Max(1, framebufferSize.Y)
        );
        var scaleX = safeFramebufferSize.X / (float)safeLogicalSize.X;
        var scaleY = safeFramebufferSize.Y / (float)safeLogicalSize.Y;

        buffer.Clear();
        foreach (var dirtyRect in dirtyRects)
        {
            var left = Math.Clamp((int)MathF.Floor(dirtyRect.X * scaleX), 0, safeFramebufferSize.X);
            var top = Math.Clamp((int)MathF.Floor(dirtyRect.Y * scaleY), 0, safeFramebufferSize.Y);
            var right = Math.Clamp(
                (int)MathF.Ceiling((dirtyRect.X + dirtyRect.Width) * scaleX),
                0,
                safeFramebufferSize.X
            );
            var bottom = Math.Clamp(
                (int)MathF.Ceiling((dirtyRect.Y + dirtyRect.Height) * scaleY),
                0,
                safeFramebufferSize.Y
            );
            var width = right - left;
            var height = bottom - top;
            if (width > 0 && height > 0)
                buffer.Add(new SceneDamageRect(left, top, width, height));
        }

        return buffer.WrittenSpan;
    }
}
