using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class MotionCurvesTests
{
    private static readonly CubicBezier EaseOut = new(0.1, 0.9, 0.2, 1.0);

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1.5, 1)]
    public void Ease_IsPinnedAtTheEnds(double t, double expected) => Assert.Equal(expected, EaseOut.Ease(t));

    [Fact]
    public void Ease_LinearCurve_FollowsTime()
    {
        var linear = new CubicBezier(0, 0, 1, 1);
        foreach (double t in new[] { 0.1, 0.25, 0.5, 0.9 })
        {
            Assert.Equal(t, linear.Ease(t), 4);
        }
    }

    [Fact]
    public void Ease_EaseOut_IsAheadOfTimeAndNeverGoesBack()
    {
        double previous = 0;
        for (double t = 0.05; t < 1; t += 0.05)
        {
            double value = EaseOut.Ease(t);
            Assert.True(value > t);
            Assert.True(value >= previous);
            previous = value;
        }
    }

    [Fact]
    public void Spring_FromTheTokens_OvershootsALittleAndSettlesInHalfASecond()
    {
        var spring = new DampedSpring(0.85, 0.35);
        IReadOnlyList<double> path = spring.Sample(1.0 / 60);

        Assert.Equal(0, path[0]);
        Assert.Equal(1, path[^1]);
        double peak = path.Max();
        Assert.InRange(peak, 1.0, 1.1);
        Assert.InRange((path.Count - 1) / 60.0, 0.3, 0.9);
    }

    [Fact]
    public void Spring_CriticallyDamped_DoesNotOvershoot()
    {
        IReadOnlyList<double> path = new DampedSpring(1, 0.35).Sample(1.0 / 60);
        Assert.All(path, value => Assert.True(value <= 1.0005));
    }

    [Fact]
    public void Spring_KeepsItsSpeedWhenTheTargetFlips()
    {
        var spring = new DampedSpring(0.85, 0.35);
        double value = 0, velocity = 0;
        spring.Advance(ref value, ref velocity, 1, 0.05);
        Assert.True(velocity > 0);

        // Reversed midway it carries on a little before turning, rather than snapping back.
        double before = value;
        spring.Advance(ref value, ref velocity, 0, 1.0 / 240);
        Assert.True(value > before);
    }

    [Fact]
    public void Spring_RejectsNonsenseTokens()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DampedSpring(0, 0.35));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DampedSpring(0.85, 0));
    }
}
