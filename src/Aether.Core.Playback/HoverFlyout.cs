namespace Aether.Core.Playback;

/// <summary>What a hover flyout should do when its timer fires.</summary>
public enum HoverFlyoutAction
{
    None,
    Open,
    Close,
}

/// <summary>
/// Hover intent for a flyout that opens over its anchor button (the volume flyout, 5.8). It opens
/// after the pointer rests on the anchor and closes once the pointer has left both the anchor and
/// the flyout for a moment, so passing over the button or crossing the gap between them does not
/// flicker. A flyout opened from the keyboard or right-click is pinned and only light-dismisses.
/// Once closed, it waits for the pointer to come back to the anchor, so Escape or a click that
/// dismisses it does not reopen it under a resting pointer.
/// The page owns the timer: it re-arms it to <see cref="DueIn"/> after every change and asks
/// <see cref="Decide"/> when it fires. Not thread-safe (UI thread only).
/// </summary>
public sealed class HoverFlyout
{
    public static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(400);

    private bool _overAnchor;
    private bool _overFlyout;
    private bool _pressed;
    private bool _pinned;

    public bool IsOpen { get; private set; }

    public void PointerOverAnchor(bool over) => _overAnchor = over;

    public void PointerOverFlyout(bool over) => _overFlyout = over;

    /// <summary>A drag on the flyout's slider keeps it open even if the pointer strays off it.</summary>
    public void Pressed(bool pressed) => _pressed = pressed;

    public void Opened(bool pinned)
    {
        IsOpen = true;
        _pinned = pinned;
    }

    public void Closed()
    {
        IsOpen = false;
        _pinned = false;
        _overAnchor = false;
        _overFlyout = false;
        _pressed = false;
    }

    /// <summary>How long to wait before <see cref="Decide"/>, or null when nothing is pending.</summary>
    public TimeSpan? DueIn => Decide() switch
    {
        HoverFlyoutAction.Open => OpenDelay,
        HoverFlyoutAction.Close => CloseDelay,
        _ => null,
    };

    public HoverFlyoutAction Decide()
    {
        if (!IsOpen)
        {
            return _overAnchor ? HoverFlyoutAction.Open : HoverFlyoutAction.None;
        }
        return _pinned || _overAnchor || _overFlyout || _pressed ? HoverFlyoutAction.None : HoverFlyoutAction.Close;
    }
}
