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
}
