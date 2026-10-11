using System.Globalization;

namespace Aether.Core.Playback;

/// <summary>The speed pill and its menu (screen 5.4): 0.1× to 4.0×.</summary>
public static class PlaybackSpeed
{
    public const double Min = 0.1;
    public const double Max = 4.0;
    public const double Normal = 1.0;

    public static IReadOnlyList<double> Options { get; } = [0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0];

    public static double Clamp(double speed) => double.IsFinite(speed) ? Math.Clamp(speed, Min, Max) : Normal;

    /// <summary>"1.0×", "0.25×": always one decimal, so the pill keeps its width as the speed changes.</summary>
    public static string Format(double speed) =>
        Clamp(speed).ToString("0.0#", CultureInfo.InvariantCulture) + "×";

    /// <summary>mpv echoes the speed back as a double; match the menu item within rounding.</summary>
    public static bool IsCurrent(double option, double speed) => Math.Abs(option - speed) < 0.005;
}
