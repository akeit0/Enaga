namespace Enaga.Rendering.Skia;

public static class NativeTextConfiguration
{
    private static readonly object Sync = new();
    private static string? defaultFamily;
    private static string[] fallbackFamilies = [];
    private static readonly Dictionary<string, string> RegisteredFonts = new(
        StringComparer.OrdinalIgnoreCase
    );

    public static void ConfigureFonts(
        string? defaultFamily = null,
        params string[] fallbackFamilies
    )
    {
        lock (Sync)
        {
            NativeTextConfiguration.defaultFamily = string.IsNullOrWhiteSpace(defaultFamily)
                ? NativeTextConfiguration.defaultFamily
                : defaultFamily;
            NativeTextConfiguration.fallbackFamilies = fallbackFamilies
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static void RegisterFont(string family, string source)
    {
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(source))
            return;

        lock (Sync)
            RegisteredFonts[family.Trim()] = source.Trim();
    }

    internal static NativeTextConfigurationSnapshot GetDefaultConfiguration()
    {
        lock (Sync)
            return new NativeTextConfigurationSnapshot(
                defaultFamily,
                fallbackFamilies.ToArray(),
                RegisteredFonts.ToArray()
            );
    }
}

internal readonly record struct NativeTextConfigurationSnapshot(
    string? DefaultFamily,
    string[] FallbackFamilies,
    KeyValuePair<string, string>[] RegisteredFonts
);
