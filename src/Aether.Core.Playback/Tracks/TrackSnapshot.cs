using System.Globalization;
using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback.Tracks;

/// <summary>The tracks of the open file and the two delays, as mpv has them right now.</summary>
public sealed record TrackSnapshot(TrackList Tracks, double AudioDelay, double SubtitleDelay)
{
    public static TrackSnapshot Empty { get; } = new(TrackList.Empty, 0, 0);

    /// <summary>
    /// Reads the properties from the calling thread, so it must not be the event thread or the UI thread of a busy
    /// frame: the dispatcher never carries <c>track-list</c>, which allocates a tree on every change. Returns
    /// <see cref="Empty"/> while the core shuts down.
    /// </summary>
    public static TrackSnapshot Read(MpvContext context)
    {
        try
        {
            return new TrackSnapshot(
                TrackList.Parse(context.GetPropertyString("track-list")),
                Seconds(context.GetPropertyString("audio-delay")),
                Seconds(context.GetPropertyString("sub-delay")));
        }
        catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
        {
            return Empty;
        }
    }

    private static double Seconds(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
}
