namespace Aether.Core.Playback;

/// <summary>Maps between seek bar pixels and seconds, and places the time bubble (screen 5).</summary>
public static class SeekBarMath
{
    /// <summary>The time under <paramref name="x"/> on a bar <paramref name="width"/> wide; 0 when nothing is loaded.</summary>
    public static double TimeAt(double x, double width, double duration) =>
        width > 0 && duration > 0 && double.IsFinite(x) && double.IsFinite(duration)
            ? Math.Clamp(x / width, 0, 1) * duration
            : 0;

    /// <summary>Where <paramref name="seconds"/> sits on a bar <paramref name="width"/> wide.</summary>
    public static double OffsetOf(double seconds, double width, double duration) =>
        width > 0 && duration > 0 && double.IsFinite(seconds) && double.IsFinite(duration)
            ? Math.Clamp(seconds / duration, 0, 1) * width
            : 0;

    /// <summary>
    /// The left edge of something <paramref name="size"/> wide centred on <paramref name="centre"/>,
    /// pushed back inside [<paramref name="min"/>, <paramref name="max"/>] near the ends.
    /// </summary>
    public static double CentreWithin(double centre, double size, double min, double max) =>
        max - min <= size ? min : Math.Clamp(centre - size / 2, min, max - size);
}
