namespace Enaga.Html;

internal readonly record struct HtmlFragmentDamageResult(
    IReadOnlyList<HtmlDirtyRect> DirtyRects,
    int AddedFragments,
    int RemovedFragments,
    int ChangedFragments
)
{
    public bool HasDamage => DirtyRects.Count > 0;
}

internal static class HtmlFragmentDamage
{
    public static HtmlFragmentDamageResult Diff(
        HtmlFragmentTree? previous,
        HtmlFragmentTree current
    )
    {
        ArgumentNullException.ThrowIfNull(current);

        if (previous is null)
            return DirtyCurrentTree(current);

        var dirtyRects = new List<HtmlDirtyRect>();
        var added = 0;
        var removed = 0;
        var changed = 0;

        foreach (var (fragmentId, currentFragment) in current.Fragments)
        {
            if (!previous.TryGetFragment(fragmentId, out var previousFragment))
            {
                AddDirtyRect(dirtyRects, currentFragment.VisualOverflow);
                added++;
                continue;
            }

            if (HasSamePaintBounds(previousFragment, currentFragment))
                continue;

            AddDirtyRect(
                dirtyRects,
                previousFragment.VisualOverflow.Union(currentFragment.VisualOverflow)
            );
            changed++;
        }

        foreach (var (fragmentId, previousFragment) in previous.Fragments)
        {
            if (current.TryGetFragment(fragmentId, out _))
                continue;

            AddDirtyRect(dirtyRects, previousFragment.VisualOverflow);
            removed++;
        }

        return new HtmlFragmentDamageResult(dirtyRects, added, removed, changed);
    }

    private static HtmlFragmentDamageResult DirtyCurrentTree(HtmlFragmentTree current)
    {
        var dirtyRects = new List<HtmlDirtyRect>();
        foreach (var fragment in current.OrderedFragments)
            AddDirtyRect(dirtyRects, fragment.VisualOverflow);

        return new HtmlFragmentDamageResult(dirtyRects, current.OrderedFragments.Count, 0, 0);
    }

    private static bool HasSamePaintBounds(HtmlFragment previous, HtmlFragment current) =>
        previous.PaintVersion == current.PaintVersion
        && previous.VisualOverflow.Equals(current.VisualOverflow);

    private static void AddDirtyRect(List<HtmlDirtyRect> dirtyRects, HtmlLayoutRect rect)
    {
        var dirtyRect = rect.ToDirtyRect();
        if (!dirtyRect.IsEmpty)
            dirtyRects.Add(dirtyRect);
    }
}
