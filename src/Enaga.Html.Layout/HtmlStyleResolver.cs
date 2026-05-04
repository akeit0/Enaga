namespace Enaga.Html;

using Enaga.Html.Css;
using Enaga.Html.Dom;
using Enaga.Layout;
using Enaga.Rendering;

internal sealed class HtmlStyleResolver(HtmlOptions options, LayoutEngineConfig layoutConfig)
{
    private readonly List<HtmlCssRule> matchingRules = new();

    public HtmlComputedStyle Resolve(
        HtmlDomElement element,
        HtmlComputedStyle? inherited,
        IReadOnlyList<HtmlDomElement> ancestors,
        IReadOnlyList<bool> ancestorHoverStates,
        HtmlStyleSheet styleSheet,
        bool isHovered,
        bool isActive,
        int viewportWidth,
        int viewportHeight,
        string? basePath)
    {
        var style = HtmlComputedStyle.CreateDefault(options, layoutConfig);
        if (inherited is not null)
            style.ApplyInheritedValues(inherited);

        style.ApplyElementDefaults(element.LocalName, layoutConfig);
        style.ApplyElementAttributes(element);

        matchingRules.Clear();
        styleSheet.AddMatchingRules(
            element,
            ancestors,
            ancestorHoverStates,
            isHovered,
            viewportWidth,
            viewportHeight,
            matchingRules);
        for (var index = 0; index < matchingRules.Count; index++)
            style.Apply(matchingRules[index].Declarations);

        var inlineStyle = element.GetAttribute("style");
        if (!string.IsNullOrWhiteSpace(inlineStyle))
            style.Apply(HtmlCssParser.ParseDeclarations(inlineStyle));

        style.NormalizeAfterCascade(element.LocalName, layoutConfig);
        style.ApplyDefaultInteraction(element.LocalName, isHovered, isActive);
        style.ResolveBackgroundImageUrl(value => HtmlUrlResolver.Resolve(value, basePath));
        return style;
    }
}
