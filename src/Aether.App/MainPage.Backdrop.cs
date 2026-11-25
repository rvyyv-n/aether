using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace Aether_App;

/// <summary>
/// Posts every frosted glass card's place over the video to the render thread, which blurs the video behind it
/// (ADR-0013), and keeps each slot's fade opacity and active state strictly locked to the XAML surface's lifecycle.
/// UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private BackdropRegion _postedBackdrop;
    private BackdropRegion _postedSidebarBackdrop;
    private readonly BackdropRegion[] _postedSurfaceRegions = new BackdropRegion[BackdropSource.SlotCount];
    private readonly bool[] _surfaceShown = new bool[BackdropSource.SlotCount];
    private readonly bool[] _surfaceFadingOut = new bool[BackdropSource.SlotCount];
    private bool _backdropAllowed;
    private object? _popupOwner;
    private FrameworkElement? _popupSurface;
    private const int PopupSettleFrames = 3;
    private FrameworkElement? _pendingPopup;
    private int _pendingPopupFrames;

    /// <summary>Starts every transient overlay blur slot at zero opacity so its first reveal always fades in smoothly.</summary>
    private void InitializeBackdrop()
    {
        VideoSurface.FadeBackdrop(BackdropSlot.Sidebar, false, 0);
        VideoSurface.FadeBackdrop(BackdropSlot.Clip, false, 0);
        VideoSurface.FadeBackdrop(BackdropSlot.Card, false, 0);
        VideoSurface.FadeBackdrop(BackdropSlot.Popup, false, 0);
        VideoSurface.FadeBackdrop(BackdropSlot.SidePanel, false, 0);
        VideoSurface.FadeBackdrop(BackdropSlot.Toast, false, 0);
        VideoSurface.FadeBackdrop(BackdropSlot.SkipChapter, false, 0);
    }

    private void CleanupBackdrop()
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnPopupRendering;
        _popupOwner = null;
        _pendingPopup = null;
        _popupSurface = null;
    }

    /// <summary>The open flyout's presenter, so the blur follows it; null when that owner closed.</summary>
    private void SetPopupSurface(object? owner, FrameworkElement? content)
    {
        FrameworkElement? found = FindPresenter(content);
        if (found is Microsoft.UI.Xaml.Controls.Control presenter)
        {
            presenter.RequestedTheme = ThemeTokens.AppTheme; // a flyout's popup does not reliably take the page's in-app theme
        }
        ShowPopupSurface(owner, found);
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
        ShowPopupSurface(sender, presenter);
    }

    /// <summary>
    /// Flyouts have their own transition and acrylic off (12.7). A flyout's popup is a window of its own that reaches the
    /// screen a few frames after it opens, so a new one waits, invisible, for <see cref="PopupSettleFrames"/>, then it
    /// and its blur fade in on the same tick, length and curve. Closing takes both away at once. The fade runs on the
    /// presenter's template root, never the presenter: setting the presenter's own visual's opacity takes the whole
    /// flyout out of WinUI's hit testing, so clicks fell through it to the page.
    /// </summary>
    private void ShowPopupSurface(object? owner, FrameworkElement? presenter)
    {
        if (presenter is null)
        {
            if (owner is not null && !ReferenceEquals(owner, _popupOwner))
            {
                return; // an older flyout finished closing after a replacement flyout already opened
            }
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnPopupRendering;
            _popupOwner = null;
            _pendingPopup = null;
            _popupSurface = null;
            HideSurface(BackdropSlot.Popup);
            return;
        }

        if (!ReferenceEquals(presenter, _popupSurface))
        {
            _popupSurface = null;
            HideSurface(BackdropSlot.Popup);
        }

        _popupOwner = owner;
        _pendingPopup = presenter;
        if (TryGetFill(presenter, out UIElement? fill))
        {
            ElementCompositionPreview.GetElementVisual(fill!).Opacity = 0;
        }
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
        if (!_pendingPopup.IsLoaded)
        {
            _popupOwner = null;
            _pendingPopup = null;
            _popupSurface = null;
            HideSurface(BackdropSlot.Popup);
            return;
        }
        _popupSurface = _pendingPopup;
        _pendingPopup = null;
        if (TryGetFill(_popupSurface, out UIElement? fill))
        {
            if (_fader is null)
            {
                ElementCompositionPreview.GetElementVisual(fill!).Opacity = 1;
            }
            else
            {
                _fader.Reveal(fill!, true);
            }
        }
        PostBackdropRegion();
    }

    private static bool TryGetFill(FrameworkElement presenter, out UIElement? fill)
    {
        if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(presenter) > 0
            && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(presenter, 0) is UIElement root)
        {
            fill = root;
            return true;
        }
        fill = null;
        return false;
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

    /// <summary>Blurs behind the clip chip/drawer, dialog card, flyout, toast, and skip-chapter prompt.</summary>
    private void PostSurfaceRegions()
    {
        if (!_backdropAllowed || !_isLoaded || !VideoSurface.IsLoaded)
        {
            HideSurface(BackdropSlot.Clip);
            HideSurface(BackdropSlot.Card);
            HideSurface(BackdropSlot.Popup);
            HideSurface(BackdropSlot.Toast);
            HideSurface(BackdropSlot.SkipChapter);
            return;
        }

        PostClipRegion();

        FrameworkElement? card = _lanShown && LanStreamOverlay is { Visibility: Visibility.Visible } ? LanStreamCard
            : IsOpenUrlShown && OpenUrlOverlay is { Visibility: Visibility.Visible } ? OpenUrlCard
            : null;
        PostSurfaceRegion(BackdropSlot.Card, card);
        PostSurfaceRegion(BackdropSlot.Popup, _popupSurface);
        PostSurfaceRegion(BackdropSlot.Toast, _toasts is { IsShown: true } && ToastLayer is { Visibility: Visibility.Visible } ? ToastCard?.Surface : null);
        PostSurfaceRegion(BackdropSlot.SkipChapter, _skipChapterShown && SkipChapterLayer is { Visibility: Visibility.Visible } ? SkipChapterButton : null);
    }

    /// <summary>
    /// Keeps <see cref="BackdropSlot.Clip"/> synced to both <see cref="ClipDrawer"/>'s visibility/bounds and chrome
    /// visibility: its rectangle continues tracking layout changes during fade-out, but it never activates or fades
    /// in while chrome is hidden.
    /// </summary>
    private void PostClipRegion()
    {
        int index = (int)BackdropSlot.Clip;
        if (ClipLayer is not { Visibility: Visibility.Visible }
            || ClipDrawer is not { Visibility: Visibility.Visible } clip
            || clip.Surface is not { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } surface)
        {
            HideSurface(BackdropSlot.Clip);
            return;
        }
        if (MeasureSurface(surface) is not { } region)
        {
            HideSurface(BackdropSlot.Clip);
            return;
        }
        if (region != _postedSurfaceRegions[index])
        {
            _postedSurfaceRegions[index] = region;
            VideoSurface.PostBackdrop(BackdropSlot.Clip, region);
        }
        if (_chromeShown && !_surfaceShown[index])
        {
            if (!_surfaceFadingOut[index])
            {
                VideoSurface.FadeBackdrop(BackdropSlot.Clip, false, 0);
            }
            _surfaceShown[index] = true;
            _surfaceFadingOut[index] = false;
            VideoSurface.SetBackdropActive(BackdropSlot.Clip, true);
            VideoSurface.FadeBackdrop(BackdropSlot.Clip, true, _fader?.RevealDurationMilliseconds(true) ?? 0);
        }
    }

    /// <summary>Fades the Clips chip/drawer blur in lockstep with <see cref="ApplyChrome"/>.</summary>
    private void FadeClipBackdropForChrome(bool visible, double durationMilliseconds)
    {
        int index = (int)BackdropSlot.Clip;
        if (!visible)
        {
            if (_surfaceShown[index])
            {
                if (durationMilliseconds <= 0)
                {
                    HideSurface(BackdropSlot.Clip);
                }
                else
                {
                    _surfaceShown[index] = false;
                    _surfaceFadingOut[index] = true;
                    VideoSurface.FadeBackdrop(BackdropSlot.Clip, false, durationMilliseconds);
                }
            }
            return;
        }

        if (!_backdropAllowed || !_isLoaded || !VideoSurface.IsLoaded
            || ClipLayer is not { Visibility: Visibility.Visible }
            || ClipDrawer is not { Visibility: Visibility.Visible } clip
            || clip.Surface is not { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } surface)
        {
            return;
        }
        if (MeasureSurface(surface) is not { } region)
        {
            HideSurface(BackdropSlot.Clip);
            return;
        }
        if (region != _postedSurfaceRegions[index])
        {
            _postedSurfaceRegions[index] = region;
            VideoSurface.PostBackdrop(BackdropSlot.Clip, region);
        }
        if (!_surfaceShown[index] && !_surfaceFadingOut[index])
        {
            VideoSurface.FadeBackdrop(BackdropSlot.Clip, false, 0);
        }
        _surfaceShown[index] = true;
        _surfaceFadingOut[index] = false;
        VideoSurface.SetBackdropActive(BackdropSlot.Clip, true);
        VideoSurface.FadeBackdrop(BackdropSlot.Clip, true, durationMilliseconds);
    }

    private void PostSurfaceRegion(BackdropSlot slot, FrameworkElement? surface)
    {
        int index = (int)slot;
        bool visible = surface is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 };
        if (!visible || surface is null)
        {
            if (!_surfaceFadingOut[index])
            {
                HideSurface(slot);
            }
            return;
        }
        if (MeasureSurface(surface) is not { } region)
        {
            HideSurface(slot);
            return;
        }
        if (region != _postedSurfaceRegions[index])
        {
            _postedSurfaceRegions[index] = region;
            VideoSurface.PostBackdrop(slot, region);
        }
        // After the post, so the first frame of a new blur is drawn at the new card, not where the last one was.
        if (!_surfaceShown[index])
        {
            if (!_surfaceFadingOut[index])
            {
                VideoSurface.FadeBackdrop(slot, false, 0);
            }
            _surfaceShown[index] = true;
            _surfaceFadingOut[index] = false;
            VideoSurface.SetBackdropActive(slot, true);
            VideoSurface.FadeBackdrop(slot, true, _fader?.RevealDurationMilliseconds(true) ?? 0);
        }
    }

    /// <summary>Starts fading an active surface blur out alongside its exit animation.</summary>
    private void FadeOutSurface(BackdropSlot slot, double durationMilliseconds)
    {
        int index = (int)slot;
        if (!_surfaceShown[index])
        {
            return;
        }
        if (durationMilliseconds <= 0)
        {
            HideSurface(slot);
            return;
        }
        _surfaceShown[index] = false;
        _surfaceFadingOut[index] = true;
        VideoSurface.FadeBackdrop(slot, false, durationMilliseconds);
    }

    private void HideSurface(BackdropSlot slot)
    {
        int index = (int)slot;
        if (!_surfaceShown[index] && !_surfaceFadingOut[index] && _postedSurfaceRegions[index] == default)
        {
            return;
        }
        _surfaceShown[index] = false;
        _surfaceFadingOut[index] = false;
        _postedSurfaceRegions[index] = default;
        VideoSurface.SetBackdropActive(slot, false);
        VideoSurface.FadeBackdrop(slot, false, 0);
    }

    /// <summary>Where <paramref name="surface"/> sits over the video, in swap-chain pixels; null when it is out of the tree.</summary>
    private BackdropRegion? MeasureSurface(FrameworkElement surface)
    {
        float scaleX = VideoSurface.CompositionScaleX;
        float scaleY = VideoSurface.CompositionScaleY;
        Rect bounds;
        try
        {
            bounds = surface.TransformToVisual(VideoSurface).TransformBounds(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
        double radius = surface switch
        {
            Microsoft.UI.Xaml.Controls.Border border => border.CornerRadius.TopLeft,
            Microsoft.UI.Xaml.Controls.Control control => control.CornerRadius.TopLeft,
            _ => 0,
        };
        SwapChainTargetSize target = DxgiPresenter.ComputeTargetSize(VideoSurface.ActualWidth, VideoSurface.ActualHeight, scaleX, scaleY);
        return new BackdropRegion(
            (float)(bounds.X * scaleX), (float)(bounds.Y * scaleY), (float)(bounds.Width * scaleX), (float)(bounds.Height * scaleY),
            (float)(radius * scaleX), scaleX, target.Width, target.Height);
    }

    /// <summary>Re-measures the cards against the video surface and posts each if it moved, resized or the scale changed.</summary>
    private void PostBackdropRegion()
    {
        PostSurfaceRegions();
        if (!_backdropAllowed || !_isLoaded || !VideoSurface.IsLoaded)
        {
            return;
        }
        PostSidebarRegion();
        PostSideRegion();
        if (Osd.Visibility != Visibility.Visible || !Osd.IsLoaded)
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
        if (!_backdropAllowed || !_isLoaded || !VideoSurface.IsLoaded || Rail.Visibility != Visibility.Visible)
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
