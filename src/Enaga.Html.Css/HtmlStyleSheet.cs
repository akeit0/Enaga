using Enaga.Html.Dom;

namespace Enaga.Html.Css;

internal sealed class HtmlStyleSheet
{
    private readonly HtmlCssRule[] rules;
    private readonly HtmlRuleIndex ruleIndex;
    private readonly Dictionary<HtmlRuleCandidateKey, int[]> candidateIndexCache = new();
    private readonly HtmlCssRule[] hoverDependentRules;

    private HtmlStyleSheet(HtmlCssRule[] rules)
    {
        this.rules = rules;
        ruleIndex = HtmlRuleIndex.Create(rules);
        hoverDependentRules = FilterHoverDependentRules(rules);
    }

    public static HtmlStyleSheet Parse(IReadOnlyList<string> authorStyleTexts, string? extraCss)
    {
        var rules = new List<HtmlCssRule>();
        var order = 0;
        foreach (var styleText in authorStyleTexts)
            HtmlCssParser.ParseRules(styleText, rules, ref order);

        HtmlCssParser.ParseRules(extraCss, rules, ref order);
        rules.Sort(
            static (left, right) =>
            {
                var specificity = left.Specificity.CompareTo(right.Specificity);
                return specificity != 0 ? specificity : left.Order.CompareTo(right.Order);
            }
        );
        return new HtmlStyleSheet([.. rules]);
    }

    public bool CanHoverAffectElement(HtmlDomElement element)
    {
        for (var index = 0; index < hoverDependentRules.Length; index++)
        {
            if (hoverDependentRules[index].HasHoverDependencyOn(element))
                return true;
        }

        return false;
    }

    public bool HasHoverDependencies => hoverDependentRules.Length > 0;

    public bool TryResolvePaintOnlyHoveredTextColor(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        int viewportWidth,
        int viewportHeight,
        out string? color
    )
    {
        var supported = TryResolvePaintOnlyHoveredProperty(
            element,
            ancestors,
            ancestorHoverStates,
            isHovered: true,
            viewportWidth,
            viewportHeight,
            CssPropertyId.Color,
            out var matched,
            out color
        );
        return supported && matched && !string.IsNullOrWhiteSpace(color);
    }

    public bool TryResolvePaintOnlyHoveredBackgroundColor(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        bool isHovered,
        int viewportWidth,
        int viewportHeight,
        out bool matched,
        out string? color
    ) =>
        TryResolvePaintOnlyHoveredProperty(
            element,
            ancestors,
            ancestorHoverStates,
            isHovered,
            viewportWidth,
            viewportHeight,
            CssPropertyId.Background,
            out matched,
            out color
        );

    private bool TryResolvePaintOnlyHoveredProperty(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        bool isHovered,
        int viewportWidth,
        int viewportHeight,
        CssPropertyId property,
        out bool matched,
        out string? value
    )
    {
        value = null;
        matched = false;
        var candidates = GetCandidateIndices(element);
        for (var index = 0; index < candidates.Length; index++)
        {
            var rule = rules[candidates[index]];
            if (
                !rule.HasHoverDependency
                || !rule.Matches(
                    element,
                    ancestors,
                    ancestorHoverStates,
                    isHovered,
                    viewportWidth,
                    viewportHeight
                )
            )
            {
                continue;
            }

            var declarations = rule.Declarations.AsSpan();
            for (
                var declarationIndex = 0;
                declarationIndex < declarations.Length;
                declarationIndex++
            )
            {
                var declaration = declarations[declarationIndex];
                if (declaration.Property is not (CssPropertyId.Color or CssPropertyId.Background))
                    return false;

                if (declaration.Property == property)
                {
                    matched = true;
                    value = declaration.Value;
                }
            }
        }

        return true;
    }

    public void AddMatchingRules(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        bool isHovered,
        int viewportWidth,
        int viewportHeight,
        List<HtmlCssRule> matches
    )
    {
        var candidates = GetCandidateIndices(element);
        for (var index = 0; index < candidates.Length; index++)
        {
            var rule = rules[candidates[index]];
            if (
                rule.Matches(
                    element,
                    ancestors,
                    ancestorHoverStates,
                    isHovered,
                    viewportWidth,
                    viewportHeight
                )
            )
                matches.Add(rule);
        }
    }

