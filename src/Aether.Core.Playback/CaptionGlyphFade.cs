namespace Aether.Core.Playback;

/// <summary>
/// Opacity of the caption-button glyphs while they fade with the chrome. The system draws those
/// glyphs, so their colour can't run on the compositor like the rest of the chrome; the window steps
/// it from a UI timer instead, on the same ease-out curve as the chrome fade. A fade reversed midway
/// starts from wherever the glyphs are. Not thread-safe (UI thread only).
/// </summary>
public sealed class CaptionGlyphFade
{
    // Control points of the chrome's cubic-bezier ease-out (ChromeFader in the app).
    private const double X1 = 0.1, Y1 = 0.9, X2 = 0.2, Y2 = 1.0;

    private readonly TimeProvider _time;
    private long _start;
    private double _from = 1;
    private double _to = 1;
    private TimeSpan _duration;

    public CaptionGlyphFade(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>0 (hidden) to 1 (shown).</summary>
    public double Opacity { get; private set; } = 1;

    /// <summary>Starts a fade towards shown or hidden; a zero duration jumps there.</summary>
    public void Start(bool visible, TimeSpan duration)
    {
        _from = Opacity;
        _to = visible ? 1 : 0;
        _duration = duration;
        _start = _time.GetTimestamp();
        if (duration <= TimeSpan.Zero)
        {
            Opacity = _to;
        }
    }

    /// <summary>Moves <see cref="Opacity"/> to now. Returns true while the fade has further steps.</summary>
    public bool Step()
    {
        if (_duration <= TimeSpan.Zero)
        {
            Opacity = _to;
            return false;
        }
        double t = Math.Clamp(_time.GetElapsedTime(_start) / _duration, 0, 1);
        Opacity = _from + ((_to - _from) * EaseOut(t));
        return t < 1;
    }

    /// <summary>The ease-out curve at time fraction <paramref name="t"/> (0..1).</summary>
    public static double EaseOut(double t)
    {
        if (t <= 0)
        {
            return 0;
        }
        if (t >= 1)
        {
            return 1;
        }
        // x(s) rises monotonically on 0..1 for these control points, so bisection finds s for x = t.
        double low = 0, high = 1, s = t;
        for (int i = 0; i < 32; i++)
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
