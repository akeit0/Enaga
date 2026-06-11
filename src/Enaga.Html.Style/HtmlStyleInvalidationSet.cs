using Enaga.Html.Dom;

namespace Enaga.Html.Style;

internal sealed class HtmlStyleInvalidationSet
{
    private readonly Dictionary<HtmlNodeId, HtmlElementSnapshotInvalidation> entries = new();

    public int Count => entries.Count;

    public RestyleHint RestyleHint { get; private set; }

    public PipelineInvalidation Invalidation { get; private set; }

    public RenderDamage EstimatedDamage { get; private set; }

    public bool IsEmpty => Count == 0;

    public void Add(HtmlElementSnapshot snapshot)
    {
        Add(HtmlElementSnapshotInvalidator.Classify(snapshot));
    }

    public void Add(HtmlElementSnapshotInvalidation invalidation)
    {
        if (invalidation.IsEmpty)
            return;

        if (entries.TryGetValue(invalidation.NodeId, out var existing))
        {
            invalidation = new HtmlElementSnapshotInvalidation(
                invalidation.NodeId,
                existing.RestyleHint | invalidation.RestyleHint,
                existing.Invalidation | invalidation.Invalidation
            );
        }

        entries[invalidation.NodeId] = invalidation;
        RecomputeTotals();
    }

    public bool TryGet(HtmlNodeId nodeId, out HtmlElementSnapshotInvalidation invalidation) =>
        entries.TryGetValue(nodeId, out invalidation);

    public IReadOnlyCollection<HtmlElementSnapshotInvalidation> Entries => entries.Values;

    public void Clear()
    {
        entries.Clear();
        RestyleHint = RestyleHint.None;
        Invalidation = PipelineInvalidation.None;
        EstimatedDamage = RenderDamage.None;
    }

    private void RecomputeTotals()
    {
        var restyle = RestyleHint.None;
        var invalidation = PipelineInvalidation.None;
        foreach (var entry in entries.Values)
        {
            restyle |= entry.RestyleHint;
            invalidation |= entry.Invalidation;
        }

        RestyleHint = restyle;
        Invalidation = invalidation;
        EstimatedDamage = HtmlElementSnapshotInvalidator.EstimateDamage(invalidation);
    }
}
