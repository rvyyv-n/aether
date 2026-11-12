using System.Text.Json;

namespace Aether.Core.Playback.Streams;

/// <summary>
/// How far back a live stream can be scrubbed (16.4): from the earliest point mpv still holds to the newest, on the
/// stream's own clock, read from <c>demuxer-cache-state</c>. The seek bar shows this window; the LIVE badge jumps to its
/// end.
/// </summary>
public readonly record struct LiveWindow(double Start, double End)
{
    /// <summary>Within this of the newest point, playback is at the live edge.</summary>
    public const double EdgeSeconds = 5;

    /// <summary>A jump to the edge lands this far short of the newest point, so it does not stall waiting for data.</summary>
    public const double EdgeMargin = 1;

    public double Length => End - Start;

    public double EdgeTarget => Math.Max(Start, End - EdgeMargin);

    public bool IsAtEdge(double position) => End - position <= EdgeSeconds;

    public double BehindBy(double position) => Math.Max(0, End - position);

    public double Clamp(double target) => Math.Clamp(target, Start, EdgeTarget);

    public double ToBar(double position) => Math.Clamp(position - Start, 0, Length);

    public double FromBar(double seconds) => Clamp(Start + seconds);

    /// <summary>The window in mpv's <c>demuxer-cache-state</c>; null while nothing can be sought.</summary>
    public static LiveWindow? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("seekable-ranges", out JsonElement ranges) || ranges.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            double start = double.MaxValue;
            double end = double.MinValue;
            foreach (JsonElement range in ranges.EnumerateArray())
            {
                if (range.ValueKind == JsonValueKind.Object
                    && range.TryGetProperty("start", out JsonElement s) && s.TryGetDouble(out double from)
                    && range.TryGetProperty("end", out JsonElement e) && e.TryGetDouble(out double to))
                {
                    start = Math.Min(start, from);
                    end = Math.Max(end, to);
                }
            }
            return end > start ? new LiveWindow(start, end) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
