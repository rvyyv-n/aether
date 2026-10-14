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

    /// <summary>The value mpv should hold now: whole hertz such as "120", or <see cref="Unknown"/>.</summary>
    public string Current { get; private set; } = Unknown;

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
    public string? Accept(nint monitor, int hertz)
    {
        _monitor = monitor;
        _hasReading = true;

        string value = Format(hertz);
        if (value == Current)
        {
            return null;
        }
        Current = value;
        return value;
    }

    /// <summary>The <c>display-fps-override</c> text for a reading: whole hertz, or <see cref="Unknown"/> when it is not a real rate.</summary>
    public static string Format(int hertz) =>
        hertz is >= MinimumHertz and <= MaximumHertz
            ? hertz.ToString(CultureInfo.InvariantCulture)
            : Unknown;
}
