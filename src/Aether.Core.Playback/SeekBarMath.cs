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

    /// <summary>
    /// The pieces of a bar <paramref name="width"/> wide once a <paramref name="gap"/>-wide cut is made at each chapter
    /// start (sorted 0..1 <paramref name="fractions"/>). A start closer than <paramref name="minSpacing"/> to the last cut
    /// or to either end gets no cut, so many short chapters never shred the bar into slivers.
    /// </summary>
    public static IReadOnlyList<(double Start, double End)> ChapterSegments(
        IReadOnlyList<double> fractions, double width, double gap, double minSpacing)
    {
        if (!(width > 0))
        {
            return [];
        }
        var segments = new List<(double, double)>();
        double start = 0;
        double lastCut = 0;
        foreach (double fraction in fractions)
        {
            double x = fraction * width;
            if (!double.IsFinite(x) || x - lastCut < minSpacing || width - x < minSpacing)
            {
                continue;
            }
            segments.Add((start, x - gap / 2));
            start = x + gap / 2;
            lastCut = x;
        }
        segments.Add((start, width));
        return segments;
    }
}
