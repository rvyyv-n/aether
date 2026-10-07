namespace Aether.Core.Playback;

/// <summary>The repeat interval of a held frame-step key as a function of hold time.</summary>
public static class FrameRepeatCurve
{
    /// <summary>
    /// The interval to wait after a repeat, given how long the key has been held since the first repeat.
    /// Linear from <see cref="FrameStepperOptions.StartInterval"/> (at 0) to
    /// <see cref="FrameStepperOptions.MinInterval"/> (at <see cref="FrameStepperOptions.AccelerationTime"/>),
    /// clamped on both sides; never increases with hold time.
    /// </summary>
    public static TimeSpan IntervalAt(TimeSpan heldSinceFirstRepeat, FrameStepperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (heldSinceFirstRepeat <= TimeSpan.Zero)
        {
            return options.StartInterval;
        }
        if (heldSinceFirstRepeat >= options.AccelerationTime)
        {
            return options.MinInterval;
        }
        double fraction = (double)heldSinceFirstRepeat.Ticks / options.AccelerationTime.Ticks;
        long ticks = options.StartInterval.Ticks - (long)((options.StartInterval.Ticks - options.MinInterval.Ticks) * fraction);
        return TimeSpan.FromTicks(Math.Clamp(ticks, options.MinInterval.Ticks, options.StartInterval.Ticks));
    }
}
