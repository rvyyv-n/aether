using System.Globalization;

namespace Aether.Core.Playback.Streams;

/// <summary>One row of the link quality picker. <see cref="Height"/> is 0 for Auto and Audio only.</summary>
public sealed record QualityOption(string Key, string Label, int Height, bool HighFps, bool AudioOnly)
{
    public const string AutoKey = "auto";
    public const string AudioKey = "audio";

    public bool IsAuto => Key == AutoKey;

    /// <summary>
    /// The yt-dlp format selector for this option, or null for Auto so the Streaming quality setting applies.
    /// The high frame rate form falls back to the plain height so a missing 60 fps stream still plays.
    /// </summary>
    public string? FormatSelector()
    {
        if (IsAuto)
        {
            return null;
        }
        if (AudioOnly)
        {
            return "ba/b";
        }

        string h = Height.ToString(CultureInfo.InvariantCulture);
        string plain = $"bv*[height<={h}]+ba/b[height<={h}]";
        return HighFps ? $"bv*[height<={h}][fps>=50]+ba/b[height<={h}][fps>=50]/{plain}" : plain;
    }
}

public static class QualityOptions
{
    /// <summary>Frame rate from which a resolution gets its own "60" entry.</summary>
    private const double HighFpsThreshold = 50;

    public static QualityOption Auto { get; } = new(QualityOption.AutoKey, "Auto", 0, false, false);

    /// <summary>Auto, then each distinct resolution highest first, then Audio only when the stream has an audio-only format.</summary>
    public static IReadOnlyList<QualityOption> From(IReadOnlyList<StreamFormat>? formats)
    {
        var options = new List<QualityOption> { Auto };
        if (formats is null)
        {
            return options;
        }

        var seen = new HashSet<(int Height, bool HighFps)>();
        bool hasAudioOnly = false;
        foreach (var format in formats)
        {
            if (IsVideo(format))
            {
                seen.Add((format.Height!.Value, format.Fps >= HighFpsThreshold));
            }
            else if (HasCodec(format.AudioCodec))
            {
                hasAudioOnly = true;
            }
        }

        foreach (var (height, highFps) in seen.OrderByDescending(s => s.Height).ThenByDescending(s => s.HighFps))
        {
            options.Add(ForResolution(height, highFps));
        }
        if (hasAudioOnly)
        {
            options.Add(new QualityOption(QualityOption.AudioKey, "Audio only", 0, false, true));
        }
        return options;
    }

    /// <summary>
    /// The option to use for a remembered key: the exact match, else the same height at the other frame rate,
    /// else the nearest lower resolution, else Auto.
    /// </summary>
    public static QualityOption Resolve(string? savedKey, IReadOnlyList<QualityOption> available)
    {
        var fallback = available.FirstOrDefault(o => o.IsAuto) ?? Auto;
        if (string.IsNullOrEmpty(savedKey) || savedKey == QualityOption.AutoKey)
        {
            return fallback;
        }

        var exact = available.FirstOrDefault(o => o.Key == savedKey);
        if (exact is not null)
        {
            return exact;
        }
        if (!TryParseKey(savedKey, out int height, out bool highFps))
        {
            return fallback;
        }

        var video = available.Where(o => !o.IsAuto && !o.AudioOnly).ToList();
        return video.FirstOrDefault(o => o.Height == height)
            ?? video.Where(o => o.Height < height).OrderByDescending(o => o.Height).ThenByDescending(o => o.HighFps == highFps).FirstOrDefault()
            ?? fallback;
    }

    private static QualityOption ForResolution(int height, bool highFps)
    {
        string h = height.ToString(CultureInfo.InvariantCulture);
        return new QualityOption(highFps ? $"h{h}f60" : $"h{h}", highFps ? $"{h}p60" : $"{h}p", height, highFps, false);
    }

    private static bool IsVideo(StreamFormat format) => format.Height is > 0 && !string.Equals(format.VideoCodec, "none", StringComparison.OrdinalIgnoreCase);

    private static bool HasCodec(string? codec) => !string.IsNullOrEmpty(codec) && !string.Equals(codec, "none", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseKey(string key, out int height, out bool highFps)
    {
        height = 0;
        highFps = key.EndsWith("f60", StringComparison.Ordinal);
        string digits = key.StartsWith('h') ? key[1..(highFps ? ^3 : ^0)] : "";
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out height) && height > 0;
    }
}
