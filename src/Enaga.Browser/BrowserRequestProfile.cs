using System.Globalization;

namespace Enaga.Browser;

public sealed record BrowserRequestProfile(string UserAgent, string AcceptLanguage)
{
    public static BrowserRequestProfile Default { get; } =
        new(
            "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko) Enaga.Browser/1.0 Safari/537.36",
            CreateDefaultAcceptLanguage()
        );

    public BrowserRequestProfile(string userAgent)
        : this(userAgent, CreateDefaultAcceptLanguage()) { }

    private static string CreateDefaultAcceptLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        var language = string.IsNullOrWhiteSpace(culture.Name) ? "en-US" : culture.Name;
        var neutral = culture.TwoLetterISOLanguageName;
        if (
            string.IsNullOrWhiteSpace(neutral)
            || string.Equals(neutral, language, StringComparison.OrdinalIgnoreCase)
        )
            return $"{language},en;q=0.8";

        return $"{language},{neutral};q=0.9,en;q=0.8";
    }
}
