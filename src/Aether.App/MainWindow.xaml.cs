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
using Windows.Graphics;
using Windows.UI;

namespace Aether_App;

/// <summary>What the page needs from the window it sits in: full screen and the system caption buttons.</summary>
public interface IShellWindow
{
    bool IsFullScreen { get; }

    /// <summary>Raised after the window enters or leaves full screen.</summary>
    event EventHandler? FullScreenChanged;

    void SetFullScreen(bool fullScreen);

    /// <summary>Keep the window above others. Saved with the settings; applies again after full screen.</summary>
    bool IsAlwaysOnTop { get; }

    /// <summary>Raised after <see cref="IsAlwaysOnTop"/> changes.</summary>
    event EventHandler? AlwaysOnTopChanged;

    void ToggleAlwaysOnTop();

    /// <summary>Fades the minimize/maximize/close glyphs with the rest of the chrome (they stay clickable).</summary>
    void SetCaptionButtonsVisible(bool visible);

    /// <summary>
    /// Raised when the pointer moves over the title strip or the caption buttons. Those are
    /// non-client regions, so the page gets no pointer events there and could not wake the controls.
    /// </summary>
    event EventHandler? TitleBarPointerMoved;

    /// <summary>Resizes a restored window around a video of this display size (no-op when maximized or full screen).</summary>
    void FitToVideo(int videoWidth, int videoHeight);

    /// <summary>Client width / height to keep while the user drags the edges; 0 lets the window resize freely.</summary>
    void SetVideoAspect(double aspect);
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
    private readonly Color _captionForeground;
    private readonly Color _captionInactiveForeground;
    private readonly Color _captionHover;
    private readonly Color _captionPressed;
    private readonly AspectRatioLock? _aspectLock;
    private readonly CaptionGlyphFade _captionFade = new();
    private readonly DispatcherQueueTimer _captionTimer;
    private readonly TimeSpan _chromeShow;
    private readonly TimeSpan _chromeHide;
    private bool _captionsVisible = true;
    private Windows.Foundation.Point _lastNonClientPointer = new(double.NaN, double.NaN);
    private RectInt32? _normalBounds; // where the window sits when neither maximised nor full screen
    private bool _maximized;
    private bool _keepRestoredSize; // the first video does not resize a window the user's last size was restored to

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
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        ApplyCaptionGlyphs(1);

        _chromeShow = TimeSpan.FromMilliseconds((double)resources["AetherMotionChromeShowMilliseconds"]);
        _chromeHide = TimeSpan.FromMilliseconds((double)resources["AetherMotionChromeHideMilliseconds"]);
        _captionTimer = DispatcherQueue.CreateTimer();
        _captionTimer.Interval = TimeSpan.FromMilliseconds(16);
        _captionTimer.IsRepeating = true;
        _captionTimer.Tick += OnCaptionTimerTick;

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

        InputNonClientPointerSource nonClient = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        nonClient.PointerEntered += OnNonClientPointerMoved;
        nonClient.PointerMoved += OnNonClientPointerMoved;

        App.Settings.Changed += OnSettingChanged;
        ApplyAlwaysOnTop();

        Shell.Host = this;
        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _captionTimer.Stop();
            DisplaySync.Dispose(); // before the aspect lock: it sits on top of that subclass
            _aspectLock?.Dispose();
            App.Settings.Changed -= OnSettingChanged;
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

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!App.Settings.Current.RememberWindowSize || _normalBounds is not { } bounds)
        {
            return;
        }
        // The process is about to exit, so write now; the file is a few bytes and Save does not throw.
        App.WindowState.Save(new SavedWindow(ToWindowRect(bounds), _maximized));
    }

    public bool IsFullScreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    public event EventHandler? FullScreenChanged;

    public void SetFullScreen(bool fullScreen)
    {
        if (fullScreen != IsFullScreen)
        {
            AppWindow.SetPresenter(fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        }
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

    public void SetCaptionButtonsVisible(bool visible)
    {
        if (visible == _captionsVisible)
        {
            return;
        }
        _captionsVisible = visible;
        TimeSpan duration = Motion.AnimationsEnabled ? (visible ? _chromeShow : _chromeHide) : TimeSpan.Zero;
        _captionFade.Start(visible, duration);
        OnCaptionTimerTick(_captionTimer, null);
        _captionTimer.Start();
    }

    private void OnCaptionTimerTick(DispatcherQueueTimer sender, object? args)
    {
        bool running = _captionFade.Step();
        ApplyCaptionGlyphs(_captionFade.Opacity);
        if (!running)
        {
            sender.Stop();
        }
    }

    private void ApplyCaptionGlyphs(double opacity)
    {
        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = Scale(_captionForeground, opacity);
        titleBar.ButtonInactiveForegroundColor = Scale(_captionInactiveForeground, opacity);
        // Hover and press fade too, so an invisible button doesn't light up under the pointer.
        titleBar.ButtonHoverBackgroundColor = Scale(_captionHover, opacity);
        titleBar.ButtonPressedBackgroundColor = Scale(_captionPressed, opacity);
        titleBar.ButtonHoverForegroundColor = Scale(Colors.White, opacity);
        titleBar.ButtonPressedForegroundColor = Scale(Colors.White, opacity);
    }

    private static Color Scale(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);

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
        if (_aspectLock is not null)
        {
            _aspectLock.AspectRatio = aspect;
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPositionChange || args.DidPresenterChange)
        {
            TrackNormalBounds();
            DisplaySync.OnWindowMoved();
        }
        if (args.DidPresenterChange)
        {
            ApplyAlwaysOnTop();
            FullScreenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args) =>
        Shell.OnWindowActivationChanged(args.WindowActivationState != WindowActivationState.Deactivated);
}
