using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class HoverFlyoutTests
{
    [Fact]
    public void RestingOnTheAnchor_OpensAfterTheDelay()
    {
        var flyout = new HoverFlyout();
        Assert.Null(flyout.DueIn);

        flyout.PointerOverAnchor(true);

        Assert.Equal(HoverFlyoutAction.Open, flyout.Decide());
        Assert.Equal(HoverFlyout.OpenDelay, flyout.DueIn);
    }

    [Fact]
    public void PassingOverTheAnchor_DoesNotOpen()
    {
        var flyout = new HoverFlyout();
        flyout.PointerOverAnchor(true);
        flyout.PointerOverAnchor(false);

        Assert.Equal(HoverFlyoutAction.None, flyout.Decide());
        Assert.Null(flyout.DueIn);
    }

    [Fact]
    public void LeavingBoth_ClosesAfterTheGrace()
    {
        var flyout = new HoverFlyout();
        flyout.PointerOverAnchor(true);
        flyout.Opened(pinned: false);
        Assert.Equal(HoverFlyoutAction.None, flyout.Decide());

        flyout.PointerOverAnchor(false);

        Assert.Equal(HoverFlyoutAction.Close, flyout.Decide());
        Assert.Equal(HoverFlyout.CloseDelay, flyout.DueIn);
    }

    [Fact]
    public void CrossingIntoTheFlyout_KeepsItOpen()
    {
        var flyout = new HoverFlyout();
        flyout.PointerOverAnchor(true);
        flyout.Opened(pinned: false);
        flyout.PointerOverAnchor(false);
        flyout.PointerOverFlyout(true);

        Assert.Equal(HoverFlyoutAction.None, flyout.Decide());
    }

    [Fact]
    public void DraggingTheSlider_KeepsItOpenOffTheFlyout()
    {
        var flyout = new HoverFlyout();
        flyout.Opened(pinned: false);
        flyout.PointerOverFlyout(true);
        flyout.Pressed(true);
        flyout.PointerOverFlyout(false);
        Assert.Equal(HoverFlyoutAction.None, flyout.Decide());

        flyout.Pressed(false);
        Assert.Equal(HoverFlyoutAction.Close, flyout.Decide());
    }

    [Fact]
    public void PinnedFlyout_OnlyLightDismisses()
    {
        var flyout = new HoverFlyout();
        flyout.Opened(pinned: true);

        Assert.Equal(HoverFlyoutAction.None, flyout.Decide());
        Assert.Null(flyout.DueIn);
    }

    [Fact]
    public void Closed_WaitsForThePointerToReturnBeforeReopening()
    {
        var flyout = new HoverFlyout();
        flyout.PointerOverAnchor(true);
        flyout.Opened(pinned: false);
        flyout.Closed();
        Assert.Equal(HoverFlyoutAction.None, flyout.Decide());

        flyout.PointerOverAnchor(true);
        Assert.Equal(HoverFlyoutAction.Open, flyout.Decide());
    }

    [Fact]
    public void Closed_ForgetsTheFlyoutPointerAndPin()
    {
        var flyout = new HoverFlyout();
        flyout.Opened(pinned: true);
        flyout.PointerOverFlyout(true);
        flyout.Pressed(true);
        flyout.Closed();

        Assert.False(flyout.IsOpen);
        Assert.Equal(HoverFlyoutAction.None, flyout.Decide());
        flyout.Opened(pinned: false);
        Assert.Equal(HoverFlyoutAction.Close, flyout.Decide());
    }
}
