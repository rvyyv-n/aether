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
    private bool _barExpanded = true;
    private double _playSlide; // how far Play and Mute move from their full-card spots to the slim pill's
    private double _volumeSlide;
    private double _seekInsetLeft; // the seek row's side insets in the slim pill, between Play and Mute
    private double _seekInsetRight;

    private UIElement[] BarExtras =>
    [
        SpeedButton, BackButton, ForwardButton, MoreButton,
    ];

    private void InitializeBar()
    {
        _bar = new MotionTween(1);
        _bar.Changed += (_, _) => ApplyBarProgress();
        foreach (FrameworkElement element in new FrameworkElement[] { ButtonRow, LeftGroup, CentreGroup, RightGroup })
        {
            element.SizeChanged += (_, _) => MeasureBarSlides();
        }
        foreach (UIElement button in new UIElement[]
        {
            OpenButton, SpeedButton, BackButton, PlayPauseButton, ForwardButton, VolumeButton, MoreButton, FullScreenButton,
        })
        {
            PressFeedback.Attach(button);
        }
    }

    /// <summary>Grows or shrinks the card to match the chrome state. Called from <see cref="ApplyChrome"/>.</summary>
    private void SyncBar()
    {
        bool expanded = _chrome.IsExpanded;
        if (_bar is null || expanded == _barExpanded)
        {
            return;
        }
        _barExpanded = expanded;
        // The hidden buttons stop taking the pointer at once; Tab still reaches them, and focus grows the card.
        foreach (UIElement extra in BarExtras)
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
        if (_bar is null)
        {
            return;
        }
        // A spring runs a little past 0 and 1: the card overshoots in height, and squeezes its padding below the slim pill.
        double p = _bar.Value;
        double slim = Motion.Token("AetherSizeOsdSlimHeight");
        double full = Motion.Token("AetherSizeOsdHeight");
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
        foreach (UIElement extra in BarExtras)
        {
            extra.Opacity = extras;
        }

        Thickness clip = ClipLayer.Margin;
        ClipLayer.Margin = new Thickness(clip.Left, clip.Top, clip.Right,
            Osd.Margin.Bottom + height + Motion.Token("AetherSizeOsdGap"));
        UpdateToastPlacement();
        PlaceRail();
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
