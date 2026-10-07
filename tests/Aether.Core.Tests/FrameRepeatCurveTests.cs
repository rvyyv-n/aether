using System;
using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class FrameRepeatCurveTests
{
    [Fact]
    public void IntervalAt_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FrameRepeatCurve.IntervalAt(TimeSpan.Zero, null!));
    }

    [Fact]
    public void IntervalAt_ZeroHoldTime_ReturnsStartInterval()
    {
        var options = new FrameStepperOptions();

        TimeSpan interval = FrameRepeatCurve.IntervalAt(TimeSpan.Zero, options);

        Assert.Equal(options.StartInterval, interval);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(-5000)]
    public void IntervalAt_NegativeHoldTime_ReturnsStartInterval(int negativeMilliseconds)
    {
        var options = new FrameStepperOptions();

        TimeSpan interval = FrameRepeatCurve.IntervalAt(TimeSpan.FromMilliseconds(negativeMilliseconds), options);

        Assert.Equal(options.StartInterval, interval);
    }

    [Fact]
    public void IntervalAt_ExactlyAccelerationTime_ReturnsMinInterval()
    {
        var options = new FrameStepperOptions();

        TimeSpan interval = FrameRepeatCurve.IntervalAt(options.AccelerationTime, options);

        Assert.Equal(options.MinInterval, interval);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(10_000)]
    public void IntervalAt_BeyondAccelerationTime_ReturnsMinInterval(int additionalMilliseconds)
    {
        var options = new FrameStepperOptions();
        TimeSpan heldTime = options.AccelerationTime + TimeSpan.FromMilliseconds(additionalMilliseconds);

        TimeSpan interval = FrameRepeatCurve.IntervalAt(heldTime, options);

        Assert.Equal(options.MinInterval, interval);
    }

    [Fact]
    public void IntervalAt_Midpoint_ApproximatelyEqualsAverage()
    {
        var options = new FrameStepperOptions
        {
            StartInterval = TimeSpan.FromMilliseconds(100),
            MinInterval = TimeSpan.FromMilliseconds(20),
            AccelerationTime = TimeSpan.FromSeconds(2),
        };

        TimeSpan midTime = TimeSpan.FromTicks(options.AccelerationTime.Ticks / 2);
        TimeSpan interval = FrameRepeatCurve.IntervalAt(midTime, options);

        TimeSpan expectedAverage = TimeSpan.FromMilliseconds((100 + 20) / 2);
        Assert.Equal(expectedAverage, interval);
    }

    [Fact]
    public void IntervalAt_DefaultOptionsMidpoint_MatchesExpectedTicks()
    {
        var options = new FrameStepperOptions(); // Start: 100ms, Min: 33ms, Accel: 2s
        TimeSpan midTime = TimeSpan.FromTicks(options.AccelerationTime.Ticks / 2);

        TimeSpan interval = FrameRepeatCurve.IntervalAt(midTime, options);

        // (100ms + 33ms) / 2 = 66.5ms = 665,000 ticks
        long expectedTicks = (options.StartInterval.Ticks + options.MinInterval.Ticks) / 2;
        Assert.Equal(TimeSpan.FromTicks(expectedTicks), interval);
    }

    [Fact]
    public void IntervalAt_SweepOverAccelerationTime_IsMonotonicNonIncreasing()
    {
        var options = new FrameStepperOptions();
        TimeSpan step = TimeSpan.FromMilliseconds(5);
        TimeSpan maxTime = options.AccelerationTime + TimeSpan.FromSeconds(1);

        TimeSpan previous = options.StartInterval;

        for (TimeSpan t = TimeSpan.Zero; t <= maxTime; t += step)
        {
            TimeSpan current = FrameRepeatCurve.IntervalAt(t, options);

            Assert.True(current <= previous, $"Expected interval at {t} ({current}) to be <= previous ({previous}).");
            Assert.True(current >= options.MinInterval, $"Interval ({current}) dropped below MinInterval ({options.MinInterval}).");
            Assert.True(current <= options.StartInterval, $"Interval ({current}) exceeded StartInterval ({options.StartInterval}).");

            previous = current;
        }
    }

    [Fact]
    public void IntervalAt_CustomOptions_ScalesCorrectly()
    {
        var options = new FrameStepperOptions
        {
            StartInterval = TimeSpan.FromMilliseconds(200),
            MinInterval = TimeSpan.FromMilliseconds(50),
            AccelerationTime = TimeSpan.FromSeconds(1),
        };

        TimeSpan quarterTime = TimeSpan.FromMilliseconds(250);
        TimeSpan interval = FrameRepeatCurve.IntervalAt(quarterTime, options);

        // Fraction = 0.25 -> 200 - (150 * 0.25) = 200 - 37.5 = 162.5ms
        long expectedTicks = options.StartInterval.Ticks - (long)((options.StartInterval.Ticks - options.MinInterval.Ticks) * 0.25);
        Assert.Equal(TimeSpan.FromTicks(expectedTicks), interval);
    }
}
