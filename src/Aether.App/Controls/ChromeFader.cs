using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace Aether_App.Controls;

/// <summary>
/// Fades chrome elements by animating their Composition visual's opacity, so the fade runs on the
/// compositor (GPU) and never on the UI thread. The two fade animations are created once and reused.
/// The elements' XAML <c>Opacity</c> is left alone, so nothing fights the animation. Toasts and the clip
/// drawer also slide in with it (<see cref="Slide"/>), following the motion profile.
/// </summary>
internal sealed class ChromeFader
{
    private readonly ScalarKeyFrameAnimation _show;
    private readonly ScalarKeyFrameAnimation _hide;
    private readonly TimeSpan _showDuration;
    private readonly TimeSpan _hideDuration;

    public ChromeFader(Compositor compositor, TimeSpan showDuration, TimeSpan hideDuration)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        _showDuration = showDuration;
        _hideDuration = hideDuration;
        CubicBezierEasingFunction easeOut = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        _show = Create(compositor, 1f, showDuration, easeOut);
        _hide = Create(compositor, 0f, hideDuration, easeOut);
    }

    /// <summary>How long <see cref="Fade"/> takes right now: zero with Windows animations off.</summary>
    public double DurationMilliseconds(bool visible) =>
        Motion.AnimationsEnabled ? (visible ? _showDuration : _hideDuration).TotalMilliseconds : 0;

    /// <summary>Fades <paramref name="element"/> in or out from wherever its opacity is now.</summary>
    public void Fade(UIElement element, bool visible)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (!Motion.AnimationsEnabled)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = visible ? 1f : 0f;
            return;
        }
        visual.StartAnimation("Opacity", visible ? _show : _hide);
    }

    /// <summary>
    /// Slides <paramref name="element"/> up into place, or a little down as it leaves, on the compositor; pair it with
    /// <see cref="Fade"/>. Quick eases in from AetherMotionQuickSlide; Springy springs in from further, with a slight
    /// overshoot. Leaving is the same quick ease-in in both.
    /// </summary>
    public void Slide(UIElement element, bool visible)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Translation");
        if (!Motion.AnimationsEnabled)
        {
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }

        Compositor compositor = visual.Compositor;
        Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();
        float quick = (float)Motion.Token("AetherMotionQuickSlide");
        if (!visible)
        {
            animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
            animation.InsertKeyFrame(1f, new Vector3(0, quick / 2, 0), Easing(compositor, Motion.EaseIn));
            animation.Duration = TimeSpan.FromMilliseconds(Motion.Token("AetherMotionQuickOutMilliseconds"));
        }
        else if (Motion.IsSpringy)
        {
            // The UI thread's spring, sampled every frame, so the toast and drawer move like the transport card.
            const double frame = 1.0 / 60;
            IReadOnlyList<double> path = Spring.FromTokens().Sample(frame);
            float from = (float)Motion.Token("AetherMotionSpringSlide");
            for (int i = 0; i < path.Count; i++)
            {
                animation.InsertKeyFrame(i / (float)(path.Count - 1), new Vector3(0, from * (1 - (float)path[i]), 0), compositor.CreateLinearEasingFunction());
            }
            animation.Duration = TimeSpan.FromSeconds(frame * (path.Count - 1));
        }
        else
        {
            animation.InsertKeyFrame(0f, new Vector3(0, quick, 0));
            animation.InsertKeyFrame(1f, Vector3.Zero, Easing(compositor, Motion.EaseOut));
            animation.Duration = TimeSpan.FromMilliseconds(Motion.Token("AetherMotionQuickInMilliseconds"));
        }
        visual.StartAnimation("Translation", animation);
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
