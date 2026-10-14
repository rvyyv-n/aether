using Aether.Core.Interop.Mpv;
using Aether.Core.Playback.Video;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Aether_App;

/// <summary>
/// The video flyout (screen 9.3) behind Video in the right-click menu: zoom, rotation, aspect ratio and colours. The
/// choices are for the open file: a new file goes back to Settings → Video, and an engine restart puts them back.
/// Changes reach mpv on a pool thread, newest first, so dragging a slider never queues up. UI thread only, apart from
/// the pool work it starts.
/// </summary>
public sealed partial class MainPage
{
    // What the user picked for the open file; null while nothing differs from Settings → Video.
    private VideoAdjustments? _video;
    private bool _showVideoAfterMenu;
    private bool _videoPushing;
    private bool _videoStale; // the state changed again while a push was running

    private void InitializeVideo()
    {
        // Light dismiss would otherwise swallow the first click on the pill, as for the volume flyout.
        VideoFlyout.OverlayInputPassThroughElement = Osd;
        VideoPanel.Adjusted += OnVideoAdjusted;
    }

    /// <summary>The menu closes after this click, and its Closed handler opens the flyout, so the two holds do not cross.</summary>
    private void OnVideoMenuClick(object sender, RoutedEventArgs e) => _showVideoAfterMenu = true;

    private void ShowVideoIfRequested()
    {
        if (_showVideoAfterMenu)
        {
            _showVideoAfterMenu = false;
            ShowVideo();
        }
    }

    private void ShowVideo()
    {
        if (_session is null || XamlRoot is null)
        {
            return;
        }
        VideoAdjustments baseline = VideoAdjustments.FromSettings(App.Settings.Current);
        VideoPanel.Baseline = baseline;
        VideoPanel.Show(_video ?? baseline);
        // The window less 8 either side (9.5), and the 1 px stroke the presenter adds.
        VideoPanel.MaxPanelHeight = Math.Max(0, XamlRoot.Size.Height - (double)Application.Current.Resources["AetherSizeMenuMaxHeightInset"] - 2);
        VideoFlyout.ShowAt(Osd, new FlyoutShowOptions { Placement = FlyoutPlacementMode.TopEdgeAlignedRight, ShowMode = FlyoutShowMode.Standard });
    }

    private void OnVideoFlyoutOpened(object? sender, object e) => HoldForMenu(true);

    private void OnVideoFlyoutClosed(object? sender, object e)
    {
        HoldForMenu(false);
        Focus(FocusState.Programmatic); // keep Space and the frame-step keys on the page
    }

    private void OnVideoAdjusted(object? sender, VideoAdjustments adjustments)
    {
        _video = adjustments == VideoAdjustments.FromSettings(App.Settings.Current) ? null : adjustments;
        PushVideo();
    }

    /// <summary>A new file starts from Settings → Video; after an engine restart the new engine gets the choices back.</summary>
    private void ApplyVideoForOpen(bool afterRestart)
    {
        if (_video is null)
        {
            return;
        }
        if (!afterRestart)
        {
            _video = null;
        }
        PushVideo();
    }

    /// <summary>Sends the whole state to mpv. Only one push runs at a time; a change made meanwhile is sent when it ends.</summary>
    private async void PushVideo()
    {
        _videoStale = true;
        if (_videoPushing)
        {
            return;
        }
        _videoPushing = true;
        try
        {
            while (_videoStale && _isLoaded && _session is { } session)
            {
                _videoStale = false;
                IReadOnlyList<KeyValuePair<string, string>> properties =
                    (_video ?? VideoAdjustments.FromSettings(App.Settings.Current)).Properties();
                await Task.Run(() =>
                {
                    foreach ((string name, string value) in properties)
                    {
                        try
                        {
                            session.Context.SetProperty(name, value);
                        }
                        catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
                        {
                            // The core refused, or is shutting down; the rest still apply.
                        }
                    }
                });
            }
        }
        finally
        {
            _videoPushing = false;
        }
    }
}
