using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class PlaybackSpeedBoundsTests
{
    [Fact]
    public void PlaybackSpeed_DefinesExpectedBoundsConstants()
    {
        Assert.Equal(0.1, PlaybackSpeed.Min);
        Assert.Equal(4.0, PlaybackSpeed.Max);
        Assert.Equal(1.0, PlaybackSpeed.Normal);
    }

    [Theory]
    [InlineData(-10.0, 0.1)]
    [InlineData(-0.01, 0.1)]
    [InlineData(0.0, 0.1)]
    [InlineData(0.05, 0.1)]
    [InlineData(0.0999, 0.1)]
    [InlineData(0.1, 0.1)]
    [InlineData(0.25, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.5, 2.5)]
    [InlineData(4.0, 4.0)]
    [InlineData(4.0001, 4.0)]
    [InlineData(5.0, 4.0)]
    [InlineData(100.0, 4.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    public void Clamp_StrictlyEnforcesBounds(double input, double expected)
    {
        Assert.Equal(expected, PlaybackSpeed.Clamp(input));
    }

    [Fact]
    public void Options_CoversAllSpeedsInStrictlyAscendingOrder()
    {
        var options = PlaybackSpeed.Options;

        // Must match specified menu options
        Assert.Equal([0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0], options);

        // Bounds validation
        Assert.Equal(PlaybackSpeed.Min, options[0]);
        Assert.Equal(PlaybackSpeed.Max, options[^1]);

        for (int i = 0; i < options.Count - 1; i++)
        {
            Assert.True(options[i] < options[i + 1], $"Option at index {i} must be strictly less than {i + 1}");
            Assert.True(options[i] >= PlaybackSpeed.Min && options[i] <= PlaybackSpeed.Max);
        }
    }

    [Theory]
    [InlineData(0.1, "0.1×")]
    [InlineData(0.25, "0.25×")]
    [InlineData(0.5, "0.5×")]
    [InlineData(0.75, "0.75×")]
    [InlineData(1.0, "1.0×")]
    [InlineData(1.25, "1.25×")]
    [InlineData(1.5, "1.5×")]
    [InlineData(2.0, "2.0×")]
    [InlineData(3.0, "3.0×")]
    [InlineData(4.0, "4.0×")]
    [InlineData(0.01, "0.1×")]
    [InlineData(10.0, "4.0×")]
    public void Format_FormatsWithMultiplicationSign(double speed, string expected)
    {
        Assert.Equal(expected, PlaybackSpeed.Format(speed));
    }

    [Theory]
    [InlineData(1.0, 1.000, true)]
    [InlineData(1.0, 1.004, true)]
    [InlineData(1.0, 0.996, true)]
    [InlineData(1.0, 1.006, false)]
    [InlineData(1.0, 0.994, false)]
    [InlineData(2.0, 2.003, true)]
    [InlineData(2.0, 2.010, false)]
    public void IsCurrent_AppliesRoundingThreshold(double option, double speed, bool expected)
    {
        Assert.Equal(expected, PlaybackSpeed.IsCurrent(option, speed));
    }

    [Theory]
    [InlineData(0.01, "set speed 0.1")]
    [InlineData(10.0, "set speed 4")]
    [InlineData(1.25, "set speed 1.25")]
    [InlineData(double.NaN, "set speed 1")]
    public void TransportState_SetSpeed_IntegratesWithPlaybackSpeedClamp(double speed, string expected)
    {
        var cmd = TransportState.SetSpeed(speed);
        Assert.Equal(expected, cmd.ToString());
    }
}
