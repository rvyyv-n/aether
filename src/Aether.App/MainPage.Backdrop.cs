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

    /// <summary>Re-measures the cards against the video surface and posts each if it moved, resized or the scale changed.</summary>
    private void PostBackdropRegion()
    {
        PostSidebarRegion();
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
