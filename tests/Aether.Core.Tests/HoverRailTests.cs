using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class HoverRailTests
{
    [Fact]
    public void ApproachingLeftEdge_RevealsHandleWithoutAutoOpening_AndClickOpensImmediately()
    {
        var rail = new HoverRail();
        Assert.False(rail.IsHandleVisible);
        Assert.Null(rail.DueIn);

        rail.PointerNearEdge(true);

        Assert.True(rail.IsHandleVisible);
        Assert.Equal(HoverRailAction.None, rail.Decide());
        Assert.Null(rail.DueIn);

        Assert.True(rail.Open());
        Assert.True(rail.IsOpen);
        Assert.False(rail.IsHandleVisible);
    }

    [Fact]
    public void PassingNearLeftEdge_HidesHandleAgain()
    {
        var rail = new HoverRail();
        rail.PointerNearEdge(true);
        Assert.True(rail.IsHandleVisible);
        rail.PointerNearEdge(false);

        Assert.False(rail.IsHandleVisible);
        Assert.Equal(HoverRailAction.None, rail.Decide());
    }

    [Fact]
    public void HoveringHandle_HoldsChromeOpen()
    {
        var rail = new HoverRail();
        rail.PointerNearEdge(true);
        Assert.False(rail.HoldsChrome);

        rail.PointerOverHandle(true);
        Assert.True(rail.HoldsChrome);

        rail.PointerOverHandle(false);
        Assert.False(rail.HoldsChrome);
    }

    [Fact]
    public void LeavingBothRailAndCorridor_ClosesAfterTheGrace()
    {
        var rail = new HoverRail();
        rail.PointerNearEdge(true);
        rail.Open();
        rail.PointerInCorridor(true);
        Assert.Equal(HoverRailAction.None, rail.Decide());
        Assert.True(rail.HoldsChrome);

        rail.PointerOverRail(true);
        rail.PointerInCorridor(false);
        Assert.Equal(HoverRailAction.None, rail.Decide());
        Assert.True(rail.HoldsChrome);

        rail.PointerOverRail(false);

        Assert.False(rail.HoldsChrome);
        Assert.Equal(HoverRailAction.Close, rail.Decide());
        Assert.Equal(HoverRail.CloseDelay, rail.DueIn);
    }

    [Fact]
    public void ADrag_StopsHandleAndOpening_ButLeavesAnOpenRail()
    {
        var rail = new HoverRail();
        rail.SetDragging(true);
        rail.PointerNearEdge(true);

        Assert.False(rail.IsHandleVisible);
        Assert.Equal(HoverRailAction.None, rail.Decide());
        Assert.False(rail.Open()); // a click during the drag too

        rail.SetDragging(false);
        Assert.True(rail.IsHandleVisible);
        Assert.True(rail.Open());
        rail.SetDragging(true);
        Assert.True(rail.IsOpen);
    }

    [Fact]
    public void Unavailable_ClosesAnOpenRailAndBlocksHandleAndOpening()
    {
        var rail = new HoverRail();
        rail.PointerNearEdge(true);
        rail.Open();

        Assert.True(rail.SetAvailable(false));
        Assert.False(rail.IsOpen);
        Assert.False(rail.SetAvailable(false));

        rail.PointerNearEdge(true);
        Assert.False(rail.IsHandleVisible);
        Assert.False(rail.Open());

        rail.SetAvailable(true);
        Assert.True(rail.IsHandleVisible);
    }

    [Fact]
    public void ClosingUnderARestingPointer_ClearsHandleUntilPointerApproachesAgain()
    {
        var rail = new HoverRail();
        rail.PointerNearEdge(true);
        rail.Open();

        Assert.True(rail.Close()); // Esc
        Assert.False(rail.Close());

        Assert.False(rail.IsHandleVisible);
        Assert.Null(rail.DueIn);
        rail.PointerNearEdge(true);
        Assert.True(rail.IsHandleVisible);
        Assert.Equal(HoverRailAction.None, rail.Decide());
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

    [Fact]
    public void Toggle_OpensHeldWithThePointerAway_AndClosesOnTheSecondPress()
    {
        var rail = new HoverRail();
        Assert.True(rail.Toggle());
        Assert.True(rail.IsOpen);
        Assert.Equal(HoverRailAction.None, rail.Decide());

        Assert.False(rail.Toggle());
        Assert.False(rail.IsOpen);
        Assert.Equal(HoverRailAction.None, rail.Decide());
    }

    [Fact]
    public void Toggle_DoesNothingWhileUnavailable_AndEscapeStillClosesIt()
    {
        var rail = new HoverRail();
        rail.SetAvailable(false);
        Assert.False(rail.Toggle());
        Assert.False(rail.IsOpen);

        rail.SetAvailable(true);
        rail.Toggle();
        Assert.True(rail.Close());
        Assert.False(rail.IsOpen);
    }
}
