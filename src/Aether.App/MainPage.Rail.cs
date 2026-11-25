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
using Windows.Foundation;

namespace Aether_App;

/// <summary>
/// The sidebar (P8.1): a small floating card on the left edge with Player, Clips, Queue, Stream and Settings.
/// Approaching the left edge gently fades in a frosted chevron tab (<see cref="RailHandle"/>); clicking it opens the
/// floating pill immediately with the render thread's blur behind it, and moving the pointer away auto-collapses it
/// cleanly. <see cref="HoverRail"/> owns the proximity, hold, and auto-collapse state. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    // Clear space kept between the card and the title bar or the transport pill.
    private const double RailInset = 8;
    // Distance from the left edge (DIPs) within which the frosted chevron handle fades in.
    private const double RailProximityWidth = 96;
    // Extra margin around the open rail where pointer motion keeps it open (covers the handle-click zone on the left).
    private const double RailCorridorPadding = 16;
    private static readonly Vector3 RailHandleHiddenOffset = new(-6, 0, 0);

    private readonly HoverRail _rail = new();
    // 0 off to the left of the window, 1 in place. Moved by the UI thread, once per frame and only while it slides,
    // because the render thread's blur has to be told where the card is on every step (see PostSidebarRegion).
    private readonly MotionTween _railSlide = new(0);
    private double _railTop = 40;
    private double _lastPlacedRailTop = double.NaN;
    private double _lastPlacedHandleTop = double.NaN;
    private bool _railFits = true;
    private bool _sidebarEnabled = true;
    private DispatcherQueueTimer? _railTimer;
    private bool _railShown;
    private bool _railHandleShown;
    private bool _railNearEdge;
    private bool _railInCorridor;
    private bool _pointerHeld;

    /// <summary>
    /// Off removes the sidebar altogether: no chevron handle, no shortcut, and an open one closes. For screens that
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
        RailHandle.Translation = RailHandleHiddenOffset;
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
        AutomationProperties.SetName(RailHandle, AppStrings.Get("Rail_Open"));
        ToolTipService.SetToolTip(RailHandle, AppStrings.Get("Rail_Open"));
        LabelRailItem(RailPlayer, AppStrings.Get("Rail_Player"));
        LabelRailItem(RailClips, AppStrings.Get("Rail_Clips"));
        LabelRailItem(RailQueue, AppStrings.Get("Rail_Queue"));
        LabelRailItem(RailStream, AppStrings.Get("Rail_Stream"));
        LabelRailItem(RailSettings, AppStrings.Get("Rail_Settings"));
        PressFeedback.Attach(RailHandle);
        foreach (UIElement item in new UIElement[] { RailPlayer, RailClips, RailQueue, RailStream, RailSettings })
        {
            PressFeedback.Attach(item);
        }
    }

    private static void LabelRailItem(Button item, string label)
    {
        ToolTipService.SetToolTip(item, label);
        AutomationProperties.SetName(item, label);
    }

    /// <summary>Settings and the chevron handle show their shortcuts; called again when shortcuts change.</summary>
    private void ApplyRailShortcutLabels()
    {
        ToolTipService.SetToolTip(RailSettings, Tip("Rail_Settings", ShortcutAction.Settings));
        ToolTipService.SetToolTip(RailHandle, Tip("Rail_Open", ShortcutAction.Sidebar));
    }

    private void ApplyRailLookMode(LookCornerRadii radii)
    {
        Rail.CornerRadius = new CornerRadius(radii.Transport);
        double handleRadius = _nativeLook ? 8 : 12;
        RailHandle.CornerRadius = new CornerRadius(0, handleRadius, handleRadius, 0);
        var itemRadius = new CornerRadius(radii.SidebarItem);
        foreach (Button item in new[] { RailPlayer, RailClips, RailQueue, RailStream, RailSettings })
        {
            item.CornerRadius = itemRadius;
        }
    }

    private void SetPointerHeld(bool held)
    {
        if (_pointerHeld == held)
        {
            return;
        }
        _pointerHeld = held;
        SyncRail();
    }

    private void UpdateRailPointer(Point position)
    {
        double top = TitleBar.Visibility == Visibility.Visible ? TitleBar.Height : 0;
        bool nearEdge = position.Y >= top && position.X <= RailProximityWidth;
        bool inCorridor = _rail.IsOpen
            && position.X <= Rail.Margin.Left + Rail.Width + RailCorridorPadding
            && position.Y >= _railTop - RailCorridorPadding
            && position.Y <= _railTop + Rail.Height + RailCorridorPadding;

        if (nearEdge == _railNearEdge && inCorridor == _railInCorridor)
        {
            return;
        }
        _railNearEdge = nearEdge;
        _railInCorridor = inCorridor;
        _rail.PointerNearEdge(nearEdge);
        _rail.PointerInCorridor(inCorridor);
        SyncRail();
    }

    private void ClearRailPointer()
    {
        if (!_railNearEdge && !_railInCorridor)
        {
            return;
        }
        _railNearEdge = false;
        _railInCorridor = false;
        _rail.PointerNearEdge(false);
        _rail.PointerInCorridor(false);
        SyncRail();
    }

    private void OnRailHandleEntered(object sender, PointerRoutedEventArgs e)
    {
        _rail.PointerOverHandle(true);
        SyncRail();
    }

    private void OnRailHandleExited(object sender, PointerRoutedEventArgs e)
    {
        _rail.PointerOverHandle(false);
        SyncRail();
    }

    private void OnRailHandleClick(object sender, RoutedEventArgs e)
    {
        if (RailHandle.FocusState == FocusState.Keyboard)
        {
            _rail.SetPinned(true);
        }
        else if (_rail.Open())
        {
            _railInCorridor = true;
            _rail.PointerInCorridor(true);
        }
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
        if (_rail.Decide() == HoverRailAction.Close)
        {
            _rail.Close();
            _railNearEdge = false;
            _railInCorridor = false;
        }
        SyncRail();
    }

    private void OnRailPlayerClick(object sender, RoutedEventArgs e) => CloseRail();

    private void OnRailClipsClick(object sender, RoutedEventArgs e)
    {
        CloseRail();
        EnsureClipLayer();
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
        _railNearEdge = false;
        _railInCorridor = false;
        SyncRail();
        if (closed)
        {
            Focus(FocusState.Programmatic);
        }
        return closed;
    }

    /// <summary>
    /// Brings the rail's look in line with <see cref="_rail"/>: availability, the proximity chevron handle, the rail
    /// itself, its hold on the chrome and the timer. <paramref name="applyChrome"/> is false when called from
    /// <see cref="ApplyChrome"/>.
    /// </summary>
    private void SyncRail(bool applyChrome = true)
    {
        if (_fader is null || _railTimer is null)
        {
            return;
        }
        bool available = _sidebarEnabled && !_tour.IsActive && _railFits && _chrome.IsVisible && !_settingsShown && _openUrl.Stage != OpenUrlStage.Editing && !IsPictureInPicture;
        _rail.SetAvailable(available);
        _rail.SetDragging(_dragging || _pointerHeld || (ClipDrawer?.IsDraggingOut == true));

        if (!available || _rail.IsOpen)
        {
            _railHandleShown = false;
            RailHandle.OpacityTransition = null;
            RailHandle.TranslationTransition = null;
            RailHandle.Opacity = 0;
            RailHandle.Translation = RailHandleHiddenOffset;
            RailHandle.IsHitTestVisible = false;
            RailHandle.Visibility = Visibility.Collapsed;
        }
        else
        {
            RailHandle.Visibility = Visibility.Visible;
            bool showHandle = _rail.IsHandleVisible;
            if (showHandle != _railHandleShown)
            {
                _railHandleShown = showHandle;
                TimeSpan duration = TimeSpan.FromMilliseconds(Motion.Token(showHandle ? "AetherMotionChromeShowMilliseconds" : "AetherMotionFastMilliseconds"));
                RailHandle.OpacityTransition = Motion.AnimationsEnabled ? new ScalarTransition { Duration = duration } : null;
                RailHandle.TranslationTransition = Motion.AnimationsEnabled ? new Vector3Transition { Duration = duration } : null;
                RailHandle.Opacity = showHandle ? 1 : 0;
                RailHandle.Translation = showHandle ? Vector3.Zero : RailHandleHiddenOffset;
                RailHandle.IsHitTestVisible = showHandle;
            }
        }

        if (_rail.IsOpen != _railShown)
        {
            _railShown = _rail.IsOpen;
            if (_railShown)
            {
                RailClips.IsEnabled = HasVideo && App.Clips.Items.Count > 0; // as the right-click menu's Clips
                _fader.Enter(Rail, null);
                _railSlide.AnimateTo(1, Motion.Token("AetherMotionQuickInMilliseconds"), Motion.EaseOut);
                VideoSurface.SetBackdropActive(BackdropSlot.Sidebar, _backdropAllowed);
                VideoSurface.FadeBackdrop(BackdropSlot.Sidebar, true, _fader.RevealDurationMilliseconds(true));
                PostSidebarRegion();
            }
            else
            {
                _fader.Exit(Rail, null, () =>
                {
                    if (!_railShown)
                    {
                        _postedSidebarBackdrop = default;
                        VideoSurface.SetBackdropActive(BackdropSlot.Sidebar, false);
                    }
                });
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
    /// Centres the card and its left-edge chevron handle between the title bar (or the top in full screen) and the
    /// bottom of the window, keeping 8 DIPs above the transport pill when the window is short.
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
        double railTop = Math.Max(y, top + RailInset);
        double handleTop = railTop + ((Rail.Height - RailHandle.Height) / 2);
        _railTop = railTop;

        if (railTop != _lastPlacedRailTop || handleTop != _lastPlacedHandleTop)
        {
            _lastPlacedRailTop = railTop;
            _lastPlacedHandleTop = handleTop;
            RailHandle.Margin = new Thickness(0, handleTop, 0, 0);
            Rail.Margin = new Thickness(Rail.Margin.Left, railTop, 0, 0);
        }
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
