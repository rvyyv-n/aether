using System.Globalization;

namespace Aether.Core.Playback.Settings;

/// <summary>
/// How far mpv's <c>sub-margin-y</c> has to be to keep subtitles clear of the transport bar. The margin is counted in
/// mpv's 720-high scaled pixels, and it stacks on top of what <c>sub-pos</c> already lifts the text by.
/// </summary>
public static class SubtitleLift
{
    /// <summary>mpv's own <c>sub-margin-y</c>, kept when nothing needs clearing.</summary>
    public const double DefaultMargin = 22;

    private const double ScaledHeight = 720;
    private const double MaxMargin = ScaledHeight / 2;

    /// <summary>
    /// The <c>sub-margin-y</c> that puts the last line <paramref name="clearance"/> above the bottom of the picture,
    /// both in the same units as <paramref name="videoHeight"/> (the picture's height on screen). A position that
    /// already sits above the clearance, such as Top, keeps the default margin.
    /// </summary>
    public static string MarginY(string position, double clearance, double videoHeight)
    {
        double margin = DefaultMargin;
        if (clearance > 0 && videoHeight > 0 && double.TryParse(MpvSettingsMap.SubtitlePosition(position), NumberStyles.Float, CultureInfo.InvariantCulture, out double pos))
        {
            double needed = clearance * ScaledHeight / videoHeight;
            double lifted = (100 - pos) / 100 * ScaledHeight;
            margin = Math.Clamp(Math.Ceiling(needed - lifted), DefaultMargin, MaxMargin);
        }
        return margin.ToString(CultureInfo.InvariantCulture);
    }
}
