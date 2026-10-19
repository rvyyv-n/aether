using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Rendering;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Aether_App;

/// <summary>
/// Posts the player card's place over the video to the render thread, which blurs the video behind it
/// (ADR-0013). UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private BackdropRegion _postedBackdrop;
    private BackdropRegion _postedSidebarBackdrop;
    private readonly BackdropRegion[] _postedSurfaceRegions = new BackdropRegion[BackdropSource.SlotCount];
    private readonly bool[] _surfaceShown = new bool[BackdropSource.SlotCount];
    private bool _backdropAllowed;
    private FrameworkElement? _popupSurface;

    /// <summary>The open flyout's presenter, so the blur follows it; null when none is open.</summary>
    private void SetPopupSurface(FrameworkElement? content)
    {
        FrameworkElement? presenter = null;
        for (DependencyObject? node = content; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is Microsoft.UI.Xaml.Controls.FlyoutPresenter found)
            {
                presenter = found;
                break;
            }
        }
        _popupSurface = presenter;
        PostBackdropRegion();
    }

    /// <summary>Blurs behind the clip chip and drawer, the open dialog card and the open flyout, like the transport card.</summary>
    private void PostSurfaceRegions()
    {
        FrameworkElement? card = LanStreamOverlay.Visibility == Visibility.Visible ? LanStreamCard
            : OpenUrlOverlay.Visibility == Visibility.Visible ? OpenUrlCard
            : null;
        PostSurfaceRegion(BackdropSlot.Clip, ClipDrawer.Visibility == Visibility.Visible ? ClipDrawer.Surface : null, fadeMilliseconds: _fader?.RevealDurationMilliseconds(true) ?? 0);
        PostSurfaceRegion(BackdropSlot.Card, card, fadeMilliseconds: _fader?.RevealDurationMilliseconds(true) ?? 0);
        PostSurfaceRegion(BackdropSlot.Popup, _popupSurface, fadeMilliseconds: 0);
    }

    private void PostSurfaceRegion(BackdropSlot slot, FrameworkElement? surface, double fadeMilliseconds)
    {
        int index = (int)slot;
        bool visible = _backdropAllowed && _isLoaded && VideoSurface.IsLoaded
            && surface is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 };
        if (visible != _surfaceShown[index])
        {
            _surfaceShown[index] = visible;
            VideoSurface.SetBackdropActive(slot, visible);
            VideoSurface.FadeBackdrop(slot, visible, visible ? fadeMilliseconds : 0);
        }
        if (!visible || surface is null)
        {
            return;
        }

        float scaleX = VideoSurface.CompositionScaleX;
        float scaleY = VideoSurface.CompositionScaleY;
        Rect bounds;
        try
        {
            bounds = surface.TransformToVisual(VideoSurface).TransformBounds(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return; // a popup that is closing is already out of the tree; the next layout pass tidies up
        }
        double radius = surface switch
        {
            Microsoft.UI.Xaml.Controls.Border border => border.CornerRadius.TopLeft,
            Microsoft.UI.Xaml.Controls.Control control => control.CornerRadius.TopLeft,
            _ => 0,
        };
        SwapChainTargetSize target = DxgiPresenter.ComputeTargetSize(VideoSurface.ActualWidth, VideoSurface.ActualHeight, scaleX, scaleY);
        var region = new BackdropRegion(
            (float)(bounds.X * scaleX), (float)(bounds.Y * scaleY), (float)(bounds.Width * scaleX), (float)(bounds.Height * scaleY),
            (float)(radius * scaleX), scaleX, target.Width, target.Height);
        if (region != _postedSurfaceRegions[index])
        {
            _postedSurfaceRegions[index] = region;
            VideoSurface.PostBackdrop(slot, region);
        }
    }

    /// <summary>Re-measures the cards against the video surface and posts each if it moved, resized or the scale changed.</summary>
    private void PostBackdropRegion()
    {
        PostSidebarRegion();
        PostSurfaceRegions();
        if (!_isLoaded || Osd.Visibility != Visibility.Visible || !Osd.IsLoaded || !VideoSurface.IsLoaded)
        {
            return;
        }

        float scaleX = VideoSurface.CompositionScaleX;
        float scaleY = VideoSurface.CompositionScaleY;
        Rect card = Osd.TransformToVisual(VideoSurface).TransformBounds(new Rect(0, 0, Osd.ActualWidth, Osd.ActualHeight));
        SwapChainTargetSize surface = DxgiPresenter.ComputeTargetSize(VideoSurface.ActualWidth, VideoSurface.ActualHeight, scaleX, scaleY);
        var region = new BackdropRegion(
            (float)(card.X * scaleX), (float)(card.Y * scaleY), (float)(card.Width * scaleX), (float)(card.Height * scaleY),
            (float)(Osd.CornerRadius.TopLeft * scaleX), scaleX, surface.Width, surface.Height);
        if (region == _postedBackdrop)
        {
            return;
        }
        _postedBackdrop = region;
        VideoSurface.PostBackdrop(BackdropSlot.Bar, region);
    }

    /// <summary>
    /// The same for the sidebar, from where the page placed it plus the slide's offset (a Translation is not part of what
    /// TransformToVisual reports). Runs on every step of the slide and on layout, but only posts when the rectangle changed,
    /// so an idle sidebar costs the render thread nothing.
    /// </summary>
    private void PostSidebarRegion()
    {
        if (!_isLoaded || !VideoSurface.IsLoaded)
        {
            return;
        }

        float scaleX = VideoSurface.CompositionScaleX;
        float scaleY = VideoSurface.CompositionScaleY;
        Point origin = VideoSurface.TransformToVisual(this).TransformPoint(new Point(0, 0));
        SwapChainTargetSize surface = DxgiPresenter.ComputeTargetSize(VideoSurface.ActualWidth, VideoSurface.ActualHeight, scaleX, scaleY);
        var region = new BackdropRegion(
            (float)((Rail.Margin.Left + RailSlideOffset - origin.X) * scaleX), (float)((_railTop - origin.Y) * scaleY),
            (float)(Rail.Width * scaleX), (float)(Rail.Height * scaleY),
            (float)(Rail.CornerRadius.TopLeft * scaleX), scaleX, surface.Width, surface.Height);
        if (region == _postedSidebarBackdrop)
        {
            return;
        }
        _postedSidebarBackdrop = region;
        VideoSurface.PostBackdrop(BackdropSlot.Sidebar, region);
    }
}