    private int[] GetCandidateIndices(HtmlDomElement element)
    {
        var key = new HtmlRuleCandidateKey(element.LocalName, element.Id, element.ClassName);
        if (candidateIndexCache.TryGetValue(key, out var cached))
            return cached;

        var candidates = ruleIndex.CreateCandidates(element, rules.Length);
        var result = candidates.Count == 0 ? [] : candidates.ToArray();
        candidateIndexCache[key] = result;
        return result;
    }

    private static HtmlCssRule[] FilterHoverDependentRules(HtmlCssRule[] rules)
    {
        List<HtmlCssRule>? hoverRules = null;
        for (var index = 0; index < rules.Length; index++)
        {
            if (!rules[index].HasHoverDependency)
                continue;

            hoverRules ??= new List<HtmlCssRule>();
            hoverRules.Add(rules[index]);
        }

        return hoverRules is null ? [] : [.. hoverRules];
    }
}

internal readonly record struct HtmlRuleCandidateKey(
    string LocalName,
    string? Id,
    string? ClassName
);

internal sealed class HtmlRuleIndex
{
    private readonly Dictionary<string, List<int>> byId;
    private readonly Dictionary<string, List<int>> byClass;
    private readonly Dictionary<string, List<int>> byTag;
    private readonly List<int> universal;

    private HtmlRuleIndex(
        Dictionary<string, List<int>> byId,
        Dictionary<string, List<int>> byClass,
        Dictionary<string, List<int>> byTag,
        List<int> universal
    )
    {
        this.byId = byId;
        this.byClass = byClass;
        this.byTag = byTag;
        this.universal = universal;
    }

    public static HtmlRuleIndex Create(HtmlCssRule[] rules)
    {
        var byId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var byClass = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var byTag = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var universal = new List<int>();

        for (var index = 0; index < rules.Length; index++)
        {
            var part = rules[index].RightmostPart;
            if (!string.IsNullOrEmpty(part.Id))
            {
                Add(byId, part.Id, index);
                continue;
            }

            if (part.ClassNames.Length > 0)
            {
                for (var classIndex = 0; classIndex < part.ClassNames.Length; classIndex++)
                    Add(byClass, part.ClassNames[classIndex], index);
                continue;
            }

            if (!string.IsNullOrEmpty(part.TagName) && part.TagName != "*")
            {
                Add(byTag, part.TagName, index);
                continue;
            }

            universal.Add(index);
        }

        return new HtmlRuleIndex(byId, byClass, byTag, universal);
    }

    public List<int> CreateCandidates(HtmlDomElement element, int ruleCount)
    {
        var candidates = new List<int>();
        var seen = ruleCount <= 256 ? stackalloc bool[ruleCount] : new bool[ruleCount];
        AddCandidates(candidates, seen, universal);
        if (byTag.TryGetValue(element.LocalName, out var tagRules))
            AddCandidates(candidates, seen, tagRules);
        if (!string.IsNullOrEmpty(element.Id) && byId.TryGetValue(element.Id, out var idRules))
            AddCandidates(candidates, seen, idRules);

        if (!string.IsNullOrWhiteSpace(element.ClassName))
        {
            var className = element.ClassName.AsSpan();
            var cursor = 0;
            while (cursor < className.Length)
            {
                while (cursor < className.Length && char.IsWhiteSpace(className[cursor]))
                    cursor++;

                var start = cursor;
                while (cursor < className.Length && !char.IsWhiteSpace(className[cursor]))
                    cursor++;

                if (
                    start < cursor
                    && byClass.TryGetValue(className[start..cursor].ToString(), out var classRules)
                )
                    AddCandidates(candidates, seen, classRules);
            }
        }

        candidates.Sort();
        return candidates;
    }

    private static void Add(Dictionary<string, List<int>> index, string key, int ruleIndex)
    {
        if (!index.TryGetValue(key, out var rules))
        {
            rules = new List<int>();
            index[key] = rules;
        }

        rules.Add(ruleIndex);
    }

    private static void AddCandidates(List<int> candidates, Span<bool> seen, List<int> rules)
    {
        for (var index = 0; index < rules.Count; index++)
        {
            var ruleIndex = rules[index];
            if (seen[ruleIndex])
                continue;

            seen[ruleIndex] = true;
            candidates.Add(ruleIndex);
        }
    }
}

