using Aether.Core.Interop.Windowing;
using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Windowing;
using Aether_App.Controls;
using Aether_App.Playback;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Aether_App;

/// <summary>What the page needs from the window it sits in: full screen and the system caption buttons.</summary>
public interface IShellWindow
{
    bool IsFullScreen { get; }

    /// <summary>Raised after the window enters or leaves full screen.</summary>
    event EventHandler? FullScreenChanged;

    /// <summary>Full screen from picture in picture leaves it straight for full screen.</summary>
    void SetFullScreen(bool fullScreen);

    /// <summary>The window is the small always-on-top picture-in-picture overlay (8.1).</summary>
    bool IsPictureInPicture { get; }

    /// <summary>Raised after the window enters or leaves picture in picture.</summary>
    event EventHandler? PictureInPictureChanged;

    /// <summary>
    /// Shrinks the window to the overlay, or returns it to the size, place and full-screen state it had. The
    /// video keeps playing: nothing in the engine stops or restarts (docs/reviews/pass-8-pip-handover-spec.md).
    /// </summary>
    void SetPictureInPicture(bool on);

    /// <summary>Keep the window above others. Saved with the settings; applies again after full screen.</summary>
    bool IsAlwaysOnTop { get; }

    /// <summary>Raised after <see cref="IsAlwaysOnTop"/> changes.</summary>
    event EventHandler? AlwaysOnTopChanged;

    void ToggleAlwaysOnTop();

    /// <summary>
    /// Raised when the pointer moves over the title strip or the caption buttons. Those are
    /// non-client regions, so the page gets no pointer events there and could not wake the controls.
    /// </summary>
    event EventHandler? TitleBarPointerMoved;

    /// <summary>Resizes a restored window around a video of this display size (no-op when maximized or full screen).</summary>
    void FitToVideo(int videoWidth, int videoHeight);

    /// <summary>Client width / height to keep while the user drags the edges; 0 lets the window resize freely.</summary>
    void SetVideoAspect(double aspect);

    /// <summary>
    /// Names the taskbar preview after the open file (null for the app name) and sets the buttons under it
    /// (11.14). Cheap to call often: nothing changes when the values are the same.
    /// </summary>
    void SetTaskbar(string? fileName, in TaskbarControls controls);

    /// <summary>Raised when a button under the taskbar preview is pressed.</summary>
    event EventHandler<TaskbarCommand>? TaskbarCommandRequested;
}

/// <summary>
/// Borderless shell: content extends into the title bar, Mica Alt shows through, and the page's own
/// title bar strip is the drag region. The system still draws the caption buttons.
/// </summary>
public sealed partial class MainWindow : Window, IShellWindow
{
    // The smallest client area (DIPs) that still fits the control bar and the empty-state buttons.
    private const double MinClientWidth = 640;
    private const double MinClientHeight = 400;

    // A video opens at one video pixel per screen pixel, within this share of the work area.
    private const double FitMaxWorkAreaFraction = 0.9;

    // The caption colours live in the design tokens so the title-bar gear and Back read the same values.
    private Color _captionForeground;
    private Color _captionInactiveForeground;
    private Color _captionHover;
    private Color _captionPressed;
    private readonly AspectRatioLock? _aspectLock;
    private readonly TaskbarThumbButtons? _taskbarButtons;
    private readonly string _playTip = AppStrings.Get("Taskbar_Play");
    private readonly string _pauseTip = AppStrings.Get("Taskbar_Pause");
    private readonly string _replayTip = AppStrings.Get("Taskbar_Replay");
    private Windows.Foundation.Point _lastNonClientPointer = new(double.NaN, double.NaN);
    private RectInt32? _normalBounds; // where the window sits when neither maximised nor full screen
    private bool _maximized;
    private bool _keepRestoredSize; // the first video does not resize a window the user's last size was restored to
    private readonly PictureInPicture _pip = new();
    private readonly UISettings _uiSettings = new(); // held so ColorValuesChanged keeps firing
    private WindowRect? _pipBounds; // where the overlay was last, for the next time it opens
    private double _videoAspect;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppStrings.Get("App_Name");

