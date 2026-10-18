using Aether.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Aether_App;

/// <summary>
/// Picture in picture (8.1, ADR-0022): the window shrinks to an always-on-top overlay showing the video edge to
/// edge, with a top shade, "Back to full window", a centre play/pause and a thin seek line, all on the chrome fade.
/// The window does the presenter switch; this side swaps the chrome and decides when to go back.
/// </summary>
public sealed partial class MainPage
{
    private bool IsPictureInPicture => _host?.IsPictureInPicture == true;

    private void TogglePictureInPicture()
    {
        if (IsPictureInPicture)
        {
            LeavePictureInPicture();
            return;
        }
        if (!HasVideo || _settingsShown || _host is not { } host)
        {
            return;
        }
        // Only the video goes along: anything that needs the full window closes first.
        CloseRail();
        ClipDrawer.Close();
        if (_diagnosticsShown)
        {
            SetDiagnosticsShown(false);
        }
        host.SetPictureInPicture(true);
    }

    /// <summary>Settings, the Open URL dialog, the clip drawer and diagnostics need the full window.</summary>
    private void LeavePictureInPicture() => _host?.SetPictureInPicture(false);

    private void OnPictureInPictureChanged(object? sender, EventArgs e)
    {
        Render();
        Focus(FocusState.Programmatic); // keep P, Space and the arrows on the page after the Back button
    }

    private void OnPipBackClick(object sender, RoutedEventArgs e) => LeavePictureInPicture();

    private void OnPictureInPictureMenuClick(object sender, RoutedEventArgs e) => TogglePictureInPicture();

    // P stays text in the Open URL field and in settings' text boxes.
    private void OnPictureInPictureAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ArePlayerKeysPaused || IsTextInputFocused())
        {
            return;
        }
        args.Handled = true;
        TogglePictureInPicture();
    }

    private void OnPipSeekPressed(object sender, PointerRoutedEventArgs e)
    {
        double width = PipSeekLine.ActualWidth;
        if (_session is not { } session || width <= 0 || session.ViewModel.Duration <= 0)
        {
            return;
        }
        e.Handled = true;
        double fraction = Math.Clamp(e.GetCurrentPoint(PipSeekLine).Position.X / width, 0, 1);
        OnSeekRequested(fraction * session.ViewModel.Duration, dragging: false);
    }

    /// <summary>Runs with the clock, which only ticks while the chrome shows, so a hidden line costs nothing.</summary>
    private void UpdatePipSeek(PlayerViewModel vm)
    {
        if (PipChrome.Visibility != Visibility.Visible)
        {
            return;
        }
        double position = TransportState.DisplayPosition(vm.State);
        PipSeekScale.ScaleX = vm.Duration > 0 ? Math.Clamp(position / vm.Duration, 0, 1) : 0;
    }
}
