using Aether.Core.Playback.Streams;
﻿using System.ComponentModel;
using Aether.Core.Interop;
using Aether.Core.Interop.Mpv;
using Aether.Core.Interop.Rendering;
using Aether.Core.Playback;
using Aether.Core.Playback.Input;
using Aether.Core.Playback.Recents;
using Aether.Core.Playback.Resume;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Tracks;
using Aether_App.Controls;
using Aether_App.Playback;
using Aether_App.Settings;
using Aether_App.Updates;
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
    private const string PlayGlyph = ""; // PlaySolid and PauseSolid: the handoff fills both
    private const string PauseGlyph = "";
    private const string ReplayGlyph = ""; // UpdateRestore: the counter-clockwise arrow of frame 3.4
    private const string FullScreenGlyph = "";
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
    private ToastService? _toasts;
    private IShellWindow? _host;

    private PlayerSession? _session;
    private StartupPhase _startup = StartupPhase.Starting;
    private string? _startupError;
    private int _startupVersion; // bumped to abandon a startup still in flight
    private bool _isLoaded;

    private string? _currentPath;
    private string? _pendingOpen; // a file from the command line, opened once the engine and video output are up
    private PlaybackCarryOver? _carryOver; // what an engine restart puts back once the new engine is up
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
    private readonly OpenUrlFlow _openUrl = new(TimeProvider.System, streamingAvailable: true);
    private readonly IYtDlpStreamResolver _streamResolver = new YtDlpStreamResolver();
    private CancellationTokenSource? _streamResolutionCts;
    private StreamFailureState? _streamFailure;
    private string? _failedStreamLink;
    private string? _currentWebLink;
    private string? _streamTitle;

    private sealed record StreamFailureState(StreamFailureReason Reason, string Title, string Subtitle, string Link);
    private DispatcherQueueTimer? _openUrlTimer;
    private DispatcherQueueTimer? _copiedTimer;
    private int _recentsVersion; // bumped to drop a missing-file check that a newer one replaced
    private bool _durationRecorded;
    private bool _settingsShown;
    private bool _diagnosticsShown;

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
        InitializeBar();
        InitializeRail();
        InitializeTour();
        InitializeContextMenu();
        InitializeTracks();
        InitializeVideo();
        BuildShortcuts();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LayoutUpdated += (_, _) => PostBackdropRegion();
        SizeChanged += (_, _) =>
        {
            PlaceRail();
            UpdateToastPlacement();
        };
        VideoSurface.CompositionScaleChanged += (_, _) => PostBackdropRegion();
        VideoSurface.PlayerReady += (_, _) => OpenPending();
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
        StreamRetryLabel.Text = AppStrings.Get("Stream_Retry");
        StreamInstallLabel.Text = AppStrings.Get("Stream_InstallYtDlp");
        StreamEditLinkLabel.Text = AppStrings.Get("Stream_EditLink");
        StreamOpenInBrowserLabel.Text = AppStrings.Get("Stream_OpenInBrowser");
        StreamFailureCancelLabel.Text = AppStrings.Get("Stream_Cancel");
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
        string backToFull = AppStrings.Get("Pip_BackToFullWindow");
        ToolTipService.SetToolTip(PipBackButton, backToFull);
        AutomationProperties.SetName(PipBackButton, backToFull);
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
                _host.PictureInPictureChanged -= OnPictureInPictureChanged;
                _host.TitleBarPointerMoved -= OnTitleBarPointerMoved;
                _host.AlwaysOnTopChanged -= OnAlwaysOnTopChanged;
                _host.TaskbarCommandRequested -= OnTaskbarCommand;
            }
            _host = value;
            if (_host is not null)
            {
                _host.FullScreenChanged += OnFullScreenChanged;
                _host.PictureInPictureChanged += OnPictureInPictureChanged;
                _host.TitleBarPointerMoved += OnTitleBarPointerMoved;
                _host.AlwaysOnTopChanged += OnAlwaysOnTopChanged;
                _host.TaskbarCommandRequested += OnTaskbarCommand;
            }
            SyncAlwaysOnTop();
        }
    }

    /// <summary>Keeps the gear flush with the system caption buttons, whatever their width at this display scale.</summary>
    public void SetCaptionInset(double width)
    {
        if (width > 0)
        {
            CaptionReserve.Width = width;
        }
    }

    /// <summary>A key-up never arrives once the window loses activation; release held keys and the cursor.</summary>
    public void OnWindowActivationChanged(bool active)
    {
        _windowActive = active;
        Brush glyph = (Brush)Application.Current.Resources[active ? "AetherCaptionGlyphBrush" : "AetherCaptionGlyphInactiveBrush"];
        SettingsButton.Foreground = glyph;
        SettingsBackButton.Foreground = glyph;
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
        YtDlpManager.StartScheduledUpdates();
        UpdateService.StartScheduledChecks();
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
            _toasts = new ToastService(ToastCard, ToastLayer, _fader, DispatcherQueue, () => Focus(FocusState.Programmatic));
            ClipLayer.SizeChanged += (_, _) => UpdateToastPlacement();
            // The Skip button steps over a toast it would touch, so it follows the toast coming and going.
            ToastLayer.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateToastPlacement());
            ToastLayer.SizeChanged += (_, _) => UpdateToastPlacement();
            SkipChapterLayer.SizeChanged += (_, _) => UpdateToastPlacement();
        }
        AppToasts.Requested += OnToastRequested;
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
        EndTour();
        CopyDetailsLabel.Text = AppStrings.Get("Shell_EngineFailedCopyDetails");
        App.Recents.Changed -= OnRecentsChanged;
        App.Shortcuts.Changed -= OnShortcutsChanged;
        AppToasts.Requested -= OnToastRequested;
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

    /// <summary>What a resolved link brings to <see cref="Open"/> beyond the stream address.</summary>
    /// <param name="Qualities">The sizes the quality picker lists; <paramref name="Quality"/> is the one playing.</param>
    private sealed record OpenedLink(
        string WebLink, string Title, IReadOnlyDictionary<string, string>? HttpHeaders, string? AudioUrl, IReadOnlyList<StreamChapter>? Chapters,
        IReadOnlyList<StreamSubtitle>? Subtitles, IReadOnlyList<QualityOption> Qualities, QualityOption Quality);

    /// <summary>Where playback was when the engine restarted. <see cref="Position"/> is null while the file was still loading.</summary>
    private sealed record PlaybackCarryOver(
        string? Path, double? Position, bool Paused, double Volume, double Speed, bool Looping, TrackChoices? Tracks, string? OriginalWebLink = null);

    /// <param name="settings">The settings to start with, or null to wait for the saved ones (first start).</param>
    private async void StartEngine(AetherSettings? settings = null)
    {
        int version = ++_startupVersion;
        _startup = StartupPhase.Starting;
        _startupError = null;
        _lastDecoder = null;
        _lookApplied = false; // a new core starts with its own decoder and no filters
        Render();

        EngineStartupResult<MpvContext> result =
            await EngineStartup.RunAsync(NativeLibraries.IsMpvAvailable, () => PlayerSession.CreateEngine(settings));

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
                if (settings is not null)
                {
                    App.Settings.MarkStarted(settings);
                }
                _session.ViewModel.IsLooping = _carryOver?.Looping ?? App.Settings.Current.LoopByDefault;
                if (_carryOver is { } carry)
                {
                    PlayerSession session = _session;
                    TrySend(() =>
                    {
                        session.SetVolume(carry.Volume);
                        session.SetSpeed(carry.Speed);
                    });
                    if (carry.Path is null)
                    {
                        _carryOver = null;
                    }
                }
                SyncVolume();
                OpenPending();
                QueueLaunchTour();
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

    /// <summary>Opens the file now if the engine and its video output are up, otherwise once they are.</summary>
    public void OpenWhenReady(string path)
    {
        _pendingOpen = path;
        OpenPending();
    }

    private void OpenPending()
    {
        if (_pendingOpen is { } pending && _session is not null && VideoSurface.IsPlayerReady)
        {
            _pendingOpen = null;
            // Another file opened during the restart replaces the one that was playing, without its position.
            PlaybackCarryOver? carry = _carryOver?.Path == pending ? _carryOver : null;
            _carryOver = null;
            if (carry?.OriginalWebLink is { } originalLink && WebLink.IsValid(originalLink))
            {
                StartResolvingStream(originalLink, carry);
            }
            else
            {
                Open(pending, carry);
            }
        }
    }

    /// <summary>
    /// Replaces the engine with one built from the current settings (Restart engine in Settings), so restart-only
    /// settings apply without relaunching. The open file comes back where it was, with its pause, volume, speed and
    /// loop, and the tracks and delays picked in the tracks flyout.
    /// </summary>
    public void RestartEngine()
    {
        if (_session is { } session)
        {
            PlayerViewModel vm = session.ViewModel;
            string? path = _pendingOpen ?? _currentPath;
            bool playing = HasVideo && _pendingOpen is null;
            string? webLink = _currentWebLink;
            _carryOver = new PlaybackCarryOver(
                path, playing ? vm.Position : null, playing && vm.IsPaused, vm.Volume, vm.Speed, vm.IsLooping, _trackChoices, webLink);
            _pendingOpen = path;
            SaveResumePosition(background: true);
            session.ViewModel.PropertyChanged -= OnViewModelChanged;
            session.FileEnded -= OnFileEnded;
            session.Dispose();
            _session = null;
        }
        StartEngine(App.Settings.Current);
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

    /// <param name="link">The link this stream was resolved from, or null for a file.</param>
    private void Open(string path, PlaybackCarryOver? carry = null, OpenedLink? link = null)
    {
        if (_session is not { } session)
        {
            return;
        }
        EndTour(); // the user has gone on to watch something
        SaveResumePosition(background: true);
        // A link is remembered by its page address: the stream address expires within hours.
        string resumeKey = link?.WebLink ?? path;
        double? startAt = carry?.Position ?? (App.Settings.Current.ResumePlayback ? App.Resume.TryGet(resumeKey) : null);
        _currentPath = path;
        _currentWebLink = link?.WebLink;
        _streamTitle = link?.Title;
        RefreshNeighbours(path, isLink: link is not null);
        ResetChapters(link?.Chapters);
        ResetLinkSubtitles(link?.Subtitles, carry);
        ResetQuality(link);
        // A restart carries the position over by itself; only a saved position is worth announcing.
        ResetResumeOffer(carry is null ? startAt : null, resumeKey);
        string headerFields = link?.HttpHeaders is { Count: > 0 } headers
            ? string.Join(",", headers.Select(kv => string.Concat(kv.Key, ": ", kv.Value)))
            : string.Empty;
        string fileOptions = MpvLoadOptions.ForStream(link?.AudioUrl);
        _fitWindowPending = carry is null; // after a restart the window keeps the size it has
        _durationRecorded = false;
        _trackChoices = carry?.Tracks; // another file starts on mpv's own picks
        _restoreTracks = carry?.Tracks;
        ApplyVideoForOpen(afterRestart: carry is not null);
        TrySend(() =>
        {
            if (carry is { Paused: true })
            {
                session.Pause(); // a new core starts unpaused
            }
            session.Context.SetProperty("http-header-fields", headerFields); // a file or another site must not inherit them
            session.Open(path, startAt, fileOptions);
        });
        if (link is null)
        {
            string name = Path.GetFileName(path);
            _ = Task.Run(() => App.Recents.Add(path, name, DateTimeOffset.Now));
        }
        Render();
    }

    /// <summary>Remembers where the current file is, for the next time it is opened (5.10).</summary>
    private void SaveResumePosition(bool background)
    {
        if ((_currentWebLink ?? _currentPath) is not { } path || _session?.ViewModel is not { } vm || !HasVideo || !App.Settings.Current.ResumePlayback)
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

    private string? _lastDecoder;

    private void OnToastRequested(ToastRequest request) => _toasts?.Show(request);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        PlayerViewModel vm = _session!.ViewModel;
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.Position):
                OnChapterSkipPosition(vm);
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
                RestoreTracksOnceLoaded();
                LoadChaptersOnceLoaded();
                AddLinkSubtitlesOnceLoaded();
                ShowResumeOfferOnceLoaded();
                EndQualitySwitchOnceLoaded();
                Render();
                break;
            case nameof(PlayerViewModel.IsPaused):
                Render();
                break;
            case nameof(PlayerViewModel.Decoder):
                if (HardwareDecodeFallback.Occurred(_lastDecoder, vm.Decoder))
                {
                    _toasts?.Show("hwdec-failed", AppStrings.Get("Toast_HwDecFailed"), ToastTone.Failure,
                        AppStrings.Get("Toast_Settings"), () => SetSettingsShown(true));
                }
                _lastDecoder = vm.Decoder;
                break;
            case nameof(PlayerViewModel.State) when _diagnosticsShown:
                Diagnostics.Update(vm.State);
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
        if (!_chromeShown && (Osd?.Visibility != Visibility.Visible))
        {
            return;
        }
        DurationText.Text = Timecode.FormatClock(vm.Duration, vm.Duration);
        SyncClipRange();
        UpdatePipSeek(vm);
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
        // No tooltip: the hover flyout is the label, and a tooltip would open across its slider.
        string name = AppStrings.Get(muted ? "Transport_Unmute" : "Transport_Mute");
        int shortcut = name.LastIndexOf(" (", StringComparison.Ordinal);
        AutomationProperties.SetName(VolumeButton, shortcut > 0 ? name[..shortcut] : name);
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

    private void OnDiagnosticsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ArePlayerKeysPaused)
        {
            return;
        }

        args.Handled = true;
        ToggleDiagnostics();
    }

    private void ToggleDiagnostics() => SetDiagnosticsShown(!_diagnosticsShown);

    /// <summary>The panel drops in from the top edge it sits on and rises back out.</summary>
    private void SetDiagnosticsShown(bool show)
    {
        _diagnosticsShown = show;
        if (show)
        {
            LeavePictureInPicture();
        }
        if (show && _session is { } session)
        {
            Diagnostics.Update(session.ViewModel.State);
        }
        if (_fader is null)
        {
            Diagnostics.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (show)
        {
            _fader.Enter(Diagnostics, ChromeFader.FromAbove);
        }
        else
        {
            _fader.Exit(Diagnostics, ChromeFader.FromAbove);
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

    private void OnVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsPictureInPicture)
        {
            LeavePictureInPicture();
        }
        else
        {
            ToggleFullScreen();
        }
    }

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
        if (EndTour() || CloseRail())
        {
            args.Handled = true;
        }
        else if (IsOpenUrlShown)
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
        else if (_diagnosticsShown)
        {
            args.Handled = true;
            SetDiagnosticsShown(false);
        }
        else if (_host is { IsFullScreen: true } host)
        {
            args.Handled = true;
            host.SetFullScreen(false);
        }
    }

    private void OnAnyKeyDown(object sender, KeyRoutedEventArgs e) => NotifyActivity();

    /// <summary>Player keys stand down while the Open URL field, settings or the tour has the keyboard.</summary>
    private bool ArePlayerKeysPaused => IsOpenUrlShown || _settingsShown || _tour.IsActive;

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
        if (shown)
        {
            EndTour(); // the gear stays clickable during the tour
        }
        _settingsShown = shown;
        if (shown)
        {
            SettingsHost.Child ??= new SettingsView();
            LeavePictureInPicture();
            _host?.SetFullScreen(false);
        }
        if (_fader is null)
        {
            SettingsHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (shown)
        {
            _fader.Enter(SettingsHost, ChromeFader.FromBelow);
        }
        else
        {
            _fader.Exit(SettingsHost, ChromeFader.FromBelow); // fades off over the player, which is already back
        }
        SettingsBackButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
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
    private async void ShowOpenUrl(string? prefill = null)
    {
        if (_session is null || _openUrl.Stage != OpenUrlStage.Closed)
        {
            return;
        }
        string? clipboard = prefill;
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

        EndTour();
        LeavePictureInPicture();
        _openUrl.Open(clipboard);
        OpenUrlBox.Text = _openUrl.Text;
        if (_fader is null)
        {
            OpenUrlOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            // The scrim only fades; the dialog rises with it.
            _fader.Enter(OpenUrlOverlay, null);
            _fader.Slide(OpenUrlCard, true, ChromeFader.FromBelow);
        }
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
            string? link = _openUrl.Link;
            HideOpenUrlDialog();
            if (link is not null)
            {
                StartResolvingStream(link);
            }
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
        if (_fader is null)
        {
            OpenUrlOverlay.Visibility = Visibility.Collapsed;
        }
        else
        {
            _fader.Exit(OpenUrlOverlay, null);
            _fader.Slide(OpenUrlCard, false, ChromeFader.FromBelow);
        }
        _chrome.SetHold(ChromeHolds.Dialog, false);
        ApplyChrome();
        Focus(FocusState.Programmatic); // playback keys back on the page
    }

    private void OnStreamCancelClick(object sender, RoutedEventArgs e)
    {
        _streamResolutionCts?.Cancel();
        _streamResolutionCts = null;
        _openUrl.Close();
        _streamFailure = null;
        ArmOpenUrlTimer();
        Render();
    }

    private async void StartResolvingStream(string link, PlaybackCarryOver? carry = null)
    {
        if (_session is null)
        {
            return;
        }

        EndTour();
        CancelQualitySwitch();
        _streamResolutionCts?.Cancel();
        var cts = new CancellationTokenSource();
        _streamResolutionCts = cts;

        _streamFailure = null;
        _failedStreamLink = null;

        _openUrl.Open(link);
        _openUrl.Submit();
        ArmOpenUrlTimer();
        Render();

        // A remembered size is asked for from the start; yt-dlp lists every size whatever was asked for.
        string savedQuality = App.Settings.Current.StreamQualityChoice;
        StreamResolutionOptions options = StreamOptions(RememberedQualitySelector());

        try
        {
            ResolvedStream resolved = await _streamResolver.ResolveAsync(link, options, cts.Token);
            if (cts.IsCancellationRequested || !_isLoaded)
            {
                return;
            }

            _openUrl.Close();
            ArmOpenUrlTimer();

            string displayName = string.IsNullOrWhiteSpace(resolved.Title) ? WebLink.Host(link) ?? link : resolved.Title;
            IReadOnlyList<QualityOption> qualities = QualityOptions.From(resolved.Formats);
            Open(resolved.StreamUrl, carry, new OpenedLink(
                link, displayName, resolved.HttpHeaders, resolved.AudioUrl, resolved.Chapters, resolved.Subtitles,
                qualities, QualityOptions.Resolve(savedQuality, qualities)));

            _ = Task.Run(() =>
            {
                App.Recents.Add(link, displayName, DateTimeOffset.Now);
                if (resolved.DurationSeconds is { } dur && dur > 0)
                {
                    App.Recents.SetDuration(link, dur);
                }
            });

            Render();
        }
        catch (OperationCanceledException)
        {
            if (!cts.IsCancellationRequested)
            {
                HandleStreamFailure(link, StreamFailureReason.Offline, "Stream resolution timed out.");
            }
        }
        catch (StreamResolutionException ex)
        {
            HandleStreamFailure(link, ex.Reason, ex.Message);
        }
        catch (Exception ex)
        {
            HandleStreamFailure(link, StreamFailureReason.Unknown, ex.Message);
        }
        finally
        {
            if (_streamResolutionCts == cts)
            {
                _streamResolutionCts = null;
            }
        }
    }

    private void HandleStreamFailure(string link, StreamFailureReason reason, string detail)
    {
        if (!_isLoaded)
        {
            return;
        }
        _openUrl.Close();
        ArmOpenUrlTimer();

        (string title, string subtitle) = reason switch
        {
            StreamFailureReason.PrivateVideo => (
                AppStrings.Get("Stream_UnavailableTitle"),
                AppStrings.Get("Stream_UnavailablePrivate")),

            StreamFailureReason.Offline => (
                AppStrings.Get("Stream_OfflineTitle"),
                AppStrings.Get("Stream_OfflineMessage")),

            StreamFailureReason.SignInRequired => (
                AppStrings.Get("Stream_SignInRequiredTitle"),
                AppStrings.Get("Stream_SignInRequiredMessage")),

            StreamFailureReason.RegionBlocked => (
                AppStrings.Get("Stream_RegionBlockedTitle"),
                AppStrings.Get("Stream_RegionBlockedMessage")),

            StreamFailureReason.LiveNotStarted => (
                AppStrings.Get("Stream_LiveNotStartedTitle"),
                AppStrings.Get("Stream_LiveNotStartedMessage")),

            StreamFailureReason.SiteNotSupported => (
                AppStrings.Get("Stream_SiteNotSupportedTitle"),
                AppStrings.Get("Stream_SiteNotSupportedMessage")),

            StreamFailureReason.YtDlpMissing => (
                AppStrings.Get("Stream_UnavailableTitle"),
                AppStrings.Get("Settings_Streaming_YtDlpMissing")),

            _ => (
                AppStrings.Get("Stream_UnavailableTitle"),
                string.IsNullOrWhiteSpace(detail) ? AppStrings.Get("Stream_UnavailableTitle") : detail),
        };

        _streamFailure = new StreamFailureState(reason, title, subtitle, link);
        _failedStreamLink = link;
        Render();
    }

    /// <summary>Downloads yt-dlp from the failure screen and, once it is in place, plays the link that was waiting.</summary>
    private async void OnStreamInstallClick(object sender, RoutedEventArgs e)
    {
        if (_failedStreamLink is not { } link)
        {
            return;
        }
        StreamInstallButton.IsEnabled = false;
        StreamInstallLabel.Text = AppStrings.Format("YtDlp_InstallProgress", 0);
        var progress = new Progress<double>(fraction =>
            StreamInstallLabel.Text = AppStrings.Format("YtDlp_InstallProgress", (int)(fraction * 100)));
        (YtDlpInstallOutcome outcome, string? version) = await YtDlpManager.InstallLatestAsync(onlyIfNewer: false, progress);
        StreamInstallButton.IsEnabled = true;
        if (!_isLoaded || _failedStreamLink != link)
        {
            return;
        }
        if (outcome == YtDlpInstallOutcome.Installed)
        {
            AppToasts.Show("ytdlp-update", AppStrings.Format("Toast_YtDlpInstalled", version ?? ""), ToastTone.Success);
            StartResolvingStream(link);
            return;
        }
        // The screen stays up with the reason and the same button, now reading Retry.
        StreamInstallLabel.Text = AppStrings.Get("Stream_Retry");
        if (_streamFailure is { } failure)
        {
            _streamFailure = failure with { Subtitle = AppStrings.Get(YtDlpManager.FailureKey(outcome)) };
        }
        Render();
    }

    private void OnStreamRetryClick(object sender, RoutedEventArgs e)
    {
        if (_failedStreamLink is { } link)
        {
            _streamFailure = null;
            StartResolvingStream(link);
        }
    }

    private void OnStreamEditLinkClick(object sender, RoutedEventArgs e)
    {
        string? linkToEdit = _failedStreamLink;
        _streamFailure = null;
        _failedStreamLink = null;
        _openUrl.Close();
        ShowOpenUrl(linkToEdit);
    }

    private async void OnStreamOpenInBrowserClick(object sender, RoutedEventArgs e)
    {
        if (_failedStreamLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out Uri? uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnStreamFailureCancelClick(object sender, RoutedEventArgs e)
    {
        _streamFailure = null;
        _failedStreamLink = null;
        _openUrl.Close();
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
            _ => (AppStrings.Get("OpenUrl_Hint"), (Tone?)null),
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

    private void OnRecentOpenRequested(object? sender, EventArgs e)
    {
        if (sender is RecentRow row)
        {
            OpenRecent(row.Item);
        }
    }

    /// <summary>
    /// Opens a recents entry from the empty state or the Recent menu. A file that went missing since the list was
    /// drawn turns into the missing row instead of opening.
    /// </summary>
    private async void OpenRecent(RecentItem recent)
    {
        if (_session is null)
        {
            return;
        }
        if (recent.IsLink)
        {
            StartResolvingStream(recent.Location);
            return;
        }
        string path = recent.Location;
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
        // Back on the mouse: a transport button left with Tab focus must not keep the card up for good.
        _chrome.SetHold(ChromeHolds.KeyboardFocus, false);
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
            if (visible && _session?.ViewModel is { } clockVm)
            {
                UpdateClock(clockVm);
            }
            _fader.Fade(TopScrim, visible);
            // The gear, Back and the system caption buttons stay put; only the name fades.
            _fader.Fade(TitleText, visible);
            _fader.Fade(PipChrome, visible);
            PipChrome.IsHitTestVisible = visible;
            _fader.Fade(Osd, visible);
            VideoSurface.FadeBackdrop(BackdropSlot.Bar, visible, _fader.DurationMilliseconds(visible));
            Osd.IsHitTestVisible = visible;
            _fader.Fade(ClipLayer, visible);
            ClipLayer.IsHitTestVisible = visible;
        }
        SyncBar();
        SyncSubtitleLift();
        SyncRail(applyChrome: false);
        UpdateCursor();
        UpdateToastPlacement();
        // Toasts wait for the full window: in the overlay they would cover most of the picture.
        _toasts?.SetBlocked((_chrome.Holds & (ChromeHolds.Flyout | ChromeHolds.Dialog | ChromeHolds.Tour)) != 0 || IsPictureInPicture);
        SyncSkipChapter();

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

    /// <summary>The toast shares the spot above the card with the export chip and clip drawer, and sits above them.</summary>
    private void UpdateToastPlacement()
    {
        bool controls = _chrome.IsVisible && Osd.Visibility == Visibility.Visible;
        double chip = ClipLayer.Visibility == Visibility.Visible ? ClipLayer.ActualHeight : 0;
        _toasts?.SetControlsShown(controls, Osd.Height, chip);
        PlaceSkipChapter(controls, chip);
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
        (int width, int height) = DisplayedVideoSize();
        double hostWidth = VideoHost.ActualWidth;
        double hostHeight = VideoHost.ActualHeight;
        if (width <= 0 || height <= 0 || hostWidth <= 0 || hostHeight <= 0)
        {
            VideoSurface.Width = double.NaN;
            VideoSurface.Height = double.NaN;
            return;
        }
        double scale = Math.Min(hostWidth / width, hostHeight / height);
        VideoSurface.Width = SnapToHost(Math.Round(width * scale), hostWidth);
        VideoSurface.Height = SnapToHost(Math.Round(height * scale), hostHeight);
    }

    /// <summary>
    /// The window keeps the video's shape only to the whole pixel, so a fitted side can come out a pixel short and
    /// leave a hairline of backdrop at the edge. Within that, it takes the host's full size; the stretch can't be seen.
    /// </summary>
    private static double SnapToHost(double fitted, double host)
    {
        const double SnapDips = 2;
        return host - fitted <= SnapDips ? host : fitted;
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
        (int width, int height) = DisplayedVideoSize();
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
        PlayerViewModel? vm = _session?.ViewModel;
        bool pip = IsPictureInPicture;
        if (pip && PictureInPicture.EndsOn(vm?.Status ?? PlaybackStatus.Idle))
        {
            LeavePictureInPicture(); // raises PictureInPictureChanged, which renders again
            return;
        }
        // Between two files of a folder the overlay keeps its look rather than flashing the home screen; so does a
        // link switching quality while its new stream loads.
        bool hasVideo = (HasVideo || pip || _qualitySwitching) && !_settingsShown;
        bool fullScreen = _host?.IsFullScreen == true;

        // Video, scrims and controls.
        VideoSurface.Opacity = hasVideo ? 1 : 0;
        VideoHost.Background = (fullScreen || pip) && hasVideo
            ? (Brush)Application.Current.Resources["AetherSurfaceLetterboxBrush"]
            : null;
        HomeView.Visibility = !_settingsShown && (!hasVideo || _dragging) ? Visibility.Visible : Visibility.Collapsed;
        DropScrim.Visibility = hasVideo && _dragging ? Visibility.Visible : Visibility.Collapsed;
        DropOutline.Visibility = _dragging ? Visibility.Visible : Visibility.Collapsed;
        Osd.Visibility = hasVideo && !pip ? Visibility.Visible : Visibility.Collapsed;
        ClipLayer.Visibility = Osd.Visibility;
        PipChrome.Visibility = hasVideo && pip ? Visibility.Visible : Visibility.Collapsed;
        // ADR-0013: no blur in picture in picture; there is no card to blur behind.
        VideoSurface.SetBackdropActive(BackdropSlot.Bar, hasVideo && !pip);
        VideoSurface.SetBackdropActive(BackdropSlot.Sidebar, hasVideo && !pip);
        PostBackdropRegion();
        TitleBar.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;
        PlaceRail();
        TopScrim.Visibility = hasVideo && !fullScreen ? Visibility.Visible : Visibility.Collapsed;
        string? fileName = _streamTitle ?? (HasVideo || vm?.Status == PlaybackStatus.Loading ? Path.GetFileName(_currentPath) : null);
        TitleBadgeText.Text = fileName ?? AppStrings.Get("App_Badge");
        TitleText.Visibility = pip ? Visibility.Collapsed : Visibility.Visible;
        SettingsButton.Visibility = pip || _settingsShown ? Visibility.Collapsed : Visibility.Visible;
        PipBackButton.Visibility = pip ? Visibility.Visible : Visibility.Collapsed;
        // Settings names itself beside its back arrow (6.1) in place of the app name and badge.
        TitleAppName.Text = AppStrings.Get(_settingsShown ? "Settings_Title" : "App_Name");
        TitleBadgeText.Visibility = _settingsShown ? Visibility.Collapsed : Visibility.Visible;
        TitleSeparator.Visibility = fileName is not null && !_settingsShown ? Visibility.Visible : Visibility.Collapsed;

        (string glyph, string label) = TransportState.PlayButton(vm?.Status ?? PlaybackStatus.Idle) switch
        {
            PlayButtonAction.Pause => (PauseGlyph, Tip("Transport_Pause", ShortcutAction.PlayPause)),
            PlayButtonAction.Replay => (ReplayGlyph, Tip("Transport_Replay", ShortcutAction.PlayPause)),
            _ => (PlayGlyph, Tip("Transport_Play", ShortcutAction.PlayPause)),
        };
        PlayPauseButton.Content = glyph;
        ToolTipService.SetToolTip(PlayPauseButton, label);
        AutomationProperties.SetName(PlayPauseButton, label);
        PipPlayPauseButton.Content = glyph;
        ToolTipService.SetToolTip(PipPlayPauseButton, label);
        AutomationProperties.SetName(PipPlayPauseButton, label);
        FullScreenButton.Content = fullScreen ? ExitFullScreenGlyph : FullScreenGlyph;
        ToolTipService.SetToolTip(FullScreenButton, fullScreen ? AppStrings.Get("Transport_ExitFullScreen") : Tip("Transport_FullScreen", ShortcutAction.FullScreen));

        UpdateTaskbar(fileName);
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

        if (_streamFailure is { } streamFail)
        {
            SetHome(streamFail.Title, streamFail.Subtitle, Tone.Warning, engine);
        }
        else if (_dragging)
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
            SetHome(AppStrings.Get("EmptyState_Title"), HomeFilesSubtitle(), null, engine);
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
            SetHome(AppStrings.Get("EmptyState_Title"), HomeFilesSubtitle(), null, engine);
        }

        bool streamFailed = _streamFailure is not null && !_dragging;
        StartupProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        OpenActions.Visibility = failed || _dragging || streaming || streamFailed ? Visibility.Collapsed : Visibility.Visible;
        OpenFileButton.IsEnabled = _session is not null;
        OpenUrlButton.IsEnabled = _session is not null;
        bool showStream = streaming && !_dragging;
        HeroGlyph.Visibility = showStream ? Visibility.Collapsed : Visibility.Visible;
        StreamProgress.Visibility = showStream ? Visibility.Visible : Visibility.Collapsed;
        StreamProgress.IsActive = showStream;
        StreamCancelButton.Visibility = showStream ? Visibility.Visible : Visibility.Collapsed;
        StreamFailureActions.Visibility = streamFailed ? Visibility.Visible : Visibility.Collapsed;
        // Without yt-dlp a plain Retry cannot succeed, so Install takes its place.
        bool needsInstall = _streamFailure?.Reason == StreamFailureReason.YtDlpMissing;
        StreamInstallButton.Visibility = needsInstall ? Visibility.Visible : Visibility.Collapsed;
        StreamRetryButton.Visibility = needsInstall ? Visibility.Collapsed : Visibility.Visible;
        // Recents belong to the plain empty state: hidden until there is history (2.10).
        bool idle = _startup == StartupPhase.Ready && !_dragging && !streaming && !streamFailed && vm?.Status != PlaybackStatus.Loading;
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
