namespace Aether.Core.Playback;

/// <summary>Where playback is, derived from mpv's file and pause state.</summary>
public enum PlaybackStatus
{
    /// <summary>No file is loaded.</summary>
    Idle,

    /// <summary>A file was requested and is not ready yet (between start-file and file-loaded).</summary>
    Loading,
    Playing,
    Paused,

    /// <summary>The loaded file reached its end (<c>eof-reached</c>) and is still open (<c>keep-open</c>).</summary>
    Ended,
}

/// <summary>
/// A snapshot of the player properties the UI shows. A plain value: the event thread builds one,
/// the UI thread applies it to <see cref="PlayerViewModel"/>. <c>default</c> is all zeros; use
/// <see cref="Initial"/> for the state before any property has been observed.
/// </summary>
/// <param name="Position"><c>time-pos</c> in seconds; 0 when unavailable.</param>
/// <param name="Duration"><c>duration</c> in seconds; 0 when unavailable.</param>
/// <param name="Speed"><c>speed</c>; 1 when unavailable.</param>
/// <param name="Volume"><c>volume</c>, 100 is unity gain (may exceed 100); 100 when unavailable.</param>
/// <param name="IsPaused"><c>pause</c>.</param>
/// <param name="IsAtEnd"><c>eof-reached</c>.</param>
/// <param name="FramesPerSecond"><c>container-fps</c>; 0 when unknown.</param>
/// <param name="Status">Derived by <see cref="PlayerEventDispatcher"/>; never observed directly.</param>
/// <param name="VideoWidth"><c>dwidth</c>: display width in pixels after aspect correction; 0 without video.</param>
/// <param name="VideoHeight"><c>dheight</c>: display height in pixels; 0 without video.</param>
public readonly record struct PlayerState(
    double Position,
    double Duration,
    double Speed,
    double Volume,
    bool IsPaused,
    bool IsAtEnd,
    double FramesPerSecond,
    PlaybackStatus Status,
    int VideoWidth = 0,
    int VideoHeight = 0)
{
    /// <summary>The state before any file or property: idle, unity speed and volume.</summary>
    public static PlayerState Initial { get; } = new(0, 0, 1, 100, false, false, 0, PlaybackStatus.Idle);
}