internal sealed record HtmlCssRule(
    HtmlSelector Selector,
    HtmlCssDeclarationBlock Declarations,
    int Specificity,
    int Order,
    HtmlMediaCondition? MediaCondition
)
{
    public bool Matches(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        bool isHovered,
        int viewportWidth,
        int viewportHeight
    ) =>
        (MediaCondition is null || MediaCondition.Matches(viewportWidth, viewportHeight))
        && Selector.Matches(element, ancestors, ancestorHoverStates, isHovered);

    public bool CanMatchRightmost(HtmlDomElement element) => Selector.CanMatchRightmost(element);

    public HtmlSelectorPart RightmostPart => Selector.RightmostPart;

    public bool HasHoverDependency => Selector.HasHoverDependency;

    public bool HasHoverDependencyOn(HtmlDomElement element) =>
        Selector.HasHoverDependencyOn(element);
}

internal sealed class HtmlSelector
{
    private readonly HtmlSelectorPart[] parts;
    private readonly HtmlSelectorCombinator[] combinators;

    private HtmlSelector(HtmlSelectorPart[] parts, HtmlSelectorCombinator[] combinators)
    {
        this.parts = parts;
        this.combinators = combinators;
        Specificity = 0;
        for (var index = 0; index < parts.Length; index++)
            Specificity += parts[index].Specificity;
    }

    public int Specificity { get; }

    public bool HasHoverDependency
    {
        get
        {
            for (var index = 0; index < parts.Length; index++)
                if (parts[index].RequiresHover)
                    return true;

            return false;
        }
    }

    public static bool TryParse(ReadOnlySpan<char> selectorText, out HtmlSelector selector)
    {
        selector = default!;
        if (selectorText.IsWhiteSpace() || HasUnsupportedSiblingCombinator(selectorText))
        {
            return false;
        }

        var normalizedSelector = selectorText.Trim();
        var parts = new List<HtmlSelectorPart>();
        var combinators = new List<HtmlSelectorCombinator>();
        var index = 0;
        var nextCombinator = HtmlSelectorCombinator.Descendant;
        while (index < normalizedSelector.Length)
        {
            SkipSelectorWhitespace(normalizedSelector, ref index);
            if (index >= normalizedSelector.Length)
                break;

            if (normalizedSelector[index] == '>')
            {
                nextCombinator = HtmlSelectorCombinator.Child;
                index++;
                continue;
            }

            var start = index;
            index = FindSelectorPartEnd(normalizedSelector, index);

            if (!TryParsePart(normalizedSelector[start..index], out var part))
                return false;

            if (parts.Count > 0)
                combinators.Add(nextCombinator);
            parts.Add(part);

            var hadWhitespace = SkipSelectorWhitespace(normalizedSelector, ref index);
            nextCombinator = HtmlSelectorCombinator.Descendant;
            if (index < normalizedSelector.Length && normalizedSelector[index] == '>')
            {
                nextCombinator = HtmlSelectorCombinator.Child;
                index++;
            }
            else if (!hadWhitespace && index < normalizedSelector.Length)
            {
                return false;
            }
        }

        if (parts.Count == 0)
            return false;

        selector = new HtmlSelector([.. parts], [.. combinators]);
        return true;
    }

