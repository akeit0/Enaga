namespace Enaga.Rendering;

public sealed class ManualDirtyRegionTracker
{
    private readonly List<SceneDamageRect> dirtyRects = [];

    public void MarkDirty(SceneDamageRect dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            return;

        dirtyRects.Add(dirtyRect);
    }

    public void MarkDirty(int x, int y, int width, int height)
    {
        MarkDirty(new SceneDamageRect(x, y, width, height));
    }

    public void Clear()
    {
        dirtyRects.Clear();
    }

    public SceneDamageRect[] ConsumeDirtyRects(int width, int height)
    {
        if (dirtyRects.Count == 0 || width <= 0 || height <= 0)
        {
            dirtyRects.Clear();
            return [];
        }

        var normalized = new SceneDamageRect[dirtyRects.Count];
        var normalizedCount = 0;
        foreach (var dirtyRect in dirtyRects)
        {
            var left = Math.Clamp(dirtyRect.X, 0, width);
            var top = Math.Clamp(dirtyRect.Y, 0, height);
            var right = Math.Clamp(dirtyRect.X + dirtyRect.Width, 0, width);
            var bottom = Math.Clamp(dirtyRect.Y + dirtyRect.Height, 0, height);
            if (right <= left || bottom <= top)
                continue;

            normalized[normalizedCount++] = new SceneDamageRect(left, top, right - left, bottom - top);
        }

        dirtyRects.Clear();
        if (normalizedCount == 0)
            return [];

        if (normalizedCount == normalized.Length)
            return normalized;

        var trimmed = new SceneDamageRect[normalizedCount];
        Array.Copy(normalized, trimmed, normalizedCount);
        return trimmed;
    }
}
