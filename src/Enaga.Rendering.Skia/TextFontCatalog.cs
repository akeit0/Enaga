using System.Text;
using Enaga.Scene;
using SkiaSharp;

namespace Enaga.Rendering.Skia;


internal sealed class TextFontCatalog : IDisposable
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, string> registeredSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WebFontCacheState> sourceStates = new(StringComparer.Ordinal);
    private readonly Dictionary<ResolvedTypefaceKey, SKTypeface?> typefaceCache = new();
    private readonly Dictionary<ResolvedSampleTypefaceKey, SKTypeface> sampleTypefaceCache = new();
    private readonly Dictionary<ResolvedSystemFallbackBucketKey, Dictionary<int, SKTypeface?>> systemFallbackTypefaceCache = new();
    private readonly HashSet<string> sourceUpdateScratch = new(StringComparer.Ordinal);
    private readonly HashSet<string> fallbackFamilyScratch = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<SKTypeface> disposeScratch = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> candidateScratch = new(8);
    private readonly SKFont glyphScratchFont = new();
    private int[] glyphCodepointScratch = [];
    private ushort[] glyphScratch = [];
    private static readonly SKFontStyle NormalFontStyle = new(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    private static readonly SKFontStyle BoldFontStyle = new(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    private static readonly SKFontStyle ItalicFontStyle = new(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);
    private static readonly SKFontStyle BoldItalicFontStyle = new(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);
    private static readonly string[] JapaneseLanguageHint = ["ja"];
    private string? defaultFamily;
    private string[] fallbackFamilies = [];
    private int version = 1;
    private bool disposed;

    public TextFontCatalog()
    {
        var configuration = NativeTextConfiguration.GetDefaultConfiguration();
        defaultFamily = configuration.DefaultFamily;
        fallbackFamilies = configuration.FallbackFamilies;
        foreach (var registeredFont in configuration.RegisteredFonts)
            registeredSources[registeredFont.Key] = registeredFont.Value;
    }

    public int CurrentVersion
    {
        get
        {
            UpdateTrackedSources();
            lock (sync)
                return version;
        }
    }

    public void Configure(string? nextDefaultFamily, IReadOnlyList<string>? nextFallbackFamilies)
    {
        ThrowIfDisposed();
        lock (sync)
        {
            defaultFamily = string.IsNullOrWhiteSpace(nextDefaultFamily) ? defaultFamily : nextDefaultFamily;
            if (nextFallbackFamilies is not null)
                fallbackFamilies = NormalizeFallbackFamilies_NoLock(nextFallbackFamilies);
            Invalidate_NoLock();
        }
    }

    public void RegisterFont(string family, string source)
    {
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(source))
            return;

        ThrowIfDisposed();
        lock (sync)
        {
            registeredSources[family.Trim()] = source.Trim();
            Invalidate_NoLock();
        }
    }

    public SKTypeface ResolveTypeface(string? requestedFamily, int fontWeight, bool italic = false)
    {
        UpdateTrackedSources();
        lock (sync)
        {
            ThrowIfDisposed_NoLock();
            var normalizedFamily = string.IsNullOrWhiteSpace(requestedFamily) ? null : requestedFamily.Trim();
            var fontStyle = CreateFontStyle(fontWeight, italic);
            var candidates = PopulateCandidates_NoLock(normalizedFamily);
            for (var index = 0; index < candidates.Count; index++)
            {
                var family = candidates[index];
                if (TryResolveFamilyTypeface_NoLock(family, fontStyle, out var typeface))
                    return typeface;
            }

            return SKTypeface.Default;
        }
    }

    public SKTypeface ResolveTypeface(SceneFont font)
        => ResolveTypeface(font.Family, font.Weight, font.Italic);

    internal int SystemFallbackCacheBucketCount => systemFallbackTypefaceCache.Count;

    internal int SystemFallbackCacheEntryCount
    {
        get
        {
            var count = 0;
            foreach (var bucket in systemFallbackTypefaceCache.Values)
                count += bucket.Count;
            return count;
        }
    }

    public SKTypeface ResolveTypefaceForText(string? requestedFamily, int fontWeight, string text, bool italic = false)
    {
        UpdateTrackedSources();
        lock (sync)
        {
            ThrowIfDisposed_NoLock();
            var normalizedFamily = string.IsNullOrWhiteSpace(requestedFamily) ? null : requestedFamily.Trim();
            var normalizedText = text ?? string.Empty;
            var isBold = fontWeight >= 600;
            var fontStyle = CreateFontStyle(fontWeight, italic);
            var cacheKey = new ResolvedSampleTypefaceKey(version, normalizedFamily ?? string.Empty, isBold, italic, normalizedText);
            if (sampleTypefaceCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var resolved = ResolveTypefaceForText_NoLock(normalizedFamily, fontWeight, fontStyle, normalizedText.AsSpan(), italic);
            sampleTypefaceCache[cacheKey] = resolved;
            return resolved;
        }
    }

    public SKTypeface ResolveTypefaceForText(SceneFont font, string text)
        => ResolveTypefaceForText(font.Family, font.Weight, text, font.Italic);

    public SKTypeface ResolveTypefaceForText(SceneFont font, ReadOnlySpan<char> text)
        => ResolveTypefaceForText(font.Family, font.Weight, text, font.Italic);

    public SKTypeface ResolveTypefaceForText(string? requestedFamily, int fontWeight, ReadOnlySpan<char> text, bool italic = false)
    {
        UpdateTrackedSources();
        lock (sync)
        {
            ThrowIfDisposed_NoLock();
            var normalizedFamily = string.IsNullOrWhiteSpace(requestedFamily) ? null : requestedFamily.Trim();
            var fontStyle = CreateFontStyle(fontWeight, italic);
            return ResolveTypefaceForText_NoLock(normalizedFamily, fontWeight, fontStyle, text, italic);
        }
    }

    internal bool TryResolveSingleTypefaceForText(string? requestedFamily, int fontWeight, string text, bool italic, out SKTypeface typeface)
    {
        UpdateTrackedSources();
        lock (sync)
        {
            ThrowIfDisposed_NoLock();
            typeface = null!;
            if (string.IsNullOrEmpty(text))
                return false;

            var normalizedFamily = string.IsNullOrWhiteSpace(requestedFamily) ? null : requestedFamily.Trim();
            var isBold = fontWeight >= 600;
            var fontStyle = CreateFontStyle(fontWeight, italic);
            var cacheKey = new ResolvedSampleTypefaceKey(version, normalizedFamily ?? string.Empty, isBold, italic, text);
            if (sampleTypefaceCache.TryGetValue(cacheKey, out var cached))
            {
                typeface = cached;
                return SupportsText(cached, text);
            }

            if (TryResolveSingleTypefaceForText_NoLock(normalizedFamily, fontStyle, text.AsSpan(), out typeface))
            {
                sampleTypefaceCache[cacheKey] = typeface;
                return true;
            }

            return false;
        }
    }

    internal bool TryResolveSingleTypefaceForText(SceneFont font, string text, out SKTypeface typeface)
        => TryResolveSingleTypefaceForText(font.Family, font.Weight, text, font.Italic, out typeface);

    internal bool TryResolveSingleTypefaceForText(SceneFont font, ReadOnlySpan<char> text, out SKTypeface typeface)
        => TryResolveSingleTypefaceForText(font.Family, font.Weight, text, font.Italic, out typeface);

    internal bool TryResolveSingleTypefaceForText(string? requestedFamily, int fontWeight, ReadOnlySpan<char> text, bool italic, out SKTypeface typeface)
    {
        UpdateTrackedSources();
        lock (sync)
        {
            ThrowIfDisposed_NoLock();
            typeface = null!;
            if (text.IsEmpty)
                return false;

            var normalizedFamily = string.IsNullOrWhiteSpace(requestedFamily) ? null : requestedFamily.Trim();
            var fontStyle = CreateFontStyle(fontWeight, italic);
            return TryResolveSingleTypefaceForText_NoLock(normalizedFamily, fontStyle, text, out typeface);
        }
    }

    private static SKFontStyle CreateFontStyle(int fontWeight, bool italic)
        => fontWeight >= 600
            ? italic ? BoldItalicFontStyle : BoldFontStyle
            : italic ? ItalicFontStyle : NormalFontStyle;

    private IReadOnlyList<string> PopulateCandidates_NoLock(string? requestedFamily)
    {
        candidateScratch.Clear();
        if (!string.IsNullOrWhiteSpace(requestedFamily))
            candidateScratch.Add(requestedFamily);

        if (!string.IsNullOrWhiteSpace(defaultFamily) &&
            !string.Equals(defaultFamily, requestedFamily, StringComparison.OrdinalIgnoreCase))
            candidateScratch.Add(defaultFamily);

        foreach (var family in fallbackFamilies)
        {
            if (string.IsNullOrWhiteSpace(family) ||
                string.Equals(family, requestedFamily, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(family, defaultFamily, StringComparison.OrdinalIgnoreCase))
                continue;
            candidateScratch.Add(family);
        }

        return candidateScratch;
    }

    private SKTypeface ResolveTypefaceForText_NoLock(string? normalizedFamily, int fontWeight, SKFontStyle fontStyle, ReadOnlySpan<char> text, bool italic)
    {
        if (RequiresWhitespaceGlyphFallback(text))
        {
            var candidates = PopulateCandidates_NoLock(normalizedFamily);
            for (var index = 0; index < candidates.Count; index++)
            {
                var family = candidates[index];
                if (TryResolveFamilyTypeface_NoLock(family, fontStyle, out var whitespaceTypeface) &&
                    SupportsWhitespaceGlyphs(whitespaceTypeface, text))
                {
                    return whitespaceTypeface;
                }
            }

            if (TryResolveSystemWhitespaceFallbackTypeface_NoLock(normalizedFamily, fontStyle, text, out var whitespaceMatchedTypeface))
                return whitespaceMatchedTypeface;
        }

        if (TryResolveSingleTypefaceForText_NoLock(normalizedFamily, fontStyle, text, out var typeface))
            return typeface;

        return ResolveTypeface(normalizedFamily, fontWeight, italic);
    }

    private bool TryResolveSingleTypefaceForText_NoLock(string? normalizedFamily, SKFontStyle fontStyle, ReadOnlySpan<char> text, out SKTypeface typeface)
    {
        var candidates = PopulateCandidates_NoLock(normalizedFamily);
        for (var index = 0; index < candidates.Count; index++)
        {
            var family = candidates[index];
            if (TryResolveFamilyTypeface_NoLock(family, fontStyle, out var candidate) &&
                SupportsText(candidate, text))
            {
                typeface = candidate;
                return true;
            }
        }

        if (TryResolveSystemFallbackTypeface_NoLock(normalizedFamily, fontStyle, text, out var matchedTypeface))
        {
            typeface = matchedTypeface;
            return true;
        }

        typeface = null!;
        return false;
    }

    private bool TryResolveFamilyTypeface_NoLock(string family, SKFontStyle fontStyle, out SKTypeface typeface)
    {
        var cacheKey = new ResolvedTypefaceKey(version, family, fontStyle.Weight >= 600, fontStyle.Slant != SKFontStyleSlant.Upright);
        if (typefaceCache.TryGetValue(cacheKey, out var cached))
        {
            typeface = cached!;
            return cached is not null;
        }

        if (TryResolveRegisteredTypeface_NoLock(family, fontStyle, out typeface) ||
            TryResolveSystemTypeface(family, fontStyle, out typeface))
        {
            typefaceCache[cacheKey] = typeface;
            return true;
        }

        typefaceCache[cacheKey] = null;
        return false;
    }

    private bool TryResolveRegisteredTypeface_NoLock(string family, SKFontStyle fontStyle, out SKTypeface typeface)
    {
        typeface = null!;
        if (!registeredSources.TryGetValue(family, out var source))
            return false;

        var result = WebFontCache.Resolve(source);
        sourceStates[source] = result.State;
        if (result.State != WebFontCacheState.Ready || string.IsNullOrWhiteSpace(result.LocalPath))
            return false;

        var loaded = SKTypeface.FromFile(result.LocalPath);
        if (loaded is null)
            return false;

        typeface = loaded;
        return true;
    }

    private static bool TryResolveSystemTypeface(string family, SKFontStyle fontStyle, out SKTypeface typeface)
    {
        typeface = SKTypeface.FromFamilyName(family, fontStyle);
        return typeface is not null;
    }

    private bool TryResolveSystemFallbackTypeface_NoLock(string? requestedFamily, SKFontStyle fontStyle, string text, out SKTypeface typeface)
        => TryResolveSystemFallbackTypeface_NoLock(requestedFamily, fontStyle, text.AsSpan(), out typeface);

    private bool TryResolveSystemFallbackTypeface_NoLock(string? requestedFamily, SKFontStyle fontStyle, ReadOnlySpan<char> text, out SKTypeface typeface)
    {
        typeface = null!;
        if (text.IsEmpty)
            return false;

        var codePoint = GetPreferredCodePoint(text);
        var languageHint = GetLanguageHint(text);
        var matched = ResolveSystemFallbackTypeface_NoLock(requestedFamily, fontStyle, codePoint, languageHint);
        if (matched is null || !SupportsText(matched, text))
            return false;

        typeface = matched;
        return true;
    }

    private bool TryResolveSystemWhitespaceFallbackTypeface_NoLock(string? requestedFamily, SKFontStyle fontStyle, string text, out SKTypeface typeface)
        => TryResolveSystemWhitespaceFallbackTypeface_NoLock(requestedFamily, fontStyle, text.AsSpan(), out typeface);

    private bool TryResolveSystemWhitespaceFallbackTypeface_NoLock(string? requestedFamily, SKFontStyle fontStyle, ReadOnlySpan<char> text, out SKTypeface typeface)
    {
        typeface = null!;
        if (!RequiresWhitespaceGlyphFallback(text))
            return false;

        var codePoint = GetPreferredNonAsciiWhitespaceCodePoint(text);
        if (codePoint == 0)
            return false;

        var languageHint = GetLanguageHint(text);
        var matched = ResolveSystemFallbackTypeface_NoLock(requestedFamily, fontStyle, codePoint, languageHint);
        if (matched is null || !SupportsWhitespaceGlyphs(matched, text))
            return false;

        typeface = matched;
        return true;
    }

    private SKTypeface? ResolveSystemFallbackTypeface_NoLock(string? requestedFamily, SKFontStyle fontStyle, int codePoint, string? languageHint)
    {
        var bucketKey = new ResolvedSystemFallbackBucketKey(
            requestedFamily ?? string.Empty,
            fontStyle.Weight >= 600,
            fontStyle.Slant != SKFontStyleSlant.Upright,
            languageHint is "ja");
        if (systemFallbackTypefaceCache.TryGetValue(bucketKey, out var bucket) &&
            bucket.TryGetValue(codePoint, out var cached))
        {
            return cached;
        }

        var matched = SKFontManager.Default.MatchCharacter(
            requestedFamily,
            fontStyle,
            languageHint is "ja" ? JapaneseLanguageHint : null,
            codePoint);
        bucket ??= systemFallbackTypefaceCache[bucketKey] = new Dictionary<int, SKTypeface?>();
        bucket[codePoint] = matched;
        return matched;
    }

    private bool SupportsText(SKTypeface typeface, string text)
        => SupportsText(typeface, text.AsSpan());

    private bool SupportsText(SKTypeface typeface, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return true;

        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
                continue;

            EnsureGlyphScratchCapacity(count + 1);
            glyphCodepointScratch[count++] = rune.Value;
        }

        return count == 0 || ContainsGlyphsNoAlloc(typeface, glyphCodepointScratch.AsSpan(0, count));
    }

    private static bool RequiresWhitespaceGlyphFallback(string text)
        => RequiresWhitespaceGlyphFallback(text.AsSpan());

    private static bool RequiresWhitespaceGlyphFallback(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return false;

        var sawNonAsciiWhitespace = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune))
                return false;

            if (rune.Value > 0x7F)
                sawNonAsciiWhitespace = true;
        }

        return sawNonAsciiWhitespace;
    }

    private bool SupportsWhitespaceGlyphs(SKTypeface typeface, string text)
        => SupportsWhitespaceGlyphs(typeface, text.AsSpan());

    private bool SupportsWhitespaceGlyphs(SKTypeface typeface, ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value <= 0x7F)
                continue;

            EnsureGlyphScratchCapacity(count + 1);
            glyphCodepointScratch[count++] = rune.Value;
        }

        return count == 0 || ContainsGlyphsNoAlloc(typeface, glyphCodepointScratch.AsSpan(0, count));
    }

    private bool ContainsGlyphsNoAlloc(SKTypeface typeface, ReadOnlySpan<int> codepoints)
    {
        if (codepoints.IsEmpty)
            return true;

        EnsureGlyphScratchCapacity(codepoints.Length);
        glyphScratchFont.Typeface = typeface;
        glyphScratchFont.GetGlyphs(codepoints, glyphScratch.AsSpan(0, codepoints.Length));
        for (var index = 0; index < codepoints.Length; index++)
        {
            if (glyphScratch[index] == 0)
                return false;
        }

        return true;
    }

    private void EnsureGlyphScratchCapacity(int length)
    {
        if (glyphCodepointScratch.Length >= length)
            return;

        var capacity = Math.Max(length, Math.Max(16, glyphCodepointScratch.Length * 2));
        Array.Resize(ref glyphCodepointScratch, capacity);
        Array.Resize(ref glyphScratch, capacity);
    }

    private static int GetPreferredNonAsciiWhitespaceCodePoint(string text)
        => GetPreferredNonAsciiWhitespaceCodePoint(text.AsSpan());

    private static int GetPreferredNonAsciiWhitespaceCodePoint(ReadOnlySpan<char> text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) && rune.Value > 0x7F)
                return rune.Value;
        }

        return 0;
    }

    private static int GetPreferredCodePoint(string text)
        => GetPreferredCodePoint(text.AsSpan());

    private static int GetPreferredCodePoint(ReadOnlySpan<char> text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune))
                return rune.Value;
        }

        return text[0];
    }

    private static string? GetLanguageHint(string text)
        => GetLanguageHint(text.AsSpan());

    private static string? GetLanguageHint(ReadOnlySpan<char> text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsJapaneseRune(rune))
                return "ja";
        }

        return null;
    }

    private static bool IsJapaneseRune(Rune rune)
    {
        return rune.Value is >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF or >= 0x4E00 and <= 0x9FFF or 0x3005 or 0x3006 or 0x30FC;
    }

    private void UpdateTrackedSources()
    {
        lock (sync)
        {
            ThrowIfDisposed_NoLock();
            var changed = false;
            sourceUpdateScratch.Clear();
            foreach (var source in registeredSources.Values)
            {
                if (!sourceUpdateScratch.Add(source))
                    continue;

                var result = WebFontCache.Resolve(source);
                if (sourceStates.TryGetValue(source, out var previousState) && previousState == result.State)
                    continue;

                sourceStates[source] = result.State;
                changed = true;
            }

            if (changed)
                Invalidate_NoLock();
        }
    }

    private string[] NormalizeFallbackFamilies_NoLock(IReadOnlyList<string> families)
    {
        fallbackFamilyScratch.Clear();
        var normalized = new List<string>(families.Count);
        for (var index = 0; index < families.Count; index++)
        {
            var family = families[index];
            if (string.IsNullOrWhiteSpace(family))
                continue;

            var trimmed = family.Trim();
            if (trimmed.Length == 0 || !fallbackFamilyScratch.Add(trimmed))
                continue;

            normalized.Add(trimmed);
        }

        return normalized.Count == 0 ? [] : normalized.ToArray();
    }

    private void Invalidate_NoLock()
    {
        version++;
        disposeScratch.Clear();
        foreach (var typeface in typefaceCache.Values)
        {
            if (typeface is not null &&
                !ReferenceEquals(typeface, SKTypeface.Default) &&
                disposeScratch.Add(typeface))
            {
                typeface.Dispose();
            }
        }

        foreach (var typeface in sampleTypefaceCache.Values)
        {
            if (!ReferenceEquals(typeface, SKTypeface.Default) && disposeScratch.Add(typeface))
                typeface.Dispose();
        }

        foreach (var bucket in systemFallbackTypefaceCache.Values)
        {
            foreach (var typeface in bucket.Values)
            {
                if (typeface is not null &&
                    !ReferenceEquals(typeface, SKTypeface.Default) &&
                    disposeScratch.Add(typeface))
                {
                    typeface.Dispose();
                }
            }
        }

        typefaceCache.Clear();
        sampleTypefaceCache.Clear();
        systemFallbackTypefaceCache.Clear();
        disposeScratch.Clear();
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;

            Invalidate_NoLock();
            registeredSources.Clear();
            sourceStates.Clear();
            fallbackFamilies = [];
            defaultFamily = null;
            glyphScratchFont.Dispose();
            disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        lock (sync)
            ThrowIfDisposed_NoLock();
    }

    private void ThrowIfDisposed_NoLock()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(TextFontCatalog));
    }

    private readonly record struct ResolvedTypefaceKey(int Version, string Family, bool IsBold, bool Italic);
    private readonly record struct ResolvedSampleTypefaceKey(int Version, string Family, bool IsBold, bool Italic, string Text);
    private readonly record struct ResolvedSystemFallbackBucketKey(string Family, bool IsBold, bool Italic, bool JapaneseLanguage);
}
