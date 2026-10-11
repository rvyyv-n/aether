using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class TransportStateTests
{
    [Theory]
    [InlineData(PlaybackStatus.Idle, PlayButtonAction.Play)]
    [InlineData(PlaybackStatus.Loading, PlayButtonAction.Play)]
    [InlineData(PlaybackStatus.Paused, PlayButtonAction.Play)]
    [InlineData(PlaybackStatus.Playing, PlayButtonAction.Pause)]
    [InlineData(PlaybackStatus.Ended, PlayButtonAction.Replay)]
    public void PlayButton_FollowsStatus(PlaybackStatus status, PlayButtonAction expected) =>
        Assert.Equal(expected, TransportState.PlayButton(status));

    [Fact]
    public void DisplayPosition_EndedReadsTheFullDuration()
    {
        var state = PlayerState.Initial with { Position = 194.96, Duration = 195.2, Status = PlaybackStatus.Ended };

        Assert.Equal(195.2, TransportState.DisplayPosition(state));
        Assert.Equal("03:15", Timecode.FormatClock(TransportState.DisplayPosition(state), state.Duration));
    }

    [Theory]
    [InlineData(PlaybackStatus.Playing, 195.2)]
    [InlineData(PlaybackStatus.Ended, 0)] // no duration known: keep the position
    public void DisplayPosition_OtherwiseIsThePosition(PlaybackStatus status, double duration)
    {
        var state = PlayerState.Initial with { Position = 42.5, Duration = duration, Status = status };

        Assert.Equal(42.5, TransportState.DisplayPosition(state));
    }

    [Theory]
    [InlineData(42.5, true, "seek 42.5 absolute+keyframes")]
    [InlineData(42.5, false, "seek 42.5 absolute+exact")]
    [InlineData(1.23456, false, "seek 1.235 absolute+exact")]
    [InlineData(-3, false, "seek 0 absolute+exact")]
    [InlineData(double.NaN, true, "seek 0 absolute+keyframes")]
    public void Seek_KeyframesWhileDragging_ExactOtherwise(double seconds, bool dragging, string expected) =>
        Assert.Equal(expected, TransportState.Seek(seconds, dragging).ToString());

    [Theory]
    [InlineData(80, 80, 0)]
    [InlineData(0, 65, 65)]
    [InlineData(0, 0, TransportState.DefaultVolume)]
    public void ToggleMute_SilencesOrRestores(double volume, double lastAudible, double expected) =>
        Assert.Equal(expected, TransportState.ToggleMute(volume, lastAudible));

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    [InlineData(0.5, false)]
    [InlineData(100, false)]
    public void IsMuted_OnlyWhenSilent(double volume, bool expected) =>
        Assert.Equal(expected, TransportState.IsMuted(volume));
}
