using System.Globalization;
using System.Numerics;
using Aether.Core.Playback;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Aether_App.Controls;

/// <summary>
/// The Springy profile's press squash: a button shrinks to AetherMotionSpringPressScale while pressed and springs
/// back on release, on the compositor. Nothing happens in Quick or with Windows animations off. Uses the element's
/// own Scale, so it does not clash with the Translation the collapsed bar sets on Play and Mute. UI thread only.
/// </summary>
internal static class PressFeedback
{
    private static readonly ConditionalWeakTable<UIElement, StrongBox<bool>> Squashed = new();

    public static void Attach(UIElement element)
    {
        // Buttons mark pointer presses handled, so listen for handled events too.
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Squash(element, true)), true);
        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Squash(element, false)), true);
        element.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => Squash(element, false)), true);
        element.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => Squash(element, false)), true);
    }

    private static void Squash(UIElement element, bool pressed)
    {
        StrongBox<bool> squashed = Squashed.GetOrCreateValue(element);
        if (pressed == squashed.Value || (pressed && (!Motion.AnimationsEnabled || !Motion.IsSpringy)))
        {
            return;
        }
        squashed.Value = pressed;
        if (element is FrameworkElement framed)
        {
            element.CenterPoint = new Vector3((float)framed.ActualWidth / 2, (float)framed.ActualHeight / 2, 0);
        }
        float scale = pressed ? (float)Motion.Token("AetherMotionSpringPressScale") : 1f;
        element.StartAnimation(Spring(new Vector3(scale, scale, 1)));
    }

    // The token spring at half its period (a press is small and quick), sampled into key frames from wherever the
    // squash is now: XAML element properties take key-frame animations, and starting from StartingValue lets a release
    // pick up a press midway.
    private static Vector3KeyFrameAnimation Spring(Vector3 to)
    {
        const double frame = 1.0 / 60;
        IReadOnlyList<double> path = new DampedSpring(
            Motion.Token("AetherMotionSpringDampingRatio"),
            Motion.Token("AetherMotionSpringPeriodMilliseconds") / 2000).Sample(frame);
        Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();
        Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Target = "Scale";
        animation.SetVector3Parameter("To", to);
        CompositionEasingFunction linear = compositor.CreateLinearEasingFunction();
        animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
        for (int i = 1; i < path.Count; i++)
        {
            string progress = path[i].ToString("0.######", CultureInfo.InvariantCulture);
            animation.InsertExpressionKeyFrame(i / (float)(path.Count - 1), $"this.StartingValue + (To - this.StartingValue) * {progress}", linear);
        }
        animation.Duration = TimeSpan.FromSeconds(frame * (path.Count - 1));
        return animation;
    }
}