    private static bool TryParsePart(ReadOnlySpan<char> selectorText, out HtmlSelectorPart selector)
    {
        selector = default;
        var requiresHover = false;
        var requiresFirstChild = false;
        var normalizedSelector = selectorText.Trim();
        var parsedPseudo = true;
        while (parsedPseudo)
        {
            parsedPseudo = false;
            if (normalizedSelector.EndsWith(":hover".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                requiresHover = true;
                normalizedSelector = normalizedSelector[..^":hover".Length];
                parsedPseudo = true;
            }
            else if (
                normalizedSelector.EndsWith(
                    ":first-child".AsSpan(),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                requiresFirstChild = true;
                normalizedSelector = normalizedSelector[..^":first-child".Length];
                parsedPseudo = true;
            }
            else if (
                normalizedSelector.EndsWith(":link".AsSpan(), StringComparison.OrdinalIgnoreCase)
            )
            {
                normalizedSelector = normalizedSelector[..^":link".Length];
                parsedPseudo = true;
            }
            else if (
                normalizedSelector.EndsWith(":visited".AsSpan(), StringComparison.OrdinalIgnoreCase)
            )
            {
                normalizedSelector = normalizedSelector[..^":visited".Length];
                parsedPseudo = true;
            }
        }

        if (normalizedSelector.Length == 0 || normalizedSelector.IndexOf(':') >= 0)
            return false;

        string? tagName = null;
        string? id = null;
        var classes = new List<string>();
        var attributes = new List<HtmlAttributeSelector>();
        var span = normalizedSelector;
        var index = 0;
        while (index < span.Length)
        {
            if (span[index] == '.')
            {
                index += 1;
                var start = index;
                while (index < span.Length && span[index] is not '.' and not '#' and not '[')
                    index += 1;

                classes.Add(span[start..index].ToString());
                continue;
            }

            if (span[index] == '#')
            {
                index += 1;
                var start = index;
                while (index < span.Length && span[index] is not '.' and not '#' and not '[')
                    index += 1;

                id = span[start..index].ToString();
                continue;
            }

            if (span[index] == '[')
            {
                var close = FindAttributeSelectorEnd(span, index);
                if (close < 0)
                    return false;

                if (!TryParseAttributeSelector(span[(index + 1)..close], out var attribute))
                    return false;

                attributes.Add(attribute);
                index = close + 1;
                continue;
            }

            var tagStart = index;
            while (index < span.Length && span[index] is not '.' and not '#' and not '[')
                index += 1;

            tagName = span[tagStart..index].ToString();
        }

        var classNames = FilterClassNames(classes);
        selector = new HtmlSelectorPart(
            string.IsNullOrWhiteSpace(tagName) ? null : tagName.ToLowerInvariant(),
            string.IsNullOrWhiteSpace(id) ? null : id,
            classNames,
            attributes.Count == 0 ? [] : [.. attributes],
            requiresHover,
            requiresFirstChild
        );
        return true;
    }

    private static int FindAttributeSelectorEnd(ReadOnlySpan<char> selectorText, int start)
    {
        var quote = '\0';
        for (var index = start + 1; index < selectorText.Length; index++)
        {
            var ch = selectorText[index];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                else if (ch == '\\' && index + 1 < selectorText.Length)
                    index++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == ']')
                return index;
        }

        return -1;
    }

    private static int FindSelectorPartEnd(ReadOnlySpan<char> selectorText, int start)
    {
        var quote = '\0';
        var bracketDepth = 0;
        var index = start;
        while (index < selectorText.Length)
        {
            var ch = selectorText[index];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                else if (ch == '\\' && index + 1 < selectorText.Length)
                    index++;
                index++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                index++;
                continue;
            }

            if (ch == '[')
            {
                bracketDepth++;
                index++;
                continue;
            }

            if (ch == ']' && bracketDepth > 0)
            {
                bracketDepth--;
                index++;
                continue;
            }

            if (bracketDepth == 0 && (char.IsWhiteSpace(ch) || ch == '>'))
                break;

            index++;
        }

        return index;
    }

    private static bool HasUnsupportedSiblingCombinator(ReadOnlySpan<char> selectorText)
    {
        var quote = '\0';
        var bracketDepth = 0;
        for (var index = 0; index < selectorText.Length; index++)
        {
            var ch = selectorText[index];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                else if (ch == '\\' && index + 1 < selectorText.Length)
                    index++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '[')
            {
                bracketDepth++;
                continue;
            }

            if (ch == ']' && bracketDepth > 0)
            {
                bracketDepth--;
                continue;
            }

            if (bracketDepth == 0 && ch is '+' or '~')
                return true;
        }

