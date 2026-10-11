namespace Aether.Core.Playback;

/// <summary>Reasons the player chrome (title bar and controls) stays visible however long the mouse is idle.</summary>
[Flags]
public enum ChromeHolds
{
    None = 0,

    /// <summary>Nothing is playing: idle, loading, paused or ended.</summary>
    NotPlaying = 1,

    /// <summary>The pointer is over the controls.</summary>
    PointerOverControls = 2,

    /// <summary>A drag, a menu or a dialog is in progress.</summary>
    Interaction = 4,

    /// <summary>A flyout from the controls is open (volume). Separate from <see cref="Interaction"/>, which a drag ending clears.</summary>
    Flyout = 8,

    /// <summary>A dialog is open (Open URL). The caption glyphs never hide under one.</summary>
    Dialog = 16,
}

/// <summary>
/// Decides when the player chrome auto-hides: after <see cref="IdleDelay"/> without activity while
/// nothing holds it. A plain state machine; the UI drives it with a single-shot timer armed for
/// <see cref="DueIn"/> and calls <see cref="Tick"/> when it fires. Not thread-safe (UI thread only).
/// </summary>
public sealed class ChromeVisibility
{
    public static readonly TimeSpan DefaultIdleDelay = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time;
    private long _lastActivity;

    public ChromeVisibility(TimeProvider? time = null, TimeSpan? idleDelay = null)
    {
        _time = time ?? TimeProvider.System;
        IdleDelay = idleDelay ?? DefaultIdleDelay;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(IdleDelay, TimeSpan.Zero, nameof(idleDelay));
        _lastActivity = _time.GetTimestamp();
    }

    public TimeSpan IdleDelay { get; }

    public bool IsVisible { get; private set; } = true;

    public ChromeHolds Holds { get; private set; }

    /// <summary>Time left until the chrome hides; null while hidden or held.</summary>
    public TimeSpan? DueIn
    {
        get
        {
            if (!IsVisible || Holds != ChromeHolds.None)
            {
                return null;
            }
            TimeSpan left = IdleDelay - _time.GetElapsedTime(_lastActivity);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>The mouse moved or a key was pressed: shows the chrome and restarts the countdown. True if it was hidden.</summary>
    public bool NotifyActivity()
    {
        _lastActivity = _time.GetTimestamp();
        return Show();
    }

    /// <summary>
    /// Sets or clears a hold. Setting one shows the chrome; clearing one restarts the countdown, so the
    /// chrome stays for a full <see cref="IdleDelay"/> after, say, the pointer leaves the controls.
    /// Returns true if the chrome became visible.
    /// </summary>
    public bool SetHold(ChromeHolds hold, bool active)
    {
        ChromeHolds previous = Holds;
        Holds = active ? previous | hold : previous & ~hold;
        if (active)
        {
            return Show();
        }
        if (previous != Holds)
        {
            _lastActivity = _time.GetTimestamp();
        }
        return false;
    }

    /// <summary>Hides the chrome if the countdown has run out and nothing holds it. True if it was just hidden.</summary>
    public bool Tick()
    {
        if (DueIn is not { } left || left > TimeSpan.Zero)
        {
            return false;
        }
        IsVisible = false;
        return true;
    }

    private bool Show()
    {
        if (IsVisible)
        {
            return false;
        }
        IsVisible = true;
        return true;
    }
}
