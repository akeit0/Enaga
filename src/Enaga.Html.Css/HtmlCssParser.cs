namespace Enaga.Html.Css;

internal readonly record struct HtmlCssDeclaration(CssPropertyId Property, string Value);

internal readonly record struct HtmlCssDeclarationBlock(HtmlCssDeclaration[] Declarations)
{
    public static HtmlCssDeclarationBlock Empty { get; } = new([]);

    public int Count => Declarations.Length;

    public HtmlCssDeclaration this[int index] => Declarations[index];

    public ReadOnlySpan<HtmlCssDeclaration> AsSpan() => Declarations;
}

internal enum HtmlWebkitScrollbarKind : byte
{
    None,
    Scrollbar,
    Track,
    Thumb
}

internal sealed class HtmlMediaCondition
{
    private static readonly HtmlMediaQuery NeverQuery = new(false, float.NaN, float.NaN, float.NaN, float.NaN);
    private readonly HtmlMediaQuery[] queries;

    private HtmlMediaCondition(HtmlMediaQuery[] queries)
    {
        this.queries = queries;
    }

    public static HtmlMediaCondition Parse(ReadOnlySpan<char> text)
    {
        if (text.IsWhiteSpace())
            return new HtmlMediaCondition([HtmlMediaQuery.Always]);

        var queries = new List<HtmlMediaQuery>();
        var start = 0;
        while (start < text.Length)
        {
            var comma = FindTopLevelComma(text, start);
            var end = comma < 0 ? text.Length : comma;
            queries.Add(ParseQuery(text[start..end].Trim()));
            if (comma < 0)
                break;
            start = comma + 1;
        }

        return new HtmlMediaCondition(queries.Count == 0 ? [HtmlMediaQuery.Always] : [.. queries]);
    }

    public static HtmlMediaCondition? Combine(HtmlMediaCondition? parent, HtmlMediaCondition child)
    {
        if (parent is null)
            return child;

        var combined = new List<HtmlMediaQuery>();
        for (var parentIndex = 0; parentIndex < parent.queries.Length; parentIndex++)
        {
            for (var childIndex = 0; childIndex < child.queries.Length; childIndex++)
                combined.Add(HtmlMediaQuery.Intersect(parent.queries[parentIndex], child.queries[childIndex]));
        }

        return new HtmlMediaCondition([.. combined]);
    }

    public bool Matches(int viewportWidth, int viewportHeight)
    {
        for (var index = 0; index < queries.Length; index++)
        {
            if (queries[index].Matches(viewportWidth, viewportHeight))
                return true;
        }

        return false;
    }

    private static HtmlMediaQuery ParseQuery(ReadOnlySpan<char> query)
    {
        var normalized = query.Trim();
        if (normalized.IsWhiteSpace())
            return HtmlMediaQuery.Always;

        if (StartsWithToken(normalized, "not"))
            return NeverQuery;

        if (StartsWithToken(normalized, "only"))
            normalized = normalized["only".Length..].TrimStart();

        if (StartsWithToken(normalized, "print"))
            return NeverQuery;

        if (StartsWithToken(normalized, "screen"))
            normalized = normalized["screen".Length..].TrimStart();
        else if (StartsWithToken(normalized, "all"))
            normalized = normalized["all".Length..].TrimStart();
        else if (normalized.Length > 0 && normalized[0] != '(')
            return NeverQuery;

        var minWidth = float.NaN;
        var maxWidth = float.NaN;
        var minHeight = float.NaN;
        var maxHeight = float.NaN;
        var cursor = 0;
        while (cursor < normalized.Length)
        {
            var open = normalized[cursor..].IndexOf('(');
            if (open < 0)
                break;
            open += cursor;
            var close = FindMatchingParen(normalized, open + 1);
            if (close < 0)
                break;

            var feature = normalized[(open + 1)..close].Trim();
            var colon = feature.IndexOf(':');
            if (colon > 0)
            {
                var name = feature[..colon].Trim();
                var value = feature[(colon + 1)..].Trim();
                if (TryParseMediaLength(value, out var length))
                {
                    if (name.Equals("min-width".AsSpan(), StringComparison.OrdinalIgnoreCase))
                        minWidth = CombineMin(minWidth, length);
                    else if (name.Equals("max-width".AsSpan(), StringComparison.OrdinalIgnoreCase))
                        maxWidth = CombineMax(maxWidth, length);
                    else if (name.Equals("min-height".AsSpan(), StringComparison.OrdinalIgnoreCase))
                        minHeight = CombineMin(minHeight, length);
                    else if (name.Equals("max-height".AsSpan(), StringComparison.OrdinalIgnoreCase))
                        maxHeight = CombineMax(maxHeight, length);
                }
            }

            cursor = close + 1;
        }

        return new HtmlMediaQuery(true, minWidth, maxWidth, minHeight, maxHeight);
    }

