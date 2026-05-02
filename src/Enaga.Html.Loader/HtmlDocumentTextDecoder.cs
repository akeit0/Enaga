using System.Text;
using System.Text.RegularExpressions;

namespace Enaga.Html.Loader;

public static partial class HtmlDocumentLoader
{
    static HtmlDocumentLoader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static string DecodeText(byte[] bytes, string? declaredEncoding)
        => DetectEncoding(bytes, declaredEncoding).GetString(SkipBom(bytes));

    private static Encoding DetectEncoding(byte[] bytes, string? declaredEncoding)
    {
        if (!string.IsNullOrWhiteSpace(declaredEncoding))
            return ResolveEncoding(declaredEncoding) ?? Encoding.UTF8;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;

        var strictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            strictUtf8.GetString(bytes);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(932);
        }
    }

    private static Encoding? ResolveEncoding(string value)
    {
        var normalized = value.Trim().Trim('"', '\'');
        if (normalized.Equals("shift_jis", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("shift-jis", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("sjis", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("windows-31j", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("x-sjis", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.GetEncoding(932);
        }

        try
        {
            return Encoding.GetEncoding(normalized);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? TryGetDeclaredEncoding(string html)
    {
        var match = CharsetRegex().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string DecodeWithBomOrUtf8(byte[] bytes)
        => Encoding.UTF8.GetString(SkipBom(bytes));

    private static ReadOnlySpan<byte> SkipBom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.AsSpan(3)
            : bytes;

    [GeneratedRegex("charset\\s*=\\s*['\\\"]?([^\\s'\\\";>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CharsetRegex();
}
