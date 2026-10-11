using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class TransportPillTests
{
    [Theory]
    [InlineData(0, 400, 200, 0)]
    [InlineData(100, 400, 200, 50)]
    [InlineData(400, 400, 200, 200)]
    [InlineData(-20, 400, 200, 0)]
    [InlineData(450, 400, 200, 200)]
    [InlineData(100, 0, 200, 0)]
    [InlineData(100, 400, 0, 0)]
    [InlineData(double.NaN, 400, 200, 0)]
    public void TimeAt_ClampsToTheBar(double x, double width, double duration, double expected) =>
        Assert.Equal(expected, SeekBarMath.TimeAt(x, width, duration), 6);

    [Theory]
    [InlineData(50, 400, 200, 100)]
    [InlineData(250, 400, 200, 400)]
    [InlineData(-1, 400, 200, 0)]
    [InlineData(50, 400, 0, 0)]
    [InlineData(double.PositiveInfinity, 400, 200, 0)]
    public void OffsetOf_ClampsToTheBar(double seconds, double width, double duration, double expected) =>
        Assert.Equal(expected, SeekBarMath.OffsetOf(seconds, width, duration), 6);

    [Fact]
    public void OffsetOf_RoundTripsTimeAt() =>
        Assert.Equal(123.456, SeekBarMath.TimeAt(SeekBarMath.OffsetOf(123.456, 387, 195.2), 387, 195.2), 6);

    [Theory]
    [InlineData(200, 50, 0, 400, 175)] // centred
    [InlineData(10, 50, 0, 400, 0)] // held at the left edge
    [InlineData(395, 50, 0, 400, 350)] // held at the right edge
    [InlineData(20, 50, 0, 40, 0)] // less room than the bubble: pinned left
    public void CentreWithin_KeepsTheBubbleInside(double centre, double size, double min, double max, double expected) =>
        Assert.Equal(expected, SeekBarMath.CentreWithin(centre, size, min, max));

    [Fact]
    public void ClipRange_InThenOutIsComplete()
    {
        ClipRange range = default(ClipRange).SetIn(30).SetOut(70);

        Assert.True(range.IsComplete);
        Assert.Equal(40, range.Length);
        Assert.Equal("00:30 – 01:10", range.Label(195));
    }

    [Fact]
    public void ClipRange_OutBeforeInStartsAgainFromOut()
    {
        ClipRange range = default(ClipRange).SetIn(70).SetOut(30);

        Assert.Equal(new ClipRange(null, 30), range);
        Assert.False(range.IsComplete);
        Assert.Equal(string.Empty, range.Label(195));
    }

    [Fact]
    public void ClipRange_InAfterOutStartsAgainFromIn() =>
        Assert.Equal(new ClipRange(70, null), default(ClipRange).SetOut(30).SetIn(70));

    [Fact]
    public void ClipRange_MovingInKeepsALaterOut() =>
        Assert.Equal(new ClipRange(50, 70), default(ClipRange).SetIn(30).SetOut(70).SetIn(50));

    [Fact]
    public void ClipRange_SamePointTwiceIsNotARange() =>
        Assert.False(default(ClipRange).SetIn(30).SetOut(30).IsComplete);

    [Fact]
    public void ClipRange_BadTimesClampToZero() =>
        Assert.Equal(new ClipRange(0, null), default(ClipRange).SetIn(double.NaN));

    [Fact]
    public void ClipRange_LabelUsesHoursForLongFiles() =>
        Assert.Equal("0:00:30 – 0:01:10", new ClipRange(30, 70).Label(3600));

    [Theory]
    [InlineData(1.0, "1.0×")]
    [InlineData(0.25, "0.25×")]
    [InlineData(0.1, "0.1×")]
    [InlineData(4.0, "4.0×")]
    [InlineData(1.333333, "1.33×")]
    [InlineData(9, "4.0×")]
    [InlineData(0, "0.1×")]
    [InlineData(double.NaN, "1.0×")]
    public void Speed_Format(double speed, string expected) =>
        Assert.Equal(expected, PlaybackSpeed.Format(speed));

    [Fact]
    public void Speed_OptionsSpanTheBoundsInOrder()
    {
        Assert.Equal(PlaybackSpeed.Min, PlaybackSpeed.Options[0]);
        Assert.Equal(PlaybackSpeed.Max, PlaybackSpeed.Options[^1]);
        Assert.Equal(PlaybackSpeed.Options.Order(), PlaybackSpeed.Options);
        Assert.Contains(PlaybackSpeed.Normal, PlaybackSpeed.Options);
    }

    [Theory]
    [InlineData(1.0, 1.0, true)]
    [InlineData(1.0, 1.001, true)]
    [InlineData(1.25, 1.0, false)]
    public void Speed_IsCurrent(double option, double speed, bool expected) =>
        Assert.Equal(expected, PlaybackSpeed.IsCurrent(option, speed));

    [Theory]
    [InlineData(1.5, "set speed 1.5")]
    [InlineData(0.01, "set speed 0.1")]
    [InlineData(8, "set speed 4")]
    public void SetSpeed_ClampsToTheMenuRange(double speed, string expected) =>
        Assert.Equal(expected, TransportState.SetSpeed(speed).ToString());

    [Theory]
    [InlineData(5, "seek 5 relative+exact")]
    [InlineData(-5, "seek -5 relative+exact")]
    [InlineData(double.NaN, "seek 0 relative+exact")]
    public void SeekBy_IsRelativeAndExact(double seconds, string expected) =>
        Assert.Equal(expected, TransportState.SeekBy(seconds).ToString());

    [Theory]
    [InlineData(520, false)]
    [InlineData(519.7, false)]
    [InlineData(519, true)]
    [InlineData(373, true)]
    public void IsCompact_BelowThe520PxPill(double width, bool expected) =>
        Assert.Equal(expected, TransportState.IsCompact(width));
}
