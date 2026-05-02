using Enaga.Html.Dom;

namespace Enaga.Html.Css;

internal sealed class HtmlStyleSheet
{
    private readonly HtmlCssRule[] rules;
    private readonly HtmlRuleIndex ruleIndex;
    private readonly Dictionary<HtmlRuleCandidateKey, int[]> candidateIndexCache = new();

    private HtmlStyleSheet(HtmlCssRule[] rules)
    {
        this.rules = rules;
        ruleIndex = HtmlRuleIndex.Create(rules);
    }

    public static HtmlStyleSheet Parse(IReadOnlyList<string> authorStyleTexts, string? extraCss)
    {
        var rules = new List<HtmlCssRule>();
        var order = 0;
        foreach (var styleText in authorStyleTexts)
            HtmlCssParser.ParseRules(styleText, rules, ref order);

        HtmlCssParser.ParseRules(extraCss, rules, ref order);
        rules.Sort(static (left, right) =>
        {
            var specificity = left.Specificity.CompareTo(right.Specificity);
            return specificity != 0 ? specificity : left.Order.CompareTo(right.Order);
        });
        return new HtmlStyleSheet([.. rules]);
    }

    public IEnumerable<HtmlCssRule> Match(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        bool isHovered,
        int viewportWidth,
        int viewportHeight)
    {
        var candidates = GetCandidateIndices(element);
        for (var index = 0; index < candidates.Length; index++)
        {
            var rule = rules[candidates[index]];
            if (rule.Matches(element, ancestors, ancestorHoverStates, isHovered, viewportWidth, viewportHeight))
                yield return rule;
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
}

internal readonly record struct HtmlRuleCandidateKey(string LocalName, string? Id, string? ClassName);

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
        List<int> universal)
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

                if (start < cursor && byClass.TryGetValue(className[start..cursor].ToString(), out var classRules))
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
    HtmlMediaCondition? MediaCondition)
{
    public bool Matches(
        HtmlDomElement element,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        bool isHovered,
        int viewportWidth,
        int viewportHeight)
        => (MediaCondition is null || MediaCondition.Matches(viewportWidth, viewportHeight)) &&
           Selector.Matches(element, ancestors, ancestorHoverStates, isHovered);

    public bool CanMatchRightmost(HtmlDomElement element)
        => Selector.CanMatchRightmost(element);

    public HtmlSelectorPart RightmostPart => Selector.RightmostPart;
}

internal sealed class HtmlSelector
{
    private static readonly System.Buffers.SearchValues<char> invalidSelectorChars = System.Buffers.SearchValues.Create("+~[");
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

    public static bool TryParse(ReadOnlySpan<char> selectorText, out HtmlSelector selector)
    {
        selector = default!;
        if (selectorText.IsWhiteSpace() ||
            selectorText.IndexOfAny(invalidSelectorChars) >= 0)
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
            while (index < normalizedSelector.Length && !char.IsWhiteSpace(normalizedSelector[index]) && normalizedSelector[index] != '>')
                index++;

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
            else if (normalizedSelector.EndsWith(":first-child".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                requiresFirstChild = true;
                normalizedSelector = normalizedSelector[..^":first-child".Length];
                parsedPseudo = true;
            }
            else if (normalizedSelector.EndsWith(":link".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                normalizedSelector = normalizedSelector[..^":link".Length];
                parsedPseudo = true;
            }
            else if (normalizedSelector.EndsWith(":visited".AsSpan(), StringComparison.OrdinalIgnoreCase))
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
        var span = normalizedSelector;
        var index = 0;
        while (index < span.Length)
        {
            if (span[index] == '.')
            {
                index += 1;
                var start = index;
                while (index < span.Length && span[index] is not '.' and not '#')
                    index += 1;

                classes.Add(span[start..index].ToString());
                continue;
            }

            if (span[index] == '#')
            {
                index += 1;
                var start = index;
                while (index < span.Length && span[index] is not '.' and not '#')
                    index += 1;

                id = span[start..index].ToString();
                continue;
            }

            var tagStart = index;
            while (index < span.Length && span[index] is not '.' and not '#')
                index += 1;

            tagName = span[tagStart..index].ToString();
        }

        var classNames = FilterClassNames(classes);
        selector = new HtmlSelectorPart(
            string.IsNullOrWhiteSpace(tagName) ? null : tagName.ToLowerInvariant(),
            string.IsNullOrWhiteSpace(id) ? null : id,
            classNames,
            requiresHover,
            requiresFirstChild);
        return true;
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

    public bool Matches(HtmlDomElement element, IReadOnlyList<HtmlDomElement> ancestors, IReadOnlyList<bool> ancestorHoverStates, bool isHovered)
    {
        if (parts.Length == 0 || !PartMatches(parts[^1], element, ancestors.Count > 0 ? ancestors[^1] : null, isHovered))
            return false;

        var ancestorIndex = ancestors.Count - 1;
        for (var partIndex = parts.Length - 2; partIndex >= 0; partIndex--)
        {
            var combinator = combinators[partIndex];
            if (combinator == HtmlSelectorCombinator.Child)
            {
                if (ancestorIndex < 0 ||
                    !PartMatches(
                        parts[partIndex],
                        ancestors[ancestorIndex],
                        ancestorIndex > 0 ? ancestors[ancestorIndex - 1] : null,
                        IsAncestorHovered(ancestorHoverStates, ancestorIndex)))
                {
                    return false;
                }

                ancestorIndex--;
                continue;
            }

            var matched = false;
            while (ancestorIndex >= 0)
            {
                if (PartMatches(
                        parts[partIndex],
                        ancestors[ancestorIndex],
                        ancestorIndex > 0 ? ancestors[ancestorIndex - 1] : null,
                        IsAncestorHovered(ancestorHoverStates, ancestorIndex)))
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

    public bool CanMatchRightmost(HtmlDomElement element)
        => parts.Length > 0 && parts[^1].CanMatchElementIdentity(element);

    public HtmlSelectorPart RightmostPart => parts[^1];

    private static bool PartMatches(HtmlSelectorPart part, HtmlDomElement element, HtmlDomElement? parent, bool isHovered)
        => part.Matches(element, isHovered) &&
           (!part.RequiresFirstChild || IsFirstElementChild(element, parent));

    private static bool IsAncestorHovered(IReadOnlyList<bool> ancestorHoverStates, int ancestorIndex)
        => ancestorIndex >= 0 &&
           ancestorIndex < ancestorHoverStates.Count &&
           ancestorHoverStates[ancestorIndex];

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
    Child
}

internal readonly record struct HtmlSelectorPart(string? TagName, string? Id, string[] ClassNames, bool RequiresHover, bool RequiresFirstChild)
{
    public int Specificity =>
        (string.IsNullOrEmpty(Id) ? 0 : 100) +
        ClassNames.Length * 10 +
        (string.IsNullOrEmpty(TagName) ? 0 : 1) +
        (RequiresHover ? 10 : 0) +
        (RequiresFirstChild ? 10 : 0);

    public bool Matches(HtmlDomElement element, bool isHovered)
    {
        if (RequiresHover && !isHovered)
            return false;
        return CanMatchElementIdentity(element);
    }

    public bool CanMatchElementIdentity(HtmlDomElement element)
    {
        if (TagName is not null &&
            TagName != "*" &&
            !string.Equals(TagName, element.LocalName, StringComparison.OrdinalIgnoreCase))
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

        return true;
    }
}
