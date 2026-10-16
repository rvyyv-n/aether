namespace Aether.Core.Playback;

/// <summary>Where the window goes when picture in picture ends.</summary>
public enum PictureInPictureReturn
{
    /// <summary>The overlapped window, at the size, place and maximised state Windows kept for it.</summary>
    Windowed,
    FullScreen,
}

/// <summary>
/// Picture in picture for the single shell window (8.1, 8.2, ADR-0022): whether it is on, where leaving returns
/// to, and a guard so a request that arrives while the presenter is switching (P pressed again from inside a
/// window-changed handler) is refused rather than nested. The window owns the presenter calls: it asks
/// <see cref="TryBeginEnter"/> or <see cref="TryBeginLeave"/>, switches, then calls <see cref="EndSwitch"/>.
/// Not thread-safe (UI thread only).
/// </summary>
public sealed class PictureInPicture
{
    public bool IsOn { get; private set; }

    /// <summary>A presenter switch is under way; every request is refused until <see cref="EndSwitch"/>.</summary>
    public bool IsSwitching { get; private set; }

    /// <summary>Remembered on enter; meaningful while <see cref="IsOn"/>.</summary>
    public PictureInPictureReturn ReturnTo { get; private set; }

    /// <summary>
    /// Starts entering when a video is open and the window is not already in (or switching to) picture in picture.
    /// True means the caller must switch the presenter and then call <see cref="EndSwitch"/>.
    /// </summary>
    public bool TryBeginEnter(bool hasVideo, bool fullScreen)
    {
        if (IsOn || IsSwitching || !hasVideo)
        {
            return false;
        }
        IsOn = true;
        IsSwitching = true;
        ReturnTo = fullScreen ? PictureInPictureReturn.FullScreen : PictureInPictureReturn.Windowed;
        return true;
    }

    /// <summary>
    /// Starts leaving. <paramref name="toFullScreen"/> overrides the remembered target (F in picture in picture goes
    /// straight to full screen). True means the caller must switch to <paramref name="target"/> and then call
    /// <see cref="EndSwitch"/>.
    /// </summary>
    public bool TryBeginLeave(bool toFullScreen, out PictureInPictureReturn target)
    {
        target = toFullScreen ? PictureInPictureReturn.FullScreen : ReturnTo;
        if (!IsOn || IsSwitching)
        {
            return false;
        }
        IsOn = false;
        IsSwitching = true;
        return true;
    }

    public void EndSwitch() => IsSwitching = false;

    /// <summary>The close button and Alt+F4 return to the full window instead of closing the app.</summary>
    public bool CancelsClose => IsOn;

    /// <summary>
    /// The video has gone, so the overlay has nothing to show. <see cref="PlaybackStatus.Loading"/> is the gap
    /// between two files of a folder playlist and keeps picture in picture on.
    /// </summary>
    public static bool EndsOn(PlaybackStatus status) => status == PlaybackStatus.Idle;
}
