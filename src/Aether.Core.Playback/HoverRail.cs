namespace Aether.Core.Playback;

/// <summary>What the hover rail should do when its timer fires.</summary>
public enum HoverRailAction
{
    None,
    Open,
    Close,
}

/// <summary>
/// Proximity-handle and auto-collapse state for the left-edge floating sidebar rail. Approaching the left edge reveals
/// a frosted chevron handle (<see cref="IsHandleVisible"/>); clicking the handle opens the rail immediately via
/// <see cref="Open"/> (never auto-opening on hover). Once open, it closes after <see cref="CloseDelay"/> when the
/// pointer leaves the rail and its corridor, or immediately from <see cref="Close"/> (Esc, an item used, or chrome
/// hiding). Nothing reveals or opens during a drag or while unavailable (Settings, a dialog, or hidden chrome). The
/// page owns the timer: it re-arms it to <see cref="DueIn"/> after every change and asks <see cref="Decide"/> when it
/// fires. Not thread-safe (UI thread only).
/// </summary>
public sealed class HoverRail
{
    public static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(300);

    private bool _nearEdge;
    private bool _overHandle;
    private bool _overRail;
    private bool _inCorridor;
    private bool _dragging;
    private bool _available = true;
    private bool _pinned;

    public bool IsOpen { get; private set; }

    /// <summary>The frosted chevron handle shows: the pointer is near the left edge (or over the handle) and the rail could open.</summary>
    public bool IsHandleVisible => !IsOpen && (_nearEdge || _overHandle) && CanOpen;

    /// <summary>Backwards-compatible alias for <see cref="IsHandleVisible"/>.</summary>
    public bool IsPeeking => IsHandleVisible;

    /// <summary>The pointer is on the open rail or hovering the handle, so auto-hiding chrome stays up.</summary>
    public bool HoldsChrome => (IsOpen && (_overRail || _inCorridor)) || (IsHandleVisible && _overHandle);

    private bool CanOpen => _available && !_dragging;

    public void PointerNearEdge(bool near) => _nearEdge = near;

    public void PointerOverHandle(bool over) => _overHandle = over;

    public void PointerOverRail(bool over) => _overRail = over;

    /// <summary>True while the pointer sits in the open rail's immediate left-edge corridor (including where the handle was clicked).</summary>
    public void PointerInCorridor(bool inside) => _inCorridor = inside;

    /// <summary>A file drag, a seek drag, a clip dragged out or a held button. Stops the handle showing and the rail opening; an open rail stays.</summary>
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

    /// <summary>
    /// Holds the rail open whatever the pointer does (keyboard shortcut or tour), and opens it if it can.
    /// Unpinning leaves it open for <see cref="Decide"/> to close as usual. True if pinning opened it.
    /// </summary>
    public bool SetPinned(bool pinned)
    {
        _pinned = pinned;
        if (!pinned || IsOpen || !_available)
        {
            return false;
        }
        IsOpen = true;
        return true;
    }

    /// <summary>
    /// The keyboard shortcut: closes an open rail, or opens it held (as <see cref="SetPinned"/>) so it stays until Esc,
    /// an item or the shortcut again, since there is no pointer to keep it open. True if it opened.
    /// </summary>
    public bool Toggle()
    {
        if (IsOpen)
        {
            Close();
            return false;
        }
        return SetPinned(true);
    }

    /// <summary>Opens the rail immediately if it can. True if it opened.</summary>
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
        _pinned = false;
        _nearEdge = false;
        _overHandle = false;
        _overRail = false;
        _inCorridor = false;
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
            return HoverRailAction.None;
        }
        return _pinned || _overRail || _inCorridor ? HoverRailAction.None : HoverRailAction.Close;
    }
}
