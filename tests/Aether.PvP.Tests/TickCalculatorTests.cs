using Aether.PvP.Review.Analysis;
using Xunit;

namespace Aether.PvP.Tests;

public class TickCalculatorTests
{
    [Fact]
    public void Compute_AtZero_ReturnsZeroTickAndZeroSubTick()
    {
        var metric = TickCalculator.Compute(0.0, sourceFps: 240);

        Assert.Equal(0, metric.Milliseconds);
        Assert.Equal(0, metric.GameTick);
        Assert.Equal(0, metric.SubTickPercentage);
        Assert.Equal(0, metric.FrameNumber);
    }

    [Fact]
    public void Compute_NegativeTime_ClampsToZero()
    {
        var metric = TickCalculator.Compute(-5.0, sourceFps: 240);

        Assert.Equal(0, metric.Milliseconds);
        Assert.Equal(0, metric.GameTick);
        Assert.Equal(0, metric.SubTickPercentage);
        Assert.Equal(0, metric.FrameNumber);
    }

    [Theory]
    [InlineData(0.050, 1, 0)]      // Exactly 1 tick (50ms)
    [InlineData(0.075, 1, 50)]     // 1 tick + 25ms (50% sub-tick)
    [InlineData(0.100, 2, 0)]      // Exactly 2 ticks (100ms)
    [InlineData(1.000, 20, 0)]     // Exactly 20 ticks (1 sec)
    [InlineData(1.025, 20, 50)]    // 20 ticks + 25ms
    public void Compute_StandardTickBoundaries_CalculatesCorrectGameTicks(
        double seconds, long expectedTick, long expectedSubTick)
    {
        var metric = TickCalculator.Compute(seconds);

        Assert.Equal(expectedTick, metric.GameTick);
        Assert.Equal(expectedSubTick, metric.SubTickPercentage);
    }

    [Fact]
    public void Compute_WithSourceFps_CalculatesCorrectFrame()
    {
        // 1.5 seconds at 240 fps = frame 360
        var metric = TickCalculator.Compute(1.5, sourceFps: 240);

        Assert.Equal(360, metric.FrameNumber);
    }
}
