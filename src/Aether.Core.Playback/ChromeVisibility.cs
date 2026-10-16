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

    /// <summary>The clip drawer is open, or a clip is being dragged out of it.</summary>
    Drawer = 32,

    /// <summary>The settings view is open.</summary>
    Settings = 64,

    /// <summary>A control in the transport card has keyboard focus (Tab).</summary>
    KeyboardFocus = 128,

    /// <summary>The pointer is on the hover rail (7.21). Keeps the chrome up but, unlike the others, leaves the pill slim.</summary>
    Rail = 256,

    /// <summary>The first-run tour is showing (7.26).</summary>
    Tour = 512,
}

/// <summary>
/// Decides when the player chrome auto-hides: after <see cref="IdleDelay"/> without activity while
/// nothing holds it. Any hold also grows the transport into its full card (<see cref="IsExpanded"/>); once the
/// last hold lets go the card shrinks back to the slim pill after <see cref="IdleDelay"/>, and the chrome hides
/// after a further idle <see cref="IdleDelay"/> (7.11). A plain state machine; the UI drives it with a
/// single-shot timer armed for <see cref="DueIn"/> and calls <see cref="Tick"/> when it fires. Not thread-safe
/// (UI thread only).
/// </summary>
public sealed class ChromeVisibility
{
    public static readonly TimeSpan DefaultIdleDelay = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time;
    private long _lastActivity;
    private long _released; // when a hold last let go: the card shrinks IdleDelay after, whatever the pointer does elsewhere

    public ChromeVisibility(TimeProvider? time = null, TimeSpan? idleDelay = null)
    {
        _time = time ?? TimeProvider.System;
        IdleDelay = idleDelay ?? DefaultIdleDelay;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(IdleDelay, TimeSpan.Zero, nameof(idleDelay));
        _lastActivity = _time.GetTimestamp();
        _released = _lastActivity;
    }

    public TimeSpan IdleDelay { get; }

    public bool IsVisible { get; private set; } = true;

    public ChromeHolds Holds { get; private set; }

    /// <summary>
    /// The transport shows its full card rather than the slim pill. Moving the pointer elsewhere shows the
    /// chrome but leaves the card slim; only a hold grows it.
    /// </summary>
    public bool IsExpanded { get; private set; }

    /// <summary>Time left until the card shrinks, or the chrome hides once it has; null while hidden or held.</summary>
    public TimeSpan? DueIn
    {
        get
        {
            if (!IsVisible || Holds != ChromeHolds.None)
            {
                return null;
            }
            TimeSpan left = IdleDelay - _time.GetElapsedTime(IsExpanded ? _released : _lastActivity);
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
    /// Sets or clears a hold. Setting one shows the chrome and grows the card (the rail's excepted); clearing one restarts the
    /// countdown, so the card stays for a full <see cref="IdleDelay"/> after, say, the pointer leaves the controls.
    /// Returns true if the chrome became visible.
    /// </summary>
    public bool SetHold(ChromeHolds hold, bool active)
    {
        ChromeHolds previous = Holds;
        Holds = active ? previous | hold : previous & ~hold;
        if (active)
        {
            if (hold != ChromeHolds.Rail)
            {
                IsExpanded = true;
            }
            return Show();
        }
        if (previous != Holds)
        {
            _lastActivity = _time.GetTimestamp();
            _released = _lastActivity;
        }
        return false;
    }

    /// <summary>
    /// Once the countdown has run out with nothing holding the chrome: shrinks the card and restarts the
    /// countdown, or hides the chrome if the card is already slim. True if the chrome was just hidden.
    /// </summary>
    public bool Tick()
    {
        if (DueIn is not { } left || left > TimeSpan.Zero)
        {
            return false;
        }
        if (IsExpanded)
        {
            IsExpanded = false;
            _lastActivity = _time.GetTimestamp();
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
