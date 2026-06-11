namespace Enaga.Html;

internal static class HtmlTextNormalizer
{
    public static string Normalize(string value, HtmlWhiteSpace whiteSpace = HtmlWhiteSpace.Normal)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (whiteSpace is HtmlWhiteSpace.Pre or HtmlWhiteSpace.PreWrap)
            return value;

        if (whiteSpace == HtmlWhiteSpace.PreLine)
            return NormalizePreservingLineBreaks(value);

        if (IsCollapsibleWhitespaceOnly(value))
            return string.Empty;

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        var previousWhitespace = true;
        foreach (var ch in value)
        {
            if (ch == '\u00a0')
            {
                buffer[length++] = ch;
                previousWhitespace = false;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (previousWhitespace)
                    continue;

                buffer[length++] = ' ';
                previousWhitespace = true;
                continue;
            }

            buffer[length++] = ch;
            previousWhitespace = false;
        }

        if (length > 0 && buffer[length - 1] == ' ')
            length--;

        return length == 0 ? string.Empty : new string(buffer[..length]);
    }

    private static bool IsCollapsibleWhitespaceOnly(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch == '\u00a0' || !char.IsWhiteSpace(ch))
                return false;
        }

        return true;
    }

    private static string NormalizePreservingLineBreaks(string value)
    {
        Span<char> buffer =
            value.Length <= 1024 ? stackalloc char[value.Length] : new char[value.Length];
        var length = 0;
        var previousHorizontalWhitespace = true;
        foreach (var ch in value)
        {
            if (ch == '\u00a0')
            {
                buffer[length++] = ch;
                previousHorizontalWhitespace = false;
                continue;
            }

            if (ch is '\r' or '\n')
            {
                if (length > 0 && buffer[length - 1] == ' ')
                    length--;
                if (length == 0 || buffer[length - 1] != '\n')
                    buffer[length++] = '\n';
                previousHorizontalWhitespace = true;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (previousHorizontalWhitespace)
                    continue;

                buffer[length++] = ' ';
                previousHorizontalWhitespace = true;
                continue;
            }

            buffer[length++] = ch;
            previousHorizontalWhitespace = false;
        }

        if (length > 0 && buffer[length - 1] == ' ')
            length--;

        return length == 0 ? string.Empty : new string(buffer[..length]);
    }
}
