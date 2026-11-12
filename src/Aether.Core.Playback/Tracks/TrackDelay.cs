using System.Globalization;

namespace Aether.Core.Playback.Tracks;

/// <summary>
/// The audio and subtitle delay steppers of the tracks flyout, ±0.1 s a press and shown as "+0.3 s", and the subtitle
/// nudge keys (15.4), ±50 ms a press.
/// </summary>
public static class TrackDelay
{
    public const double Step = 0.1;

    public const double Nudge = 0.05;

    /// <summary>mpv takes any value; a stepper that runs off to minutes is a stuck key, not a wish.</summary>
    public const double Limit = 60;

    /// <summary>One step up or down, on the 0.01 grid so repeated presses do not drift (0.1 + 0.2 is not 0.3).</summary>
    public static double Stepped(double current, int direction) => Moved(current, direction < 0 ? -Step : Step);

    /// <summary>One subtitle nudge earlier (<paramref name="direction"/> below zero) or later.</summary>
    public static double Nudged(double current, int direction) => Moved(current, direction < 0 ? -Nudge : Nudge);

    public static string Format(double seconds)
    {
        double rounded = Math.Round(seconds, 2);
        string sign = rounded > 0 ? "+" : rounded < 0 ? "−" : "";
        return string.Create(CultureInfo.CurrentCulture, $"{sign}{Math.Abs(rounded):0.0#} s");
    }

    /// <summary>The nudge toast's "+150 ms".</summary>
    public static string Milliseconds(double seconds)
    {
        long ms = (long)Math.Round(seconds * 1000);
        string sign = ms > 0 ? "+" : ms < 0 ? "−" : "";
        return string.Create(CultureInfo.CurrentCulture, $"{sign}{Math.Abs(ms)} ms");
    }

    /// <summary>The value mpv's <c>set</c> takes, with a dot whatever the language of the PC.</summary>
    public static string ForMpv(double seconds) => seconds.ToString("0.0##", CultureInfo.InvariantCulture);

    private static double Moved(double current, double by) => Math.Clamp(Math.Round(current + by, 2), -Limit, Limit);
}