        return false;
    }

    private static bool TryParseAttributeSelector(
        ReadOnlySpan<char> selectorText,
        out HtmlAttributeSelector selector
    )
    {
        selector = default;
        var text = selectorText.Trim();
        if (text.IsWhiteSpace())
            return false;

        var caseInsensitive = false;
        if (text.Length >= 2 && char.IsWhiteSpace(text[^2]) && (text[^1] is 'i' or 'I'))
        {
            caseInsensitive = true;
            text = text[..^2].TrimEnd();
        }

        var opIndex = -1;
        var match = HtmlAttributeMatch.Exists;
        if (
            TryFindAttributeOperator(
                text,
                "~=",
                HtmlAttributeMatch.Includes,
                out opIndex,
                out match
            )
            || TryFindAttributeOperator(
                text,
                "|=",
                HtmlAttributeMatch.DashMatch,
                out opIndex,
                out match
            )
            || TryFindAttributeOperator(
                text,
                "^=",
                HtmlAttributeMatch.Prefix,
                out opIndex,
                out match
            )
            || TryFindAttributeOperator(
                text,
                "$=",
                HtmlAttributeMatch.Suffix,
                out opIndex,
                out match
            )
            || TryFindAttributeOperator(
                text,
                "*=",
                HtmlAttributeMatch.Substring,
                out opIndex,
                out match
            )
            || TryFindAttributeOperator(text, "=", HtmlAttributeMatch.Exact, out opIndex, out match)
        )
        {
            var name = text[..opIndex].Trim().ToString();
            var value = TrimQuotes(
                    text[(opIndex + (match == HtmlAttributeMatch.Exact ? 1 : 2))..].Trim()
                )
                .ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(value))
                return false;

            selector = new HtmlAttributeSelector(name, match, value, caseInsensitive);
            return true;
        }

        var attrName = text.ToString();
        if (string.IsNullOrWhiteSpace(attrName))
            return false;

        selector = new HtmlAttributeSelector(
            attrName,
            HtmlAttributeMatch.Exists,
            null,
            caseInsensitive
        );
        return true;
    }

    private static bool TryFindAttributeOperator(
        ReadOnlySpan<char> text,
        string op,
        HtmlAttributeMatch operatorMatch,
        out int opIndex,
        out HtmlAttributeMatch match
    )
    {
        opIndex = text.IndexOf(op.AsSpan(), StringComparison.Ordinal);
        match = operatorMatch;
        return opIndex > 0;
    }

    private static ReadOnlySpan<char> TrimQuotes(ReadOnlySpan<char> value)
    {
        var trimmed = value.Trim();
        return
            trimmed.Length >= 2
            && (
                (trimmed[0] == '"' && trimmed[^1] == '"')
                || (trimmed[0] == '\'' && trimmed[^1] == '\'')
            )
            ? trimmed[1..^1]
            : trimmed;
    }

    private static string[] FilterClassNames(List<string> classes)
    {
        if (classes.Count == 0)
            return [];

        var count = 0;
        for (var index = 0; index < classes.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(classes[index]))
                count++;
        }

        if (count == 0)
            return [];

        var filtered = new string[count];
        var target = 0;
        for (var index = 0; index < classes.Count; index++)
        {
            var className = classes[index];
            if (!string.IsNullOrWhiteSpace(className))
                filtered[target++] = className;
        }

        return filtered;
    }

    public bool Matches(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        bool isHovered
    )
    {
        if (
            parts.Length == 0
            || !PartMatches(
                parts[^1],
                element,
                ancestors.Count > 0 ? ancestors[^1] : null,
                isHovered
            )
        )
            return false;

        var ancestorIndex = ancestors.Count - 1;
        for (var partIndex = parts.Length - 2; partIndex >= 0; partIndex--)
        {
            var combinator = combinators[partIndex];
            if (combinator == HtmlSelectorCombinator.Child)
            {
                if (
                    ancestorIndex < 0
                    || !PartMatches(
                        parts[partIndex],
                        ancestors[ancestorIndex],
                        ancestorIndex > 0 ? ancestors[ancestorIndex - 1] : null,
                        IsAncestorHovered(ancestorHoverStates, ancestorIndex)
                    )
                )
                {
                    return false;
                }

                ancestorIndex--;
                continue;
            }

            var matched = false;
            while (ancestorIndex >= 0)
            {
                if (
                    PartMatches(
                        parts[partIndex],
                        ancestors[ancestorIndex],
                        ancestorIndex > 0 ? ancestors[ancestorIndex - 1] : null,
                        IsAncestorHovered(ancestorHoverStates, ancestorIndex)
                    )
                )
                {
                    matched = true;
                    ancestorIndex--;
                    break;
                }

                ancestorIndex--;
            }

            if (!matched)
                return false;
        }

        return true;
    }

    public bool CanMatchRightmost(HtmlDomElement element) =>
        parts.Length > 0 && parts[^1].CanMatchElementIdentity(element);

    public HtmlSelectorPart RightmostPart => parts[^1];

    public bool HasHoverDependencyOn(HtmlDomElement element)
    {
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].RequiresHover && parts[index].CanMatchElementIdentity(element))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PartMatches(
        HtmlSelectorPart part,
        HtmlDomElement element,
        HtmlDomElement? parent,
        bool isHovered
    ) =>
        part.Matches(element, isHovered)
        && (!part.RequiresFirstChild || IsFirstElementChild(element, parent));

    private static bool IsAncestorHovered(
        IReadOnlyList<bool> ancestorHoverStates,
        int ancestorIndex
    ) =>
        ancestorIndex >= 0
        && ancestorIndex < ancestorHoverStates.Count
        && ancestorHoverStates[ancestorIndex];

    private static bool IsFirstElementChild(HtmlDomElement element, HtmlDomElement? parent)
    {
        if (parent is null)
            return false;

        for (var index = 0; index < parent.Children.Count; index++)
        {
            if (parent.Children[index] is not HtmlDomElement childElement)
                continue;

            return ReferenceEquals(childElement, element) || childElement == element;
        }

        return false;
    }

    private static bool SkipSelectorWhitespace(ReadOnlySpan<char> selectorText, ref int index)
    {
        var skipped = false;
        while (index < selectorText.Length && char.IsWhiteSpace(selectorText[index]))
        {
            skipped = true;
            index++;
        }

        return skipped;
    }
}

