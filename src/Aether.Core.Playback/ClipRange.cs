namespace Aether.Core.Playback;

/// <summary>
/// The In (I) and Out (O) points on the seek bar. Setting a point on the wrong side of the other one
/// starts a new range from it, so the band never runs backwards.
/// </summary>
public readonly record struct ClipRange(double? In, double? Out)
{
    public bool IsEmpty => In is null && Out is null;

    /// <summary>Both points are set: the band, the clip chip and Export clip need both.</summary>
    public bool IsComplete => In is not null && Out is not null;

    public double Length => IsComplete ? Out!.Value - In!.Value : 0;

    public ClipRange SetIn(double seconds)
    {
        double at = Clean(seconds);
        return new ClipRange(at, Out is { } end && end > at ? end : null);
    }

    public ClipRange SetOut(double seconds)
    {
        double at = Clean(seconds);
        return new ClipRange(In is { } start && start < at ? start : null, at);
    }

    /// <summary>"00:30 – 01:10" for the clip chip, in the clock shape of a file <paramref name="duration"/> long.</summary>
    public string Label(double duration) =>
        IsComplete
            ? $"{Timecode.FormatClock(In!.Value, duration)} – {Timecode.FormatClock(Out!.Value, duration)}"
            : string.Empty;

    private static double Clean(double seconds) => double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
}
