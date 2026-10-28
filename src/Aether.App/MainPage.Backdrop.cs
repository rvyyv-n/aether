using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
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
    private const int PopupSettleFrames = 3;
    private FrameworkElement? _pendingPopup;
    private int _pendingPopupFrames;

    /// <summary>The open flyout's presenter, so the blur follows it; null when none is open.</summary>
    private void SetPopupSurface(FrameworkElement? content)
    {
        FrameworkElement? found = FindPresenter(content);
        if (found is Microsoft.UI.Xaml.Controls.Control presenter)
        {
            presenter.RequestedTheme = ThemeTokens.AppTheme; // a flyout's popup does not reliably take the page's in-app theme
        }
        ShowPopupSurface(found);
    }

    /// <summary>
    /// The same for a menu, which has no content element of its own: its first item is inside the presenter. The menu is
    /// frosted over a video and solid where there is no blur to go with it (no video, picture in picture). Menus follow the
    /// app's theme, set here so a menu opened from inside a forced-dark layer still matches the rest of the app.
    /// </summary>
    private void SetMenuSurface(object? sender, bool open)
    {
        FrameworkElement? presenter = open && sender is Microsoft.UI.Xaml.Controls.MenuFlyout { Items.Count: > 0 } menu
            ? FindPresenter(menu.Items[0])
            : null;
        if (presenter is Microsoft.UI.Xaml.Controls.Control control)
        {
            control.RequestedTheme = ThemeTokens.AppTheme;
            control.Background = ThemeTokens.Get<Microsoft.UI.Xaml.Media.Brush>(
                ThemeTokens.AppTheme, _backdropAllowed ? "AetherSurfaceMenuBrush" : "AetherSurfaceMenuFallbackBrush");
        }
        ShowPopupSurface(presenter);
    }

    /// <summary>
    /// Flyouts have their own transition and acrylic off (12.7). A flyout's popup is a window of its own that reaches the
    /// screen a few frames after it opens, so a new one waits, invisible, for <see cref="PopupSettleFrames"/>, then it
    /// and its blur fade in on the same tick, length and curve. Closing takes both away at once.
    /// </summary>
    private void ShowPopupSurface(FrameworkElement? presenter)
    {
        _pendingPopup = presenter;
        if (presenter is null)
        {
            _popupSurface = null;
            PostBackdropRegion();
            return;
        }
        ElementCompositionPreview.GetElementVisual(presenter).Opacity = 0;
        _pendingPopupFrames = PopupSettleFrames;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnPopupRendering;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnPopupRendering;
    }

    private void OnPopupRendering(object? sender, object e)
    {
        if (_pendingPopup is not null && --_pendingPopupFrames > 0)
        {
            return;
        }
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnPopupRendering;
        if (_pendingPopup is null)
        {
            return;
        }
        _popupSurface = _pendingPopup;
        _pendingPopup = null;
        if (_fader is null)
        {
            ElementCompositionPreview.GetElementVisual(_popupSurface).Opacity = 1;
        }
        else
        {
            _fader.Reveal(_popupSurface, true);
        }
        PostBackdropRegion();
    }

    private static FrameworkElement? FindPresenter(DependencyObject? content)
    {
        for (DependencyObject? node = content; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is Microsoft.UI.Xaml.Controls.FlyoutPresenter or Microsoft.UI.Xaml.Controls.MenuFlyoutPresenter)
            {
                return (FrameworkElement)node;
            }
        }
        return null;
    }

    /// <summary>Blurs behind the clip chip and drawer, the open dialog card and the open flyout, like the transport card.</summary>
    private void PostSurfaceRegions()
    {
        FrameworkElement? card = LanStreamOverlay.Visibility == Visibility.Visible ? LanStreamCard
            : OpenUrlOverlay.Visibility == Visibility.Visible ? OpenUrlCard
            : null;
        PostSurfaceRegion(BackdropSlot.Clip, ClipDrawer.Visibility == Visibility.Visible ? ClipDrawer.Surface : null, fadeMilliseconds: _fader?.RevealDurationMilliseconds(true) ?? 0);
        PostSurfaceRegion(BackdropSlot.Card, card, fadeMilliseconds: _fader?.RevealDurationMilliseconds(true) ?? 0);
        PostSurfaceRegion(BackdropSlot.Popup, _popupSurface, fadeMilliseconds: _fader?.RevealDurationMilliseconds(true) ?? 0);
    }

    private void PostSurfaceRegion(BackdropSlot slot, FrameworkElement? surface, double fadeMilliseconds)
    {
        int index = (int)slot;
        bool visible = _backdropAllowed && _isLoaded && VideoSurface.IsLoaded
            && surface is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 };
        if (!visible || surface is null)
        {
            ShowSurface(slot, false, 0);
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
        // After the post, so the first frame of a new blur is drawn at the new card, not where the last one was.
        ShowSurface(slot, true, fadeMilliseconds);
    }

    private void ShowSurface(BackdropSlot slot, bool visible, double fadeMilliseconds)
    {
        if (visible == _surfaceShown[(int)slot])
        {
            return;
        }
        _surfaceShown[(int)slot] = visible;
        VideoSurface.SetBackdropActive(slot, visible);
        VideoSurface.FadeBackdrop(slot, visible, fadeMilliseconds);
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
