using Enaga.Scene;
using SkiaSharp;

namespace Enaga.Rendering.Skia;

internal sealed class SkiaFontCollection : IDisposable
{
    private const int CacheLimit = 128;
    private readonly TextFontCatalog fontCatalog;
    private readonly Lock sync = new();
    private readonly Dictionary<SkiaFontDescription, SkiaFontCacheEntry> cache = new();
    private readonly Queue<SkiaFontDescription> cacheOrder = new();
    private bool disposed;

    public SkiaFontCollection(TextFontCatalog fontCatalog)
    {
        this.fontCatalog = fontCatalog ?? throw new ArgumentNullException(nameof(fontCatalog));
    }

    public SkiaFontDataLease Get(SceneFont font)
    {
        var description = SkiaFontDescription.From(font, fontCatalog.CurrentVersion);
        SkiaFontCacheEntry entry;
        lock (sync)
        {
            ThrowIfDisposed_NoLock();
            if (!cache.TryGetValue(description, out entry!))
            {
                var typeface = fontCatalog.ResolveTypeface(font);
                entry = new SkiaFontCacheEntry(new SkiaFontData(font, typeface));
                cache[description] = entry;
                cacheOrder.Enqueue(description);
                Trim_NoLock();
            }

            entry.RefCount++;
        }

        return new SkiaFontDataLease(this, entry);
    }

    private void Trim_NoLock()
    {
        while (cache.Count > CacheLimit && cacheOrder.Count > 0)
        {
            var oldestDescription = cacheOrder.Dequeue();
            if (!cache.Remove(oldestDescription, out var oldest))
                continue;

            oldest.Evicted = true;
            if (oldest.RefCount == 0)
                oldest.Dispose();
        }
    }

    private void Release(SkiaFontCacheEntry entry)
    {
        lock (sync)
        {
            entry.RefCount--;
            if (entry.RefCount == 0 && entry.Evicted)
                entry.Dispose();
        }
    }

    internal sealed class SkiaFontCacheEntry : IDisposable
    {
        public SkiaFontCacheEntry(SkiaFontData data)
        {
            Data = data;
        }

        public SkiaFontData Data { get; }
        public int RefCount { get; set; }
        public bool Evicted { get; set; }

        public void Dispose()
        {
            Data.Dispose();
        }
    }

    public readonly struct SkiaFontDataLease : IDisposable
    {
        private readonly SkiaFontCacheEntry? entry;
        private readonly SkiaFontCollection? owner;

        internal SkiaFontDataLease(SkiaFontCollection owner, SkiaFontCacheEntry entry)
        {
            this.owner = owner;
            this.entry = entry;
        }

        public SkiaFontData Data => entry?.Data ?? throw new ObjectDisposedException(nameof(SkiaFontDataLease));

        public void Dispose()
        {
            if (entry is not null)
                owner?.Release(entry);
        }
    }

    public sealed class SkiaFontData : IDisposable
    {
        public SkiaFontData(SceneFont font, SKTypeface typeface)
        {
            FontDescription = font;
            Typeface = typeface;
            Font = SkiaFontSynthesis.CreateFont(typeface, font);
            Metrics = Font.Metrics;
        }

        public SceneFont FontDescription { get; }
        public SKTypeface Typeface { get; }
        public SKFont Font { get; }
        public SKFontMetrics Metrics { get; }

        public void Dispose()
        {
            Font.Dispose();
        }
    }

    private readonly record struct SkiaFontDescription(
        int FontVersion,
        int SizeQuarterPx,
        string Identity,
        int Weight,
        bool Italic)
    {
        public static SkiaFontDescription From(SceneFont font, int fontVersion)
        {
            return new SkiaFontDescription(
                fontVersion,
                QuantizePixel(font.Size),
                font.CacheIdentity,
                font.Weight,
                font.Italic);
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
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;

            foreach (var entry in cache.Values)
            {
                entry.Evicted = true;
                if (entry.RefCount == 0)
                    entry.Dispose();
            }

            cache.Clear();
            cacheOrder.Clear();
            disposed = true;
        }
    }

    private void ThrowIfDisposed_NoLock()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(SkiaFontCollection));
    }
}
