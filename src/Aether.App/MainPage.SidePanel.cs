using Aether.Core.Interop.Rendering;
using Aether.Core.Playback;
using Aether_App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Aether_App;

/// <summary>What the card on the right edge shows.</summary>
internal enum SidePanel
{
    None,
    Queue,
    Transcript,
}

/// <summary>
/// The card on the right edge, which shows the queue drawer (13.3) or the transcript (15.3), one at a time. It slides in
/// from the edge with the render thread's blur behind it and holds the chrome while it is open; picking the other
/// switches the card in place. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private const double SideInset = 16;

    // 0 off the right edge, 1 in place. A render transform, so TransformToVisual sees it and the blur follows each step.
    private readonly MotionTween _sideSlide = new(0);
    private readonly TranslateTransform _sideOffset = new();
    private SidePanel _sidePanel;
    // Stays set to the outgoing panel until its exit slide/fade completes so TranscriptPane slides out and tracks blur cleanly.
    private SidePanel _visibleSidePanel;
    private bool _sideLayerWired;

    private void InitializeQueue()
    {
        _sideSlide.Changed += (_, _) => ApplySideSlide();
        VideoSurface.FadeBackdrop(BackdropSlot.SidePanel, false, 0);
    }

    private void EnsureSideLayer()
    {
        if (SideLayer is not null)
        {
            return;
        }
        App.EnsureSettingsControlsLoaded();
        FindName(nameof(SideLayer));
        if (SideLayer is null || _sideLayerWired)
        {
            return;
        }
        _sideLayerWired = true;
        QueueDrawer.Queue = _playlist;
        QueueDrawer.PlayRequested += (_, item) => Open(item, fromQueue: true);
        QueueDrawer.Edited += (_, _) =>
        {
            _queueIsFolder = false; // the user's own order now: a jump must not read the folder back over it
            Render();
        };
        QueueDrawer.ModeChanged += (_, _) => Render();
        QueueDrawer.CloseRequested += (_, _) => CloseSidePanel();
        TranscriptPane.CloseRequested += (_, _) => CloseSidePanel();
        TranscriptPane.SeekRequested += (_, seconds) => OnSeekRequested(seconds, dragging: false);
        SideLayer.RenderTransform = _sideOffset;
        ApplySideLookMode();
    }

    private void ApplySideLookMode()
    {
        if (SideLayer is null)
        {
            return;
        }
        var radius = new CornerRadius(LookCornerRadii.For(_nativeLook ? LookModes.Native : LookModes.Aether).SidePanel);
        QueueDrawer.SetCardCornerRadius(radius);
        TranscriptPane.SetCardCornerRadius(radius);
    }

    private bool IsSidePanelAvailable => !_settingsShown && !_tour.IsActive && !IsPictureInPicture && _openUrl.Stage != OpenUrlStage.Editing;

    private FrameworkElement? SideSurface =>
        SideLayer is null
            ? null
            : _visibleSidePanel switch
            {
                SidePanel.Transcript => TranscriptPane?.Surface,
                SidePanel.Queue => QueueDrawer?.Surface,
                _ => null,
            };

    private void OnQueueAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => ToggleSidePanel(args, SidePanel.Queue);

    private void OnTranscriptAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => ToggleSidePanel(args, SidePanel.Transcript);

    private void ToggleSidePanel(KeyboardAcceleratorInvokedEventArgs args, SidePanel panel)
    {
        if (ArePlayerKeysPaused)
        {
            return;
        }
        args.Handled = true;
        if (_sidePanel == panel)
        {
            CloseSidePanel();
        }
        else
        {
            ShowSidePanel(panel);
        }
    }

    private void OnQueueMenuClick(object sender, RoutedEventArgs e) => ShowSidePanel(SidePanel.Queue);

    private void OnTranscriptMenuClick(object sender, RoutedEventArgs e) => ShowSidePanel(SidePanel.Transcript);

    private void OnRailQueueClick(object sender, RoutedEventArgs e)
    {
        CloseRail();
        ShowSidePanel(SidePanel.Queue);
    }

    private void ShowSidePanel(SidePanel panel)
    {
        if (_sidePanel == panel || _fader is null || !IsSidePanelAvailable)
        {
            return;
        }
        EnsureSideLayer();
        if (SideLayer is null)
        {
            return;
        }
        bool opening = _sidePanel == SidePanel.None;
        _sidePanel = panel;
        _visibleSidePanel = panel;
        QueueDrawer.Visibility = panel == SidePanel.Queue ? Visibility.Visible : Visibility.Collapsed;
        TranscriptPane.Visibility = panel == SidePanel.Transcript ? Visibility.Visible : Visibility.Collapsed;
        if (panel == SidePanel.Queue)
        {
            QueueDrawer.Refresh();
            QueueDrawer.ShowPlaying();
        }
        else
        {
            LoadTranscript();
        }
        if (opening)
        {
            ClipDrawer?.Close(); // both grow out of the same corner
            PlaceSidePanel();
            _fader.Enter(SideLayer, null);
            _sideSlide.AnimateTo(1, Motion.Token("AetherMotionQuickInMilliseconds"), Motion.EaseOut);
            VideoSurface.SetBackdropActive(BackdropSlot.SidePanel, _backdropAllowed);
            VideoSurface.FadeBackdrop(BackdropSlot.SidePanel, true, _fader.RevealDurationMilliseconds(true));
            PostSideRegion();
            SyncDrawerHold();
        }
        else
        {
            PostSideRegion();
        }
    }

    /// <summary>Takes the card away and gives the keyboard back to the page. True if it was open.</summary>
    private bool CloseSidePanel()
    {
        if (_sidePanel == SidePanel.None || _fader is null || SideLayer is null)
        {
            return false;
        }
        _sidePanel = SidePanel.None;
        _fader.Exit(SideLayer, null, () =>
        {
            if (_sidePanel == SidePanel.None)
            {
                _visibleSidePanel = SidePanel.None;
                _postedSurfaceRegions[(int)BackdropSlot.SidePanel] = default;
                VideoSurface.SetBackdropActive(BackdropSlot.SidePanel, false);
            }
        });
        _sideSlide.AnimateTo(0, Motion.Token("AetherMotionQuickOutMilliseconds"), Motion.EaseIn);
        VideoSurface.FadeBackdrop(BackdropSlot.SidePanel, false, _fader.RevealDurationMilliseconds(false));
        SyncDrawerHold();
        Focus(FocusState.Programmatic);
        return true;
    }

    /// <summary>The queue list follows the queue whenever the page renders: a new file, a jump, a folder read again.</summary>
    private void RefreshSidePanel()
    {
        if (_sidePanel == SidePanel.Queue)
        {
            QueueDrawer?.Refresh();
        }
        if (_sidePanel != SidePanel.None && !IsSidePanelAvailable)
        {
            CloseSidePanel();
        }
    }

    private void SyncDrawerHold()
    {
        _chrome.SetHold(ChromeHolds.Drawer, (ClipDrawer?.IsOpen == true) || (ClipDrawer?.IsDraggingOut == true) || _sidePanel != SidePanel.None);
        ApplyChrome();
    }

    /// <summary>From under the title bar to 8 above the full transport card, 16 in from the right edge.</summary>
    private void PlaceSidePanel()
    {
        if (SideLayer is null)
        {
            return;
        }
        double top = (TitleBar.Visibility == Visibility.Visible ? TitleBar.Height : 0) + 8;
        double bottom = Osd.Margin.Bottom + _osdFullHeight + 8;
        SideLayer.Margin = new Thickness(0, top, SideInset, bottom);
    }

    private void ApplySideSlide()
    {
        if (SideSurface is not { } surface)
        {
            return;
        }
        _sideOffset.X = (1 - _sideSlide.Value) * (surface.ActualWidth + SideInset);
        PostSideRegion();
    }

    private void PostSideRegion()
    {
        if (_backdropAllowed && SideLayer is { Visibility: Visibility.Visible }
            && SideSurface is { } surface && MeasureSurface(surface) is { } region
            && region != _postedSurfaceRegions[(int)BackdropSlot.SidePanel])
        {
            _postedSurfaceRegions[(int)BackdropSlot.SidePanel] = region;
            VideoSurface.PostBackdrop(BackdropSlot.SidePanel, region);
        }
    }
}
