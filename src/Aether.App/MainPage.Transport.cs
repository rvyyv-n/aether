using Aether.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Aether_App;

/// <summary>
/// The Pass 5 transport pill (screen 5): seek bar, skip and frame-step buttons, the speed menu, In/Out
/// points and the compact layout below a 520 px pill. Kept apart from the shell so each pass's wiring
/// stays in one place. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private ClipRange _clipRange;
    private string? _clipRangePath; // the file the In/Out points belong to; a new file clears them

    private void InitializeTransport()
    {
        SeekBar.SeekRequested += OnSeekRequested;
        ClipDrawer.ClearRequested += (_, _) => SetClipRange(default);
        ClipDrawer.HoldChanged += OnClipDrawerHoldChanged;
        foreach (double option in PlaybackSpeed.Options)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = PlaybackSpeed.Format(option),
                Tag = option,
                Style = (Style)Application.Current.Resources["AetherToggleMenuItemStyle"],
            };
            item.Click += OnSpeedItemClick;
            SpeedMenu.Items.Add(item);
        }
        ShowSpeed(PlaybackSpeed.Normal);
    }

    private void ApplyTransportStrings()
    {
        AutomationProperties.SetName(SeekBar, AppStrings.Get("Transport_Seek"));
        Label(BackButton, "Transport_Back5s");
        Label(ForwardButton, "Transport_Forward5s");
        Label(BackFrameButton, "Transport_Back1Frame");
        Label(ForwardFrameButton, "Transport_Forward1Frame");
        Label(LoopButton, "Transport_Loop");
        Label(AlwaysOnTopButton, "Transport_AlwaysOnTop");
        Label(MoreButton, "Transport_MoreControls");
        ToolTipService.SetToolTip(SpeedButton, AppStrings.Get("Transport_Speed"));
        MoreBackFrameItem.Text = AppStrings.Get("Menu_Back1Frame");
        MoreForwardFrameItem.Text = AppStrings.Get("Menu_Forward1Frame");
        MoreLoopItem.Text = AppStrings.Get("Menu_Loop");
        MoreAlwaysOnTopItem.Text = AppStrings.Get("Menu_AlwaysOnTop");

        static void Label(UIElement button, string key)
        {
            string text = AppStrings.Get(key);
            ToolTipService.SetToolTip(button, text);
            // The tooltip carries the shortcut; the accessible name is the action alone.
            int shortcut = text.LastIndexOf(" (", StringComparison.Ordinal);
            AutomationProperties.SetName(button, shortcut > 0 ? text[..shortcut] : text);
        }
    }

    /// <summary>Keyframe seeks follow the drag; the release seeks to the exact frame.</summary>
    private void OnSeekRequested(double seconds, bool dragging)
    {
        if (_session is not { } session || !HasVideo)
        {
            return;
        }
        _chrome.SetHold(ChromeHolds.Interaction, dragging);
        ApplyChrome();
        PositionText.Text = Timecode.FormatClock(seconds, session.ViewModel.Duration);
        TrySend(() => session.Seek(seconds, dragging));
    }

    private void SkipBy(double seconds)
    {
        if (_session is { } session && HasVideo)
        {
            TrySend(() => session.SeekBy(seconds));
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => SkipBy(-TransportState.SkipSeconds);

    private void OnForwardClick(object sender, RoutedEventArgs e) => SkipBy(TransportState.SkipSeconds);

    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SkipBy(-TransportState.SkipSeconds));

    private void OnForwardAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SkipBy(TransportState.SkipSeconds));

    private void OnBackFrameClick(object sender, RoutedEventArgs e) => StepFrame(FrameStepDirection.Backward);

    private void OnForwardFrameClick(object sender, RoutedEventArgs e) => StepFrame(FrameStepDirection.Forward);

    private void StepFrame(FrameStepDirection direction)
    {
        if (_session is { } session && HasVideo)
        {
            session.StepFrame(direction);
        }
    }

    /// <summary>Playback keys do nothing without a video, and leave the Open URL field and settings their keys.</summary>
    private void OnTransportKey(KeyboardAcceleratorInvokedEventArgs args, Action action)
    {
        if (!HasVideo || ArePlayerKeysPaused)
        {
            return;
        }
        args.Handled = true;
        action();
    }

    // Speed (5.4): the pill names the current speed; its menu checks it.

    private void ShowSpeed(double speed)
    {
        SpeedButton.Content = PlaybackSpeed.Format(speed);
        AutomationProperties.SetName(SpeedButton, $"{AppStrings.Get("Transport_Speed")} {PlaybackSpeed.Format(speed)}");
        foreach (MenuFlyoutItemBase item in SpeedMenu.Items)
        {
            if (item is ToggleMenuFlyoutItem { Tag: double option } toggle)
            {
                toggle.IsChecked = PlaybackSpeed.IsCurrent(option, speed);
            }
        }
    }

    private void OnSpeedClick(object sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(SpeedButton);

    private void OnSpeedItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem { Tag: double option } && _session is { } session)
        {
            ShowSpeed(option); // until mpv echoes it back; a click on the current speed must stay checked
            TrySend(() => session.SetSpeed(option));
        }
    }

    private void OnSpeedMenuOpened(object sender, object e)
    {
        SpeedButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AetherFillEmphasisRestBrush"];
        HoldForMenu(true);
    }

    private void OnSpeedMenuClosed(object sender, object e)
    {
        SpeedButton.ClearValue(BackgroundProperty);
        HoldForMenu(false);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(MoreButton);

    private void HoldForMenu(bool open)
    {
        _chrome.SetHold(ChromeHolds.Flyout, open);
        ApplyChrome();
    }

    /// <summary>Below a 520 px pill (5.7), frame step, loop and always on top move into More controls.</summary>
    private void OnOsdSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = TransportState.IsCompact(e.NewSize.Width);
        Visibility full = compact ? Visibility.Collapsed : Visibility.Visible;
        BackFrameButton.Visibility = full;
        ForwardFrameButton.Visibility = full;
        LoopButton.Visibility = full;
        AlwaysOnTopButton.Visibility = full;
        MoreLoopItem.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        MoreAlwaysOnTopItem.Visibility = MoreLoopItem.Visibility;
        MoreButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    // Loop and Always on top: the buttons and their More items follow the state, whoever changed it.

    private void OnLoopClick(object sender, RoutedEventArgs e)
    {
        if (_session is { } session)
        {
            session.ViewModel.ToggleLoop();
        }
        SyncLoop(); // a toggle with no session must not stay checked
    }

    private void SyncLoop()
    {
        bool looping = _session?.ViewModel.IsLooping ?? false;
        LoopButton.IsChecked = looping;
        MoreLoopItem.IsChecked = looping;
    }

    private void OnAlwaysOnTopClick(object sender, RoutedEventArgs e)
    {
        _host?.ToggleAlwaysOnTop();
        SyncAlwaysOnTop();
    }

    private void OnAlwaysOnTopChanged(object? sender, EventArgs e) => SyncAlwaysOnTop();

    private void SyncAlwaysOnTop()
    {
        bool onTop = _host?.IsAlwaysOnTop ?? false;
        AlwaysOnTopButton.IsChecked = onTop;
        MoreAlwaysOnTopItem.IsChecked = onTop;
    }

    // In (I) and Out (O): the band on the seek bar and the export chip above the pill.

    private void OnSetInAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SetClipRange(_clipRange.SetIn(CurrentPosition())));

    private void OnSetOutAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SetClipRange(_clipRange.SetOut(CurrentPosition())));

    private double CurrentPosition() =>
        _session is { } session ? TransportState.DisplayPosition(session.ViewModel.State) : 0;

    private void SetClipRange(ClipRange range)
    {
        _clipRange = range;
        _clipRangePath = _currentPath;
        SeekBar.Range = range;
        ClipDrawer.Update(range, _currentPath, _session?.ViewModel.Duration ?? 0);
    }

    private void OnClipDrawerHoldChanged(object? sender, EventArgs e)
    {
        _chrome.SetHold(ChromeHolds.Drawer, ClipDrawer.IsOpen || ClipDrawer.IsDraggingOut);
        ApplyChrome();
    }

    /// <summary>In and Out belong to one file: opening another clears them.</summary>
    private void SyncClipRange()
    {
        if (!_clipRange.IsEmpty && _clipRangePath != _currentPath)
        {
            SetClipRange(default);
        }
    }
}