    private static int FindTopLevelComma(ReadOnlySpan<char> text, int start)
    {
        var parenDepth = 0;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '(')
                parenDepth++;
            else if (text[index] == ')' && parenDepth > 0)
                parenDepth--;
            else if (text[index] == ',' && parenDepth == 0)
                return index;
        }

        return -1;
    }

    private static int FindMatchingParen(ReadOnlySpan<char> text, int start)
    {
        var depth = 0;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '(')
                depth++;
            else if (text[index] == ')')
            {
                if (depth == 0)
                    return index;
                depth--;
            }
        }

        return -1;
    }

    private static bool StartsWithToken(ReadOnlySpan<char> text, string token)
    {
        if (!text.StartsWith(token.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        return text.Length == token.Length ||
               char.IsWhiteSpace(text[token.Length]) ||
               text[token.Length] == '(';
    }

    private static bool TryParseMediaLength(ReadOnlySpan<char> value, out float pixels)
    {
        pixels = 0;
        var normalized = value.Trim();
        if (normalized.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^2].Trim();
        else if (normalized.EndsWith("rem".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return float.TryParse(normalized[..^3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pixels) && (pixels *= 16) >= 0;
        else if (normalized.EndsWith("em".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return float.TryParse(normalized[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pixels) && (pixels *= 16) >= 0;

        return float.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pixels);
    }

    private static float CombineMin(float current, float next)
        => float.IsNaN(current) ? next : Math.Max(current, next);

    private static float CombineMax(float current, float next)
        => float.IsNaN(current) ? next : Math.Min(current, next);

    private readonly record struct HtmlMediaQuery(bool Supported, float MinWidth, float MaxWidth, float MinHeight, float MaxHeight)
    {
        public static HtmlMediaQuery Always => new(true, float.NaN, float.NaN, float.NaN, float.NaN);

        public static HtmlMediaQuery Intersect(HtmlMediaQuery left, HtmlMediaQuery right)
            => new(
                left.Supported && right.Supported,
                CombineMin(left.MinWidth, right.MinWidth),
                CombineMax(left.MaxWidth, right.MaxWidth),
                CombineMin(left.MinHeight, right.MinHeight),
                CombineMax(left.MaxHeight, right.MaxHeight));

        public bool Matches(int viewportWidth, int viewportHeight)
        {
            if (!Supported)
                return false;
            if (!float.IsNaN(MinWidth) && viewportWidth < MinWidth)
                return false;
            if (!float.IsNaN(MaxWidth) && viewportWidth > MaxWidth)
                return false;
            if (!float.IsNaN(MinHeight) && viewportHeight < MinHeight)
                return false;
            if (!float.IsNaN(MaxHeight) && viewportHeight > MaxHeight)
                return false;

            return true;
        }
    }
}

internal static class HtmlCssParser
{
    public static HtmlCssDeclarationBlock ParseDeclarations(string? cssText)
    {
        if (string.IsNullOrWhiteSpace(cssText))
            return HtmlCssDeclarationBlock.Empty;

        var declarations = new List<HtmlCssDeclaration>();
        ParseDeclarationBlock(cssText.AsSpan(), declarations);
        return declarations.Count == 0
            ? HtmlCssDeclarationBlock.Empty
            : new HtmlCssDeclarationBlock([.. declarations]);
    }

    public static void ParseRules(string? cssText, List<HtmlCssRule> rules, ref int order)
    {
        if (string.IsNullOrWhiteSpace(cssText))
            return;

        ParseRules(cssText.AsSpan(), rules, ref order, mediaCondition: null);
    }

    private static void ParseRules(ReadOnlySpan<char> cssText, List<HtmlCssRule> rules, ref int order, HtmlMediaCondition? mediaCondition)
    {
        if (cssText.IsWhiteSpace())
            return;

        var span = cssText;
        var cursor = 0;
        while (cursor < span.Length)
        {
            SkipWhitespaceAndComments(span, ref cursor);
            if (cursor >= span.Length)
                break;

            var selectorStart = cursor;
            var open = FindNextTopLevel(span, selectorStart, '{');
            if (open < 0)
                break;

            var close = FindMatchingBlockEnd(span, open + 1);
            if (close < 0)
                break;

            var selectorGroup = span[selectorStart..open].Trim();
            if (TryParseMediaPrelude(selectorGroup, out var childMediaCondition))
            {
                ParseRules(
                    span[(open + 1)..close],
                    rules,
                    ref order,
                    HtmlMediaCondition.Combine(mediaCondition, childMediaCondition));
            }
            else if (!selectorGroup.StartsWith("@".AsSpan(), StringComparison.Ordinal))
            {
                var declarations = new List<HtmlCssDeclaration>();
                ParseDeclarationBlock(span[(open + 1)..close], declarations);
                if (declarations.Count > 0)
                    AddRules(selectorGroup, new HtmlCssDeclarationBlock([.. declarations]), rules, ref order, mediaCondition);
            }

            cursor = close + 1;
        }
    }

    private static void AddRules(
        ReadOnlySpan<char> selectorGroup,
        HtmlCssDeclarationBlock declarations,
        List<HtmlCssRule> rules,
        ref int order,
        HtmlMediaCondition? mediaCondition)
    {
        var start = 0;
        while (start < selectorGroup.Length)
        {
            var comma = selectorGroup[start..].IndexOf(',');
            var end = comma < 0 ? selectorGroup.Length : start + comma;
            var selectorText = selectorGroup[start..end].Trim();
            var scrollbarKind = ResolveWebkitScrollbarKind(ref selectorText);
            var effectiveDeclarations = scrollbarKind == HtmlWebkitScrollbarKind.None
                ? declarations
                : RewriteWebkitScrollbarDeclarations(declarations, scrollbarKind);
            if (selectorText.Length > 0 && HtmlSelector.TryParse(selectorText, out var selector))
            {
                rules.Add(new HtmlCssRule(selector, effectiveDeclarations, selector.Specificity, order, mediaCondition));
                order++;
            }

            if (comma < 0)
                break;
            start = end + 1;
        }
    }

    private static HtmlWebkitScrollbarKind ResolveWebkitScrollbarKind(ref ReadOnlySpan<char> selectorText)
    {
        var selector = selectorText;
        var marker = selector.IndexOf("::-webkit-scrollbar".AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return HtmlWebkitScrollbarKind.None;

        var after = selector[(marker + "::-webkit-scrollbar".Length)..];
        var kind = after.StartsWith("-thumb".AsSpan(), StringComparison.OrdinalIgnoreCase)
            ? HtmlWebkitScrollbarKind.Thumb
            : after.StartsWith("-track".AsSpan(), StringComparison.OrdinalIgnoreCase)
                ? HtmlWebkitScrollbarKind.Track
                : HtmlWebkitScrollbarKind.Scrollbar;
        selectorText = selector[..marker].Trim();
        if (selectorText.Length == 0)
            selectorText = "*".AsSpan();
        return kind;
    }

    private static HtmlCssDeclarationBlock RewriteWebkitScrollbarDeclarations(HtmlCssDeclarationBlock declarations, HtmlWebkitScrollbarKind kind)
    {
        var rewritten = new List<HtmlCssDeclaration>(declarations.Count);
        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            if (kind == HtmlWebkitScrollbarKind.Scrollbar && declaration.Property is CssPropertyId.Width or CssPropertyId.Height)
                rewritten.Add(new HtmlCssDeclaration(CssPropertyId.ScrollbarWidth, declaration.Value));
            else if (kind == HtmlWebkitScrollbarKind.Track && declaration.Property == CssPropertyId.Background)
                rewritten.Add(new HtmlCssDeclaration(CssPropertyId.ScrollbarTrackColor, declaration.Value));
            else if (kind == HtmlWebkitScrollbarKind.Thumb && declaration.Property == CssPropertyId.Background)
                rewritten.Add(new HtmlCssDeclaration(CssPropertyId.ScrollbarThumbColor, declaration.Value));
        }

        return rewritten.Count == 0 ? HtmlCssDeclarationBlock.Empty : new HtmlCssDeclarationBlock([.. rewritten]);
    }

    private static bool TryParseMediaPrelude(ReadOnlySpan<char> prelude, out HtmlMediaCondition condition)
    {
        condition = default!;
        var normalized = prelude.Trim();
        if (!normalized.StartsWith("@media".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        normalized = normalized["@media".Length..].Trim();
        condition = HtmlMediaCondition.Parse(normalized);
        return true;
    }

    private static void ParseDeclarationBlock(ReadOnlySpan<char> block, List<HtmlCssDeclaration> declarations)
    {
        var cursor = 0;
        while (cursor < block.Length)
        {
            SkipWhitespaceAndComments(block, ref cursor);
            if (cursor >= block.Length)
                break;

            var nameStart = cursor;
            var colon = FindNextTopLevel(block, cursor, ':');
            if (colon < 0)
                break;

            var property = ResolveProperty(block[nameStart..colon].Trim());
            cursor = colon + 1;
            var valueStart = cursor;
            var semicolon = FindNextTopLevel(block, cursor, ';');
            var valueEnd = semicolon < 0 ? block.Length : semicolon;
            cursor = semicolon < 0 ? block.Length : semicolon + 1;

            if (property == CssPropertyId.Unknown)
                continue;

            var value = block[valueStart..valueEnd].Trim();
            if (value.Length == 0)
                continue;

            declarations.Add(new HtmlCssDeclaration(property, value.ToString()));
        }
    }

    private static CssPropertyId ResolveProperty(ReadOnlySpan<char> name)
    {
        return name switch
        {
            "display" => CssPropertyId.Display,
            "flex-direction" => CssPropertyId.FlexDirection,
            "flex-wrap" => CssPropertyId.FlexWrap,
            "direction" => CssPropertyId.Direction,
            "justify-content" => CssPropertyId.JustifyContent,
            "align-items" => CssPropertyId.AlignItems,
            "align-self" => CssPropertyId.AlignSelf,
            "order" => CssPropertyId.Order,
            "position" => CssPropertyId.Position,
            "box-sizing" => CssPropertyId.BoxSizing,
            "width" => CssPropertyId.Width,
            "height" => CssPropertyId.Height,
            "min-width" => CssPropertyId.MinWidth,
            "max-width" => CssPropertyId.MaxWidth,
            "min-height" => CssPropertyId.MinHeight,
            "max-height" => CssPropertyId.MaxHeight,
            "aspect-ratio" => CssPropertyId.AspectRatio,
            "left" => CssPropertyId.Left,
            "top" => CssPropertyId.Top,
            "right" => CssPropertyId.Right,
            "bottom" => CssPropertyId.Bottom,
            "flex-grow" => CssPropertyId.FlexGrow,
            "flex-shrink" => CssPropertyId.FlexShrink,
            "flex-basis" => CssPropertyId.FlexBasis,
            "flex" => CssPropertyId.Flex,
            "float" => CssPropertyId.Float,
            "clear" => CssPropertyId.Clear,
            "margin" => CssPropertyId.Margin,
            "margin-inline" => CssPropertyId.MarginInline,
            "margin-inline-start" => CssPropertyId.MarginInlineStart,
            "margin-inline-end" => CssPropertyId.MarginInlineEnd,
            "margin-block" => CssPropertyId.MarginBlock,
            "margin-block-start" => CssPropertyId.MarginBlockStart,
            "margin-block-end" => CssPropertyId.MarginBlockEnd,
            "margin-top" => CssPropertyId.MarginTop,
            "margin-right" => CssPropertyId.MarginRight,
            "margin-bottom" => CssPropertyId.MarginBottom,
            "margin-left" => CssPropertyId.MarginLeft,
            "padding" => CssPropertyId.Padding,
            "padding-top" => CssPropertyId.PaddingTop,
            "padding-right" => CssPropertyId.PaddingRight,
            "padding-bottom" => CssPropertyId.PaddingBottom,
            "padding-left" => CssPropertyId.PaddingLeft,
            "gap" or "row-gap" or "column-gap" => CssPropertyId.Gap,
            "border-width" => CssPropertyId.BorderWidth,
            "border-top-width" => CssPropertyId.BorderTopWidth,
            "border-right-width" => CssPropertyId.BorderRightWidth,
            "border-bottom-width" => CssPropertyId.BorderBottomWidth,
            "border-left-width" => CssPropertyId.BorderLeftWidth,
            "border-style" => CssPropertyId.BorderStyle,
            "border-top-style" => CssPropertyId.BorderTopStyle,
            "border-right-style" => CssPropertyId.BorderRightStyle,
            "border-bottom-style" => CssPropertyId.BorderBottomStyle,
            "border-left-style" => CssPropertyId.BorderLeftStyle,
            "border" => CssPropertyId.Border,
            "border-top" => CssPropertyId.BorderTop,
            "border-right" => CssPropertyId.BorderRight,
            "border-bottom" => CssPropertyId.BorderBottom,
            "border-left" => CssPropertyId.BorderLeft,
            "border-radius" or "border-top-left-radius" or "border-top-right-radius" or "border-bottom-right-radius" or "border-bottom-left-radius" => CssPropertyId.BorderRadius,
            "border-collapse" => CssPropertyId.BorderCollapse,
            "border-spacing" => CssPropertyId.BorderSpacing,
            "box-shadow" => CssPropertyId.BoxShadow,
            "border-color" => CssPropertyId.BorderColor,
            "border-top-color" => CssPropertyId.BorderTopColor,
            "border-right-color" => CssPropertyId.BorderRightColor,
            "border-bottom-color" => CssPropertyId.BorderBottomColor,
            "border-left-color" => CssPropertyId.BorderLeftColor,
            "background" or "background-color" => CssPropertyId.Background,
            "background-image" => CssPropertyId.BackgroundImage,
            "background-size" => CssPropertyId.BackgroundSize,
            "color" => CssPropertyId.Color,
            "font-size" => CssPropertyId.FontSize,
            "font-family" => CssPropertyId.FontFamily,
            "font-weight" => CssPropertyId.FontWeight,
            "font-style" => CssPropertyId.FontStyle,
            "text-align" => CssPropertyId.TextAlign,
            "text-transform" => CssPropertyId.TextTransform,
            "text-decoration" or "text-decoration-line" => CssPropertyId.TextDecoration,
            "text-shadow" => CssPropertyId.TextShadow,
            "list-style" or "list-style-type" => CssPropertyId.ListStyle,
            "white-space" => CssPropertyId.WhiteSpace,
            "text-overflow" => CssPropertyId.TextOverflow,
            "line-height" => CssPropertyId.LineHeight,
            "overflow" or "overflow-x" or "overflow-y" => CssPropertyId.Overflow,
            "contain" => CssPropertyId.Contain,
            "object-fit" => CssPropertyId.ObjectFit,
            "place-content" => CssPropertyId.PlaceContent,
            "place-items" => CssPropertyId.PlaceItems,
            "place-self" => CssPropertyId.PlaceSelf,
            _ => CssPropertyId.Unknown
        };
    }

    private static int FindNextTopLevel(ReadOnlySpan<char> span, int start, char target)
    {
        var quote = '\0';
        var parenDepth = 0;
        for (var index = start; index < span.Length; index++)
        {
            var ch = span[index];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                else if (ch == '\\' && index + 1 < span.Length)
                    index++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '/' && index + 1 < span.Length && span[index + 1] == '*')
            {
                index = SkipComment(span, index + 2);
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                continue;
            }

            if (ch == ')' && parenDepth > 0)
            {
                parenDepth--;
                continue;
            }

            if (parenDepth == 0 && ch == target)
                return index;
        }

        return -1;
    }

    private static int FindMatchingBlockEnd(ReadOnlySpan<char> span, int start)
    {
        var quote = '\0';
        var braceDepth = 0;
        for (var index = start; index < span.Length; index++)
        {
            var ch = span[index];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                else if (ch == '\\' && index + 1 < span.Length)
                    index++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '/' && index + 1 < span.Length && span[index + 1] == '*')
            {
                index = SkipComment(span, index + 2);
                continue;
            }

            if (ch == '{')
            {
                braceDepth++;
                continue;
            }

            if (ch == '}')
            {
                if (braceDepth > 0)
                {
                    braceDepth--;
                    continue;
                }

                return index;
            }
        }

        return -1;
    }

    private static void SkipWhitespaceAndComments(ReadOnlySpan<char> span, ref int cursor)
    {
        while (cursor < span.Length)
        {
            if (char.IsWhiteSpace(span[cursor]))
            {
                cursor++;
                continue;
            }

            if (span[cursor] == '/' && cursor + 1 < span.Length && span[cursor + 1] == '*')
            {
                cursor = SkipComment(span, cursor + 2) + 1;
                continue;
            }

            break;
        }
    }

    private static int SkipComment(ReadOnlySpan<char> span, int start)
    {
        for (var index = start; index + 1 < span.Length; index++)
        {
            if (span[index] == '*' && span[index + 1] == '/')
                return index + 1;
        }

        return span.Length - 1;
    }
}
