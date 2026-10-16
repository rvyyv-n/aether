using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Aether_App.Controls;

/// <summary>
/// Fades and slides elements by animating their Composition visual, so the motion runs on the compositor (GPU) and
/// never on the UI thread. The elements' XAML <c>Opacity</c> is left alone, so nothing fights the animation.
/// Two fades: the chrome's (<see cref="Fade"/>, in step with the blur and caption glyphs) and the shorter enter/exit
/// one (<see cref="Reveal"/>) that goes with a slide (<see cref="Enter"/>, <see cref="Exit"/>) for toasts, the clip
/// drawer, the rail, Settings, diagnostics and the Open URL dialog. Slides follow the motion profile.
/// </summary>
internal sealed class ChromeFader
{
    /// <summary>Slide directions: where the element comes in from, and goes back towards as it leaves.</summary>
    public static readonly Vector2 FromBelow = new(0, 1);
    public static readonly Vector2 FromAbove = new(0, -1);
    public static readonly Vector2 FromLeft = new(-1, 0);

    // Bumped on every Enter and Exit, so an exit that finishes after the element came back does not collapse it.
    private static readonly ConditionalWeakTable<UIElement, StrongBox<int>> Generations = new();

    private readonly Compositor _compositor;
    private readonly ScalarKeyFrameAnimation _show;
    private readonly ScalarKeyFrameAnimation _hide;
    private readonly ScalarKeyFrameAnimation _reveal;
    private readonly ScalarKeyFrameAnimation _conceal;
    private readonly TimeSpan _showDuration;
    private readonly TimeSpan _hideDuration;
    private readonly TimeSpan _revealDuration;
    private readonly TimeSpan _concealDuration;

