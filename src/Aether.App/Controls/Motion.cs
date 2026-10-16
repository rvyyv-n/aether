using System.Globalization;
using System.Numerics;
using Aether.Core.Playback;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace Aether_App.Controls;

/// <summary>
/// Reduced motion: with Windows animation effects off, fades are instant (handoff, locked decisions).
/// Read on each fade, so a change in Settings applies without a restart. Also the motion profile
/// (7.15 preview: Quick or Springy) and the curves both profiles share. UI thread.
/// </summary>
internal static class Motion
{
    private static readonly UISettings Settings = new();

    public static bool AnimationsEnabled => Settings.AnimationsEnabled;

    /// <summary>Settings › Advanced › Motion style (preview); read as each animation starts, so a change applies at once.</summary>
    public static bool IsSpringy => App.Settings.Current.MotionStyle == "springy";

    public static double Token(string key) => (double)Application.Current.Resources[key];

    /// <summary>A cubic-bezier token, "x1,y1,x2,y2".</summary>
    public static Vector4 Curve(string key)
    {
        string[] parts = ((string)Application.Current.Resources[key]).Split(',');
        float At(int i) => float.Parse(parts[i], CultureInfo.InvariantCulture);
        return new Vector4(At(0), At(1), At(2), At(3));
    }

    public static Vector4 EaseOut => Curve("AetherMotionEaseOut");

    public static Vector4 EaseIn => Curve("AetherMotionEaseIn");

    /// <summary>The eased fraction at time fraction <paramref name="t"/> (0..1) of <paramref name="curve"/>.</summary>
    public static double Ease(Vector4 curve, double t) => new CubicBezier(curve.X, curve.Y, curve.Z, curve.W).Ease(t);

    /// <summary>The Springy profile's spring, from the AetherMotionSpring* tokens.</summary>
    public static DampedSpring Spring() => new(
        Token("AetherMotionSpringDampingRatio"),
        Token("AetherMotionSpringPeriodMilliseconds") / 1000);
}
