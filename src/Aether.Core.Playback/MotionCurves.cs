namespace Aether.Core.Playback;

/// <summary>A CSS-style cubic-bezier easing curve through (0,0), (x1,y1), (x2,y2) and (1,1).</summary>
public readonly record struct CubicBezier(double X1, double Y1, double X2, double Y2)
{
    /// <summary>The eased fraction at time fraction <paramref name="t"/> (0..1).</summary>
    public double Ease(double t)
    {
        if (t <= 0)
        {
            return 0;
        }
        if (t >= 1)
        {
            return 1;
        }
        // x(s) rises monotonically on 0..1 for control points inside the unit square, so bisection finds s for x = t.
        double low = 0, high = 1, s = t;
        for (int i = 0; i < 24; i++)
        {
            s = (low + high) / 2;
            if (Bezier(s, X1, X2) < t)
            {
                low = s;
            }
            else
            {
                high = s;
            }
        }
        return Bezier(s, Y1, Y2);
    }

    private static double Bezier(double s, double p1, double p2)
    {
        double u = 1 - s;
        return (3 * u * u * s * p1) + (3 * u * s * s * p2) + (s * s * s);
    }
}

/// <summary>
/// The Springy motion profile's damped spring. The transport card's height goes through layout, so the app steps it
/// on the UI thread; compositor slides get the same path sampled into key frames, so both feel alike.
/// </summary>
public readonly struct DampedSpring
{
    private const double Step = 1.0 / 240; // fixed sub-steps keep the integration stable at any frame rate
    private const double RestDistance = 0.0005;
    private const double RestSpeed = 0.005;

    private readonly double _stiffness;
    private readonly double _damping;

    /// <param name="dampingRatio">1 settles without overshoot; below 1 overshoots a little.</param>
    /// <param name="periodSeconds">The undamped spring's period (SwiftUI's "response").</param>
    public DampedSpring(double dampingRatio, double periodSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dampingRatio, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(periodSeconds, 0);
        double omega = 2 * Math.PI / periodSeconds;
        _stiffness = omega * omega;
        _damping = 2 * dampingRatio * omega;
    }

    /// <summary>Moves <paramref name="value"/> and <paramref name="velocity"/> on by <paramref name="seconds"/>. True once at rest on the target.</summary>
    public bool Advance(ref double value, ref double velocity, double target, double seconds)
    {
        for (double left = Math.Min(seconds, 0.1); left > 0; left -= Step)
        {
            double dt = Math.Min(Step, left);
            velocity += ((-_stiffness * (value - target)) - (_damping * velocity)) * dt;
            value += velocity * dt;
        }
        if (Math.Abs(value - target) < RestDistance && Math.Abs(velocity) < RestSpeed)
        {
            value = target;
            velocity = 0;
            return true;
        }
        return false;
    }

    /// <summary>The spring's path from 0 to 1, sampled every <paramref name="interval"/> seconds until it rests (at most 200 points).</summary>
    public IReadOnlyList<double> Sample(double interval)
    {
        var points = new List<double> { 0 };
        double value = 0, velocity = 0;
        bool resting = false;
        while (!resting && points.Count < 200)
        {
            resting = Advance(ref value, ref velocity, 1, interval);
            points.Add(value);
        }
        return points;
    }
}
