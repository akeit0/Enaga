namespace Enaga.Html.Style;

public static class RestyleClassifier
{
    public static RestyleKind? Classify(RestyleHint hint, bool hasCurrentStyle)
    {
        if (!hasCurrentStyle)
            return RestyleKind.MatchAndCascade;

        if ((hint & (RestyleHint.MatchSelf | RestyleHint.MatchDescendants | RestyleHint.PseudoState)) != 0)
            return RestyleKind.MatchAndCascade;

        if ((hint & RestyleHint.ReplaceInlineStyle) != 0)
            return RestyleKind.CascadeWithReplacements;

        if ((hint & (RestyleHint.CascadeSelf | RestyleHint.CascadeDescendants | RestyleHint.MediaQuery)) != 0)
            return RestyleKind.CascadeOnly;

        return null;
    }
}
