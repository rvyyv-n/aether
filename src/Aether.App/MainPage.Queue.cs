using Aether.Core.Interop.Rendering;
using Aether.Core.Playback;
using Aether_App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Aether_App;

/// <summary>
/// The queue drawer (13.3) on the right edge: opened from the sidebar, the right-click menu or Q, it slides in from
/// the edge with the render thread's blur behind it and holds the chrome while it is open. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private const double QueueInset = 16;

    // 0 off the right edge, 1 in place. A render transform, so TransformToVisual sees it and the blur follows each step.
    private readonly MotionTween _queueSlide = new(0);
    private readonly TranslateTransform _queueOffset = new();
    private bool _queueShown;

    private void InitializeQueue()
    {
        QueueDrawer.Queue = _playlist;
        QueueDrawer.PlayRequested += (_, item) => Open(item, fromQueue: true);
        QueueDrawer.Edited += (_, _) =>
        {
            _queueIsFolder = false; // the user's own order now: a jump must not read the folder back over it
            Render();
        };
        QueueDrawer.ModeChanged += (_, _) => Render();
        QueueDrawer.CloseRequested += (_, _) => CloseQueue();
        QueueLayer.RenderTransform = _queueOffset;
        _queueSlide.Changed += (_, _) => ApplyQueueSlide();
        VideoSurface.FadeBackdrop(BackdropSlot.Queue, false, 0);
    }

    private bool IsQueueAvailable => !_settingsShown && !_tour.IsActive && !IsPictureInPicture && _openUrl.Stage != OpenUrlStage.Editing;

    private void OnQueueAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ArePlayerKeysPaused)
        {
            return;
        }
        args.Handled = true;
        if (!CloseQueue())
        {
            OpenQueue();
        }
    }

    private void OnQueueMenuClick(object sender, RoutedEventArgs e) => OpenQueue();

    private void OnRailQueueClick(object sender, RoutedEventArgs e)
    {
        CloseRail();
        OpenQueue();
    }

    private void OpenQueue()
    {
        if (_queueShown || _fader is null || !IsQueueAvailable)
        {
            return;
        }
        _queueShown = true;
        ClipDrawer.Close(); // both grow out of the same corner
        QueueDrawer.Refresh();
        PlaceQueue();
        _fader.Enter(QueueLayer, null);
        _queueSlide.AnimateTo(1, Motion.Token("AetherMotionQuickInMilliseconds"), Motion.EaseOut);
        VideoSurface.FadeBackdrop(BackdropSlot.Queue, true, _fader.RevealDurationMilliseconds(true));
        SyncDrawerHold();
        QueueDrawer.ShowPlaying();
    }

    /// <summary>Takes the drawer away and gives the keyboard back to the page. True if it was open.</summary>
    private bool CloseQueue()
    {
        if (!_queueShown || _fader is null)
        {
            return false;
        }
        _queueShown = false;
        _fader.Exit(QueueLayer, null);
        _queueSlide.AnimateTo(0, Motion.Token("AetherMotionQuickOutMilliseconds"), Motion.EaseIn);
        VideoSurface.FadeBackdrop(BackdropSlot.Queue, false, _fader.RevealDurationMilliseconds(false));
        SyncDrawerHold();
        Focus(FocusState.Programmatic);
        return true;
    }

    /// <summary>The list follows the queue whenever the page renders: a new file, a jump, a folder read again.</summary>
    private void RefreshQueueDrawer()
    {
        if (_queueShown)
        {
            QueueDrawer.Refresh();
        }
        if (_queueShown && !IsQueueAvailable)
        {
            CloseQueue();
        }
    }

    private void SyncDrawerHold()
    {
        _chrome.SetHold(ChromeHolds.Drawer, ClipDrawer.IsOpen || ClipDrawer.IsDraggingOut || _queueShown);
        ApplyChrome();
    }

    /// <summary>From under the title bar to 8 above the full transport card, 16 in from the right edge.</summary>
    private void PlaceQueue()
    {
        double top = (TitleBar.Visibility == Visibility.Visible ? TitleBar.Height : 0) + 8;
        double bottom = Osd.Margin.Bottom + (double)Application.Current.Resources["AetherSizeOsdHeight"] + 8;
        QueueLayer.Margin = new Thickness(0, top, QueueInset, bottom);
    }

    private void ApplyQueueSlide()
    {
        _queueOffset.X = (1 - _queueSlide.Value) * (QueueDrawer.Surface.ActualWidth + QueueInset);
        PostQueueRegion();
    }

    private void PostQueueRegion()
    {
        if (QueueLayer.Visibility == Visibility.Visible && MeasureSurface(QueueDrawer.Surface) is { } region
            && region != _postedSurfaceRegions[(int)BackdropSlot.Queue])
        {
            _postedSurfaceRegions[(int)BackdropSlot.Queue] = region;
            VideoSurface.PostBackdrop(BackdropSlot.Queue, region);
        }
    }
}
