using System.Globalization;
using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback;

/// <summary>What the Play/Pause button shows and does next.</summary>
public enum PlayButtonAction
{
    Play,
    Pause,

    /// <summary>The file ended; pressing restarts it (<see cref="PlayToggleCommands"/>).</summary>
    Replay,
}

/// <summary>The controls pill's view of the player state (screen 3, frame 5.8).</summary>
public static class TransportState
{
    /// <summary>Volume restored by unmuting when nothing audible was remembered.</summary>
    public const double DefaultVolume = 100;

    /// <summary>Back and Forward 5 s (← and →).</summary>
    public const double SkipSeconds = 5;

    /// <summary>Below this card width, frame step, loop and always on top move into More controls (5.7).</summary>
    public const double CompactPillWidth = 520;

    public static PlayButtonAction PlayButton(PlaybackStatus status) => status switch
    {
        PlaybackStatus.Ended => PlayButtonAction.Replay,
        PlaybackStatus.Playing => PlayButtonAction.Pause,
        _ => PlayButtonAction.Play,
    };

    /// <summary>
    /// The position the clock and seek bar show. An ended file reads "03:15 / 03:15": mpv's last
    /// <c>time-pos</c> is the last frame's start, which can fall a second short of the duration.
    /// </summary>
    public static double DisplayPosition(in PlayerState state) =>
        state.Status == PlaybackStatus.Ended && state.Duration > 0 ? state.Duration : state.Position;

    /// <summary>
    /// Seek to <paramref name="seconds"/> from the seek bar: keyframe seeks while the bar is dragged
    /// keep up with the pointer, and the release lands on the exact frame.
    /// </summary>
    public static MpvCommand Seek(double seconds, bool dragging) => new(
        "seek",
        Math.Max(0, double.IsFinite(seconds) ? seconds : 0).ToString("0.###", CultureInfo.InvariantCulture),
        dragging ? "absolute+keyframes" : "absolute+exact");

    /// <summary>Skip by <paramref name="seconds"/> (negative goes back), landing on the exact frame.</summary>
    public static MpvCommand SeekBy(double seconds) => new(
        "seek",
        (double.IsFinite(seconds) ? seconds : 0).ToString("0.###", CultureInfo.InvariantCulture),
        "relative+exact");

    public static MpvCommand SetSpeed(double speed) => new(
        "set",
        "speed",
        PlaybackSpeed.Clamp(speed).ToString("0.###", CultureInfo.InvariantCulture));

    /// <summary>The card is min(520, window − 32) wide; half a pixel of slack absorbs layout rounding.</summary>
    public static bool IsCompact(double pillWidth) => pillWidth < CompactPillWidth - 0.5;

    /// <summary>The volume after Mute (M): silent if audible, otherwise back to <paramref name="lastAudible"/>.</summary>
    public static double ToggleMute(double volume, double lastAudible) =>
        volume > 0 ? 0 : lastAudible > 0 ? lastAudible : DefaultVolume;

    /// <summary>Clicking the volume button toggles mute, so its label names what the click does.</summary>
    public static bool IsMuted(double volume) => volume <= 0;
}
