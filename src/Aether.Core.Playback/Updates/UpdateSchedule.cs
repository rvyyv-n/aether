namespace Aether.Core.Playback.Updates;

/// <summary>When Aether looks for a new version: a few seconds after launch, then at most once a day, and only while automatic checks are on.</summary>
public static class UpdateSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>Kept clear of the first seconds so a check never competes with the first file opening.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait before looking at the schedule again after a failed check.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    public static bool IsDue(bool autoCheck, DateTimeOffset? lastCheck, DateTimeOffset now) =>
        autoCheck && (lastCheck is null || now - lastCheck.Value >= Interval || lastCheck.Value > now);

    public static TimeSpan DueIn(DateTimeOffset? lastCheck, DateTimeOffset now)
    {
        if (lastCheck is null || lastCheck.Value > now)
        {
            return TimeSpan.Zero;
        }
        TimeSpan wait = lastCheck.Value + Interval - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }
}
