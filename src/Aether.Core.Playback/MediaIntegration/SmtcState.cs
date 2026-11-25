namespace Aether.Core.Playback.MediaIntegration;

/// <summary>Playback status published to Windows System Media Transport Controls (20.1).</summary>
public enum SmtcPlaybackState
{
    Closed,
    Stopped,
    Paused,
    Playing,
    Changing,
}

/// <summary>Transport command received from Windows global media keys or Quick Settings Now Playing.</summary>
public enum SmtcCommand
{
    Play,
    Pause,
    Stop,
    Previous,
    Next,
}

/// <summary>Title and source subtitle published to Windows Quick Settings and lock-screen Now Playing.</summary>
public readonly record struct SmtcMetadata(string Title, string Subtitle, bool HasMedia)
{
    public static SmtcMetadata Empty { get; } = new(string.Empty, string.Empty, false);

    public static SmtcMetadata From(string? streamTitle, string? path, string? webLink, string appFallback = "Aether")
    {
        string title = !string.IsNullOrWhiteSpace(streamTitle)
            ? streamTitle.Trim()
            : !string.IsNullOrWhiteSpace(path)
                ? Path.GetFileNameWithoutExtension(path)
                : string.Empty;

        if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(path))
        {
            title = Path.GetFileName(path);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Empty;
        }

        string subtitle = ResolveSubtitle(path, webLink, appFallback);
        return new SmtcMetadata(title, subtitle, true);
    }

    private static string ResolveSubtitle(string? path, string? webLink, string appFallback)
    {
        if (!string.IsNullOrWhiteSpace(webLink)
            && Uri.TryCreate(webLink, UriKind.Absolute, out Uri? uri)
            && !string.IsNullOrWhiteSpace(uri.Host))
        {
            string host = uri.Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                string folder = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    return folder;
                }
            }
        }

        return appFallback;
    }
}

/// <summary>Button enablement and playback status for Windows SMTC.</summary>
public readonly record struct SmtcButtonState(
    bool IsEnabled,
    bool IsPlayEnabled,
    bool IsPauseEnabled,
    bool IsStopEnabled,
    bool IsPreviousEnabled,
    bool IsNextEnabled,
    SmtcPlaybackState PlaybackStatus)
{
    public static SmtcButtonState From(Aether.Core.Playback.PlaybackStatus status, bool hasPrevious, bool hasNext) => status switch
    {
        Aether.Core.Playback.PlaybackStatus.Idle => new(false, false, false, false, false, false, SmtcPlaybackState.Closed),
        Aether.Core.Playback.PlaybackStatus.Loading => new(true, false, false, true, hasPrevious, hasNext, SmtcPlaybackState.Changing),
        Aether.Core.Playback.PlaybackStatus.Playing => new(true, true, true, true, hasPrevious, hasNext, SmtcPlaybackState.Playing),
        Aether.Core.Playback.PlaybackStatus.Paused => new(true, true, true, true, hasPrevious, hasNext, SmtcPlaybackState.Paused),
        Aether.Core.Playback.PlaybackStatus.Ended => new(true, true, false, false, hasPrevious, hasNext, SmtcPlaybackState.Stopped),
        _ => new(false, false, false, false, false, false, SmtcPlaybackState.Closed),
    };
}

/// <summary>Timeline range and playhead position published to Windows SMTC.</summary>
public readonly record struct SmtcTimeline(
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan Position,
    TimeSpan MinSeekTime,
    TimeSpan MaxSeekTime,
    bool IsSeekable)
{
    /// <summary>Position jump threshold (ms) that forces an immediate timeline update (e.g. seek or chapter jump).</summary>
    public const double SeekJumpThresholdSeconds = 1.5;

    /// <summary>Default periodic push interval (ms) while playing steadily.</summary>
    public const long DefaultPushIntervalMs = 5000;

    public static SmtcTimeline Empty { get; } = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, false);

    public static SmtcTimeline From(double positionSeconds, double durationSeconds, bool isLive)
    {
        if (isLive || !double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            return Empty;
        }

        double clampedPosition = double.IsFinite(positionSeconds)
            ? Math.Clamp(positionSeconds, 0, durationSeconds)
            : 0;

        TimeSpan end = TimeSpan.FromSeconds(durationSeconds);
        TimeSpan pos = TimeSpan.FromSeconds(clampedPosition);
        return new SmtcTimeline(TimeSpan.Zero, end, pos, TimeSpan.Zero, end, true);
    }

    /// <summary>
    /// Decides whether <paramref name="current"/> should be sent to Windows SMTC, avoiding per-frame COM calls
    /// while still reflecting seeks, file changes and periodic progress promptly.
    /// </summary>
    public static bool ShouldPush(
        in SmtcTimeline previous,
        in SmtcTimeline current,
        long elapsedMs,
        long intervalMs = DefaultPushIntervalMs)
    {
        if (previous.IsSeekable != current.IsSeekable || previous.EndTime != current.EndTime)
        {
            return true;
        }
        if (!current.IsSeekable)
        {
            return false;
        }
        if (elapsedMs < 0)
        {
            return true;
        }
        double expectedSeconds = previous.Position.TotalSeconds + elapsedMs / 1000.0;
        if (Math.Abs(current.Position.TotalSeconds - expectedSeconds) >= SeekJumpThresholdSeconds)
        {
            return true;
        }
        return elapsedMs >= intervalMs && current.Position != previous.Position;
    }
}
