using System.Numerics;
using Aether.Core.Playback;
using Aether.Core.Playback.Input;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Tour;
using Aether_App.Controls;
using Aether_App.Input;
using Aether_App.Settings;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Aether_App;

/// <summary>
/// The first-run tour (7.26, docs/reviews/pass-7-first-run-tour-spec.md): a scrim with a lit hole round one real
/// control at a time and a card beside it. <see cref="FirstRunTour"/> keeps the steps; this file finds each step's
/// control, moves the light to it on the bar's tween and brings the card in and out with the shared enter/exit
/// motion. It opens once on an idle home screen and again from Settings › About. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private const double TourStartDelayMilliseconds = 400;
    private const double LightPadding = 6;

    private readonly FirstRunTour _tour = new();
    private readonly MotionTween _tourLight = new(1);
    private DispatcherQueueTimer? _tourStartTimer;
    private TourRect _lightFrom;
    private TourRect _lightTo;
    private double _lightRadiusFrom;
    private double _lightRadiusTo;
    private bool _tourMenuShown;
    private bool _tourBarShown;

    private readonly record struct TourTarget(TourRect Rect, double Radius, bool Dashed = false);

    private void InitializeTour()
    {
        _tourStartTimer = DispatcherQueue.CreateTimer();
        _tourStartTimer.IsRepeating = false;
        _tourStartTimer.Interval = TimeSpan.FromMilliseconds(TourStartDelayMilliseconds);
        _tourStartTimer.Tick += OnTourStartTimerTick;
        _tourLight.Changed += (_, _) => ApplyLight();

        AutomationProperties.SetName(TourCard, AppStrings.Get("Tour_Name"));
        TourSkipButton.Content = AppStrings.Get("Tour_Skip");
        TourBackButton.Content = AppStrings.Get("Tour_Back");
    }

    /// <summary>Settings › About: shows the tour again over whatever is open.</summary>
    public void ShowTour()
    {
        SetSettingsShown(false);
        StartTour();
    }

    /// <summary>After the engine comes up: the tour opens once, on a launch that lands on an idle home screen.</summary>
    private async void QueueLaunchTour()
    {
        await App.SettingsLoaded;
        if (_isLoaded && !App.Settings.Current.TourSeen)
        {
            _tourStartTimer?.Start();
        }
    }

    private void OnTourStartTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (FirstRunTour.ShouldStartOnLaunch(App.Settings.Current.TourSeen, IsHomeIdle))
        {
            StartTour();
        }
    }

    private bool IsHomeIdle =>
        _isLoaded
        && _startup == StartupPhase.Ready
        && _session?.ViewModel.Status == PlaybackStatus.Idle
        && _pendingOpen is null
        && _openUrl.Stage == OpenUrlStage.Closed
        && !_settingsShown
        && !_dragging;

    private void StartTour()
    {
        if (_fader is null || !_tour.Start())
        {
            return;
        }
        // Saved as it opens, so closing the window halfway does not bring it back next time.
        App.Settings.Set(SettingKeys.TourSeen, true);
        _ = Task.Run(() =>
        {
            try
            {
                App.Settings.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Shown again next launch at worst.
            }
        });

        _chrome.SetHold(ChromeHolds.Tour, true);
        ApplyChrome();
        SetShortcutsEnabled(false);
        CloseRail();
        ClipDrawer.Close();

        _lightFrom = _lightTo = default;
        _tourBarShown = false;
        StartRingPulse();
        _fader.Enter(TourLayer, null);
        ShowTourStep(animate: false);
    }

    /// <summary>Ends the tour (Skip, Done, Esc, or the user going elsewhere). True if it was showing.</summary>
    private bool EndTour()
    {
        _tourStartTimer?.Stop();
        if (!_tour.End())
        {
            return false;
        }
        LeaveTourStep();
        _tourLight.Stop();
        StopRingPulse();
        if (_fader is not null)
        {
            _fader.Exit(TourCard, ChromeFader.FromBelow);
            _fader.Exit(TourLayer, null);
        }
        _chrome.SetHold(ChromeHolds.Tour, false);
        ApplyChrome();
        SetShortcutsEnabled(true);
        if (!_settingsShown && !IsOpenUrlShown)
        {
            Focus(FocusState.Programmatic);
        }
        return true;
    }

    private void SetShortcutsEnabled(bool enabled)
    {
        foreach (KeyboardAccelerator accelerator in _shortcutAccelerators)
        {
            accelerator.IsEnabled = enabled;
        }
    }

    private void OnTourNextClick(object sender, RoutedEventArgs e)
    {
        if (_tour.IsLast)
        {
            EndTour();
            return;
        }
        _tour.Next();
        ShowTourStep(animate: true);
    }

    private void OnTourBackClick(object sender, RoutedEventArgs e)
    {
        if (_tour.Back())
        {
            ShowTourStep(animate: true);
        }
    }

    private void OnTourSkipClick(object sender, RoutedEventArgs e) => EndTour();

    private void OnTourCardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Right:
                e.Handled = true;
                OnTourNextClick(this, e);
                break;
            case VirtualKey.Left:
                e.Handled = true;
                OnTourBackClick(this, e);
                break;
        }
    }

    private void OnTourLayerSizeChanged(object sender, SizeChangedEventArgs e) => RefreshTourLayout();

    /// <summary>The window or the bar changed size: the light and card jump to their new places rather than gliding.</summary>
    private void RefreshTourLayout()
    {
        if (!_tour.IsActive || TourLayer.ActualWidth <= 0)
        {
            return;
        }
        MoveLight(TourTargetOf(_tour.Current), animate: false);
        PlaceTourCard();
    }

    /// <summary>Undoes what a step opened for itself: the menu.</summary>
    private void LeaveTourStep()
    {
        if (_tourMenuShown)
        {
            _tourMenuShown = false;
            ContextMenu.Hide();
            ContextMenu.OverlayInputPassThroughElement = null;
        }
    }

    private void ShowTourStep(bool animate)
    {
        TourStep step = _tour.Current;
        LeaveTourStep();
        UpdateLayout(); // the bar may have grown

        MoveLight(TourTargetOf(step), animate);
        if (step == TourStep.Menu)
        {
            ShowTourMenu();
        }

        int index = _tour.Index;
        if (animate && _fader is not null && TourCard.Visibility == Visibility.Visible)
        {
            // The card steps aside while the light moves, then comes in beside its new target.
            _fader.Exit(TourCard, CardSlide(), () =>
            {
                if (_tour.IsActive && _tour.Index == index)
                {
                    FillTourCard();
                }
            });
        }
        else
        {
            FillTourCard();
        }
    }

    /// <summary>The real right-click menu, opened at the left edge without taking the keyboard; the card stays clickable.</summary>
    private void ShowTourMenu()
    {
        if (!TourLayout.MenuFits(TourLayer.ActualWidth, TourCard.Width))
        {
            return;
        }
        RefreshContextMenu();
        ContextMenu.OverlayInputPassThroughElement = TourLayer;
        ContextMenu.ShowAt(VideoHost, new FlyoutShowOptions
        {
            Position = new Point(TourLayout.MenuInset, TitleBarInset + TourLayout.MenuInset),
            ShowMode = FlyoutShowMode.Transient,
        });
        _tourMenuShown = true;
    }

    private double TitleBarInset => TitleBar.Visibility == Visibility.Visible ? TitleBar.Height : 0;

    /// <summary>The control a step lights, in the tour layer's coordinates, or null when it is not on screen.</summary>
    private TourTarget? TourTargetOf(TourStep step)
    {
        switch (step)
        {
            case TourStep.Open:
                if (HomeView.Visibility == Visibility.Visible && OpenActions.Visibility == Visibility.Visible)
                {
                    return Target(OpenActions, Motion.Token("AetherSizeControl") / 2 + LightPadding);
                }
                return Osd.Visibility == Visibility.Visible ? Target(OpenButton, 6 + LightPadding) : null;
            case TourStep.Controls:
                if (Osd.Visibility == Visibility.Visible)
                {
                    return Target(Osd, Osd.CornerRadius.TopLeft + LightPadding);
                }
                // No video: a dashed outline where the bar will sit.
                double width = Math.Min(Motion.Token("AetherSizeOsdWidth"), TourLayer.ActualWidth - Osd.Margin.Left - Osd.Margin.Right);
                double height = Motion.Token("AetherSizeOsdHeight");
                var slot = new TourRect(
                    (TourLayer.ActualWidth - width) / 2, TourLayer.ActualHeight - Osd.Margin.Bottom - height, width, height);
                return new TourTarget(slot.Inflate(LightPadding), Osd.CornerRadius.TopLeft + LightPadding, Dashed: true);
            case TourStep.Settings:
                // The gear sits flush with the top edge, so its ring goes just inside it.
                return TitleBar.Visibility == Visibility.Visible && SettingsButton.Visibility == Visibility.Visible
                    ? Target(SettingsButton, 4, padding: -2)
                    : null;
            default:
                return null;
        }
    }

    private TourTarget? Target(FrameworkElement element, double radius, double padding = LightPadding)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return null;
        }
        Rect bounds = element.TransformToVisual(TourLayer).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return new TourTarget(new TourRect(bounds.X, bounds.Y, bounds.Width, bounds.Height).Inflate(padding), radius);
    }

    /// <summary>Glides the light from where it is to <paramref name="target"/>, or shrinks it away when there is none.</summary>
    private void MoveLight(TourTarget? target, bool animate)
    {
        TourRect current = TourRect.Lerp(_lightFrom, _lightTo, _tourLight.Value);
        double radius = _lightRadiusFrom + (_lightRadiusTo - _lightRadiusFrom) * _tourLight.Value;
        _lightTo = target?.Rect ?? current.Collapse();
        _lightRadiusTo = target?.Radius ?? radius;
        TourRing.StrokeDashArray = target is { Dashed: true } ? new DoubleCollection { 4, 3 } : null;
        if (animate && current.IsEmpty && target is { } grow)
        {
            current = grow.Rect.Collapse(); // after a step with no light, it grows out of its new target
            radius = grow.Radius;
        }
        if (animate && !current.IsEmpty)
        {
            _lightFrom = current;
            _lightRadiusFrom = radius;
            _tourLight.JumpTo(0);
            _tourLight.AnimateTo(1, Motion.Token("AetherMotionQuickGrowMilliseconds"), Motion.EaseOut);
        }
        else
        {
            // Nothing lit yet: the light appears in place, with the scrim's fade.
            _lightFrom = _lightTo;
            _lightRadiusFrom = _lightRadiusTo;
            _tourLight.JumpTo(1);
        }
    }

    /// <summary>Cuts the light out of the scrim and draws the ring round it, at the tween's current point.</summary>
    private void ApplyLight()
    {
        double width = TourLayer.ActualWidth;
        double height = TourLayer.ActualHeight;
        double top = TitleBarInset;
        TourRect light = TourRect.Lerp(_lightFrom, _lightTo, _tourLight.Value);
        double radius = Math.Max(0, _lightRadiusFrom + (_lightRadiusTo - _lightRadiusFrom) * _tourLight.Value);

        var scrim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        scrim.Children.Add(new RectangleGeometry { Rect = new Rect(0, 0, width, height) });
        if (!light.IsEmpty)
        {
            scrim.Children.Add(RoundedRect(light, radius));
        }
        TourScrim.Data = scrim;
        // The title bar stays undimmed and live; a light reaching into it is cut off there.
        TourScrim.Clip = new RectangleGeometry { Rect = new Rect(0, top, width, Math.Max(0, height - top)) };
        TourBlocker.Margin = new Thickness(0, top, 0, 0);

        bool ring = light.Width >= 2 && light.Height >= 2;
        TourRing.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        if (ring)
        {
            TourRing.Margin = new Thickness(light.X, light.Y, 0, 0);
            TourRing.Width = light.Width;
            TourRing.Height = light.Height;
            TourRing.RadiusX = TourRing.RadiusY = Math.Min(radius, Math.Min(light.Width, light.Height) / 2);
        }
    }

    private static PathGeometry RoundedRect(TourRect r, double radius)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        var corner = new Size(radius, radius);
        var figure = new PathFigure { StartPoint = new Point(r.X + radius, r.Y), IsClosed = true, IsFilled = true };
        void Line(double x, double y) => figure.Segments.Add(new LineSegment { Point = new Point(x, y) });
        void Arc(double x, double y)
        {
            if (radius > 0)
            {
                figure.Segments.Add(new ArcSegment { Point = new Point(x, y), Size = corner, SweepDirection = SweepDirection.Clockwise });
            }
        }
        Line(r.Right - radius, r.Y);
        Arc(r.Right, r.Y + radius);
        Line(r.Right, r.Bottom - radius);
        Arc(r.Right - radius, r.Bottom);
        Line(r.X + radius, r.Bottom);
        Arc(r.X, r.Bottom - radius);
        Line(r.X, r.Y + radius);
        Arc(r.X + radius, r.Y);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>The card comes from its target's side: down from above it, up from below, in from the left.</summary>
    private Vector2 CardSlide() => FirstRunTour.PlacementOf(_tour.Current) switch
    {
        TourPlacement.Below => ChromeFader.FromAbove,
        TourPlacement.Right => ChromeFader.FromLeft,
        _ => ChromeFader.FromBelow,
    };

    /// <summary>Puts the current step's words and keys on the card, places it and brings it in.</summary>
    private void FillTourCard()
    {
        TourStep step = _tour.Current;
        (string title, string body) = step switch
        {
            TourStep.Open => ("Tour_Open_Title", "Tour_Open_Body"),
            TourStep.Controls => ("Tour_Controls_Title", "Tour_Controls_Body"),
            TourStep.Menu => ("Tour_Menu_Title", "Tour_Menu_Body"),
            TourStep.Shortcuts => ("Tour_Shortcuts_Title", "Tour_Shortcuts_Body"),
            _ => ("Tour_Settings_Title", "Tour_Settings_Body"),
        };
        TourProgress.Text = AppStrings.Format("Tour_Progress", _tour.Index + 1, _tour.Count);
        TourTitle.Text = AppStrings.Get(title);
        TourBody.Text = AppStrings.Get(body);
        FillTourKeys(step == TourStep.Shortcuts);
        FillTourBar(animate: _tourBarShown);
        TourBackButton.Visibility = _tour.IsFirst ? Visibility.Collapsed : Visibility.Visible;
        TourNextButton.Content = AppStrings.Get(_tour.IsLast ? "Tour_Done" : "Tour_Next");
        AutomationProperties.SetName(TourNextButton, (string)TourNextButton.Content);

        PlaceTourCard();
        _fader?.Enter(TourCard, CardSlide());
        TourNextButton.Focus(FocusState.Programmatic);

        string announcement = $"{TourProgress.Text}. {TourTitle.Text}. {TourBody.Text}";
        (FrameworkElementAutomationPeer.FromElement(TourCard) ?? FrameworkElementAutomationPeer.CreatePeerForElement(TourCard))
            ?.RaiseNotificationEvent(
                AutomationNotificationKind.Other,
                AutomationNotificationProcessing.ImportantMostRecent,
                announcement,
                "AetherTour");
    }

    private void PlaceTourCard()
    {
        TourStep step = _tour.Current;
        double width = TourLayer.ActualWidth;
        double height = TourLayer.ActualHeight;
        // The margin is the card's position, so it is cleared first or the measured height would include it.
        TourCard.Margin = default;
        // A collapsed card measures as nothing, which put it where its real height ran off the window.
        bool wasCollapsed = TourCard.Visibility != Visibility.Visible;
        if (wasCollapsed)
        {
            TourCard.Visibility = Visibility.Visible;
        }
        TourCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (wasCollapsed)
        {
            TourCard.Visibility = Visibility.Collapsed;
        }
        TourRect? target = step switch
        {
            TourStep.Menu => _tourMenuShown ? new TourRect(TourLayout.MenuInset, TitleBarInset, TourLayout.MenuWidth, height - TitleBarInset) : null,
            // Beside the home screen's content, in the open space, rather than over the file list.
            TourStep.Shortcuts => HomeContentBounds(),
            _ => TourTargetOf(step)?.Rect,
        };
        (double x, double y) = TourLayout.PlaceCard(
            target, TourCard.Width, TourCard.DesiredSize.Height, width, height, FirstRunTour.PlacementOf(step));
        TourCard.Margin = new Thickness(x, y, 0, 0);
    }

    private TourRect? HomeContentBounds() =>
        HomeView.Visibility == Visibility.Visible && HomeHero.ActualWidth > 0 && Target(HomeHero, 0, padding: 0) is { } home
            ? home.Rect
            : null;

    /// <summary>The Shortcuts step lists the user's keys as they are now; unbound ones are left out.</summary>
    private void FillTourKeys(bool show)
    {
        TourKeys.Children.Clear();
        TourKeys.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            return;
        }
        foreach (ShortcutAction action in FirstRunTour.Keys)
        {
            ShortcutEntry entry = App.Shortcuts.Get(action);
            if (entry.IsUnbound)
            {
                continue;
            }
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = entry.Name,
                Style = (Style)Application.Current.Resources["AetherBodyTextStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var keys = new KeycapStrip { Keys = entry.Bindings[0].Keycaps };
            Grid.SetColumn(keys, 1);
            row.Children.Add(keys);
            TourKeys.Children.Add(row);
        }
    }

    /// <summary>The progress bar fills to the current step; it grows from where it was unless motion is off.</summary>
    private void FillTourBar(bool animate)
    {
        float fraction = (_tour.Index + 1f) / _tour.Count;
        Visual visual = ElementCompositionPreview.GetElementVisual(TourBarFill);
        visual.StopAnimation("Scale.X");
        if (animate && Motion.AnimationsEnabled)
        {
            Compositor compositor = visual.Compositor;
            ScalarKeyFrameAnimation grow = compositor.CreateScalarKeyFrameAnimation();
            grow.InsertExpressionKeyFrame(0f, "this.StartingValue");
            grow.InsertKeyFrame(1f, fraction, compositor.CreateCubicBezierEasingFunction(
                new Vector2(Motion.EaseOut.X, Motion.EaseOut.Y), new Vector2(Motion.EaseOut.Z, Motion.EaseOut.W)));
            grow.Duration = TimeSpan.FromMilliseconds(Motion.Token("AetherMotionQuickGrowMilliseconds"));
            visual.StartAnimation("Scale.X", grow);
        }
        else
        {
            visual.Scale = new Vector3(fraction, 1, 1);
        }
        _tourBarShown = true;
    }

    /// <summary>A gentle pulse on the ring round the lit control; none with Windows animations off.</summary>
    private void StartRingPulse()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(TourRing);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;
        if (!Motion.AnimationsEnabled)
        {
            return;
        }
        Compositor compositor = visual.Compositor;
        ScalarKeyFrameAnimation pulse = compositor.CreateScalarKeyFrameAnimation();
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.37f, 0), new Vector2(0.63f, 1));
        pulse.InsertKeyFrame(0f, 1f);
        pulse.InsertKeyFrame(0.5f, 0.4f, ease);
        pulse.InsertKeyFrame(1f, 1f, ease);
        pulse.Duration = TimeSpan.FromMilliseconds(1800);
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", pulse);
    }

    private void StopRingPulse()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(TourRing);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;
    }
}
