using System.ComponentModel;
using Aether.Core.Interop;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether_App.Controls;
using Aether_App.Playback;
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
    private bool _fitWindowPending; // the next video size resizes the window (set per opened file)
    private bool _windowSyncQueued;
    private bool _dragging;
    private string? _dragPath;
    private bool _dragSupported;

    private bool _windowActive = true;
    private bool _pointerInside;
    private Point _lastPointer = new(double.NaN, double.NaN);
    private bool _updatingVolume;

    public MainPage()
    {
        InitializeComponent();
        IsTabStop = true; // receives the frame-step and playback keys
        ApplyStaticStrings();
        _chrome.SetHold(ChromeHolds.NotPlaying, true);
        AddHandler(KeyDownEvent, new KeyEventHandler(OnAnyKeyDown), handledEventsToo: true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Labels that never change, from the strings file (the XAML text is only a design-time stand-in).</summary>
    private void ApplyStaticStrings()
    {
        TitleAppName.Text = AppStrings.Get("App_Name");
        ToolTipService.SetToolTip(SettingsButton, AppStrings.Get("TitleBar_SettingsTooltip"));
        AutomationProperties.SetName(SettingsButton, AppStrings.Get("Settings_Title"));
        SearchedKey.Text = AppStrings.Get("EmptyState_DetailsSearched");
        ErrorKey.Text = AppStrings.Get("EmptyState_DetailsError");
        RetryButton.Content = AppStrings.Get("Shell_EngineFailedRetry");
        OpenFolderButton.Content = AppStrings.Get("Shell_EngineFailedOpenFolder");
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
            }
            _host = value;
            if (_host is not null)
            {
                _host.FullScreenChanged += OnFullScreenChanged;
            }
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
        }
        if (_session is null)
        {
            StartEngine();
        }
        Render();
        Focus(FocusState.Programmatic);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _startupVersion++; // a startup still running disposes its engine when it returns
        _chromeTimer?.Stop();
        CursorVisibility.Show();
        if (_session is { } session)
        {
            session.ViewModel.PropertyChanged -= OnViewModelChanged;
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
            await EngineStartup.RunAsync(NativeLibraries.IsMpvAvailable, () => MpvContext.Create());

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
                _startup = StartupPhase.Ready;
                SyncVolume();
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

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            StartEngine();
        }
    }

    private async void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchFolderPathAsync(LibrarySearchDirectory);

    private static string LibrarySearchDirectory => NativeLibraries.SearchDirectory ?? AppContext.BaseDirectory;

    // ---- Playback ----

    private void Open(string path)
    {
        if (_session is not { } session)
        {
            return;
        }
        _currentPath = path;
        _fitWindowPending = true;
        TrySend(() => session.Open(path));
        Render();
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
                PositionText.Text = Timecode.FormatClock(vm.Position, vm.Duration);
                break;
            case nameof(PlayerViewModel.Duration):
                // Both change shape together when the clip is an hour or more.
                PositionText.Text = Timecode.FormatClock(vm.Position, vm.Duration);
                DurationText.Text = Timecode.FormatClock(vm.Duration, vm.Duration);
                break;
            case nameof(PlayerViewModel.Volume):
                SyncVolume();
                break;
            case nameof(PlayerViewModel.VideoWidth):
            case nameof(PlayerViewModel.VideoHeight):
                FitVideo();
                QueueWindowSync();
                break;
            case nameof(PlayerViewModel.IsPaused):
            case nameof(PlayerViewModel.Status):
                Render();
                break;
        }
    }

    private void SyncVolume()
    {
        double volume = _session?.ViewModel.Volume ?? 100;
        _updatingVolume = true;
        VolumeSlider.Value = Math.Clamp(volume, 0, 100);
        _updatingVolume = false;
        VolumeIcon.Glyph = volume <= 0 ? MuteGlyph : VolumeGlyph;
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingVolume || _session is not { } session)
        {
            return;
        }
        VolumeIcon.Glyph = e.NewValue <= 0 ? MuteGlyph : VolumeGlyph;
        TrySend(() => session.SetVolume(e.NewValue));
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

    private void OnPlayPauseAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        TogglePause();
    }

    private void OnFullScreenAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleFullScreen();
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_host is { IsFullScreen: true } host)
        {
            args.Handled = true;
            host.SetFullScreen(false);
        }
    }

    private void OnAnyKeyDown(object sender, KeyRoutedEventArgs e) => NotifyActivity();

    // ---- Drag and drop (4.5) ----

    private async void OnDragEnter(object sender, DragEventArgs e)
    {
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
        bool hasVideo = HasVideo;
        bool fullScreen = _host?.IsFullScreen == true;
        PlayerViewModel? vm = _session?.ViewModel;

        // Video, scrims and controls.
        VideoSurface.Opacity = hasVideo ? 1 : 0;
        VideoHost.Background = fullScreen && hasVideo
            ? (Brush)Application.Current.Resources["AetherSurfaceLetterboxBrush"]
            : null;
        HomeView.Visibility = !hasVideo || _dragging ? Visibility.Visible : Visibility.Collapsed;
        DropScrim.Visibility = hasVideo && _dragging ? Visibility.Visible : Visibility.Collapsed;
        DropOutline.Visibility = _dragging ? Visibility.Visible : Visibility.Collapsed;
        Osd.Visibility = hasVideo ? Visibility.Visible : Visibility.Collapsed;
        TitleBar.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;
        TopScrim.Visibility = hasVideo && !fullScreen ? Visibility.Visible : Visibility.Collapsed;
        TitleBadgeText.Text = hasVideo || vm?.Status == PlaybackStatus.Loading
            ? Path.GetFileName(_currentPath) ?? AppStrings.Get("App_Badge")
            : AppStrings.Get("App_Badge");

        bool showPlay = vm is null || vm.IsPaused || vm.Status == PlaybackStatus.Ended;
        PlayPauseButton.Content = showPlay ? PlayGlyph : PauseGlyph;
        ToolTipService.SetToolTip(PlayPauseButton, AppStrings.Get(showPlay ? "Transport_Play" : "Transport_Pause"));
        FullScreenButton.Content = fullScreen ? ExitFullScreenGlyph : FullScreenGlyph;
        ToolTipService.SetToolTip(FullScreenButton, AppStrings.Get(fullScreen ? "Transport_ExitFullScreen" : "Transport_FullScreen"));

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
        bool highlightGlyph = false;
        double glyphOpacity = 1;

        // The status line always describes the engine; the renderer has one name, everywhere.
        string renderer = AppStrings.Get("EmptyState_Renderer");
        (Tone Tone, string Label, string? Detail) engine = _startup switch
        {
            StartupPhase.Starting => (Tone.Neutral, AppStrings.Get("Shell_StartingEngine"), AppStrings.Get("Shell_LoadingLibmpv")),
            StartupPhase.Ready => (Tone.Success, AppStrings.Get("EmptyState_EngineReady"), renderer),
            _ => (Tone.Warning, AppStrings.Get("Shell_EngineNotRunning"), renderer),
        };

        if (_dragging)
        {
            highlightGlyph = _dragSupported;
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
        else if (_startup == StartupPhase.Starting)
        {
            busy = true;
            SetHome(AppStrings.Get("EmptyState_Title"), AppStrings.Get("EmptyState_SubtitleFilesOnly"), null, engine);
        }
        else if (failed)
        {
            glyphOpacity = 0.45;
            bool missing = _startup == StartupPhase.MissingBinary;
            SetHome(
                AppStrings.Get("Shell_EngineFailedTitle"),
                AppStrings.Get(missing ? "Shell_EngineFailedMissingFile" : "Shell_EngineFailedUnavailable"),
                Tone.Warning,
                engine);
            SearchedValue.Text = LibrarySearchDirectory;
            ErrorValue.Text = missing
                ? AppStrings.Get("Shell_EngineFailedReinstall")
                : _startupError ?? AppStrings.Get("Shell_EngineFailedUnknownError");
        }
        else if (vm?.Status == PlaybackStatus.Loading)
        {
            busy = true;
            SetHome(
                AppStrings.Get("Shell_OpeningTitle"),
                Path.GetFileName(_currentPath) ?? string.Empty,
                null,
                (Tone.Neutral, AppStrings.Get("Shell_Loading"), MediaFormats.Label(_currentPath)));
        }
        else
        {
            SetHome(AppStrings.Get("EmptyState_Title"), AppStrings.Get("EmptyState_SubtitleFilesOnly"), null, engine);
        }

        StartupProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        DetailsPanel.Visibility = failed && !_dragging ? Visibility.Visible : Visibility.Collapsed;
        Visibility searched = _startup == StartupPhase.MissingBinary ? Visibility.Visible : Visibility.Collapsed;
        SearchedKey.Visibility = searched;
        SearchedValue.Visibility = searched;

        var glyphBrush = new SolidColorBrush(highlightGlyph ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
        GlyphFrame.BorderBrush = glyphBrush;
        GlyphSprocketLeft.Stroke = glyphBrush;
        GlyphSprocketRight.Stroke = glyphBrush;
        GlyphPlay.Foreground = highlightGlyph ? glyphBrush : new SolidColorBrush(Windows.UI.Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
        HomeGlyph.Opacity = glyphOpacity;
    }

    /// <param name="titleTone">Dot beside the title (failures), or null for none.</param>
    /// <param name="status">Dot, label and muted detail of the status line. Colour stays in the dots; the text is white.</param>
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
        StatusDetail.Text = status.Detail is null ? string.Empty : "· " + status.Detail;
    }

    private static Brush ToneBrush(Tone tone) => (Brush)Application.Current.Resources[tone switch
    {
        Tone.Success => "AetherStatusReadyBrush",
        Tone.Warning => "AetherStatusWarningBrush",
        _ => "AetherStatusNeutralBrush",
    }];
}
