namespace Aether.Core.Playback.Streams;

/// <summary>When Aether next looks for a newer yt-dlp: once a day, and only while automatic updates are on.</summary>
public static class YtDlpUpdateSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>True when a check is due: updates are on and the last check was a day or more ago (or never).</summary>
    public static bool IsDue(bool autoUpdate, DateTimeOffset? lastCheck, DateTimeOffset now) =>
        autoUpdate && (lastCheck is null || now - lastCheck.Value >= Interval || lastCheck.Value > now);

    /// <summary>How long to wait before the next check, for a timer that keeps running while the app stays open.</summary>
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
