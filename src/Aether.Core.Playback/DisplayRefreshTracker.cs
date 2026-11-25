using System.Globalization;

namespace Aether.Core.Playback;

/// <summary>
/// Decides what to tell mpv's <c>display-fps-override</c> as the window's display changes, and when the display needs
/// reading again. With <c>video-sync=display-resample</c> mpv has no way of seeing the refresh rate on Windows and
/// assumes 60 Hz, which drops every other frame of a 120 fps clip; this gives it the real rate.
/// <para>Threading: one thread (the UI thread). No system calls, so it is testable without a display.</para>
/// </summary>
public sealed class DisplayRefreshTracker
{
    /// <summary>mpv's value for "no override", so it picks its own rate.</summary>
    public const string Unknown = "0";

    // Windows reports 0 or 1 for "hardware default"; nothing real is outside this range.
    private const int MinimumHertz = 10;
    private const int MaximumHertz = 1000;

    private nint _monitor;
    private bool _hasReading;

    /// <summary>The value mpv should hold now: hertz such as "120" or "59.94", or <see cref="Unknown"/>.</summary>
    public string Current { get; private set; } = Unknown;

    /// <summary>The measured monitor refresh rate in hertz, or 0 when unknown.</summary>
    public double CurrentHertz { get; private set; }

    /// <summary>
    /// True when the display should be read again: after a display change (<paramref name="displayChanged"/>, because
    /// the same monitor can switch rate), when the window is on another monitor, or before the first reading.
    /// </summary>
    public bool NeedsReading(nint monitor, bool displayChanged) =>
        displayChanged || !_hasReading || monitor != _monitor;

    /// <summary>
    /// Records a reading of <paramref name="monitor"/>. Returns the new value for mpv, or null when it is the same as
    /// <see cref="Current"/> and nothing needs sending.
    /// </summary>
    public string? Accept(nint monitor, double hertz)
    {
        _monitor = monitor;
        _hasReading = true;
        CurrentHertz = hertz is >= MinimumHertz and <= MaximumHertz ? hertz : 0;

        string value = Format(hertz);
        if (value == Current)
        {
            return null;
        }
        Current = value;
        return value;
    }

    /// <summary>
    /// The effective <c>display-fps-override</c> sent to mpv: fractional rates up to 60 Hz (such as 59.94 or 60)
    /// when Smooth motion is enabled, and <see cref="Unknown"/> on high-refresh (&gt;60 Hz) displays per ADR-0018.
    /// </summary>
    public static string EffectiveOverride(double refreshHz, bool smoothMotion) =>
        smoothMotion && refreshHz is >= MinimumHertz and <= 60.5 ? Format(refreshHz) : Unknown;

    /// <summary>
    /// The <c>display-fps-override</c> text for a reading: hertz to three decimals without trailing zeros ("60",
    /// "59.94"), or <see cref="Unknown"/> when it is not a real rate. The fraction matters: pacing against 60 on a
    /// 59.94 Hz display drops a frame every 16 seconds or so.
    /// </summary>
    public static string Format(double hertz) =>
        hertz is >= MinimumHertz and <= MaximumHertz
            ? hertz.ToString("0.###", CultureInfo.InvariantCulture)
            : Unknown;
}