internal enum HtmlSelectorCombinator : byte
{
    Descendant,
    Child,
}

internal enum HtmlAttributeMatch : byte
{
    Exists,
    Exact,
    Includes,
    DashMatch,
    Prefix,
    Suffix,
    Substring,
}

internal readonly record struct HtmlAttributeSelector(
    string Name,
    HtmlAttributeMatch Match,
    string? Value,
    bool CaseInsensitive
)
{
    public bool Matches(HtmlDomElement element)
    {
        var attrValue = element.GetAttribute(Name);
        if (attrValue is null)
            return false;

        if (Match == HtmlAttributeMatch.Exists)
            return true;

        var expected = Value ?? string.Empty;
        var comparison = CaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return Match switch
        {
            HtmlAttributeMatch.Exact => string.Equals(attrValue, expected, comparison),
            HtmlAttributeMatch.Includes => AttributeListIncludes(attrValue, expected, comparison),
            HtmlAttributeMatch.DashMatch => string.Equals(attrValue, expected, comparison)
                || attrValue.StartsWith(expected + "-", comparison),
            HtmlAttributeMatch.Prefix => attrValue.StartsWith(expected, comparison),
            HtmlAttributeMatch.Suffix => attrValue.EndsWith(expected, comparison),
            HtmlAttributeMatch.Substring => attrValue.Contains(expected, comparison),
            _ => false,
        };
    }

    private static bool AttributeListIncludes(
        string value,
        string expected,
        StringComparison comparison
    )
    {
        var span = value.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            while (index < span.Length && char.IsWhiteSpace(span[index]))
                index++;

            var start = index;
            while (index < span.Length && !char.IsWhiteSpace(span[index]))
                index++;

            if (start < index && span[start..index].Equals(expected.AsSpan(), comparison))
                return true;
        }

        return false;
    }
}

internal readonly record struct HtmlSelectorPart(
    string? TagName,
    string? Id,
    string[] ClassNames,
    HtmlAttributeSelector[] AttributeSelectors,
    bool RequiresHover,
    bool RequiresFirstChild
)
{
    public int Specificity =>
        (string.IsNullOrEmpty(Id) ? 0 : 100)
        + ClassNames.Length * 10
        + AttributeSelectors.Length * 10
        + (string.IsNullOrEmpty(TagName) ? 0 : 1)
        + (RequiresHover ? 10 : 0)
        + (RequiresFirstChild ? 10 : 0);

    public bool Matches(HtmlDomElement element, bool isHovered)
    {
        if (RequiresHover && !isHovered)
            return false;
        return CanMatchElementIdentity(element);
    }

    public bool CanMatchElementIdentity(HtmlDomElement element)
    {
        if (
            TagName is not null
            && TagName != "*"
            && !string.Equals(TagName, element.LocalName, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }
        if (Id is not null && !string.Equals(Id, element.Id, StringComparison.Ordinal))
            return false;

        for (var index = 0; index < ClassNames.Length; index++)
        {
            if (!element.HasClass(ClassNames[index]))
                return false;
        }

        for (var index = 0; index < AttributeSelectors.Length; index++)
        {
            if (!AttributeSelectors[index].Matches(element))
                return false;
        }

        return true;
    }
}
