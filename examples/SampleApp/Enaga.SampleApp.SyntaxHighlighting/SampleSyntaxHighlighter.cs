using Acornima;
using Acornima.Jsx;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo;
namespace Enaga.SampleApp.SyntaxHighlighting;

public sealed class SampleSyntaxHighlighter
{
    public HighlightedCodeLine[] BuildHighlightedLines(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return [];

        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var tokens = new List<HighlightedToken>();

        try
        {
            var parser = new JsxParser(new JsxParserOptions
            {
                OnToken = (in Token token) =>
                {
                    if (token.End <= token.Start || token.End > text.Length)
                        return;

                    tokens.Add(new HighlightedToken(token.Start, token.End, ClassifyToken(token, text)));
                }
            });

            parser.ParseScript(text);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SampleSyntaxHighlighter] Failed to highlight source: {ex.Message}");
            return [];
        }

        return BuildLines(text, tokens);
    }

    public JsArray BuildHighlightedLineArrays(JsRealm realm, string? source)
    {
        ArgumentNullException.ThrowIfNull(realm);

        var lines = BuildHighlightedLines(source);
        var lineValues = new JsValue[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            lineValues[i] = JsValue.FromObject(CreateLineArray(realm, lines[i]));
        return realm.CreateArrayObject(lineValues);
    }

    private static string ClassifyToken(in Token token, string source)
    {
        var text = source[token.Start..token.End];
        if (token.Kind is TokenKind.StringLiteral or TokenKind.Template)
            return "string";
        if (token.Kind is TokenKind.NumericLiteral or TokenKind.BigIntLiteral or TokenKind.BooleanLiteral or TokenKind.NullLiteral)
            return "number";
        if (token.Kind == TokenKind.Keyword)
            return "keyword";
        if (token.Kind is TokenKind.Identifier or TokenKind.Extension)
            return IsJsxTagName(source, token.Start, token.End) ? "jsx-tag" : "identifier";
        if (token.Kind == TokenKind.Punctuator)
            return IsOperatorText(token.KindText) || IsOperatorText(text) ? "operator" : "punctuation";
        if (text is "<" or ">" or "</" or "/>" or "{" or "}" or "(" or ")" or "[" or "]" or "," or ";" or ":" or "." or "=" or "=>")
            return "punctuation";
        return "plain";
    }

    private static bool IsOperatorText(string? text)
    {
        return text is "=" or "=>" or "+" or "-" or "*" or "/" or "%" or "++" or "--"
            or "==" or "===" or "!=" or "!==" or ">" or "<" or ">=" or "<="
            or "&&" or "||" or "??" or "!" or "?" or ":" or "." or "..." or "*=" or "+=" or "-="
            or "/=" or "%=";
    }

    private static bool IsJsxTagName(string source, int start, int end)
    {
        if (start <= 0)
            return false;

        var previous = source[start - 1];
        if (previous != '<' && previous != '/')
            return false;

        if (end <= start)
            return false;

        var first = source[start];
        return char.IsLetter(first);
    }

    private static HighlightedCodeLine[] BuildLines(string source, List<HighlightedToken> sortedTokens)
    {
        sortedTokens.Sort(static (left, right) => left.Start.CompareTo(right.Start));

        var segments = new List<HighlightedCodeSegment>();
        var cursor = 0;
        foreach (var token in sortedTokens)
        {
            if (token.Start > cursor)
                segments.Add(new HighlightedCodeSegment(source[cursor..token.Start], "plain"));

            segments.Add(new HighlightedCodeSegment(source[token.Start..token.End], token.Kind));
            cursor = token.End;
        }

        if (cursor < source.Length)
            segments.Add(new HighlightedCodeSegment(source[cursor..], "plain"));

        var lines = new List<HighlightedCodeLine>();
        var currentLine = new List<HighlightedCodeSegment>();

        foreach (var segment in segments)
        {
            var parts = segment.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    currentLine.Add(new HighlightedCodeSegment(parts[i], segment.Kind));

                if (i < parts.Length - 1)
                {
                    lines.Add(new HighlightedCodeLine([.. currentLine]));
                    currentLine.Clear();
                }
            }
        }

        lines.Add(new HighlightedCodeLine([.. currentLine]));
        return [.. lines];
    }

    private static JsArray CreateLineArray(JsRealm realm, in HighlightedCodeLine line)
    {
        var segmentValues = new JsValue[line.Segments.Length];
        for (var i = 0; i < line.Segments.Length; i++)
            segmentValues[i] = JsValue.FromObject(CreateSegmentArray(realm, line.Segments[i]));
        return realm.CreateArrayObject(segmentValues);
    }

    private static JsArray CreateSegmentArray(JsRealm realm, in HighlightedCodeSegment segment)
    {
        return realm.CreateArrayObject([
            JsValue.FromString(segment.Text),
            JsValue.FromString(segment.Kind),
        ]);
    }

    public readonly record struct HighlightedCodeSegment(string Text, string Kind);

    public readonly record struct HighlightedCodeLine(HighlightedCodeSegment[] Segments);

    private readonly record struct HighlightedToken(int Start, int End, string Kind);
}
