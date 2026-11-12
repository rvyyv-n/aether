using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App;

/// <summary>
/// The page's own minimise, maximise and close buttons (12.10), which fade with the chrome over a video. The window
/// registers them as its caption buttons, so Windows runs them and shows Snap Layouts over maximise; the pointer over
/// them is the window's, so it passes hover and press on here. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";

    /// <summary>The visible caption button the window registers as <paramref name="kind"/>; null when there is none.</summary>
    public FrameworkElement? CaptionButton(NonClientRegionKind kind)
    {
        Button? button = CaptionFor(kind);
        return button is { Visibility: Visibility.Visible } && TitleBar.Visibility == Visibility.Visible ? button : null;
    }

    public void SetMaximized(bool maximized)
    {
        CaptionMaximize.Content = maximized ? RestoreGlyph : MaximizeGlyph;
        AutomationProperties.SetName(CaptionMaximize, AppStrings.Get(maximized ? "Caption_Restore" : "Caption_Maximize"));
    }

    /// <summary>Shows hover, or press, on the caption button of <paramref name="kind"/>; any other kind clears them.</summary>
    public void SetCaptionPointer(NonClientRegionKind? kind, bool pressed)
    {
        foreach (Button button in (Button[])[CaptionMinimize, CaptionMaximize, CaptionClose])
        {
            string state = button == (kind is { } k ? CaptionFor(k) : null) ? pressed ? "Pressed" : "PointerOver" : "Normal";
            VisualStateManager.GoToState(button, state, true);
        }
    }

    private Button? CaptionFor(NonClientRegionKind kind) => kind switch
    {
        NonClientRegionKind.Minimize => CaptionMinimize,
        NonClientRegionKind.Maximize => CaptionMaximize,
        NonClientRegionKind.Close => CaptionClose,
        _ => null,
    };

    private void ApplyCaptionStrings()
    {
        AutomationProperties.SetName(CaptionMinimize, AppStrings.Get("Caption_Minimize"));
        AutomationProperties.SetName(CaptionClose, AppStrings.Get("Caption_Close"));
        SetMaximized(CaptionMaximize.Content as string == RestoreGlyph);
    }

    private void OnCaptionMinimizeClick(object sender, RoutedEventArgs e) => _host?.MinimizeWindow();

    private void OnCaptionMaximizeClick(object sender, RoutedEventArgs e) => _host?.ToggleMaximizeWindow();

    private void OnCaptionCloseClick(object sender, RoutedEventArgs e) => _host?.Close();
}
