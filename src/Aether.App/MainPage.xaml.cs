using System.ComponentModel;
using Aether.Core.Interop;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Playback.Input;
using Aether.Core.Playback.Recents;
using Aether.Core.Playback.Resume;
using Aether_App.Controls;
using Aether_App.Playback;
using Aether_App.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Aether_App;

/// <summary>
/// The player shell: video surface, home states (starting, ready, opening, drag-over, startup
/// failure), the title bar strip and the auto-hiding controls. Everything here runs on the UI thread;
/// the engine starts on the thread pool (<see cref="EngineStartup"/>) and nothing waits on mpv.
/// </summary>
public sealed partial class MainPage : Page
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string ReplayGlyph = ""; // UpdateRestore: the counter-clockwise arrow of frame 3.4
    private const string FullScreenGlyph = "";
    private const string ExitFullScreenGlyph = "";
    private const string VolumeGlyph = "";
    private const string MuteGlyph = "";

    private enum StartupPhase
    {
        Starting,
        Ready,
        MissingBinary,
        Failed,
    }

    private readonly ChromeVisibility _chrome = new();
    private DispatcherQueueTimer? _chromeTimer;
    private ChromeFader? _fader;
    private IShellWindow? _host;

    private PlayerSession? _session;
    private StartupPhase _startup = StartupPhase.Starting;
    private string? _startupError;
    private int _startupVersion; // bumped to abandon a startup still in flight
    private bool _isLoaded;

    private string? _currentPath;
    private string? _pendingOpen; // a file from the command line, opened once the engine is up
    private bool _fitWindowPending; // the next video size resizes the window (set per opened file)
    private bool _windowSyncQueued;
    private bool _dragging;
    private string? _dragPath;
    private bool _dragSupported;

    private bool _windowActive = true;
    private bool _pointerInside;
    private Point _lastPointer = new(double.NaN, double.NaN);
    private bool _updatingVolume;
    private double _lastAudibleVolume = TransportState.DefaultVolume;
    private readonly HoverFlyout _volumeHover = new();
    private DispatcherQueueTimer? _volumeHoverTimer;
    private bool _volumeOpeningPinned;

    // Links cannot play until streaming lands, so Play in the dialog reports that instead (4.D5).
    private readonly OpenUrlFlow _openUrl = new(TimeProvider.System, streamingAvailable: false);
    private DispatcherQueueTimer? _openUrlTimer;
    private DispatcherQueueTimer? _copiedTimer;
    private int _recentsVersion; // bumped to drop a missing-file check that a newer one replaced
    private bool _durationRecorded;
    private bool _settingsShown;

    public MainPage()
    {
        InitializeComponent();
        IsTabStop = true; // receives the frame-step and playback keys
        ApplyStaticStrings();
        // Light dismiss would otherwise swallow the first click on the pill, e.g. Mute under the flyout.
        VolumeFlyout.OverlayInputPassThroughElement = Osd;
        VolumeFlyoutContent.AddHandler(PointerPressedEvent, new PointerEventHandler(OnVolumeFlyoutPressed), true);
        VolumeFlyoutContent.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnVolumeFlyoutReleased), true);
        VolumeFlyoutContent.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnVolumeFlyoutReleased), true);
        _chrome.SetHold(ChromeHolds.NotPlaying, true);
        AddHandler(KeyDownEvent, new KeyEventHandler(OnAnyKeyDown), handledEventsToo: true);
        InitializeTransport();
        BuildShortcuts();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Labels that never change, from the strings file (the XAML text is only a design-time stand-in).</summary>
    private void ApplyStaticStrings()
    {
        TitleAppName.Text = AppStrings.Get("App_Name");
        ToolTipService.SetToolTip(SettingsButton, AppStrings.Get("TitleBar_SettingsTooltip"));
        AutomationProperties.SetName(SettingsButton, AppStrings.Get("Settings_Title"));
        ToolTipService.SetToolTip(SettingsBackButton, AppStrings.Get("Settings_Back"));
        AutomationProperties.SetName(SettingsBackButton, AppStrings.Get("Settings_Back"));
        SearchedKey.Text = AppStrings.Get("EmptyState_DetailsSearched");
        ErrorKey.Text = AppStrings.Get("EmptyState_DetailsError");
        OpenFileLabel.Text = AppStrings.Get("EmptyState_OpenFile");
        OpenUrlLabel.Text = AppStrings.Get("EmptyState_OpenUrl");
        StreamCancelButton.Content = AppStrings.Get("Stream_Cancel");
        RecentsHeader.Text = AppStrings.Get("EmptyState_Recent");
        ClearRecentsButton.Content = AppStrings.Get("EmptyState_ClearList");
        OpenUrlTitle.Text = AppStrings.Get("OpenUrl_Title");
        OpenUrlFieldLabel.Text = AppStrings.Get("OpenUrl_Placeholder");
        AutomationProperties.SetName(OpenUrlBox, OpenUrlFieldLabel.Text);
        OpenUrlPlayLabel.Text = AppStrings.Get("OpenUrl_Play");
        OpenUrlCancelButton.Content = AppStrings.Get("OpenUrl_Cancel");
        AutomationProperties.SetName(OpenButton, AppStrings.Get("Menu_OpenFile"));
        AutomationProperties.SetName(VolumeButton, AppStrings.Get("Transport_Volume"));
        AutomationProperties.SetName(VolumeSlider, AppStrings.Get("Transport_Volume"));
        ApplyTransportStrings();
        ApplyShortcutLabels();
        RetryLabel.Text = AppStrings.Get("Shell_EngineFailedRetry");
        CopyDetailsLabel.Text = AppStrings.Get("Shell_EngineFailedCopyDetails");
    }

    public PlayerSession? Session => _session;

    /// <summary>The strip the window uses as its drag region.</summary>
    public UIElement TitleBarDragRegion => TitleBarDragArea;

    /// <summary>Set once by the window that hosts this page.</summary>
    public IShellWindow? Host
    {
        get => _host;
        set
        {
            if (_host is not null)
            {
                _host.FullScreenChanged -= OnFullScreenChanged;
                _host.TitleBarPointerMoved -= OnTitleBarPointerMoved;
                _host.AlwaysOnTopChanged -= OnAlwaysOnTopChanged;
            }
            _host = value;
            if (_host is not null)
            {
                _host.FullScreenChanged += OnFullScreenChanged;
                _host.TitleBarPointerMoved += OnTitleBarPointerMoved;
                _host.AlwaysOnTopChanged += OnAlwaysOnTopChanged;
            }
            SyncAlwaysOnTop();
        }
    }

    /// <summary>A key-up never arrives once the window loses activation; release held keys and the cursor.</summary>
    public void OnWindowActivationChanged(bool active)
    {
        _windowActive = active;
        if (!active)
        {
            _session?.ReleaseKeys();
        }
        UpdateCursor();
    }

    // ---- Lifecycle and engine startup (4.6) ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (_fader is null)
        {
            var resources = Application.Current.Resources;
            _fader = new ChromeFader(
                ElementCompositionPreview.GetElementVisual(this).Compositor,
                TimeSpan.FromMilliseconds((double)resources["AetherMotionChromeShowMilliseconds"]),
                TimeSpan.FromMilliseconds((double)resources["AetherMotionChromeHideMilliseconds"]));
            _chromeTimer = DispatcherQueue.CreateTimer();
            _chromeTimer.IsRepeating = false;
            _chromeTimer.Tick += OnChromeTimerTick;
            _volumeHoverTimer = DispatcherQueue.CreateTimer();
            _volumeHoverTimer.IsRepeating = false;
            _volumeHoverTimer.Tick += OnVolumeHoverTimerTick;
            _openUrlTimer = DispatcherQueue.CreateTimer();
            _openUrlTimer.IsRepeating = false;
            _openUrlTimer.Tick += OnOpenUrlTimerTick;
            _copiedTimer = DispatcherQueue.CreateTimer();
            _copiedTimer.Interval = TimeSpan.FromSeconds(2);
            _copiedTimer.IsRepeating = false;
            _copiedTimer.Tick += OnCopiedTimerTick;
        }
        if (_session is null)
        {
            StartEngine();
        }
        App.Recents.Changed += OnRecentsChanged;
        App.Shortcuts.Changed += OnShortcutsChanged;
        BuildShortcuts(); // settings may have finished loading since the constructor
        ApplyShortcutLabels();
        RefreshRecents();
        Render();
        Focus(FocusState.Programmatic);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _startupVersion++; // a startup still running disposes its engine when it returns
        _chromeTimer?.Stop();
        _volumeHoverTimer?.Stop();
        _openUrlTimer?.Stop();
        _copiedTimer?.Stop();
        CopyDetailsLabel.Text = AppStrings.Get("Shell_EngineFailedCopyDetails");
        App.Recents.Changed -= OnRecentsChanged;
        App.Shortcuts.Changed -= OnShortcutsChanged;
        SaveResumePosition(background: false);
        CursorVisibility.Show();
        if (_session is { } session)
        {
            session.ViewModel.PropertyChanged -= OnViewModelChanged;
            session.FileEnded -= OnFileEnded;
            session.Dispose();
            _session = null;
        }
    }

    private async void StartEngine()
    {
        int version = ++_startupVersion;
        _startup = StartupPhase.Starting;
        _startupError = null;
        Render();

        EngineStartupResult<MpvContext> result =
            await EngineStartup.RunAsync(NativeLibraries.IsMpvAvailable, () => PlayerSession.CreateEngine());

        if (version != _startupVersion || !_isLoaded)
        {
            result.Engine?.Dispose(); // abandoned: unloaded or retried meanwhile
            return;
        }

        switch (result.Status)
        {
            case EngineStartupStatus.Ready:
                try
                {
                    _session = PlayerSession.Create(result.Engine!, VideoSurface, this, DispatcherQueue);
                }
                catch (Exception exception)
                {
                    _startup = StartupPhase.Failed;
                    _startupError = exception.Message;
                    break;
                }
                _session.ViewModel.PropertyChanged += OnViewModelChanged;
                _session.FileEnded += OnFileEnded;
                _startup = StartupPhase.Ready;
                _session.ViewModel.IsLooping = App.Settings.Current.LoopByDefault;
                SyncVolume();
                if (_pendingOpen is { } pending)
                {
                    _pendingOpen = null;
                    Open(pending);
                }
                break;
            case EngineStartupStatus.MissingBinary:
                _startup = StartupPhase.MissingBinary;
                break;
            default:
                _startup = StartupPhase.Failed;
                _startupError = result.Error;
                break;
        }
        Render();
    }

    /// <summary>Opens the file now if the engine is running, otherwise once it has started.</summary>
    public void OpenWhenReady(string path)
    {
        if (_session is not null)
        {
            Open(path);
        }
        else
        {
            _pendingOpen = path;
        }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            StartEngine();
        }
    }

    /// <summary>Copies the failure as shown: title, subtitle and the details rows that are visible.</summary>
    private void OnCopyDetailsClick(object sender, RoutedEventArgs e)
    {
        string text = FailureDetails.Format(
            HomeTitle.Text,
            HomeSubtitle.Text,
            [
                (SearchedKey.Text, SearchedValue.Visibility == Visibility.Visible ? SearchedValue.Text : null),
                (ErrorKey.Text, ErrorValue.Text),
            ]);
        var package = new DataPackage();
        package.SetText(text);
        try
        {
            Clipboard.SetContent(package);
            CopyDetailsLabel.Text = AppStrings.Get("Shell_EngineFailedCopied");
            _copiedTimer?.Start(); // Start on a running timer restarts the 2 seconds
        }
        catch (Exception)
        {
            // Another app holds the clipboard open; the details stay selectable in the tile.
        }
    }

    private void OnCopiedTimerTick(DispatcherQueueTimer sender, object args) =>
        CopyDetailsLabel.Text = AppStrings.Get("Shell_EngineFailedCopyDetails");

    // The loader also tries mpv-2.dll, but the failure names the file the installer ships.
    private static string LibraryPath =>
        Path.Combine(NativeLibraries.SearchDirectory ?? AppContext.BaseDirectory, "libmpv-2.dll");

    // ---- Playback ----

    private void Open(string path)
    {
        if (_session is not { } session)
        {
            return;
        }
        SaveResumePosition(background: true);
        double? startAt = App.Settings.Current.ResumePlayback ? App.Resume.TryGet(path) : null;
        _currentPath = path;
        _fitWindowPending = true;
        _durationRecorded = false;
        TrySend(() => session.Open(path, startAt));
        string name = Path.GetFileName(path);
        _ = Task.Run(() => App.Recents.Add(path, name, DateTimeOffset.Now));
        Render();
    }

    /// <summary>Remembers where the current file is, for the next time it is opened (5.10).</summary>
    private void SaveResumePosition(bool background)
    {
        if (_currentPath is not { } path || _session?.ViewModel is not { } vm || !HasVideo || !App.Settings.Current.ResumePlayback)
        {
            return;
        }
        double position = vm.Position;
        double duration = vm.Duration;
        DateTimeOffset now = DateTimeOffset.Now;
        if (background)
        {
            _ = Task.Run(() => App.Resume.Save(path, position, duration, now));
        }
        else
        {
            App.Resume.Save(path, position, duration, now); // on close: a queued write could be lost with the process
        }
    }

    private void TogglePause()
    {
        if (_session is { } session && HasVideo)
        {
            TrySend(session.TogglePause);
        }
    }

    /// <summary>mpv rejects commands only while the core shuts down; there is nothing to do then.</summary>
    private static void TrySend(Action send)
    {
        try
        {
            send();
        }
        catch (MpvException)
        {
        }
    }

    private bool HasVideo =>
        _session?.ViewModel.Status is PlaybackStatus.Playing or PlaybackStatus.Paused or PlaybackStatus.Ended;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        PlayerViewModel vm = _session!.ViewModel;
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.Position):
                UpdateClock(vm);
                break;
            case nameof(PlayerViewModel.Duration):
                UpdateClock(vm);
                RecordDuration(vm);
                break;
            case nameof(PlayerViewModel.Volume):
                SyncVolume();
                break;
            case nameof(PlayerViewModel.Speed):
                ShowSpeed(vm.Speed);
                break;
            case nameof(PlayerViewModel.IsLooping):
                SyncLoop();
                break;
            case nameof(PlayerViewModel.VideoWidth):
            case nameof(PlayerViewModel.VideoHeight):
                FitVideo();
                QueueWindowSync();
                break;
            case nameof(PlayerViewModel.Status):
                UpdateClock(vm); // an ended file reads its full duration
                RecordDuration(vm);
                Render();
                break;
            case nameof(PlayerViewModel.IsPaused):
                Render();
                break;
        }
    }

    /// <summary>The recents row shows the length once the file has loaded; once per opened file.</summary>
    private void RecordDuration(PlayerViewModel vm)
    {
        if (_durationRecorded || !HasVideo || vm.Duration <= 0 || _currentPath is not { } path)
        {
            return;
        }
        _durationRecorded = true;
        double duration = vm.Duration;
        _ = Task.Run(() => App.Recents.SetDuration(path, duration));
    }

    /// <summary>Position and duration text and the seek bar. Both times change shape together from an hour.</summary>
    private void UpdateClock(PlayerViewModel vm)
    {
        DurationText.Text = Timecode.FormatClock(vm.Duration, vm.Duration);
        SyncClipRange();
        SeekBar.Duration = vm.Duration;
        double position = TransportState.DisplayPosition(vm.State);
        SeekBar.Position = position;
        if (!SeekBar.IsDragging)
        {
            PositionText.Text = Timecode.FormatClock(position, vm.Duration); // while dragging it shows the drag time
        }
    }

    private void SyncVolume()
    {
        double volume = _session?.ViewModel.Volume ?? TransportState.DefaultVolume;
        _updatingVolume = true;
        VolumeSlider.Value = Math.Clamp(volume, 0, 100);
        _updatingVolume = false;
        ShowVolume(volume);
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingVolume || _session is not { } session)
        {
            return;
        }
        ShowVolume(e.NewValue);
        TrySend(() => session.SetVolume(e.NewValue));
    }

    private void ShowVolume(double volume)
    {
        if (volume > 0)
        {
            _lastAudibleVolume = volume;
        }
        bool muted = TransportState.IsMuted(volume);
        VolumeButton.Content = muted ? MuteGlyph : VolumeGlyph;
        ToolTipService.SetToolTip(VolumeButton, Tip(muted ? "Transport_Unmute" : "Transport_Mute", ShortcutAction.Mute));
    }

    private void OnMuteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_session is not null && !ArePlayerKeysPaused)
        {
            args.Handled = true;
            ToggleMute();
        }
    }

    private void OnLoopAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_session is { } session && !ArePlayerKeysPaused)
        {
            args.Handled = true;
            session.ViewModel.ToggleLoop();
        }
    }

    private void OnAlwaysOnTopAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_host is { } host && !ArePlayerKeysPaused)
        {
            args.Handled = true;
            host.ToggleAlwaysOnTop();
        }
    }

    private void OnVolumeClick(object sender, RoutedEventArgs e) => ToggleMute();

    private void ToggleMute()
    {
        if (_session is not { } session)
        {
            return;
        }
        double volume = TransportState.ToggleMute(session.ViewModel.Volume, _lastAudibleVolume);
        TrySend(() => session.SetVolume(volume));
    }

    // ---- Volume flyout: hover opens it, right-click or Shift+F10 pins it ----

    private void OnVolumeButtonPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _volumeHover.PointerOverAnchor(true);
        ArmVolumeHover();
    }

    private void OnVolumeButtonPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _volumeHover.PointerOverAnchor(false);
        ArmVolumeHover();
    }

    private void OnVolumeFlyoutPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _volumeHover.PointerOverFlyout(true);
        ArmVolumeHover();
    }

    private void OnVolumeFlyoutPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _volumeHover.PointerOverFlyout(false);
        ArmVolumeHover();
    }

    private void OnVolumeFlyoutPressed(object sender, PointerRoutedEventArgs e)
    {
        _volumeHover.Pressed(true);
        ArmVolumeHover();
    }

    private void OnVolumeFlyoutReleased(object sender, PointerRoutedEventArgs e)
    {
        _volumeHover.Pressed(false);
        ArmVolumeHover();
    }

    private void OnVolumeContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        if (_volumeHover.IsOpen)
        {
            _volumeHover.Opened(pinned: true);
            ArmVolumeHover();
            VolumeSlider.Focus(FocusState.Keyboard);
        }
        else
        {
            ShowVolumeFlyout(pinned: true);
        }
    }

    private void ArmVolumeHover()
    {
        if (_volumeHoverTimer is null)
        {
            return;
        }
        if (_volumeHover.DueIn is { } due)
        {
            _volumeHoverTimer.Interval = due;
            _volumeHoverTimer.Start();
        }
        else
        {
            _volumeHoverTimer.Stop();
        }
    }

    private void OnVolumeHoverTimerTick(DispatcherQueueTimer sender, object args)
    {
        switch (_volumeHover.Decide())
        {
            case HoverFlyoutAction.Open:
                ShowVolumeFlyout(pinned: false);
                break;
            case HoverFlyoutAction.Close:
                VolumeFlyout.Hide();
                break;
        }
    }

    private void ShowVolumeFlyout(bool pinned)
    {
        _volumeOpeningPinned = pinned;
        // Transient: a hover-opened flyout must not take focus from the page's playback keys.
        VolumeFlyout.ShowAt(VolumeButton, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.Top,
            ShowMode = pinned ? FlyoutShowMode.Standard : FlyoutShowMode.Transient,
        });
    }

    private void OnVolumeFlyoutOpened(object? sender, object e)
    {
        _volumeHover.Opened(_volumeOpeningPinned);
        ArmVolumeHover();
        if (_volumeOpeningPinned)
        {
            VolumeSlider.Focus(FocusState.Keyboard);
        }
        _chrome.SetHold(ChromeHolds.Flyout, true);
        ApplyChrome();
    }

    private void OnVolumeFlyoutClosed(object? sender, object e)
    {
        _volumeHover.Closed();
        ArmVolumeHover();
        _chrome.SetHold(ChromeHolds.Flyout, false);
        ApplyChrome();
        Focus(FocusState.Programmatic); // keep Space and the frame-step keys on the page
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => TogglePause();

    private void OnFullScreenClick(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void OnVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        if (_host is { } host)
        {
            host.SetFullScreen(!host.IsFullScreen);
        }
    }

    private void OnFullScreenChanged(object? sender, EventArgs e) => Render();

    // ---- Keys ----

    private async void OnOpenAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!IsOpenUrlShown)
        {
            await PickAndOpenAsync();
        }
    }

    private async void OnOpenFileClick(object sender, RoutedEventArgs e) => await PickAndOpenAsync();

    /// <summary>The one file picker, shared by Ctrl+O and both Open buttons.</summary>
    private async Task PickAndOpenAsync()
    {
        if (_session is null || _host is not Window window)
        {
            return;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        foreach (string extension in MediaFormats.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        _chrome.SetHold(ChromeHolds.Interaction, true);
        ApplyChrome();
        StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        finally
        {
            _chrome.SetHold(ChromeHolds.Interaction, false);
            ApplyChrome();
        }
        if (file is not null && MediaFormats.IsSupported(file.Path))
        {
            Open(file.Path);
        }
    }

    // Space, F and M stay text while the Open URL field has focus, and belong to the focused control in
    // settings: unhandled, they pass through.
    private void OnPlayPauseAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ArePlayerKeysPaused)
        {
            return;
        }
        args.Handled = true;
        TogglePause();
    }

    private void OnFullScreenAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ArePlayerKeysPaused)
        {
            return;
        }
        args.Handled = true;
        ToggleFullScreen();
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsOpenUrlShown)
        {
            args.Handled = true;
            CloseOpenUrl();
        }
        else if (_settingsShown)
        {
            args.Handled = true;
            SetSettingsShown(false);
        }
        else if (ClipDrawer.Close())
        {
            args.Handled = true;
        }
        else if (_host is { IsFullScreen: true } host)
        {
            args.Handled = true;
            host.SetFullScreen(false);
        }
    }

    private void OnAnyKeyDown(object sender, KeyRoutedEventArgs e) => NotifyActivity();

    /// <summary>Player keys stand down while the Open URL field or settings has the keyboard.</summary>
    private bool ArePlayerKeysPaused => IsOpenUrlShown || _settingsShown;

    // ---- Settings (6.1) ----

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SetSettingsShown(!_settingsShown);

    private void OnSettingsBackClick(object sender, RoutedEventArgs e) => SetSettingsShown(false);

    /// <summary>Settings covers the player; playback carries on underneath.</summary>
    private void SetSettingsShown(bool shown)
    {
        if (_settingsShown == shown)
        {
            return;
        }
        _settingsShown = shown;
        if (shown)
        {
            SettingsHost.Child ??= new SettingsView();
            _host?.SetFullScreen(false);
        }
        SettingsHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        SettingsBackButton.Visibility = SettingsHost.Visibility;
        _chrome.SetHold(ChromeHolds.Settings, shown);
        Render();
        if (!shown)
        {
            Focus(FocusState.Programmatic);
        }
    }

    // ---- Open URL (2.5, 2.6, 2.12) ----

    private bool IsOpenUrlShown => _openUrl.Stage == OpenUrlStage.Editing;

    private void OnOpenUrlAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowOpenUrl();
    }

    private void OnOpenUrlClick(object sender, RoutedEventArgs e) => ShowOpenUrl();

    /// <summary>Opens the dialog prefilled from the clipboard (a link only) with the text selected.</summary>
    private async void ShowOpenUrl()
    {
        if (_session is null || _openUrl.Stage != OpenUrlStage.Closed)
        {
            return;
        }
        string? clipboard = null;
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                clipboard = await content.GetTextAsync();
            }
        }
        catch (Exception)
        {
            // Another app holds the clipboard; open with an empty field.
        }
        if (!_isLoaded || _openUrl.Stage != OpenUrlStage.Closed)
        {
            return;
        }

        _openUrl.Open(clipboard);
        OpenUrlBox.Text = _openUrl.Text;
        OpenUrlOverlay.Visibility = Visibility.Visible;
        _chrome.SetHold(ChromeHolds.Dialog, true);
        ApplyChrome();
        RenderOpenUrl();
        OpenUrlBox.Focus(FocusState.Programmatic);
        OpenUrlBox.SelectAll();
    }

    private void OnOpenUrlTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsOpenUrlShown)
        {
            _openUrl.SetText(OpenUrlBox.Text);
            RenderOpenUrl();
        }
    }

    private void OnOpenUrlBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            SubmitOpenUrl();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CloseOpenUrl();
        }
    }

    private void OnOpenUrlPlayClick(object sender, RoutedEventArgs e) => SubmitOpenUrl();

    private void OnOpenUrlCancelClick(object sender, RoutedEventArgs e) => CloseOpenUrl();

    private void SubmitOpenUrl()
    {
        if (_openUrl.Submit() == OpenUrlSubmitResult.Started)
        {
            HideOpenUrlDialog();
            ArmOpenUrlTimer();
            Render();
        }
        else
        {
            RenderOpenUrl();
        }
    }

    private void CloseOpenUrl()
    {
        if (!IsOpenUrlShown)
        {
            return;
        }
        _openUrl.Close();
        HideOpenUrlDialog();
    }

    private void HideOpenUrlDialog()
    {
        OpenUrlOverlay.Visibility = Visibility.Collapsed;
        _chrome.SetHold(ChromeHolds.Dialog, false);
        ApplyChrome();
        Focus(FocusState.Programmatic); // playback keys back on the page
    }

    private void OnStreamCancelClick(object sender, RoutedEventArgs e)
    {
        _openUrl.Close();
        ArmOpenUrlTimer();
        Render();
    }

    private void ArmOpenUrlTimer()
    {
        if (_openUrlTimer is null)
        {
            return;
        }
        if (_openUrl.DueIn is { } due)
        {
            _openUrlTimer.Interval = due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1);
            _openUrlTimer.Start();
        }
        else
        {
            _openUrlTimer.Stop();
        }
    }

    private void OnOpenUrlTimerTick(DispatcherQueueTimer sender, object args)
    {
        _openUrl.Tick();
        ArmOpenUrlTimer();
        Render();
    }

    /// <summary>Play follows the link check; the line under the field is the hint or the reason Play did nothing.</summary>
    private void RenderOpenUrl()
    {
        OpenUrlPlayButton.IsEnabled = _openUrl.CanSubmit;
        // OpenUrl_Hint (Ctrl+V plays a copied link) replaces the not-available text once links play.
        (string note, Tone? dot) = _openUrl.Note switch
        {
            OpenUrlNote.Invalid => (AppStrings.Get("OpenUrl_InvalidLink"), Tone.Warning),
            OpenUrlNote.Unavailable => (AppStrings.Get("OpenUrl_NotAvailable"), Tone.Neutral),
            _ => (AppStrings.Get("OpenUrl_NotAvailable"), (Tone?)null),
        };
        OpenUrlNoteText.Text = note;
        OpenUrlNoteDot.Visibility = dot is null ? Visibility.Collapsed : Visibility.Visible;
        if (dot is { } tone)
        {
            OpenUrlNoteDot.Fill = ToneBrush(tone);
        }
    }

    // ---- Recents (2.10, 2.18) ----

    private void OnRecentsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RefreshRecents);

    /// <summary>Rebuilds the rows; the missing-file check runs on the thread pool so a slow drive never stalls the UI.</summary>
    private async void RefreshRecents()
    {
        int version = ++_recentsVersion;
        IReadOnlyList<RecentItem> items = App.Recents.Items;
        bool[] missing = await Task.Run(() => items.Select(item => RecentDisplay.IsMissing(item, File.Exists)).ToArray());
        if (version != _recentsVersion || !_isLoaded)
        {
            return;
        }
        RecentsList.Children.Clear();
        DateTimeOffset now = DateTimeOffset.Now;
        for (int i = 0; i < items.Count; i++)
        {
            var row = new RecentRow(items[i], missing[i], now);
            row.OpenRequested += OnRecentOpenRequested;
            row.RemoveRequested += OnRecentRemoveRequested;
            RecentsList.Children.Add(row);
        }
        Render();
    }

    /// <summary>A file that went missing since the list was drawn turns into the missing row instead of opening.</summary>
    private async void OnRecentOpenRequested(object? sender, EventArgs e)
    {
        if (sender is not RecentRow { Item.IsLink: false } row || _session is null)
        {
            return;
        }
        string path = row.Item.Location;
        bool exists = await Task.Run(() => File.Exists(path));
        if (!_isLoaded)
        {
            return;
        }
        if (exists)
        {
            Open(path);
        }
        else
        {
            RefreshRecents();
        }
    }

    private void OnRecentRemoveRequested(object? sender, EventArgs e)
    {
        if (sender is RecentRow row)
        {
            string location = row.Item.Location;
            _ = Task.Run(() => App.Recents.Remove(location));
        }
    }

    private void OnClearRecentsClick(object sender, RoutedEventArgs e) => _ = Task.Run(App.Recents.Clear);

    // ---- Drag and drop (4.5) ----

    private async void OnDragEnter(object sender, DragEventArgs e)
    {
        if (ClipDrawer.IsDraggingOut)
        {
            return; // a clip leaving the drawer is not a file to open
        }
        _dragging = true;
        _dragPath = null;
        _dragSupported = false;
        _chrome.SetHold(ChromeHolds.Interaction, true);
        ApplyChrome();
        Render();

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }
        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            IStorageItem? item = items.FirstOrDefault();
            _dragPath = item?.Path;
            _dragSupported = item is IStorageFile && MediaFormats.IsSupported(_dragPath) && _session is not null;
            e.AcceptedOperation = _dragSupported ? DataPackageOperation.Copy : DataPackageOperation.None;
        }
        catch (Exception)
        {
            _dragSupported = false; // the source withdrew the data
        }
        finally
        {
            deferral.Complete();
        }
        if (_dragging)
        {
            Render();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _dragSupported ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = AppStrings.Get("Shell_DropCaption");
        e.DragUIOverride.IsCaptionVisible = _dragSupported;
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => EndDrag();

    private void OnDrop(object sender, DragEventArgs e)
    {
        string? path = _dragSupported ? _dragPath : null;
        EndDrag();
        if (path is not null)
        {
            Open(path);
        }
    }

    private void EndDrag()
    {
        _dragging = false;
        _chrome.SetHold(ChromeHolds.Interaction, false);
        ApplyChrome();
        Render();
    }

    // ---- Auto-hiding chrome (4.4) ----

    private void OnPagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        // WinUI repeats PointerMoved without movement (e.g. when the cursor is hidden); ignore those.
        Point position = e.GetCurrentPoint(this).Position;
        if (position == _lastPointer)
        {
            return;
        }
        _lastPointer = position;
        NotifyActivity();
    }

    private void OnTitleBarPointerMoved(object? sender, EventArgs e) => NotifyActivity();

    private void OnPagePointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        UpdateCursor();
    }

    private void OnOsdPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _chrome.SetHold(ChromeHolds.PointerOverControls, true);
        ApplyChrome();
    }

    private void OnOsdPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _chrome.SetHold(ChromeHolds.PointerOverControls, false);
        ApplyChrome();
    }

    private void NotifyActivity()
    {
        _chrome.NotifyActivity();
        ApplyChrome();
    }

    private void OnChromeTimerTick(DispatcherQueueTimer sender, object args)
    {
        _chrome.Tick();
        ApplyChrome();
    }

    private bool _chromeShown = true;

    /// <summary>Fades the chrome to match <see cref="ChromeVisibility.IsVisible"/> and re-arms the idle timer.</summary>
    private void ApplyChrome()
    {
        bool visible = _chrome.IsVisible;
        if (visible != _chromeShown && _fader is not null)
        {
            _chromeShown = visible;
            _fader.Fade(TopScrim, visible);
            _fader.Fade(TitleBar, visible);
            _fader.Fade(Osd, visible);
            Osd.IsHitTestVisible = visible;
            _fader.Fade(ClipLayer, visible);
            ClipLayer.IsHitTestVisible = visible;
            _host?.SetCaptionButtonsVisible(visible);
        }
        UpdateCursor();

        if (_chromeTimer is null)
        {
            return;
        }
        if (_chrome.DueIn is { } due)
        {
            // A countdown already running is fine to replace: re-arming restarts it from now.
            _chromeTimer.Interval = due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1);
            _chromeTimer.Start();
        }
        else
        {
            _chromeTimer.Stop();
        }
    }

    private void UpdateCursor()
    {
        if (!_chrome.IsVisible && _windowActive && _pointerInside)
        {
            CursorVisibility.Hide();
        }
        else
        {
            CursorVisibility.Show();
        }
    }

    // ---- Layout and state presentation ----

    private void OnVideoHostSizeChanged(object sender, SizeChangedEventArgs e) => FitVideo();

    /// <summary>Sizes the surface to the video's display aspect ratio, centred in the host.</summary>
    private void FitVideo()
    {
        int width = _session?.ViewModel.VideoWidth ?? 0;
        int height = _session?.ViewModel.VideoHeight ?? 0;
        double hostWidth = VideoHost.ActualWidth;
        double hostHeight = VideoHost.ActualHeight;
        if (width <= 0 || height <= 0 || hostWidth <= 0 || hostHeight <= 0)
        {
            VideoSurface.Width = double.NaN;
            VideoSurface.Height = double.NaN;
            return;
        }
        double scale = Math.Min(hostWidth / width, hostHeight / height);
        VideoSurface.Width = Math.Round(width * scale);
        VideoSurface.Height = Math.Round(height * scale);
    }

    /// <summary>
    /// Width and height arrive as two changes in one flush; sync the window once both are applied, so
    /// a new width is never paired with the previous file's height.
    /// </summary>
    private void QueueWindowSync()
    {
        if (!_windowSyncQueued)
        {
            _windowSyncQueued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, SyncWindowToVideo);
        }
    }

    private void SyncWindowToVideo()
    {
        _windowSyncQueued = false;
        int width = _session?.ViewModel.VideoWidth ?? 0;
        int height = _session?.ViewModel.VideoHeight ?? 0;
        if (width <= 0 || height <= 0)
        {
            _host?.SetVideoAspect(0);
            return;
        }
        _host?.SetVideoAspect((double)width / height);
        if (_fitWindowPending)
        {
            _fitWindowPending = false;
            _host?.FitToVideo(width, height);
        }
    }

    /// <summary>Brings every view element in line with the startup, playback, drag and window state.</summary>
    private void Render()
    {
        bool hasVideo = HasVideo && !_settingsShown;
        bool fullScreen = _host?.IsFullScreen == true;
        PlayerViewModel? vm = _session?.ViewModel;

        // Video, scrims and controls.
        VideoSurface.Opacity = hasVideo ? 1 : 0;
        VideoHost.Background = fullScreen && hasVideo
            ? (Brush)Application.Current.Resources["AetherSurfaceLetterboxBrush"]
            : null;
        HomeView.Visibility = !_settingsShown && (!hasVideo || _dragging) ? Visibility.Visible : Visibility.Collapsed;
        DropScrim.Visibility = hasVideo && _dragging ? Visibility.Visible : Visibility.Collapsed;
        DropOutline.Visibility = _dragging ? Visibility.Visible : Visibility.Collapsed;
        Osd.Visibility = hasVideo ? Visibility.Visible : Visibility.Collapsed;
        ClipLayer.Visibility = Osd.Visibility;
        TitleBar.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;
        TopScrim.Visibility = hasVideo && !fullScreen ? Visibility.Visible : Visibility.Collapsed;
        TitleBadgeText.Text = HasVideo || vm?.Status == PlaybackStatus.Loading
            ? Path.GetFileName(_currentPath) ?? AppStrings.Get("App_Badge")
            : AppStrings.Get("App_Badge");

        (string glyph, string label) = TransportState.PlayButton(vm?.Status ?? PlaybackStatus.Idle) switch
        {
            PlayButtonAction.Pause => (PauseGlyph, Tip("Transport_Pause", ShortcutAction.PlayPause)),
            PlayButtonAction.Replay => (ReplayGlyph, Tip("Transport_Replay", ShortcutAction.PlayPause)),
            _ => (PlayGlyph, Tip("Transport_Play", ShortcutAction.PlayPause)),
        };
        PlayPauseButton.Content = glyph;
        ToolTipService.SetToolTip(PlayPauseButton, label);
        AutomationProperties.SetName(PlayPauseButton, label);
        FullScreenButton.Content = fullScreen ? ExitFullScreenGlyph : FullScreenGlyph;
        ToolTipService.SetToolTip(FullScreenButton, fullScreen ? AppStrings.Get("Transport_ExitFullScreen") : Tip("Transport_FullScreen", ShortcutAction.FullScreen));

        RenderHome(vm);

        _chrome.SetHold(ChromeHolds.NotPlaying, vm?.Status != PlaybackStatus.Playing);
        ApplyChrome();
    }

    private enum Tone
    {
        Neutral,
        Success,
        Warning,
    }

    private void RenderHome(PlayerViewModel? vm)
    {
        bool failed = _startup is StartupPhase.MissingBinary or StartupPhase.Failed;
        bool busy = false;
        bool streaming = _openUrl.Stage is OpenUrlStage.GettingStream or OpenUrlStage.StillWorking;

        // The strip reports the engine only; the renderer sits at its right end.
        (Tone Tone, string Label, string? Detail) engine = _startup switch
        {
            StartupPhase.Starting => (Tone.Neutral, AppStrings.Get("Shell_StartingEngine"), AppStrings.Get("Shell_LoadingLibmpv")),
            StartupPhase.Ready => (Tone.Success, AppStrings.Get("EmptyState_EngineReady"), null),
            _ => (Tone.Warning, AppStrings.Get("Shell_EngineNotRunning"), null),
        };

        if (_dragging)
        {
            if (_dragSupported)
            {
                SetHome(AppStrings.Get("Shell_DropToPlay"), AppStrings.Get("Shell_DropFormats"), null, engine);
            }
            else if (_session is null && MediaFormats.IsSupported(_dragPath))
            {
                SetHome(AppStrings.Get("Shell_EngineNotReadyTitle"), Path.GetFileName(_dragPath) ?? string.Empty, Tone.Warning, engine);
            }
            else
            {
                SetHome(
                    AppStrings.Get("Shell_UnsupportedFormatTitle"),
                    AppStrings.Format("Shell_UnsupportedFormatMessage", Path.GetFileName(_dragPath) ?? string.Empty),
                    Tone.Warning,
                    engine);
            }
        }
        else if (streaming)
        {
            bool slow = _openUrl.Stage == OpenUrlStage.StillWorking;
            string host = _openUrl.Host ?? string.Empty;
            SetHome(
                AppStrings.Get(slow ? "Stream_StillWorking" : "Stream_GettingStream"),
                slow ? AppStrings.Format("Stream_TakingLonger", host) : host,
                null,
                engine);
        }
        else if (_startup == StartupPhase.Starting)
        {
            busy = true;
            SetHome(AppStrings.Get("EmptyState_Title"), AppStrings.Get("EmptyState_SubtitleFilesOnly"), null, engine);
        }
        else if (failed)
        {
            bool missing = _startup == StartupPhase.MissingBinary;
            SetHome(
                AppStrings.Get("Shell_EngineFailedTitle"),
                AppStrings.Get(missing ? "Shell_EngineFailedMissingFile" : "Shell_EngineFailedUnavailable"),
                Tone.Warning,
                engine);
            SearchedValue.Text = LibraryPath;
            ErrorValue.Text = missing
                ? AppStrings.Get("Shell_EngineFailedReinstall")
                : _startupError ?? AppStrings.Get("Shell_EngineFailedUnknownError");
        }
        else if (vm?.Status == PlaybackStatus.Loading)
        {
            busy = true;
            SetHome(AppStrings.Get("Shell_OpeningTitle"), Path.GetFileName(_currentPath) ?? string.Empty, null, engine);
        }
        else
        {
            SetHome(AppStrings.Get("EmptyState_Title"), AppStrings.Get("EmptyState_SubtitleFilesOnly"), null, engine);
        }

        StartupProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        OpenActions.Visibility = failed || _dragging || streaming ? Visibility.Collapsed : Visibility.Visible;
        OpenFileButton.IsEnabled = _session is not null;
        OpenUrlButton.IsEnabled = _session is not null;
        bool showStream = streaming && !_dragging;
        HeroGlyph.Visibility = showStream ? Visibility.Collapsed : Visibility.Visible;
        StreamProgress.Visibility = showStream ? Visibility.Visible : Visibility.Collapsed;
        StreamProgress.IsActive = showStream;
        StreamCancelButton.Visibility = showStream ? Visibility.Visible : Visibility.Collapsed;
        // Recents belong to the plain empty state: hidden until there is history (2.10).
        bool idle = _startup == StartupPhase.Ready && !_dragging && !streaming && vm?.Status != PlaybackStatus.Loading;
        RecentsPanel.Visibility = idle && RecentsList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Visibility failure = failed && !_dragging ? Visibility.Visible : Visibility.Collapsed;
        DetailsPanel.Visibility = failure;
        FailureActions.Visibility = failure;
        Visibility searched = _startup == StartupPhase.MissingBinary ? Visibility.Visible : Visibility.Collapsed;
        SearchedKey.Visibility = searched;
        SearchedValue.Visibility = searched;
    }

    /// <param name="titleTone">Dot beside the title (failures), or null for none.</param>
    /// <param name="status">Dot, label and muted detail of the status strip. Colour stays in the dots; the text is white.</param>
    private void SetHome(string title, string subtitle, Tone? titleTone, (Tone Tone, string Label, string? Detail) status)
    {
        HomeTitle.Text = title;
        HomeSubtitle.Text = subtitle;
        TitleDot.Visibility = titleTone is null ? Visibility.Collapsed : Visibility.Visible;
        if (titleTone is { } tone)
        {
            TitleDot.Fill = ToneBrush(tone);
        }
        StatusDot.Fill = ToneBrush(status.Tone);
        StatusLabel.Text = status.Label;
        StatusDetail.Text = status.Detail ?? string.Empty;
        StatusDetail.Visibility = status.Detail is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Brush ToneBrush(Tone tone) => (Brush)Application.Current.Resources[tone switch
    {
        Tone.Success => "AetherStatusReadyBrush",
        Tone.Warning => "AetherStatusWarningBrush",
        _ => "AetherStatusNeutralBrush",
    }];
}