        var resources = Application.Current.Resources;
        _captionForeground = (Color)resources["AetherCaptionGlyphColor"];
        _captionInactiveForeground = (Color)resources["AetherCaptionGlyphInactiveColor"];
        _captionHover = (Color)resources["AetherCaptionHoverColor"];
        _captionPressed = (Color)resources["AetherCaptionPressedColor"];

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Shell.TitleBarDragRegion);
        ApplyTaskbarIcon();
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        ApplyTheme();
        App.Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => App.Settings.Changed -= OnSettingsChanged;

        Shell.ActualThemeChanged += (_, _) =>
        {
            ApplyTheme();
        App.Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => App.Settings.Changed -= OnSettingsChanged;
            ApplyTaskbarIcon();
        };


        try
        {
            _aspectLock = new AspectRatioLock(WinRT.Interop.WindowNative.GetWindowHandle(this))
            {
                MinimumClientSize = (MinClientWidth, MinClientHeight),
            };
        }
        catch (InvalidOperationException)
        {
            _aspectLock = null; // the window still works, just without the drag-resize aspect lock and minimum size
        }

        DisplaySync = new DisplayRefreshSync(WinRT.Interop.WindowNative.GetWindowHandle(this));

        try
        {
            _taskbarButtons = new TaskbarThumbButtons(
                WinRT.Interop.WindowNative.GetWindowHandle(this),
                AppStrings.Get("Taskbar_Previous"),
                AppStrings.Get("Taskbar_Next"),
                IsLightTaskbar(),
                OnTaskbarButton);
        }
        catch (InvalidOperationException)
        {
            _taskbarButtons = null; // the preview still shows, just without the buttons
        }

        InputNonClientPointerSource nonClient = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        nonClient.PointerEntered += OnNonClientPointerMoved;
        nonClient.PointerMoved += OnNonClientPointerMoved;

        App.Settings.Changed += OnSettingChanged;
        ApplyAlwaysOnTop();
        _uiSettings.ColorValuesChanged += OnSystemColoursChanged;

        Shell.Host = this;
        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _taskbarButtons?.Dispose(); // before the display sync: it sits on top of that subclass
            DisplaySync.Dispose(); // before the aspect lock: it sits on top of that subclass
            _aspectLock?.Dispose();
            App.Settings.Changed -= OnSettingChanged;
            _uiSettings.ColorValuesChanged -= OnSystemColoursChanged;
        };
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnAppWindowClosing;
        TrackNormalBounds();
    }

    /// <summary>The refresh rate of the monitor this window is on, for the playback engine.</summary>
    internal DisplayRefreshSync DisplaySync { get; }

    /// <summary>Opens this file as soon as the engine is ready (launch from a file or the command line).</summary>
    public void OpenWhenReady(string path) => Shell.OpenWhenReady(path);

    /// <summary>Starts the playback engine again with the current settings, keeping the open file.</summary>
    public void RestartEngine() => Shell.RestartEngine();

    public void ShowTour() => Shell.ShowTour();

    /// <summary>
    /// Moves and resizes the window to where it was left, on a monitor that is still connected, and maximises it
    /// again if it was. Call before the first <see cref="Window.Activate"/>.
    /// </summary>
    public void RestorePlacement(SavedWindow saved)
    {
        var areas = DisplayArea.FindAll();
        var workAreas = new WindowRect[areas.Count];
        for (int i = 0; i < workAreas.Length; i++)
        {
            workAreas[i] = ToWindowRect(areas[i].WorkArea);
        }
        WindowRect fitted = WindowPlacement.Fit(saved.Bounds, workAreas, ToWindowRect(DisplayArea.Primary.WorkArea));
        AppWindow.MoveAndResize(new RectInt32(fitted.X, fitted.Y, fitted.Width, fitted.Height));
        if (saved.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
        _keepRestoredSize = true;
        _pipBounds = saved.PictureInPicture;
        TrackNormalBounds();
    }

    /// <summary>Brings the window to the front and restores it if minimised; for a file handed over by a second launch.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        AppWindow.Show();
        Activate();
        ForegroundWindow.Bring(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private static WindowRect ToWindowRect(RectInt32 rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    // Minimised windows report an off-screen position and full screen is not the size the user chose, so those
    // states keep the last normal bounds.
    private void TrackNormalBounds()
    {
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay)
        {
            // Mid-switch the overlay still has the presenter's default size, not the place it is about to take.
            if (!_pip.IsSwitching)
            {
                TrackPipBounds();
            }
            return;
        }
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }
        switch (presenter.State)
        {
            case OverlappedPresenterState.Restored:
                _maximized = false;
                _normalBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
                break;
            case OverlappedPresenterState.Maximized:
                _maximized = true;
                break;
        }
    }

    private void TrackPipBounds()
    {
        PointInt32 position = AppWindow.Position;
        SizeInt32 size = AppWindow.Size;
        // A minimised overlay sits far off-screen at -32000; that is not a place to come back to.
        if (position.X > -30000 && position.Y > -30000 && size.Width > 0 && size.Height > 0)
        {
            _pipBounds = new WindowRect(position.X, position.Y, size.Width, size.Height);
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Closing the overlay returns to the full window; playback carries on.
        if (_pip.CancelsClose)
        {
            args.Cancel = true;
            LeavePictureInPicture(toFullScreen: false);
            return;
        }
        if (!App.Settings.Current.RememberWindowSize || _normalBounds is not { } bounds)
        {
            return;
        }
        // The process is about to exit, so write now; the file is a few bytes and Save does not throw.
        App.WindowState.Save(new SavedWindow(ToWindowRect(bounds), _maximized, _pipBounds));
    }

    public bool IsFullScreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    public event EventHandler? FullScreenChanged;

    public void SetFullScreen(bool fullScreen)
    {
        if (_pip.IsOn)
        {
            if (fullScreen)
            {
                LeavePictureInPicture(toFullScreen: true);
            }
            return;
        }
        if (fullScreen != IsFullScreen)
        {
            AppWindow.SetPresenter(fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        }
    }

    public bool IsPictureInPicture => _pip.IsOn;

    public event EventHandler? PictureInPictureChanged;

    public void SetPictureInPicture(bool on)
    {
        if (on)
        {
            EnterPictureInPicture();
        }
        else
        {
            LeavePictureInPicture(toFullScreen: false);
        }
    }

    // Order matters (pass-8-pip-handover-spec.md): the work areas are read before the switch, because the overlay
    // presenter reported a different one in the 8.1 spike, and the aspect lock's minimum drops first, because it
    // answers WM_GETMINMAXINFO for every presenter and would hold the overlay at the full window's minimum.
    private void EnterPictureInPicture()
    {
        if (!_pip.TryBeginEnter(hasVideo: true, IsFullScreen))
        {
            return;
        }
        try
        {
            var areas = DisplayArea.FindAll();
            var workAreas = new WindowRect[areas.Count];
            for (int i = 0; i < workAreas.Length; i++)
            {
                workAreas[i] = ToWindowRect(areas[i].WorkArea);
            }
            WindowRect work = ToWindowRect(DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea);
            WindowRect primary = ToWindowRect(DisplayArea.Primary.WorkArea);
            double scale = Shell.XamlRoot?.RasterizationScale ?? 1;

            if (_aspectLock is not null)
            {
                _aspectLock.MinimumClientSize = (PipPlacement.MinWidth, PipPlacement.MinHeight);
            }
            AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);

            // Measured on the overlay itself: full screen has no frame, the overlay has the usual resize border.
            int frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
            int frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
            WindowRect placed = _pipBounds is { } saved
                ? PipPlacement.Remembered(saved, _videoAspect, scale, frameWidth, frameHeight, workAreas, primary)
                : PipPlacement.Initial(work, _videoAspect, scale, frameWidth, frameHeight);
            AppWindow.MoveAndResize(new RectInt32(placed.X, placed.Y, placed.Width, placed.Height));
        }
        finally
        {
            _pip.EndSwitch();
        }
        PictureInPictureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LeavePictureInPicture(bool toFullScreen)
    {
        if (!_pip.TryBeginLeave(toFullScreen, out PictureInPictureReturn target))
        {
            return;
        }
        try
        {
            TrackPipBounds();
            // Windows restores the overlapped window's own size, place and maximised state.
            AppWindow.SetPresenter(target == PictureInPictureReturn.FullScreen
                ? AppWindowPresenterKind.FullScreen
                : AppWindowPresenterKind.Overlapped);
            // Only after the switch, so the overlay is never stretched to the full window's minimum on its way out.
            if (_aspectLock is not null)
            {
                _aspectLock.MinimumClientSize = (MinClientWidth, MinClientHeight);
            }
        }
        finally
        {
            _pip.EndSwitch();
        }
        PictureInPictureChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsAlwaysOnTop => App.Settings.Current.AlwaysOnTop;

    public event EventHandler? AlwaysOnTopChanged;

    public void ToggleAlwaysOnTop()
    {
        App.Settings.Set(SettingKeys.AlwaysOnTop, !IsAlwaysOnTop);
        _ = Task.Run(() =>
        {
            try
            {
                App.Settings.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The choice still holds this session; it is saved with the next change.
            }
        });
    }

    // The setting changes on the thread pool when it loads, so the window is updated on its own thread.
    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key == SettingKeys.AlwaysOnTop)
        {
            DispatcherQueue.TryEnqueue(ApplyAlwaysOnTop);
        }
    }

    private void ApplyAlwaysOnTop()
    {
        // Only the overlapped presenter can be topmost; full screen applies it again on the way back.
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.IsAlwaysOnTop != IsAlwaysOnTop)
        {
            presenter.IsAlwaysOnTop = IsAlwaysOnTop;
        }
        AlwaysOnTopChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? TitleBarPointerMoved;

    private void OnNonClientPointerMoved(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        // Repeats at the same point (as WinUI does for client moves) are not activity.
        if (args.Point == _lastNonClientPointer)
        {
            return;
        }
        _lastNonClientPointer = args.Point;
        TitleBarPointerMoved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The pale star on a dark taskbar, the graphite one on a light taskbar. The exe's own icon (Start, shortcuts)
    /// is always the pale one, since Windows gives unpackaged apps no per-theme icon there.
    /// </summary>
    private void ApplyTaskbarIcon()
    {
        bool lightTaskbar = IsLightTaskbar();
        AppWindow.SetIcon(lightTaskbar ? "Assets/AppIcon.light.ico" : "Assets/AppIcon.ico");
        _taskbarButtons?.SetLightTheme(lightTaskbar);
    }

    private static bool IsLightTaskbar() => Registry.GetValue(
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
        "SystemUsesLightTheme", 0) is int value && value != 0;

    public event EventHandler<TaskbarCommand>? TaskbarCommandRequested;

    private void OnTaskbarButton(TaskbarButton button) => TaskbarCommandRequested?.Invoke(this, button switch
    {
        TaskbarButton.Previous => TaskbarCommand.Previous,
        TaskbarButton.Next => TaskbarCommand.Next,
        _ => TaskbarCommand.PlayPause,
    });

    public void SetTaskbar(string? fileName, in TaskbarControls controls)
    {
        // The taskbar preview's title is the window title.
        string title = TaskbarControls.Title(AppStrings.Get("App_Name"), fileName);
        if (!string.Equals(Title, title, StringComparison.Ordinal))
        {
            Title = title;
        }
        (TaskbarGlyph glyph, string tip) = controls.PlayPause switch
        {
            PlayButtonAction.Pause => (TaskbarGlyph.Pause, _pauseTip),
            PlayButtonAction.Replay => (TaskbarGlyph.Play, _replayTip),
            _ => (TaskbarGlyph.Play, _playTip),
        };
        _taskbarButtons?.Update(controls.PreviousEnabled, glyph, tip, controls.PlayPauseEnabled, controls.NextEnabled);
    }

    // Raised off the UI thread when the Windows light/dark setting changes.
    private void OnSystemColoursChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        ApplyTaskbarIcon();
        ApplyTheme();
        App.Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => App.Settings.Changed -= OnSettingsChanged;
    });

    private void OnSettingsChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key == SettingKeys.Theme)
        {
            DispatcherQueue.TryEnqueue(ApplyTheme);
        }
    }

    private void ApplyTheme()
    {
        string theme = App.Settings.Current.Theme;
        Shell.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateCaptionColors();
        ApplyTaskbarIcon();
    }

    private void UpdateCaptionColors()
    {
        var resources = Application.Current.Resources;
        _captionForeground = (Color)resources["AetherCaptionGlyphColor"];
        _captionInactiveForeground = (Color)resources["AetherCaptionGlyphInactiveColor"];
        _captionHover = (Color)resources["AetherCaptionHoverColor"];
        _captionPressed = (Color)resources["AetherCaptionPressedColor"];

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = _captionForeground;
        titleBar.ButtonInactiveForegroundColor = _captionInactiveForeground;
        titleBar.ButtonHoverBackgroundColor = _captionHover;
        titleBar.ButtonPressedBackgroundColor = _captionPressed;

        bool isLight = Shell.ActualTheme == ElementTheme.Light;
        Color activeHoverGlyph = isLight ? _captionForeground : Colors.White;
        titleBar.ButtonHoverForegroundColor = activeHoverGlyph;
        titleBar.ButtonPressedForegroundColor = activeHoverGlyph;
    }

    public void FitToVideo(int videoWidth, int videoHeight)
    {
        if (_keepRestoredSize)
        {
            _keepRestoredSize = false;
            return;
        }
        if (videoWidth <= 0 || videoHeight <= 0
            || AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            return;
        }

        RectInt32 work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        SizeInt32 size = AppWindow.Size;
        SizeInt32 client = AppWindow.ClientSize;
        PointInt32 position = AppWindow.Position;
        int frameWidth = size.Width - client.Width;
        int frameHeight = size.Height - client.Height;
        double scale = Shell.XamlRoot?.RasterizationScale ?? 1;

        // Wide enough for both minimums at the video's aspect, so the window never opens below either.
        double minWidth = Math.Max(MinClientWidth, MinClientHeight * videoWidth / videoHeight);
        PixelRect workArea = new(work.X, work.Y, work.X + work.Width, work.Y + work.Height);
        (int clientWidth, int clientHeight) = WindowGeometry.FitClient(
            videoWidth, videoHeight, workArea, frameWidth, frameHeight,
            FitMaxWorkAreaFraction, (int)Math.Ceiling(minWidth * scale));
        int width = clientWidth + frameWidth;
        int height = clientHeight + frameHeight;
        (int x, int y) = WindowGeometry.Recentre(
            new PixelRect(position.X, position.Y, position.X + size.Width, position.Y + size.Height), width, height, workArea);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    public void SetVideoAspect(double aspect)
    {
        _videoAspect = aspect;
        if (_aspectLock is not null)
        {
            _aspectLock.AspectRatio = aspect;
        }
        WindowBorder.SetShown(WinRT.Interop.WindowNative.GetWindowHandle(this), shown: aspect <= 0);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPositionChange || args.DidPresenterChange)
        {
            TrackNormalBounds();
            DisplaySync.OnWindowMoved();
            UpdateCaptionInset();
        }
        if (args.DidPresenterChange)
        {
            ApplyAlwaysOnTop();
            FullScreenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Shell.OnWindowActivationChanged(args.WindowActivationState != WindowActivationState.Deactivated);
        UpdateCaptionInset();
    }

    // RightInset is in physical pixels and changes with the display scale; the page lays out in DIPs.
    private void UpdateCaptionInset()
    {
        double scale = Shell.XamlRoot?.RasterizationScale ?? 0;
        if (scale > 0)
        {
            Shell.SetCaptionInset(AppWindow.TitleBar.RightInset / scale);
        }
    }
}
