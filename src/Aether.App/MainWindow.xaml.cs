using Aether.Core.Interop.Windowing;
using Aether.Core.Playback;
using Aether_App.Controls;
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
    private static readonly Color CaptionForeground = Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF);
    private static readonly Color CaptionInactiveForeground = Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF);
    private static readonly Color CaptionHover = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
    private static readonly Color CaptionPressed = Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF);

    // A video opens at one video pixel per screen pixel, within this share of the work area, and
    // no narrower than this (DIPs) so the controls fit.
    private const double FitMaxWorkAreaFraction = 0.9;
    private const double FitMinClientWidth = 640;

    private readonly AspectRatioLock? _aspectLock;
    private readonly CaptionGlyphFade _captionFade = new();
    private readonly DispatcherQueueTimer _captionTimer;
    private readonly TimeSpan _chromeShow;
    private readonly TimeSpan _chromeHide;
    private bool _captionsVisible = true;
    private Windows.Foundation.Point _lastNonClientPointer = new(double.NaN, double.NaN);

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Shell.TitleBarDragRegion);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        ApplyCaptionGlyphs(1);

        var resources = Application.Current.Resources;
        _chromeShow = TimeSpan.FromMilliseconds((double)resources["AetherMotionChromeShowMilliseconds"]);
        _chromeHide = TimeSpan.FromMilliseconds((double)resources["AetherMotionChromeHideMilliseconds"]);
        _captionTimer = DispatcherQueue.CreateTimer();
        _captionTimer.Interval = TimeSpan.FromMilliseconds(16);
        _captionTimer.IsRepeating = true;
        _captionTimer.Tick += OnCaptionTimerTick;

        try
        {
            _aspectLock = new AspectRatioLock(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }
        catch (InvalidOperationException)
        {
            _aspectLock = null; // the window still works, just without the drag-resize aspect lock
        }

        InputNonClientPointerSource nonClient = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        nonClient.PointerEntered += OnNonClientPointerMoved;
        nonClient.PointerMoved += OnNonClientPointerMoved;

        Shell.Host = this;
        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _captionTimer.Stop();
            _aspectLock?.Dispose();
        };
        AppWindow.Changed += OnAppWindowChanged;
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
        titleBar.ButtonForegroundColor = Scale(CaptionForeground, opacity);
        titleBar.ButtonInactiveForegroundColor = Scale(CaptionInactiveForeground, opacity);
        // Hover and press fade too, so an invisible button doesn't light up under the pointer.
        titleBar.ButtonHoverBackgroundColor = Scale(CaptionHover, opacity);
        titleBar.ButtonPressedBackgroundColor = Scale(CaptionPressed, opacity);
        titleBar.ButtonHoverForegroundColor = Scale(Colors.White, opacity);
        titleBar.ButtonPressedForegroundColor = Scale(Colors.White, opacity);
    }

    private static Color Scale(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);

    public void FitToVideo(int videoWidth, int videoHeight)
    {
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

        PixelRect workArea = new(work.X, work.Y, work.X + work.Width, work.Y + work.Height);
        (int clientWidth, int clientHeight) = WindowGeometry.FitClient(
            videoWidth, videoHeight, workArea, frameWidth, frameHeight,
            FitMaxWorkAreaFraction, (int)Math.Round(FitMinClientWidth * scale));
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
        if (args.DidPresenterChange)
        {
            FullScreenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args) =>
        Shell.OnWindowActivationChanged(args.WindowActivationState != WindowActivationState.Deactivated);
}
