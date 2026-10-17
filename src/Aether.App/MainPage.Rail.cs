using System.Numerics;
using Aether.Core.Interop.Rendering;
using Aether.Core.Playback;
using Aether.Core.Playback.Input;
using Aether_App.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Aether_App;

/// <summary>
/// The sidebar (P8.1): a small floating card on the left edge with Player, Clips, Stream and Settings.
/// <see cref="HoverRail"/> decides when it peeks, opens and closes; this file wires the pointer and the keyboard shortcut
/// to it, places the card, and slides and fades it in and out with the render thread's blur behind it. It is there over
/// the player and the home screen, never under Settings or a dialog, and hides with the chrome. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    // Clear space kept between the card and the title bar or the transport pill.
    private const double RailInset = 8;

    private readonly HoverRail _rail = new();
    // 0 off to the left of the window, 1 in place. Moved by the UI thread, once per frame and only while it slides,
    // because the render thread's blur has to be told where the card is on every step (see PostSidebarRegion).
    private readonly MotionTween _railSlide = new(0);
    private double _railTop = 40;
    private bool _railFits = true;
    private bool _sidebarEnabled = true;
    private DispatcherQueueTimer? _railTimer;
    private bool _railShown;
    private bool _pointerHeld;

    /// <summary>
    /// Off removes the sidebar altogether: no hot zone, no peek, no shortcut, and an open one closes. For screens that
    /// have no use for it, such as the first-run tour.
    /// </summary>
    public bool IsSidebarEnabled
    {
        get => _sidebarEnabled;
        set
        {
            if (_sidebarEnabled != value)
            {
                _sidebarEnabled = value;
                SyncRail();
            }
        }
    }

    private void InitializeRail()
    {
        _railSlide.Changed += (_, _) => ApplyRailSlide();
        ApplyRailSlide();
        // Starts hidden; the bar's blur starts shown, the sidebar's must not.
        VideoSurface.FadeBackdrop(BackdropSlot.Sidebar, false, 0);
        _railTimer = DispatcherQueue.CreateTimer();
        _railTimer.IsRepeating = false;
        _railTimer.Tick += OnRailTimerTick;
        // A held button is a drag of some kind (seek bar, selection, window): the rail must not open under it.
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => SetPointerHeld(true)), handledEventsToo: true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => SetPointerHeld(false)), handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => SetPointerHeld(false)), handledEventsToo: true);

        AutomationProperties.SetName(Rail, AppStrings.Get("Rail_Name"));
        LabelRailItem(RailPlayer, AppStrings.Get("Rail_Player"));
        LabelRailItem(RailClips, AppStrings.Get("Rail_Clips"));
        LabelRailItem(RailStream, AppStrings.Get("Rail_Stream"));
        LabelRailItem(RailSettings, AppStrings.Get("Rail_Settings"));
        foreach (UIElement item in new UIElement[] { RailPlayer, RailClips, RailStream, RailSettings })
        {
            PressFeedback.Attach(item);
        }
    }

    private static void LabelRailItem(Button item, string label)
    {
        ToolTipService.SetToolTip(item, label);
        AutomationProperties.SetName(item, label);
    }

    /// <summary>Settings shows its shortcut, as the gear's tooltip does; called again when shortcuts change.</summary>
    private void ApplyRailShortcutLabels() =>
        ToolTipService.SetToolTip(RailSettings, Tip("Rail_Settings", ShortcutAction.Settings));

    private void SetPointerHeld(bool held)
    {
        _pointerHeld = held;
        SyncRail();
    }

    private void OnRailHotZoneEntered(object sender, PointerRoutedEventArgs e)
    {
        _rail.PointerInHotZone(true);
        SyncRail();
    }

    private void OnRailHotZoneExited(object sender, PointerRoutedEventArgs e)
    {
        _rail.PointerInHotZone(false);
        SyncRail();
    }

    private void OnRailHotZoneTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        _rail.Open();
        SyncRail();
    }

    private void OnRailPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _rail.PointerOverRail(true);
        SyncRail();
    }

    private void OnRailPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _rail.PointerOverRail(false);
        SyncRail();
    }

    private void OnRailTimerTick(DispatcherQueueTimer sender, object args)
    {
        switch (_rail.Decide())
        {
            case HoverRailAction.Open:
                _rail.Open();
                break;
            case HoverRailAction.Close:
                _rail.Close();
                break;
        }
        SyncRail();
    }

    private void OnRailPlayerClick(object sender, RoutedEventArgs e) => CloseRail();

    private void OnRailClipsClick(object sender, RoutedEventArgs e)
    {
        CloseRail();
        ClipDrawer.Open();
    }

    private void OnRailSettingsClick(object sender, RoutedEventArgs e)
    {
        CloseRail();
        SetSettingsShown(true);
    }

    private void OnSidebarAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ArePlayerKeysPaused || !_sidebarEnabled)
        {
            return;
        }
        args.Handled = true;
        if (_rail.IsOpen)
        {
            CloseRail();
            return;
        }
        NotifyActivity(); // the sidebar lives with the chrome, so wake it first
        _rail.Toggle();
        SyncRail();
    }

    /// <summary>Esc and the items close it at once and give the keyboard back to the page. True if it was open.</summary>
    private bool CloseRail()
    {
        bool closed = _rail.Close();
        SyncRail();
        if (closed)
        {
            Focus(FocusState.Programmatic);
        }
        return closed;
    }

    /// <summary>
    /// Brings the rail's look in line with <see cref="_rail"/>: availability, the peek strip, the rail itself, its hold
    /// on the chrome and the timer. <paramref name="applyChrome"/> is false when called from <see cref="ApplyChrome"/>.
    /// </summary>
    private void SyncRail(bool applyChrome = true)
    {
        if (_fader is null || _railTimer is null)
        {
            return;
        }
        bool available = _sidebarEnabled && !_tour.IsActive && _railFits && _chrome.IsVisible && !_settingsShown && _openUrl.Stage != OpenUrlStage.Editing && !IsPictureInPicture;
        _rail.SetAvailable(available);
        _rail.SetDragging(_dragging || _pointerHeld || ClipDrawer.IsDraggingOut);
        RailHotZone.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

        double peek = _rail.IsPeeking ? 1 : 0;
        if (RailPeek.Opacity != peek)
        {
            RailPeek.OpacityTransition = Motion.AnimationsEnabled
                ? new ScalarTransition { Duration = TimeSpan.FromMilliseconds(Motion.Token("AetherMotionFastMilliseconds")) }
                : null;
            RailPeek.Opacity = peek;
        }

        if (_rail.IsOpen != _railShown)
        {
            _railShown = _rail.IsOpen;
            if (_railShown)
            {
                RailClips.IsEnabled = HasVideo && App.Clips.Items.Count > 0; // as the right-click menu's Clips
                _fader.Enter(Rail, null);
                _railSlide.AnimateTo(1, Motion.Token("AetherMotionQuickInMilliseconds"), Motion.EaseOut);
                VideoSurface.FadeBackdrop(BackdropSlot.Sidebar, true, _fader.RevealDurationMilliseconds(true));
            }
            else
            {
                _fader.Exit(Rail, null);
                _railSlide.AnimateTo(0, Motion.Token("AetherMotionQuickOutMilliseconds"), Motion.EaseIn);
                VideoSurface.FadeBackdrop(BackdropSlot.Sidebar, false, _fader.RevealDurationMilliseconds(false));
            }
        }

        bool hold = _rail.HoldsChrome;
        if (hold != ((_chrome.Holds & ChromeHolds.Rail) != 0))
        {
            _chrome.SetHold(ChromeHolds.Rail, hold);
            if (applyChrome)
            {
                ApplyChrome();
            }
        }

        if (_rail.DueIn is { } due)
        {
            _railTimer.Interval = due;
            _railTimer.Start();
        }
        else
        {
            _railTimer.Stop();
        }
    }

    /// <summary>
    /// Centres the card between the title bar (or the top in full screen) and the bottom of the window, and keeps it
    /// 8 above the pill when the window is short. If that leaves less than 8 under the title bar, it doesn't open.
    /// </summary>
    private void PlaceRail()
    {
        double top = TitleBar.Visibility == Visibility.Visible ? TitleBar.Height : 0;
        double bottom = ActualHeight;
        double y = top + ((bottom - top - Rail.Height) / 2);
        if (Osd.Visibility == Visibility.Visible)
        {
            y = Math.Min(y, bottom - Osd.Margin.Bottom - Osd.Height - RailInset - Rail.Height);
        }
        bool fits = y >= top + RailInset;
        _railTop = Math.Max(y, top + RailInset);

        RailHotZone.Margin = new Thickness(0, top, 0, 0);
        RailPeek.Margin = new Thickness(0, _railTop - top, 0, 0);
        Rail.Margin = new Thickness(Rail.Margin.Left, _railTop, 0, 0);
        if (fits != _railFits)
        {
            _railFits = fits;
            SyncRail();
        }
    }

    /// <summary>Where the slide has put the card: from its own width plus margin off the left edge, to nothing.</summary>
    private double RailSlideOffset => -(1 - _railSlide.Value) * (Rail.Width + Rail.Margin.Left);

    private void ApplyRailSlide()
    {
        Rail.Translation = new Vector3((float)RailSlideOffset, 0, 0);
        PostSidebarRegion();
    }
}
