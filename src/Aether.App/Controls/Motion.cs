using System.Globalization;
using System.Numerics;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace Aether_App.Controls;

/// <summary>
/// Reduced motion: with Windows animation effects off, fades are instant (handoff, locked decisions).
/// Read on each fade, so a change in Settings applies without a restart. Also the motion profile
/// (7.15 preview: Quick or Springy) and the curves both profiles share. UI thread.
/// </summary>
internal static class Motion
{
    private static readonly UISettings Settings = new();

    public static bool AnimationsEnabled => Settings.AnimationsEnabled;

    /// <summary>Settings › Advanced › Motion style (preview); read as each animation starts, so a change applies at once.</summary>
    public static bool IsSpringy => App.Settings.Current.MotionStyle == "springy";

    public static double Token(string key) => (double)Application.Current.Resources[key];

    /// <summary>A cubic-bezier token, "x1,y1,x2,y2".</summary>
    public static Vector4 Curve(string key)
    {
        string[] parts = ((string)Application.Current.Resources[key]).Split(',');
        float At(int i) => float.Parse(parts[i], CultureInfo.InvariantCulture);
        return new Vector4(At(0), At(1), At(2), At(3));
    }

    public static Vector4 EaseOut => Curve("AetherMotionEaseOut");

    public static Vector4 EaseIn => Curve("AetherMotionEaseIn");

    /// <summary>The eased fraction at time fraction <paramref name="t"/> (0..1) of <paramref name="curve"/>.</summary>
    public static double Ease(Vector4 curve, double t)
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
            if (Bezier(s, curve.X, curve.Z) < t)
            {
                low = s;
            }
            else
            {
                high = s;
            }
        }
        return Bezier(s, curve.Y, curve.W);
    }

    private static double Bezier(double s, double p1, double p2)
    {
        double u = 1 - s;
        return (3 * u * u * s * p1) + (3 * u * s * s * p2) + (s * s * s);
    }
}

/// <summary>
/// The Springy profile's damped spring (tokens AetherMotionSpring*). The transport card's height drives layout, so it
/// is stepped on the UI thread; compositor slides get the same spring sampled into key frames, so both feel alike.
/// </summary>
internal readonly struct Spring
{
    private const double Step = 1.0 / 240; // fixed sub-steps keep the integration stable at any frame rate
    private const double RestDistance = 0.0005;
    private const double RestSpeed = 0.005;

    private readonly double _stiffness;
    private readonly double _damping;

    private Spring(double dampingRatio, double periodSeconds)
    {
        double omega = 2 * Math.PI / periodSeconds;
        _stiffness = omega * omega;
        _damping = 2 * dampingRatio * omega;
    }

    public static Spring FromTokens() => new(
        Motion.Token("AetherMotionSpringDampingRatio"),
        Motion.Token("AetherMotionSpringPeriodMilliseconds") / 1000);

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

    /// <summary>The spring's path from 0 to 1, sampled every <paramref name="interval"/> seconds until it rests.</summary>
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
