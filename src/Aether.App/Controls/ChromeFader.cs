using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace Aether_App.Controls;

/// <summary>
/// Fades chrome elements by animating their Composition visual's opacity, so the fade runs on the
/// compositor (GPU) and never on the UI thread. The two animations are created once and reused.
/// The elements' XAML <c>Opacity</c> is left alone, so nothing fights the animation.
/// </summary>
internal sealed class ChromeFader
{
    private readonly ScalarKeyFrameAnimation _show;
    private readonly ScalarKeyFrameAnimation _hide;

    public ChromeFader(Compositor compositor, TimeSpan showDuration, TimeSpan hideDuration)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        CubicBezierEasingFunction easeOut = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        _show = Create(compositor, 1f, showDuration, easeOut);
        _hide = Create(compositor, 0f, hideDuration, easeOut);
    }

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

    private static ScalarKeyFrameAnimation Create(Compositor compositor, float to, TimeSpan duration, CompositionEasingFunction easing)
    {
        ScalarKeyFrameAnimation animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
        animation.InsertKeyFrame(1f, to, easing);
        animation.Duration = duration;
        return animation;
    }
}
