using Enaga.Rendering;
using Xunit;

namespace Enaga.Tests;

public sealed class ManualDirtyRegionTrackerTests
{
    [Fact]
    public void ConsumeDirtyRects_ClampsRectanglesToViewport()
    {
        var tracker = new ManualDirtyRegionTracker();
        tracker.MarkDirty(-20, 12, 80, 40);

        var dirtyRects = tracker.ConsumeDirtyRects(50, 40);

        var dirtyRect = Assert.Single(dirtyRects);
        Assert.Equal(new SceneDamageRect(0, 12, 50, 28), dirtyRect);
    }

    [Fact]
    public void ConsumeDirtyRects_ClearsConsumedRegions()
    {
        var tracker = new ManualDirtyRegionTracker();
        tracker.MarkDirty(4, 6, 20, 18);

        var first = tracker.ConsumeDirtyRects(100, 100);
        var second = tracker.ConsumeDirtyRects(100, 100);

        Assert.Single(first);
        Assert.Empty(second);
    }
}
