using Enaga.Html;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlFragmentDamageTests
{
    [Fact]
    public void Diff_without_previous_tree_marks_current_visual_overflow()
    {
        var current = CreateTree(Fragment(1, new HtmlLayoutRect(10.2f, 20.1f, 30.4f, 40.6f)));

        var damage = HtmlFragmentDamage.Diff(null, current);

        Assert.True(damage.HasDamage);
        Assert.Equal(1, damage.AddedFragments);
        var rect = Assert.Single(damage.DirtyRects);
        Assert.Equal(new HtmlDirtyRect(10, 20, 31, 41), rect);
    }

    [Fact]
    public void Diff_unions_old_and_new_visual_overflow_when_fragment_moves()
    {
        var previous = CreateTree(Fragment(1, new HtmlLayoutRect(10, 10, 20, 20)));
        var current = CreateTree(Fragment(1, new HtmlLayoutRect(25, 15, 20, 20)));

        var damage = HtmlFragmentDamage.Diff(previous, current);

        Assert.Equal(1, damage.ChangedFragments);
        var rect = Assert.Single(damage.DirtyRects);
        Assert.Equal(new HtmlDirtyRect(10, 10, 35, 25), rect);
    }

    [Fact]
    public void Diff_marks_removed_fragment_old_visual_overflow()
    {
        var previous = CreateTree(
            Fragment(1, new HtmlLayoutRect(0, 0, 10, 10)),
            Fragment(2, new HtmlLayoutRect(40, 50, 10, 10))
        );
        var current = CreateTree(Fragment(1, new HtmlLayoutRect(0, 0, 10, 10)));

        var damage = HtmlFragmentDamage.Diff(previous, current);

        Assert.Equal(1, damage.RemovedFragments);
        var rect = Assert.Single(damage.DirtyRects);
        Assert.Equal(new HtmlDirtyRect(40, 50, 10, 10), rect);
    }

    [Fact]
    public void Diff_ignores_unchanged_fragment()
    {
        var previous = CreateTree(Fragment(1, new HtmlLayoutRect(0, 0, 10, 10)));
        var current = CreateTree(Fragment(1, new HtmlLayoutRect(0, 0, 10, 10)));

        var damage = HtmlFragmentDamage.Diff(previous, current);

        Assert.False(damage.HasDamage);
        Assert.Empty(damage.DirtyRects);
    }

    private static HtmlFragmentTree CreateTree(params HtmlFragment[] fragments) =>
        new(new HtmlFragmentId(fragments[0].Id.Value), fragments);

    private static HtmlFragment Fragment(int id, HtmlLayoutRect visualOverflow) =>
        new(
            new HtmlFragmentId(id),
            new HtmlFormattingNodeId(id),
            ParentId: null,
            Children: [],
            HtmlFragmentKind.BlockBox,
            BorderBox: visualOverflow,
            VisualOverflow: visualOverflow,
            PaintVersion: 1
        );
}
