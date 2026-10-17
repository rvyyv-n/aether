using Aether.Core.Playback;
using Aether.Core.Playback.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Aether_App;

/// <summary>
/// The Pass 5 transport pill (screen 5): seek bar, skip and frame-step buttons, the speed menu, In/Out
/// points and the compact layout below a 560 px card. Kept apart from the shell so each pass's wiring
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
        foreach (Button button in new[]
        {
            OpenButton, SpeedButton, BackButton, PlayPauseButton, ForwardButton,
            VolumeButton, MoreButton, FullScreenButton,
        })
        {
            button.PointerExited += OnTipOwnerPointerExited;
            button.PointerCanceled += OnTipOwnerPointerExited;
        }
    }

    /// <summary>
    /// Moving to the next icon would leave the last one's tooltip fading out underneath the new one. Taking the
    /// tooltip off and putting it back closes it at once, so only the icon under the pointer shows one.
    /// </summary>
    private static void OnTipOwnerPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var owner = (DependencyObject)sender;
        object? tip = ToolTipService.GetToolTip(owner);
        if (tip is not null)
        {
            ToolTipService.SetToolTip(owner, null);
            ToolTipService.SetToolTip(owner, tip);
        }
    }

    /// <summary>Plays the next video in the folder when a local file ends and Play next in folder is on.</summary>
    private async void OnFileEnded(object? sender, EventArgs e)
    {
        if (!App.Settings.Current.PlayNextInFolder
            || _currentPath is not { } finished
            || !Path.IsPathFullyQualified(finished)
            || Path.GetDirectoryName(finished) is not { Length: > 0 } folder)
        {
            return;
        }
        string? next = await Task.Run(() =>
        {
            try
            {
                return FolderPlaylist.NextAfter(finished, Directory.EnumerateFiles(folder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null; // the folder went away or is unreadable: stay on the last frame
            }
        });
        // The user may have opened something else, or closed the page, while the folder was read.
        if (next is not null && _isLoaded && _session is not null && _currentPath == finished)
        {
            Open(next);
        }
    }

    private void ApplyTransportStrings()
    {
        AutomationProperties.SetName(SeekBar, AppStrings.Get("Transport_Seek"));
        Label(MoreButton, "Transport_MoreControls");
        ToolTipService.SetToolTip(SpeedButton, AppStrings.Get("Transport_Speed"));
        MoreAudioItem.Text = AppStrings.Get("Menu_Audio");
        MoreSubtitlesItem.Text = AppStrings.Get("Menu_Subtitles");
        MoreSettingsItem.Text = AppStrings.Get("Menu_Settings");
        MoreLoopItem.Text = AppStrings.Get("Menu_Loop");
        MoreAlwaysOnTopItem.Text = AppStrings.Get("Menu_AlwaysOnTop");
    }

    private static void Label(UIElement button, string key, ShortcutAction? action = null)
    {
        string text = AppStrings.Get(key);
        // The tooltip carries the shortcut; the accessible name is the action alone.
        int shortcut = text.LastIndexOf(" (", StringComparison.Ordinal);
        AutomationProperties.SetName(button, shortcut > 0 ? text[..shortcut] : text);
        ToolTipService.SetToolTip(button, action is { } a ? App.Shortcuts.Tip(text, a) : text);
    }

    /// <summary>Keyframe seeks follow the drag; the release seeks to the exact frame.</summary>
    private void OnSeekRequested(double seconds, bool dragging)
    {
        if (_session is not { } session || !HasVideo)
        {
            return;
        }
        _chapterSkipper.OnUserSeek(seconds);
        _chrome.SetHold(ChromeHolds.Interaction, dragging);
        ApplyChrome();
        PositionText.Text = Timecode.FormatClock(seconds, session.ViewModel.Duration);
        TrySend(() => session.Seek(seconds, dragging));
    }

    private void SkipBy(double seconds)
    {
        if (_session is { } session && HasVideo)
        {
            _chapterSkipper.OnUserSeek(Math.Max(0, session.ViewModel.Position + seconds));
            TrySend(() => session.SeekBy(seconds));
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => SkipBy(-TransportState.SkipSeconds);

    private void OnForwardClick(object sender, RoutedEventArgs e) => SkipBy(TransportState.SkipSeconds);

    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SkipBy(-TransportState.SkipSeconds));

    private void OnForwardAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SkipBy(TransportState.SkipSeconds));

    private void OnBackFineAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SkipBy(-TransportState.FineSkipSeconds));

    private void OnForwardFineAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SkipBy(TransportState.FineSkipSeconds));

    private void OnBackLongAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SkipBy(-TransportState.LongSkipSeconds));

    private void OnForwardLongAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SkipBy(TransportState.LongSkipSeconds));

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

    private double _speed = PlaybackSpeed.Normal;
    private bool _speedMenuOpen;

    private void ShowSpeed(double speed)
    {
        _speed = speed;
        SpeedButton.Content = PlaybackSpeed.Format(speed);
        ApplySpeedLook();
        AutomationProperties.SetName(SpeedButton, $"{AppStrings.Get("Transport_Speed")} {PlaybackSpeed.Format(speed)}");
        foreach (MenuFlyoutItemBase item in SpeedMenu.Items.Concat(CtxSpeed.Items))
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
        _speedMenuOpen = true;
        ApplySpeedLook();
        HoldForMenu(true);
    }

    private void OnSpeedMenuClosed(object sender, object e)
    {
        _speedMenuOpen = false;
        ApplySpeedLook();
        HoldForMenu(false);
    }

    // Filled while its menu is open; filled and outlined, like Loop when on, while the speed is not 1x.
    private void ApplySpeedLook()
    {
        bool changed = !PlaybackSpeed.IsCurrent(PlaybackSpeed.Normal, _speed);
        if (changed || _speedMenuOpen)
        {
            SpeedButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AetherFillEmphasisRestBrush"];
        }
        else
        {
            SpeedButton.ClearValue(BackgroundProperty);
        }
        if (changed)
        {
            SpeedButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AetherStrokeControlBrush"];
        }
        else
        {
            SpeedButton.ClearValue(BorderBrushProperty);
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(MoreButton);

    private void OnMoreMenuOpened(object sender, object e)
    {
        MoreButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AetherFillEmphasisRestBrush"];
        // The tracks flyout has nothing to list without a file, as in the right-click menu.
        MoreAudioItem.IsEnabled = MoreSubtitlesItem.IsEnabled = HasVideo;
        HoldForMenu(true);
    }

    private void OnMoreMenuClosed(object sender, object e)
    {
        MoreButton.ClearValue(BackgroundProperty);
        HoldForMenu(false);
        ShowTracksIfRequested();
    }

    private void HoldForMenu(bool open)
    {
        _chrome.SetHold(ChromeHolds.Flyout, open);
        ApplyChrome();
    }

    /// <summary>Below a 560 px card (5.7), the tracks, loop, always on top and settings live in More controls; the slim pill hides them anyway.</summary>
    private void OnOsdSizeChanged(object sender, SizeChangedEventArgs e) => RefreshTourLayout(); // the tour can light the growing bar

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
        MoreAlwaysOnTopItem.IsChecked = onTop;
        CtxAlwaysOnTop.IsChecked = onTop;
    }

    // In (I) and Out (O): the band on the seek bar and the export chip above the pill.

    private void OnSetInAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SetClipRange(_clipRange.SetIn(CurrentPosition())));

    private void OnSetOutAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SetClipRange(_clipRange.SetOut(CurrentPosition())));

    private void OnExportClipAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, ClipDrawer.RequestExport);

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
