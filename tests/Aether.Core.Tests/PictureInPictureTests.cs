using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class PictureInPictureTests
{
    [Fact]
    public void InitialState_StartsOff_NotSwitching_DoesNotCancelClose()
    {
        var pip = new PictureInPicture();

        Assert.False(pip.IsOn);
        Assert.False(pip.IsSwitching);
        Assert.False(pip.CancelsClose);
    }

    [Fact]
    public void TryBeginEnter_WithoutVideo_IsRefused()
    {
        var pip = new PictureInPicture();

        Assert.False(pip.TryBeginEnter(hasVideo: false, fullScreen: false));
        Assert.False(pip.IsOn);
        Assert.False(pip.IsSwitching);
    }

    [Fact]
    public void TryBeginEnter_FromWindowed_RemembersWindowed_AndSetsOnAndSwitchingUntilEndSwitch()
    {
        var pip = new PictureInPicture();

        Assert.True(pip.TryBeginEnter(hasVideo: true, fullScreen: false));
        Assert.True(pip.IsOn);
        Assert.True(pip.IsSwitching);
        Assert.True(pip.CancelsClose);
        Assert.Equal(PictureInPictureReturn.Windowed, pip.ReturnTo);

        pip.EndSwitch();

        Assert.True(pip.IsOn);
        Assert.False(pip.IsSwitching);
        Assert.True(pip.CancelsClose);
        Assert.Equal(PictureInPictureReturn.Windowed, pip.ReturnTo);
    }

    [Fact]
    public void TryBeginEnter_FromFullScreen_RemembersFullScreen()
    {
        var pip = new PictureInPicture();

        Assert.True(pip.TryBeginEnter(hasVideo: true, fullScreen: true));
        Assert.True(pip.IsOn);
        Assert.True(pip.IsSwitching);
        Assert.Equal(PictureInPictureReturn.FullScreen, pip.ReturnTo);

        pip.EndSwitch();

        Assert.Equal(PictureInPictureReturn.FullScreen, pip.ReturnTo);
    }

    [Fact]
    public void TryBeginEnter_WhileSwitching_IsRefused()
    {
        var pip = new PictureInPicture();
        Assert.True(pip.TryBeginEnter(hasVideo: true, fullScreen: false));
        Assert.True(pip.IsSwitching);

        // Nested enter while switching is refused
        Assert.False(pip.TryBeginEnter(hasVideo: true, fullScreen: false));
        Assert.False(pip.TryBeginEnter(hasVideo: true, fullScreen: true));
    }

    [Fact]
    public void TryBeginLeave_WhileSwitching_IsRefused()
    {
        var pip = new PictureInPicture();
        Assert.True(pip.TryBeginEnter(hasVideo: true, fullScreen: false));

        // Re-entrant leave during enter switch is refused
        Assert.False(pip.TryBeginLeave(toFullScreen: false, out _));

        pip.EndSwitch();
        Assert.True(pip.TryBeginLeave(toFullScreen: false, out _));
        Assert.True(pip.IsSwitching);

        // Nested leave or enter during leave switch is refused
        Assert.False(pip.TryBeginLeave(toFullScreen: false, out _));
        Assert.False(pip.TryBeginEnter(hasVideo: true, fullScreen: false));
    }

    [Fact]
    public void TryBeginLeave_WhenOff_IsRefused()
    {
        var pip = new PictureInPicture();

        Assert.False(pip.TryBeginLeave(toFullScreen: false, out _));
        Assert.False(pip.TryBeginLeave(toFullScreen: true, out _));
    }

    [Fact]
    public void TryBeginEnter_WhenAlreadyOn_IsRefused()
    {
        var pip = new PictureInPicture();
        Assert.True(pip.TryBeginEnter(hasVideo: true, fullScreen: false));
        pip.EndSwitch();

        Assert.False(pip.TryBeginEnter(hasVideo: true, fullScreen: false));
        Assert.False(pip.TryBeginEnter(hasVideo: true, fullScreen: true));
    }

    [Fact]
    public void TryBeginLeave_AfterEndSwitch_SucceedsAndReturnsRememberedTarget()
    {
        var pip = new PictureInPicture();
        pip.TryBeginEnter(hasVideo: true, fullScreen: false);
        pip.EndSwitch();

        Assert.True(pip.TryBeginLeave(toFullScreen: false, out var target));
        Assert.Equal(PictureInPictureReturn.Windowed, target);
        Assert.False(pip.IsOn);
        Assert.True(pip.IsSwitching);

        pip.EndSwitch();
        Assert.False(pip.IsSwitching);
    }

    [Fact]
    public void TryBeginLeave_FromFullScreenEnter_ReturnsFullScreen()
    {
        var pip = new PictureInPicture();
        pip.TryBeginEnter(hasVideo: true, fullScreen: true);
        pip.EndSwitch();

        Assert.True(pip.TryBeginLeave(toFullScreen: false, out var target));
        Assert.Equal(PictureInPictureReturn.FullScreen, target);
    }

    [Fact]
    public void TryBeginLeave_ToFullScreenOverride_OverridesRememberedTargetToFullScreen()
    {
        var pip = new PictureInPicture();
        pip.TryBeginEnter(hasVideo: true, fullScreen: false);
        pip.EndSwitch();

        Assert.True(pip.TryBeginLeave(toFullScreen: true, out var target));
        Assert.Equal(PictureInPictureReturn.FullScreen, target);
    }

    [Fact]
    public void CancelsClose_IsTrueOnlyWhileOn()
    {
        var pip = new PictureInPicture();
        Assert.False(pip.CancelsClose);

        pip.TryBeginEnter(hasVideo: false, fullScreen: false);
        Assert.False(pip.CancelsClose);

        pip.TryBeginEnter(hasVideo: true, fullScreen: false);
        Assert.True(pip.CancelsClose);

        pip.EndSwitch();
        Assert.True(pip.CancelsClose);

        pip.TryBeginLeave(toFullScreen: false, out _);
        Assert.False(pip.CancelsClose);

        pip.EndSwitch();
        Assert.False(pip.CancelsClose);
    }

    [Fact]
    public void FullCycle_EnterLeaveEnter_Succeeds()
    {
        var pip = new PictureInPicture();

        // First enter
        Assert.True(pip.TryBeginEnter(hasVideo: true, fullScreen: false));
        pip.EndSwitch();
        Assert.True(pip.IsOn);

        // Leave
        Assert.True(pip.TryBeginLeave(toFullScreen: false, out var target));
        Assert.Equal(PictureInPictureReturn.Windowed, target);
        pip.EndSwitch();
        Assert.False(pip.IsOn);

        // Re-enter
        Assert.True(pip.TryBeginEnter(hasVideo: true, fullScreen: true));
        pip.EndSwitch();
        Assert.True(pip.IsOn);
        Assert.Equal(PictureInPictureReturn.FullScreen, pip.ReturnTo);
    }

    [Theory]
    [InlineData(PlaybackStatus.Idle, true)]
    [InlineData(PlaybackStatus.Loading, false)]
    [InlineData(PlaybackStatus.Playing, false)]
    [InlineData(PlaybackStatus.Paused, false)]
    [InlineData(PlaybackStatus.Ended, false)]
    public void EndsOn_TrueOnlyForPlaybackStatusIdle(PlaybackStatus status, bool expected)
    {
        Assert.Equal(expected, PictureInPicture.EndsOn(status));
    }
}
