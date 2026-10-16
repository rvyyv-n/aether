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
/// The hover rail (7.21, screen 6): Player, Clips, Stream and Settings in a slim column on the left edge.
/// <see cref="HoverRail"/> decides when it peeks, opens and closes; this file wires the pointer to it, places the rail
/// between the title bar and the pill, and brings it in and out. It is there over the player and the home screen,
/// never under Settings or a dialog, and hides with the chrome. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private const double RailInset = 8;

    private readonly HoverRail _rail = new();
    private DispatcherQueueTimer? _railTimer;
    private bool _railShown;
    private bool _pointerHeld;

    private void InitializeRail()
    {
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

    /// <summary>Esc and the items close it at once. True if it was open.</summary>
    private bool CloseRail()
    {
        bool closed = _rail.Close();
        SyncRail();
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
        bool available = _chrome.IsVisible && !_settingsShown && _openUrl.Stage != OpenUrlStage.Editing && !IsPictureInPicture;
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
                _fader.Enter(Rail, ChromeFader.FromLeft);
            }
            else
            {
                _fader.Exit(Rail, ChromeFader.FromLeft);
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

    /// <summary>From 8 under the title bar (or the top in full screen) to 8 above the pill, or 24 from the bottom without one.</summary>
    private void PlaceRail()
    {
        double top = TitleBar.Visibility == Visibility.Visible ? TitleBar.Height : 0;
        double bottom = Osd.Visibility == Visibility.Visible
            ? Osd.Margin.Bottom + Osd.Height + RailInset
            : Osd.Margin.Bottom;
        RailHotZone.Margin = new Thickness(0, top, 0, 0);
        RailPeek.Margin = new Thickness(0, RailInset, 0, bottom);
        Rail.Margin = new Thickness(RailInset, top + RailInset, 0, bottom);
    }
}
