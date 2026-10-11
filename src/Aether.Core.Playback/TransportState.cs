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

    /// <summary>The volume after Mute (M): silent if audible, otherwise back to <paramref name="lastAudible"/>.</summary>
    public static double ToggleMute(double volume, double lastAudible) =>
        volume > 0 ? 0 : lastAudible > 0 ? lastAudible : DefaultVolume;

    /// <summary>Clicking the volume button toggles mute, so its label names what the click does.</summary>
    public static bool IsMuted(double volume) => volume <= 0;
}
