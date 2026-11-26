using Aether.Core.Playback;
using Aether.Core.Playback.Input;
using Aether.Core.Playback.Thumbnails;
using Aether_App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

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
    private bool _clipLayerWired;

    private void InitializeTransport()
    {
        SeekBar.SeekRequested += OnSeekRequested;
        SeekBar.PreviewRequested += OnSeekPreviewRequested;
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
        Osd.ActualThemeChanged += (_, _) =>
        {
            // The bar follows the app's theme, so custom fills/brushes refresh on theme flips.
            ApplySpeedLook();
            ApplyQualityLook();
            SyncLoop();
            SyncAlwaysOnTop();
            RefreshLiveEdgeAppearance();
        };
        foreach (Button button in new[]
        {
            OpenButton, SpeedButton, QualityButton, BackButton, StepBackButton, PlayPauseButton,
            StepForwardButton, ForwardButton, TracksButton, LoopButton, AlwaysOnTopButton,
            PrevChapterButton, ChaptersButton, NextChapterButton, VolumeButton, MoreButton, FullScreenButton,
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

    /// <summary>Plays the next video in the queue when a local file ends and Play next in folder is on.</summary>
    private async void OnFileEnded(object? sender, EventArgs e)
    {
        if ((App.Settings.Current.PlayNextInFolder || _playlist.Repeat != RepeatMode.None)
            && await StepQueue(queue => queue.Next(ended: true)) is { } next)
        {
            Open(next, fromQueue: true);
        }
    }

    private void ApplyTransportStrings()
    {
        AutomationProperties.SetName(SeekBar, AppStrings.Get("Transport_Seek"));
        Label(MoreButton, "Transport_MoreControls");
        ToolTipService.SetToolTip(SpeedButton, AppStrings.Get("Transport_Speed"));
        ToolTipService.SetToolTip(QualityButton, AppStrings.Get("Transport_Quality"));
        MoreAudioItem.Text = AppStrings.Get("Menu_Audio");
        MoreSubtitlesItem.Text = AppStrings.Get("Menu_Subtitles");
        MoreChaptersItem.Text = AppStrings.Get("Menu_Chapters");
        MoreSettingsItem.Text = AppStrings.Get("Menu_Settings");
        MoreLoopItem.Text = AppStrings.Get("Menu_Loop");
        MoreAlwaysOnTopItem.Text = AppStrings.Get("Menu_AlwaysOnTop");
        Label(TracksButton, "Transport_Tracks");
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
        double target = FromSeekBar(seconds);
        _chapterSkipper.OnUserSeek(target);
        _segmentSkipper.OnUserSeek(target);
        _chrome.SetHold(ChromeHolds.Interaction, dragging);
        ApplyChrome();
        SetPositionText(SeekBarClock(target, session.ViewModel.Duration));
        TrySend(() => session.Seek(target, dragging));
    }

    private void ResetThumbnails(string path, OpenedLink? link)
    {
        SeekBar.ClearPreview();
        if (_thumbnails is null)
        {
            SeekBar.PreviewsEnabled = false;
            return;
        }

        if (_isLive)
        {
            _thumbnails.ClearSource();
            SeekBar.PreviewsEnabled = false;
        }
        else if (link is not null)
        {
            _thumbnails.SetStreamSource(link.Storyboards);
            SeekBar.PreviewsEnabled = StoryboardIndex.SelectBest(link.Storyboards) is not null;
        }
        else
        {
            _thumbnails.SetLocalSource(path);
            SeekBar.PreviewsEnabled = !string.IsNullOrEmpty(path);
        }
    }

    private void OnSeekPreviewRequested(double seconds, int bucket, double duration)
    {
        if (_thumbnails is null || !HasVideo || _isLive)
        {
            return;
        }

        if (_thumbnails.RequestPreview(seconds, bucket, duration) is { } frame)
        {
            SeekBar.SetPreviewFrame(bucket, frame.Width, frame.Height, frame.BgraPixels);
        }
    }

    private void OnThumbnailReady(int generation, int bucket, ThumbnailFrame frame)
    {
        if (_thumbnails is { } service && generation == service.CurrentGeneration && HasVideo && !_isLive)
        {
            SeekBar.SetPreviewFrame(bucket, frame.Width, frame.Height, frame.BgraPixels);
        }
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

    private void OnStepBackClick(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            _session?.StepFrame(FrameStepDirection.Backward);
        }
    }

    private void OnStepForwardClick(object sender, RoutedEventArgs e)
    {
        if (HasVideo)
        {
            _session?.StepFrame(FrameStepDirection.Forward);
        }
    }

    private void OnTracksButtonClick(object sender, RoutedEventArgs e) => ShowTracks();

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

    private void OnSpeedClick(object sender, RoutedEventArgs e) =>
        ShowBarFlyout(SpeedMenu, SpeedButton, FlyoutPlacementMode.TopEdgeAlignedLeft);

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
        SetMenuSurface(sender, true);
    }

    private void OnSpeedMenuClosed(object sender, object e)
    {
        _speedMenuOpen = false;
        ApplySpeedLook();
        HoldForMenu(false);
        SetMenuSurface(sender, false);
    }

    // Filled while its menu is open; filled and outlined, like Loop when on, while the speed is not 1x.
    private void ApplySpeedLook()
    {
        bool changed = !PlaybackSpeed.IsCurrent(PlaybackSpeed.Normal, _speed);
        if (changed || _speedMenuOpen)
        {
            SpeedButton.Background = ThemeTokens.Get<Microsoft.UI.Xaml.Media.Brush>(SpeedButton, "AetherFillEmphasisRestBrush");
        }
        else
        {
            SpeedButton.ClearValue(BackgroundProperty);
        }
        if (changed)
        {
            SpeedButton.BorderBrush = ThemeTokens.Get<Microsoft.UI.Xaml.Media.Brush>(SpeedButton, "AetherStrokeControlBrush");
        }
        else
        {
            SpeedButton.ClearValue(BorderBrushProperty);
        }
    }

    // The folder button asks which to open, so a link can be played while a video is already on.
    private void OnOpenButtonClick(object sender, RoutedEventArgs e) =>
        ShowBarFlyout(OpenMenu, OpenButton, FlyoutPlacementMode.TopEdgeAlignedLeft);

    private void OnOpenMenuOpened(object sender, object e)
    {
        OpenButton.Background = ThemeTokens.Get<Microsoft.UI.Xaml.Media.Brush>(OpenButton, "AetherFillEmphasisRestBrush");
        OpenMenuFileItem.Text = AppStrings.Get("Menu_OpenFile");
        OpenMenuUrlItem.Text = AppStrings.Get("Menu_OpenUrl");
        OpenMenuFileItem.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.OpenFile);
        OpenMenuUrlItem.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.OpenUrl);
        HoldForMenu(true);
        SetMenuSurface(sender, true);
    }

    private void OnOpenMenuClosed(object sender, object e)
    {
        OpenButton.ClearValue(BackgroundProperty);
        HoldForMenu(false);
        SetMenuSurface(sender, false);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e) =>
        ShowBarFlyout(MoreMenu, MoreButton, FlyoutPlacementMode.TopEdgeAlignedRight);

    /// <summary>
    /// Maximum scroll height for flyouts docked above the expanded transport bar so WinUI keeps them strictly
    /// between the title bar (or top edge in full screen) and <see cref="Osd"/> instead of flipping sideways.
    /// </summary>
    private double MaxBarFlyoutPanelHeight()
    {
        double titleBar = TitleBar.Visibility == Visibility.Visible ? TitleBar.Height : 0;
        double barHeight = Math.Max(_osdFullHeight, Osd.ActualHeight);
        double gap = (double)Application.Current.Resources["AetherSizeOsdGap"];
        return Aether.Core.Playback.Windowing.WindowPlacement.MaxFlyoutHeightAboveBar(
            XamlRoot!.Size.Height, titleBar, Osd.Margin.Bottom, barHeight, gap);
    }

    /// <summary>
    /// Opens a transport button's flyout strictly above <see cref="Osd"/> while horizontally aligned to
    /// <paramref name="anchor"/>, so small popups never cover the seek bar or timecodes.
    /// </summary>
    private void ShowBarFlyout(
        FlyoutBase flyout,
        FrameworkElement anchor,
        FlyoutPlacementMode placement,
        FlyoutShowMode showMode = FlyoutShowMode.Standard,
        bool closeClipDrawer = true)
    {
        if (closeClipDrawer)
        {
            ClipDrawer?.Close();
        }
        // Anchoring to Osd directly guarantees WinUI places the flyout strictly above the full transport card (never
        // overlapping SeekRow). Outer edge buttons align to Osd's own edges; interior buttons use Position along Osd's
        // top edge with Osd as the ExclusionRect.
        if (ReferenceEquals(anchor, OpenButton) && placement == FlyoutPlacementMode.TopEdgeAlignedLeft)
        {
            flyout.ShowAt(Osd, new FlyoutShowOptions { Placement = FlyoutPlacementMode.TopEdgeAlignedLeft, ShowMode = showMode });
            return;
        }
        if (ReferenceEquals(anchor, MoreButton) && placement == FlyoutPlacementMode.TopEdgeAlignedRight)
        {
            flyout.ShowAt(Osd, new FlyoutShowOptions { Placement = FlyoutPlacementMode.TopEdgeAlignedRight, ShowMode = showMode });
            return;
        }

        Point? position = null;
        Rect? exclusion = null;
        if (Osd.ActualWidth > 0 && Osd.ActualHeight > 0 && anchor.ActualWidth > 0)
        {
            try
            {
                double localX = placement switch
                {
                    FlyoutPlacementMode.TopEdgeAlignedLeft => 0,
                    FlyoutPlacementMode.TopEdgeAlignedRight => anchor.ActualWidth,
                    _ => anchor.ActualWidth / 2,
                };
                Point inOsd = anchor.TransformToVisual(Osd).TransformPoint(new Point(localX, 0));
                position = new Point(inOsd.X, 0);
                exclusion = new Rect(0, 0, Osd.ActualWidth, Osd.ActualHeight);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
            }
        }
        flyout.ShowAt(Osd, new FlyoutShowOptions
        {
            Placement = placement,
            Position = position,
            ExclusionRect = exclusion,
            ShowMode = showMode,
        });
    }

    private void OnMoreMenuOpened(object sender, object e)
    {
        MoreButton.Background = ThemeTokens.Get<Microsoft.UI.Xaml.Media.Brush>(MoreButton, "AetherFillEmphasisRestBrush");
        // The tracks flyout has nothing to list without a file, as in the right-click menu.
        MoreAudioItem.IsEnabled = MoreSubtitlesItem.IsEnabled = HasVideo;
        MoreChaptersItem.IsEnabled = _chapters.Count > 0 && HasVideo;
        HoldForMenu(true);
        SetMenuSurface(sender, true);
    }

    private void OnMoreMenuClosed(object sender, object e)
    {
        MoreButton.ClearValue(BackgroundProperty);
        HoldForMenu(false);
        SetMenuSurface(sender, false);
        ShowTracksIfRequested();
        ShowChaptersIfRequested();
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
        ApplyToggleButtonLook(LoopButton, looping);
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
        ApplyToggleButtonLook(AlwaysOnTopButton, onTop);
    }

    private static void ApplyToggleButtonLook(Button button, bool active)
    {
        if (active)
        {
            button.Background = ThemeTokens.Get<Microsoft.UI.Xaml.Media.Brush>(button, "AetherFillEmphasisRestBrush");
            button.BorderBrush = ThemeTokens.Get<Microsoft.UI.Xaml.Media.Brush>(button, "AetherStrokeControlBrush");
        }
        else
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
        }
    }

    // In (I) and Out (O): the band on the seek bar and the export chip above the pill.

    private void OnSetInAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SetClipRange(_clipRange.SetIn(CurrentPosition())));

    private void OnSetOutAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () => SetClipRange(_clipRange.SetOut(CurrentPosition())));

    private void EnsureClipLayer()
    {
        if (ClipLayer is not null)
        {
            return;
        }
        App.EnsureSettingsControlsLoaded();
        FindName(nameof(ClipLayer));
        if (ClipLayer is null || _clipLayerWired)
        {
            return;
        }
        _clipLayerWired = true;
        ClipDrawer.ExportFilterSource = ExportFilterChain;
        ClipDrawer.ClearRequested += (_, _) => SetClipRange(default);
        ClipDrawer.CutStartChanged += SeekBar.SetCutStart;
        ClipDrawer.HoldChanged += OnClipDrawerHoldChanged;
        ClipDrawer.SurfaceShapeChanged += (_, _) => PostBackdropRegion();
        ClipLayer.SizeChanged += (_, _) => UpdateToastPlacement();
        double gap = _osdGap > 0 ? _osdGap : Motion.Token("AetherSizeOsdGap");
        ClipLayer.MaxWidth = Osd.MaxWidth;
        ClipLayer.Margin = new Thickness(ClipLayer.Margin.Left, ClipLayer.Margin.Top, ClipLayer.Margin.Right,
            Osd.Margin.Bottom + Osd.Height + gap);
        ClipLayer.Visibility = Osd.Visibility;
        _clipLayerShown = IsClipLayerVisible();
        ClipLayer.IsHitTestVisible = _clipLayerShown;
        _fader?.Fade(ClipLayer, _clipLayerShown);
        ApplyClipLookMode();
        PostBackdropRegion();
    }

    private void ApplyClipLookMode()
    {
        if (ClipDrawer is not null)
        {
            ClipDrawer.ApplyLookRadii(LookCornerRadii.For(_nativeLook ? LookModes.Native : LookModes.Aether));
        }
    }

    private void OnExportClipAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        OnTransportKey(args, () =>
        {
            EnsureClipLayer();
            ClipDrawer.RequestExport();
        });

    private double CurrentPosition() =>
        _session is { } session ? TransportState.DisplayPosition(session.ViewModel.State) : 0;

    private void SetClipRange(ClipRange range)
    {
        _clipRange = range;
        _clipRangePath = _currentPath;
        SeekBar.Range = range;
        if (!range.IsEmpty)
        {
            EnsureClipLayer();
        }
        ClipDrawer?.Update(range, _currentPath, _session?.ViewModel.Duration ?? 0);
    }

    private void OnClipDrawerHoldChanged(object? sender, EventArgs e) => SyncDrawerHold();

    /// <summary>In and Out belong to one file: opening another clears them.</summary>
    private void SyncClipRange()
    {
        if (!_clipRange.IsEmpty && _clipRangePath != _currentPath)
        {
            SetClipRange(default);
        }
    }
}
