using Enaga.Rendering;
using Xunit;

namespace Enaga.Tests;

public sealed class LowLevelRepaintMatcherTests
{
    [Fact]
    public void IsMatch_NextFrameMatchesFrameEvent()
    {
        var request = new LowLevelRepaintRequest(
            LowLevelRepaintRequestKind.NextFrame,
            new SceneDamageRect(10, 12, 40, 20));
        LowLevelRepaintEvent[] events = [new(LowLevelRepaintEventKind.Frame)];

        Assert.True(LowLevelRepaintMatcher.IsMatch(request, events));
    }

    [Fact]
    public void IsMatch_PointerMoveRequiresSensitiveRectHit()
    {
        var request = new LowLevelRepaintRequest(
            LowLevelRepaintRequestKind.NextPointerMove,
            new SceneDamageRect(20, 30, 40, 20),
            new SceneDamageRect(100, 100, 30, 30));
        LowLevelRepaintEvent[] outsideEvents = [new(LowLevelRepaintEventKind.PointerMove, 90, 90)];
        LowLevelRepaintEvent[] insideEvents = [new(LowLevelRepaintEventKind.PointerMove, 110, 110)];

        Assert.False(LowLevelRepaintMatcher.IsMatch(request, outsideEvents));
        Assert.True(LowLevelRepaintMatcher.IsMatch(request, insideEvents));
    }

    [Fact]
    public void IsMatch_NextInputMatchesTextInput()
    {
        var request = new LowLevelRepaintRequest(
            LowLevelRepaintRequestKind.NextInput,
            new SceneDamageRect(0, 0, 20, 20));
        LowLevelRepaintEvent[] events = [new(LowLevelRepaintEventKind.TextInput)];

        Assert.True(LowLevelRepaintMatcher.IsMatch(request, events));
    }
}
