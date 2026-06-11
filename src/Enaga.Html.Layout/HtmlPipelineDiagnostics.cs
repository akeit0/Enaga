namespace Enaga.Html;

public readonly record struct HtmlPipelineMetricsSnapshot(
    long StyleMatches,
    long StyleCascades,
    long LayoutCacheHits,
    long LayoutCacheMisses,
    long FragmentsRebuilt,
    long DisplayListCommandsRebuilt,
    int DirtyRectCount,
    long DirtyRectArea
)
{
    public HtmlPipelineMetricsSnapshot WithDirtyRects(int count, long area) =>
        this with
        {
            DirtyRectCount = count,
            DirtyRectArea = area,
        };
}

internal sealed class HtmlPipelineMetrics
{
    private long styleMatches;
    private long styleCascades;
    private long layoutCacheHits;
    private long layoutCacheMisses;
    private long fragmentsRebuilt;
    private long displayListCommandsRebuilt;
    private int dirtyRectCount;
    private long dirtyRectArea;

    public void Reset()
    {
        styleMatches = 0;
        styleCascades = 0;
        layoutCacheHits = 0;
        layoutCacheMisses = 0;
        fragmentsRebuilt = 0;
        displayListCommandsRebuilt = 0;
        dirtyRectCount = 0;
        dirtyRectArea = 0;
    }

    public void AddStyleMatchCascade()
    {
        styleMatches++;
        styleCascades++;
    }

    public void AddStyleMatchCascade(int count)
    {
        if (count <= 0)
            return;

        styleMatches += count;
        styleCascades += count;
    }

    public void AddLayoutCacheHit() => layoutCacheHits++;

    public void AddLayoutCacheMiss() => layoutCacheMisses++;

    public void AddFragmentsRebuilt(int count) => fragmentsRebuilt += count;

    public void AddDisplayListCommandsRebuilt(int count) => displayListCommandsRebuilt += count;

    public HtmlPipelineMetricsSnapshot Snapshot() =>
        new(
            styleMatches,
            styleCascades,
            layoutCacheHits,
            layoutCacheMisses,
            fragmentsRebuilt,
            displayListCommandsRebuilt,
            dirtyRectCount,
            dirtyRectArea
        );
}
