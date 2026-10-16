using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class HoverRailTests
{
    [Fact]
    public void RestingInTheHotZone_PeeksThenOpensAfterTheDelay()
    {
        var rail = new HoverRail();
        Assert.False(rail.IsPeeking);
        Assert.Null(rail.DueIn);

        rail.PointerInHotZone(true);

        Assert.True(rail.IsPeeking);
        Assert.Equal(HoverRailAction.Open, rail.Decide());
        Assert.Equal(HoverRail.OpenDelay, rail.DueIn);
        Assert.True(rail.Open());
        Assert.True(rail.IsOpen);
        Assert.False(rail.IsPeeking);
    }

    [Fact]
    public void PassingThroughTheHotZone_DoesNotOpen()
    {
        var rail = new HoverRail();
        rail.PointerInHotZone(true);
        rail.PointerInHotZone(false);

        Assert.Equal(HoverRailAction.None, rail.Decide());
        Assert.False(rail.IsPeeking);
    }

    [Fact]
    public void LeavingBothTheRailAndTheZone_ClosesAfterTheGrace()
    {
        var rail = new HoverRail();
        rail.PointerInHotZone(true);
        rail.Open();
        rail.PointerOverRail(true);
        rail.PointerInHotZone(false);
        Assert.Equal(HoverRailAction.None, rail.Decide());
        Assert.True(rail.HoldsChrome);

        rail.PointerOverRail(false);

        Assert.False(rail.HoldsChrome);
        Assert.Equal(HoverRailAction.Close, rail.Decide());
        Assert.Equal(HoverRail.CloseDelay, rail.DueIn);
    }

    [Fact]
    public void ADrag_StopsItPeekingAndOpening_ButLeavesAnOpenRail()
    {
        var rail = new HoverRail();
        rail.SetDragging(true);
        rail.PointerInHotZone(true);

        Assert.False(rail.IsPeeking);
        Assert.Equal(HoverRailAction.None, rail.Decide());
        Assert.False(rail.Open()); // a click during the drag too

        rail.SetDragging(false);
        Assert.True(rail.Open());
        rail.SetDragging(true);
        Assert.True(rail.IsOpen);
    }

    [Fact]
    public void Unavailable_ClosesAnOpenRailAndBlocksOpening()
    {
        var rail = new HoverRail();
        rail.PointerInHotZone(true);
        rail.Open();

        Assert.True(rail.SetAvailable(false));
        Assert.False(rail.IsOpen);
        Assert.False(rail.SetAvailable(false));

        rail.PointerInHotZone(true);
        Assert.False(rail.IsPeeking);
        Assert.False(rail.Open());

        rail.SetAvailable(true);
        Assert.True(rail.IsPeeking);
    }

    [Fact]
    public void ClosingUnderARestingPointer_WaitsForThePointerToComeBack()
    {
        var rail = new HoverRail();
        rail.PointerInHotZone(true);
        rail.Open();

        Assert.True(rail.Close()); // Esc
        Assert.False(rail.Close());

        Assert.False(rail.IsPeeking);
        Assert.Null(rail.DueIn);
        rail.PointerInHotZone(true);
        Assert.Equal(HoverRailAction.Open, rail.Decide());
    }

    [Fact]
    public void Click_OpensAtOnce()
    {
        var rail = new HoverRail();
        Assert.True(rail.Open());
        Assert.False(rail.Open());
    }

    [Fact]
    public void Pinned_StaysOpenWithThePointerAway_UntilUnpinned()
    {
        var rail = new HoverRail();
        Assert.True(rail.SetPinned(true));
        Assert.True(rail.IsOpen);
        Assert.Equal(HoverRailAction.None, rail.Decide());
        Assert.Null(rail.DueIn);

        rail.SetPinned(false);
        Assert.Equal(HoverRailAction.Close, rail.Decide());
    }

    [Fact]
    public void Pinned_DoesNotOpenWhileUnavailable_AndCloseUnpins()
    {
        var rail = new HoverRail();
        rail.SetAvailable(false);
        Assert.False(rail.SetPinned(true));
        Assert.False(rail.IsOpen);

        rail.SetAvailable(true);
        rail.SetPinned(true);
        Assert.True(rail.Close());
        rail.Open();
        Assert.Equal(HoverRailAction.Close, rail.Decide());
    }
}
