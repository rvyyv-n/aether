using System.Globalization;

namespace Aether.Core.Playback;

/// <summary>Formats playback positions for the controls.</summary>
public static class Timecode
{
    /// <summary>
    /// <c>hh:mm:ss.fff</c>, truncated to the millisecond (a frame shows the time it started at).
    /// Hours grow past two digits; negative, NaN and infinite values show as zero.
    /// </summary>
    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            return "00:00:00.000";
        }
        // The epsilon keeps values like 83.456 (stored as 83.45599…) from truncating a millisecond early.
        long totalMilliseconds = (long)Math.Floor(seconds * 1000 + 1e-6);
        long hours = totalMilliseconds / 3_600_000;
        int minutes = (int)(totalMilliseconds / 60_000 % 60);
        int secs = (int)(totalMilliseconds / 1000 % 60);
        int millis = (int)(totalMilliseconds % 1000);
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{secs:00}.{millis:000}");
    }

    /// <summary>
    /// The controls' clock: <c>mm:ss</c>, or <c>h:mm:ss</c> when the clip (<paramref name="durationSeconds"/>)
    /// or the position is an hour or more, so position and duration always share one shape.
    /// Truncated to the second; negative, NaN and infinite values show as zero.
    /// </summary>
    public static string FormatClock(double seconds, double durationSeconds)
    {
        long total = WholeSeconds(seconds);
        long hours = total / 3600;
        int minutes = (int)(total / 60 % 60);
        int secs = (int)(total % 60);
        return hours > 0 || WholeSeconds(durationSeconds) >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{secs:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{secs:00}");
    }

    private static long WholeSeconds(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 ? (long)Math.Floor(seconds + 1e-6) : 0;
}
