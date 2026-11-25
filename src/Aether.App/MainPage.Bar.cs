using System.Numerics;
using Aether.Core.Playback;
using Aether_App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Aether_App;

/// <summary>
/// The collapsed bar (7.11): while a video plays the transport is the slim one-row pill (s5-08), and any chrome hold
/// (hover, keyboard focus, pause, a drag, a menu or dialog) grows it into the full card. <see cref="ChromeVisibility.IsExpanded"/>
/// decides; this file animates between the two. One progress value, 0 slim to 1 full, drives the card's height, the seek
/// row's place, Play and Mute sliding to their slim spots and the other buttons fading; the clip chip and toasts ride on
/// the card's height. It runs on the UI thread because the height goes through layout, which the render thread's blur
/// follows (PostBackdropRegion on LayoutUpdated). UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private const double ButtonGap = 4;

    private MotionTween? _bar;
    private MotionTween? _subtitleLift; // 0 with the bar hidden, 1 with it showing
    private UIElement[]? _barExtras;
    private TransportBarControl[] _enabledBarControls = [];
    private double _baseOsdMaxWidth;
    private double _osdSlimHeight;
    private double _osdFullHeight;
    private double _osdGap;
    private bool _barExpanded = true;
    private double _playSlide; // how far Play and Mute move from their full-card spots to the slim pill's
    private double _volumeSlide;
    private double _seekInsetLeft; // the seek row's side insets in the slim pill, between Play and Mute
    private double _seekInsetRight;

    private void InitializeBar()
    {
        _baseOsdMaxWidth = Osd.MaxWidth;
        _osdSlimHeight = Motion.Token("AetherSizeOsdSlimHeight");
        _osdFullHeight = Motion.Token("AetherSizeOsdHeight");
        _osdGap = Motion.Token("AetherSizeOsdGap");

        _bar = new MotionTween(1);
        _bar.Changed += (_, _) => ApplyBarProgress();
        _subtitleLift = new MotionTween(0);
        _subtitleLift.Changed += (_, _) => UpdateSubtitleLift();
        VideoSurface.SizeChanged += (_, _) => UpdateSubtitleLift();
        foreach (FrameworkElement element in new FrameworkElement[] { ButtonRow, LeftGroup, CentreGroup, RightGroup })
        {
            element.SizeChanged += (_, _) => MeasureBarSlides();
        }
        foreach (UIElement button in new UIElement[]
        {
            OpenButton, SpeedButton, QualityButton, BackButton, StepBackButton, PlayPauseButton,
            StepForwardButton, ForwardButton, TracksButton, LoopButton, AlwaysOnTopButton,
            PrevChapterButton, ChaptersButton, NextChapterButton, VolumeButton, MoreButton, FullScreenButton,
        })
        {
            PressFeedback.Attach(button);
        }
        ApplyTransportBarLayout();
    }

    /// <summary>Applies user-configured optional bar control visibility and order (12.3) while keeping slim-pill anchors fixed.</summary>
    internal void ApplyTransportBarLayout()
    {
        IReadOnlyList<TransportBarItem> items = TransportBarLayout.Parse(App.Settings.Current.TransportBarControls);
        var enabled = new List<TransportBarControl>(items.Count);
        bool skipVisible = false;
        bool frameStepVisible = false;
        int skipOrder = int.MaxValue;
        int frameStepOrder = int.MaxValue;
        var sideItems = new List<(TransportBarControl Control, UIElement[] Buttons)>(6);

        for (int i = 0; i < items.Count; i++)
        {
            TransportBarItem item = items[i];
            if (item.Visible)
            {
                enabled.Add(item.Control);
            }
            switch (item.Control)
            {
                case TransportBarControl.Skip:
                    skipVisible = item.Visible;
                    skipOrder = i;
                    break;
                case TransportBarControl.FrameStep:
                    frameStepVisible = item.Visible;
                    frameStepOrder = i;
                    break;
                case TransportBarControl.Speed:
                    SpeedButton.Visibility = item.Visible ? Visibility.Visible : Visibility.Collapsed;
                    if (item.Visible) sideItems.Add((item.Control, [SpeedButton]));
                    break;
                case TransportBarControl.Quality:
                    UpdateQualityButtonVisibility(item.Visible);
                    if (item.Visible) sideItems.Add((item.Control, [QualityButton]));
                    break;
                case TransportBarControl.Tracks:
                    TracksButton.Visibility = item.Visible ? Visibility.Visible : Visibility.Collapsed;
                    if (item.Visible) sideItems.Add((item.Control, [TracksButton]));
                    break;
                case TransportBarControl.Loop:
                    LoopButton.Visibility = item.Visible ? Visibility.Visible : Visibility.Collapsed;
                    if (item.Visible) sideItems.Add((item.Control, [LoopButton]));
                    break;
                case TransportBarControl.AlwaysOnTop:
                    AlwaysOnTopButton.Visibility = item.Visible ? Visibility.Visible : Visibility.Collapsed;
                    if (item.Visible) sideItems.Add((item.Control, [AlwaysOnTopButton]));
                    break;
                case TransportBarControl.Chapters:
                    Visibility chapVis = item.Visible ? Visibility.Visible : Visibility.Collapsed;
                    PrevChapterButton.Visibility = chapVis;
                    ChaptersButton.Visibility = chapVis;
                    NextChapterButton.Visibility = chapVis;
                    if (item.Visible) sideItems.Add((item.Control, [PrevChapterButton, ChaptersButton, NextChapterButton]));
                    break;
            }
        }
        _enabledBarControls = [.. enabled];

        Visibility skipVis = skipVisible ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = skipVis;
        ForwardButton.Visibility = skipVis;
        Visibility stepVis = frameStepVisible ? Visibility.Visible : Visibility.Collapsed;
        StepBackButton.Visibility = stepVis;
        StepForwardButton.Visibility = stepVis;

        // Keep OpenButton pinned first in LeftGroup and Volume/More/FullScreen pinned last in RightGroup for slim-pill geometry.
        int leftSideCount = sideItems.Count <= 2 ? sideItems.Count : (sideItems.Count + 1) / 2;
        LeftGroup.Children.Clear();
        LeftGroup.Children.Add(OpenButton);
        for (int i = 0; i < leftSideCount; i++)
        {
            foreach (UIElement button in sideItems[i].Buttons)
            {
                LeftGroup.Children.Add(button);
            }
        }

        CentreGroup.Children.Clear();
        if (skipOrder <= frameStepOrder)
        {
            CentreGroup.Children.Add(BackButton);
            CentreGroup.Children.Add(StepBackButton);
            CentreGroup.Children.Add(PlayPauseButton);
            CentreGroup.Children.Add(StepForwardButton);
            CentreGroup.Children.Add(ForwardButton);
        }
        else
        {
            CentreGroup.Children.Add(StepBackButton);
            CentreGroup.Children.Add(BackButton);
            CentreGroup.Children.Add(PlayPauseButton);
            CentreGroup.Children.Add(ForwardButton);
            CentreGroup.Children.Add(StepForwardButton);
        }

        RightGroup.Children.Clear();
        for (int i = leftSideCount; i < sideItems.Count; i++)
        {
            foreach (UIElement button in sideItems[i].Buttons)
            {
                RightGroup.Children.Add(button);
            }
        }
        RightGroup.Children.Add(VolumeButton);
        RightGroup.Children.Add(MoreButton);
        RightGroup.Children.Add(FullScreenButton);

        _barExtras =
        [
            SpeedButton, QualityButton, BackButton, StepBackButton, StepForwardButton, ForwardButton,
            TracksButton, LoopButton, AlwaysOnTopButton, PrevChapterButton, ChaptersButton, NextChapterButton, MoreButton,
        ];

        int visibleCount = 5; // Open, Play/Pause, Volume, More, FullScreen
        if (skipVisible) visibleCount += 2;
        if (frameStepVisible) visibleCount += 2;
        foreach ((TransportBarControl ctrl, UIElement[] btns) in sideItems)
        {
            if (ctrl == TransportBarControl.Quality && _currentWebLink is null)
            {
                continue;
            }
            visibleCount += btns.Length;
        }
        Osd.MaxWidth = Math.Min(760, _baseOsdMaxWidth + Math.Max(0, visibleCount - 8) * 40);
        if (ClipLayer is not null)
        {
            ClipLayer.MaxWidth = Osd.MaxWidth;
        }

        double extrasOpacity = _bar is { Value: var progress } ? Math.Clamp((progress - 0.4) / 0.6, 0, 1) : 1;
        foreach (UIElement extra in _barExtras)
        {
            extra.IsHitTestVisible = _barExpanded;
            extra.Opacity = extrasOpacity;
        }

        SyncLoop();
        SyncAlwaysOnTop();
        UpdateChapterButtons();
        DispatcherQueue.TryEnqueue(MeasureBarSlides);
    }

    private bool IsBarControlEnabled(TransportBarControl control) =>
        Array.IndexOf(_enabledBarControls, control) >= 0;

    private void ApplyBarLookMode(LookCornerRadii radii)
    {
        Osd.CornerRadius = new CornerRadius(radii.Transport);
        PlayPauseButton.CornerRadius = new CornerRadius(radii.PlayButton);
        var btnRadius = new CornerRadius(radii.TransportButton);
        foreach (Button button in new[]
        {
            OpenButton, SpeedButton, QualityButton, BackButton, StepBackButton, StepForwardButton, ForwardButton,
            TracksButton, LoopButton, AlwaysOnTopButton, PrevChapterButton, ChaptersButton, NextChapterButton, VolumeButton, MoreButton, FullScreenButton,
        })
        {
            button.CornerRadius = btnRadius;
        }
    }

    /// <summary>Grows or shrinks the card to match the chrome state. Called from <see cref="ApplyChrome"/>.</summary>
    private void SyncBar()
    {
        bool expanded = _chrome.IsExpanded;
        if (_bar is null || _barExtras is null || expanded == _barExpanded)
        {
            return;
        }
        _barExpanded = expanded;
        // The hidden buttons stop taking the pointer at once; Tab still reaches them, and focus grows the card.
        foreach (UIElement extra in _barExtras)
        {
            extra.IsHitTestVisible = expanded;
        }
        if (Osd.Visibility != Visibility.Visible || !_chromeShown)
        {
            _bar.JumpTo(expanded ? 1 : 0);
        }
        else if (expanded)
        {
            _bar.AnimateTo(1, Motion.Token("AetherMotionQuickGrowMilliseconds"), Motion.EaseOut);
        }
        else
        {
            _bar.AnimateTo(0, Motion.Token("AetherMotionQuickShrinkMilliseconds"), Motion.EaseIn);
        }
    }

    /// <summary>
    /// Keeps the subtitles above the bar while it shows, and eases them back down as it hides. Only the part of the bar
    /// that is over the picture counts, so a letterboxed video leaves the subtitles where they are.
    /// </summary>
    private void SyncSubtitleLift()
    {
        if (_subtitleLift is null)
        {
            return;
        }
        bool shown = _chromeShown && Osd.Visibility == Visibility.Visible;
        if (shown == (_subtitleLift.Target > 0))
        {
            return;
        }
        if (Osd.Visibility != Visibility.Visible)
        {
            _subtitleLift.JumpTo(0);
            return;
        }
        string duration = shown ? "AetherMotionChromeShowMilliseconds" : "AetherMotionChromeHideMilliseconds";
        _subtitleLift.AnimateTo(shown ? 1 : 0, Motion.Token(duration), Motion.EaseOut);
    }

    private void UpdateSubtitleLift()
    {
        if (_subtitleLift is null || _session is not { } session)
        {
            return;
        }
        double bar = (Osd.Margin.Bottom + Osd.Height + _osdGap) * Math.Clamp(_subtitleLift.Value, 0, 1);
        double letterbox = Math.Max(0, (VideoHost.ActualHeight - VideoSurface.ActualHeight) / 2);
        session.SetSubtitleClearance(Math.Max(0, bar - letterbox), VideoSurface.ActualHeight);
    }

    /// <summary>Where Play, Mute and the seek row sit in the slim pill, from the full card's layout.</summary>
    private void MeasureBarSlides()
    {
        double playSlim = LeftGroup.ActualOffset.X + OpenButton.ActualOffset.X + OpenButton.ActualWidth + ButtonGap;
        _playSlide = playSlim - (CentreGroup.ActualOffset.X + PlayPauseButton.ActualOffset.X);
        double volumeSlim = RightGroup.ActualOffset.X + FullScreenButton.ActualOffset.X - ButtonGap - VolumeButton.ActualWidth;
        _volumeSlide = volumeSlim - (RightGroup.ActualOffset.X + VolumeButton.ActualOffset.X);
        _seekInsetLeft = playSlim + PlayPauseButton.ActualWidth + ButtonGap;
        _seekInsetRight = ButtonRow.ActualWidth - volumeSlim + ButtonGap;
        ApplyBarProgress();
    }

    private void ApplyBarProgress()
    {
        if (_bar is null || _barExtras is null)
        {
            return;
        }
        // A spring runs a little past 0 and 1: the card overshoots in height, and squeezes its padding below the slim pill.
        double p = _bar.Value;
        double slim = _osdSlimHeight;
        double full = _osdFullHeight;
        double row = ButtonRow.Height;
        double height = slim + ((full - slim) * p);
        double stroke = Osd.BorderThickness.Top;
        double slimPadding = ((slim - row) / 2) - stroke;
        double fullPadding = ((full - row - SeekRow.Height - ButtonGap) / 2) - stroke;
        double padding = Math.Max(0, Math.Min(slimPadding + ((fullPadding - slimPadding) * p), ((height - row) / 2) - stroke));
        Osd.Height = height;
        Osd.Padding = new Thickness(padding);

        double slide = 1 - Math.Clamp(p, 0, 1);
        SeekRow.Margin = new Thickness(_seekInsetLeft * slide, (row - SeekRow.Height) / 2 * slide, _seekInsetRight * slide, 0);
        PlayPauseButton.Translation = new Vector3((float)(_playSlide * (1 - p)), 0, 0);
        VolumeButton.Translation = new Vector3((float)(_volumeSlide * (1 - p)), 0, 0);
        // The extras come in over the second half of the grow and are gone in the first part of the shrink.
        double extras = Math.Clamp((p - 0.4) / 0.6, 0, 1);
        foreach (UIElement extra in _barExtras)
        {
            extra.Opacity = extras;
        }

        if (ClipLayer is not null)
        {
            Thickness clip = ClipLayer.Margin;
            ClipLayer.Margin = new Thickness(clip.Left, clip.Top, clip.Right,
                Osd.Margin.Bottom + height + _osdGap);
        }
        UpdateToastPlacement();
        PlaceRail();
        UpdateSubtitleLift();
    }

    // Tabbing onto any transport control grows the card; the buttons only take focus from the keyboard.
    private void OnOsdGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (args.FocusState == FocusState.Keyboard)
        {
            _chrome.SetHold(ChromeHolds.KeyboardFocus, true);
            ApplyChrome();
        }
    }

    private void OnOsdLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        for (DependencyObject? next = args.NewFocusedElement; next is not null; next = VisualTreeHelper.GetParent(next))
        {
            if (ReferenceEquals(next, Osd))
            {
                return;
            }
        }
        _chrome.SetHold(ChromeHolds.KeyboardFocus, false);
        ApplyChrome();
    }
}
