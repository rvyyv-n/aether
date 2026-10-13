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

    /// <summary>Re-measures the card against the video surface and posts it if it moved, resized or the scale changed.</summary>
    private void PostBackdropRegion()
    {
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
        VideoSurface.PostBackdrop(region);
    }
}
