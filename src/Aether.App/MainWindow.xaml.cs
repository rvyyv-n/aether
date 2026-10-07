using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
        titleBar.ButtonHoverBackgroundColor = CaptionHover;
        titleBar.ButtonPressedBackgroundColor = CaptionPressed;
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonPressedForegroundColor = Colors.White;
        SetCaptionButtonsVisible(true);

        Shell.Host = this;
        Activated += OnActivated;
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

    public void SetCaptionButtonsVisible(bool visible)
    {
        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = visible ? CaptionForeground : Colors.Transparent;
        titleBar.ButtonInactiveForegroundColor = visible ? CaptionInactiveForeground : Colors.Transparent;
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