    public ChromeFader(Compositor compositor, TimeSpan showDuration, TimeSpan hideDuration)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _showDuration = showDuration;
        _hideDuration = hideDuration;
        _revealDuration = TimeSpan.FromMilliseconds(Motion.Token("AetherMotionQuickInMilliseconds"));
        _concealDuration = TimeSpan.FromMilliseconds(Motion.Token("AetherMotionQuickOutMilliseconds"));
        CubicBezierEasingFunction easeOut = Easing(compositor, Motion.EaseOut);
        _show = Create(compositor, 1f, showDuration, easeOut);
        _hide = Create(compositor, 0f, hideDuration, easeOut);
        _reveal = Create(compositor, 1f, _revealDuration, easeOut);
        _conceal = Create(compositor, 0f, _concealDuration, easeOut);
    }

    /// <summary>A fader on the page's compositor with the chrome timings.</summary>
    public static ChromeFader ForElement(UIElement element) => new(
        ElementCompositionPreview.GetElementVisual(element).Compositor,
        TimeSpan.FromMilliseconds(Motion.Token("AetherMotionChromeShowMilliseconds")),
        TimeSpan.FromMilliseconds(Motion.Token("AetherMotionChromeHideMilliseconds")));

    /// <summary>How long <see cref="Fade"/> takes right now: zero with Windows animations off.</summary>
    public double DurationMilliseconds(bool visible) =>
        Motion.AnimationsEnabled ? (visible ? _showDuration : _hideDuration).TotalMilliseconds : 0;

    /// <summary>How long <see cref="Reveal"/> takes right now: zero with Windows animations off.</summary>
    public double RevealDurationMilliseconds(bool visible) =>
        Motion.AnimationsEnabled ? (visible ? _revealDuration : _concealDuration).TotalMilliseconds : 0;

    /// <summary>The chrome fade: <paramref name="element"/> in or out from wherever its opacity is now.</summary>
    public void Fade(UIElement element, bool visible) => FadeWith(element, visible, _show, _hide);

    /// <summary>The enter/exit fade, quicker out than the chrome's; pair it with <see cref="Slide(UIElement, bool, Vector2)"/>.</summary>
    public void Reveal(UIElement element, bool visible) => FadeWith(element, visible, _reveal, _conceal);

    /// <summary>Slides up into place from below, or a little down as it leaves.</summary>
    public void Slide(UIElement element, bool visible) => Slide(element, visible, FromBelow);

    /// <summary>
    /// Slides <paramref name="element"/> into place from <paramref name="from"/>, or a little back that way as it
    /// leaves, on the compositor. Quick eases in from AetherMotionQuickSlide; Springy springs in from further, with a
    /// slight overshoot. Leaving is the same quick ease-in in both.
    /// </summary>
    public void Slide(UIElement element, bool visible, Vector2 from)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Translation");
        if (!Motion.AnimationsEnabled)
        {
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }

        Vector3 direction = new(from, 0);
        Vector3KeyFrameAnimation animation = _compositor.CreateVector3KeyFrameAnimation();
        float quick = (float)Motion.Token("AetherMotionQuickSlide");
        if (!visible)
        {
            animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
            animation.InsertKeyFrame(1f, direction * (quick / 2), Easing(_compositor, Motion.EaseIn));
            animation.Duration = _concealDuration;
        }
        else if (Motion.IsSpringy)
        {
            // The UI thread's spring, sampled every frame, so these move like the transport card.
            const double frame = 1.0 / 60;
            IReadOnlyList<double> path = Motion.Spring().Sample(frame);
            float distance = (float)Motion.Token("AetherMotionSpringSlide");
            CompositionEasingFunction linear = _compositor.CreateLinearEasingFunction();
            for (int i = 0; i < path.Count; i++)
            {
                animation.InsertKeyFrame(i / (float)(path.Count - 1), direction * (distance * (1 - (float)path[i])), linear);
            }
            animation.Duration = TimeSpan.FromSeconds(frame * (path.Count - 1));
        }
        else
        {
            animation.InsertKeyFrame(0f, direction * quick);
            animation.InsertKeyFrame(1f, Vector3.Zero, Easing(_compositor, Motion.EaseOut));
            animation.Duration = _revealDuration;
        }
        visual.StartAnimation("Translation", animation);
    }

    /// <summary>
    /// Makes <paramref name="element"/> visible and brings it in: fades up from nothing unless it was still leaving,
    /// sliding from <paramref name="from"/> (null to only fade, for a full-window scrim).
    /// </summary>
    public void Enter(UIElement element, Vector2? from)
    {
        Bump(element);
        element.IsHitTestVisible = true;
        if (element.Visibility != Visibility.Visible)
        {
            ElementCompositionPreview.GetElementVisual(element).Opacity = 0;
            element.Visibility = Visibility.Visible;
        }
        Reveal(element, true);
        if (from is { } direction)
        {
            Slide(element, true, direction);
        }
    }

    /// <summary>
    /// Takes <paramref name="element"/> out, then collapses it, so nothing invisible takes clicks or Tab, and calls
    /// <paramref name="collapsed"/>. Neither happens if it enters again first. It stops taking the pointer at once.
    /// </summary>
    public void Exit(UIElement element, Vector2? from, Action? collapsed = null)
    {
        int generation = Bump(element);
        if (element.Visibility != Visibility.Visible)
        {
            collapsed?.Invoke();
            return;
        }
        element.IsHitTestVisible = false;
        CompositionScopedBatch? batch = Motion.AnimationsEnabled ? _compositor.CreateScopedBatch(CompositionBatchTypes.Animation) : null;
        Reveal(element, false);
        if (from is { } direction)
        {
            Slide(element, false, direction);
        }
        if (batch is null)
        {
            element.Visibility = Visibility.Collapsed;
            collapsed?.Invoke();
            return;
        }
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (Generations.TryGetValue(element, out StrongBox<int>? box) && box.Value == generation)
            {
                element.Visibility = Visibility.Collapsed;
                collapsed?.Invoke();
            }
        };
    }

    private static int Bump(UIElement element) => ++Generations.GetOrCreateValue(element).Value;

    private static void FadeWith(UIElement element, bool visible, ScalarKeyFrameAnimation show, ScalarKeyFrameAnimation hide)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (!Motion.AnimationsEnabled)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = visible ? 1f : 0f;
            return;
        }
        visual.StartAnimation("Opacity", visible ? show : hide);
    }

    private static CubicBezierEasingFunction Easing(Compositor compositor, Vector4 curve) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(curve.X, curve.Y), new Vector2(curve.Z, curve.W));

    private static ScalarKeyFrameAnimation Create(Compositor compositor, float to, TimeSpan duration, CompositionEasingFunction easing)
    {
        ScalarKeyFrameAnimation animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
        animation.InsertKeyFrame(1f, to, easing);
        animation.Duration = duration;
        return animation;
    }
}
