namespace Aether.Core.Playback;

/// <summary>What the hover rail should do when its timer fires.</summary>
public enum HoverRailAction
{
    None,
    Open,
    Close,
}

/// <summary>
/// Hover intent for the left-edge rail (7.21, screen 6). Resting in the 4 px hot zone shows the peek strip and opens
/// the rail after <see cref="OpenDelay"/>; a click there opens it at once. It closes once the pointer has left both
/// the hot zone and the rail for <see cref="CloseDelay"/>, or straight away from <see cref="Close"/> (Esc, an item
/// used, the chrome hiding). Nothing opens or peeks during a drag or while the rail is unavailable (Settings, a
/// dialog, hidden chrome). Once closed it waits for the pointer to come back into the hot zone, so Esc does not
/// reopen it under a resting pointer. The page owns the timer: it re-arms it to <see cref="DueIn"/> after every
/// change and asks <see cref="Decide"/> when it fires. Not thread-safe (UI thread only).
/// </summary>
public sealed class HoverRail
{
    public static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(300);

    private bool _inHotZone;
    private bool _overRail;
    private bool _dragging;
    private bool _available = true;

    public bool IsOpen { get; private set; }

    /// <summary>The 4 px strip shows: the pointer is in the hot zone and the rail could open.</summary>
    public bool IsPeeking => !IsOpen && _inHotZone && CanOpen;

    /// <summary>The pointer is on the open rail, so the chrome should not hide under it.</summary>
    public bool HoldsChrome => IsOpen && _overRail;

    private bool CanOpen => _available && !_dragging;

    public void PointerInHotZone(bool inside) => _inHotZone = inside;

    public void PointerOverRail(bool over) => _overRail = over;

    /// <summary>A file drag, a seek drag, a clip dragged out or a held button. Stops the rail opening; an open rail stays.</summary>
    public void SetDragging(bool dragging) => _dragging = dragging;

    /// <summary>False under Settings, a dialog or hidden chrome. Returns true if that closed the rail.</summary>
    public bool SetAvailable(bool available)
    {
        _available = available;
        if (available || !IsOpen)
        {
            return false;
        }
        Close();
        return true;
    }

    /// <summary>Opens the rail if it can. True if it opened.</summary>
    public bool Open()
    {
        if (IsOpen || !CanOpen)
        {
            return false;
        }
        IsOpen = true;
        return true;
    }

    /// <summary>Closes the rail now. True if it was open.</summary>
    public bool Close()
    {
        bool wasOpen = IsOpen;
        IsOpen = false;
        _inHotZone = false;
        _overRail = false;
        return wasOpen;
    }

    /// <summary>How long to wait before <see cref="Decide"/>, or null when nothing is pending.</summary>
    public TimeSpan? DueIn => Decide() switch
    {
        HoverRailAction.Open => OpenDelay,
        HoverRailAction.Close => CloseDelay,
        _ => null,
    };

    public HoverRailAction Decide()
    {
        if (!IsOpen)
        {
            return IsPeeking ? HoverRailAction.Open : HoverRailAction.None;
        }
        return _inHotZone || _overRail ? HoverRailAction.None : HoverRailAction.Close;
    }
}
