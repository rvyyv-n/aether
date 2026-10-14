using System.Globalization;

namespace Aether.Core.Playback.Tracks;

/// <summary>The audio and subtitle delay steppers of the tracks flyout: ±0.1 s a press, shown as "+0.3 s".</summary>
public static class TrackDelay
{
    public const double Step = 0.1;

    /// <summary>mpv takes any value; a stepper that runs off to minutes is a stuck key, not a wish.</summary>
    public const double Limit = 60;

    /// <summary>One step up or down, on the 0.1 grid so repeated presses do not drift (0.1 + 0.2 is not 0.3).</summary>
    public static double Stepped(double current, int direction) =>
        Math.Clamp(Math.Round(current + (direction < 0 ? -Step : Step), 1), -Limit, Limit);

    public static string Format(double seconds)
    {
        double rounded = Math.Round(seconds, 1);
        string sign = rounded > 0 ? "+" : rounded < 0 ? "−" : "";
        return string.Create(CultureInfo.CurrentCulture, $"{sign}{Math.Abs(rounded):0.0} s");
    }

    /// <summary>The value mpv's <c>set</c> takes, with a dot whatever the language of the PC.</summary>
    public static string ForMpv(double seconds) => seconds.ToString("0.0", CultureInfo.InvariantCulture);
}
