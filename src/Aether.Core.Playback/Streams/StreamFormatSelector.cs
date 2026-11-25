using System.Globalization;

namespace Aether.Core.Playback.Streams;

public static class StreamFormatSelector
{
    private const double HighFpsThreshold = 50.0;

    public static ResolvedStream? TrySelect(ResolvedStream cached, StreamResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(cached);
        ArgumentNullException.ThrowIfNull(options);

        if (cached.Formats is null || cached.Formats.Count == 0)
        {
            return null;
        }

        if (options.RequestedQuality is null && !string.IsNullOrWhiteSpace(options.FormatSelectorOverride))
        {
            return null;
        }

        if (options.RequestedQuality is { AudioOnly: true })
        {
            if (!string.IsNullOrEmpty(cached.AudioUrl))
            {
                return cached with
                {
                    StreamUrl = cached.AudioUrl,
                    AudioUrl = null,
                    VideoFormatId = cached.AudioFormatId,
                    AudioFormatId = null,
                };
            }

            StreamFormat? audioOnly = SelectBestAudio(cached.Formats);
            if (audioOnly is null)
            {
                return null;
            }

            return cached with
            {
                StreamUrl = audioOnly.Url,
                AudioUrl = null,
                VideoFormatId = audioOnly.FormatId,
                AudioFormatId = null,
                HttpHeaders = NonEmptyHeaders(audioOnly.HttpHeaders) ?? cached.HttpHeaders,
            };
        }

        int maxHeight;
        bool highFps = false;
        if (options.RequestedQuality is null || options.RequestedQuality.IsAuto)
        {
            maxHeight = ParseStreamingQualityMaxHeight(options.StreamingQuality);
        }
        else if (options.RequestedQuality.Height > 0)
        {
            maxHeight = options.RequestedQuality.Height;
            highFps = options.RequestedQuality.HighFps;
        }
        else
        {
            return null;
        }

        StreamFormat? bestAudio = string.IsNullOrEmpty(cached.AudioUrl) ? SelectBestAudio(cached.Formats) : null;
        bool hasPairableAudio = !string.IsNullOrEmpty(cached.AudioUrl) || bestAudio is not null;

        var candidates = new List<StreamFormat>();
        foreach (StreamFormat format in cached.Formats)
        {
            if (!IsPlayable(format) || !HasCodec(format.VideoCodec))
            {
                continue;
            }

            if (maxHeight > 0 && (format.Height is null || format.Height.Value <= 0 || format.Height.Value > maxHeight))
            {
                continue;
            }

            if (!HasCodec(format.AudioCodec) && !hasPairableAudio)
            {
                continue;
            }

            candidates.Add(format);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (highFps)
        {
            var highFpsCandidates = candidates.Where(f => (f.Fps ?? 0) >= HighFpsThreshold).ToList();
            if (highFpsCandidates.Count > 0)
            {
                candidates = highFpsCandidates;
            }
        }

        StreamFormat picked = candidates
            .OrderByDescending(f => f.Height ?? 0)
            .ThenByDescending(f => !options.PreferGpuDecodableFormats || !IsAv1(f.VideoCodec))
            .ThenByDescending(f => IsDirectHttp(f.Protocol))
            .ThenByDescending(f => f.Bitrate ?? 0)
            .ThenByDescending(f => f.Fps ?? 0)
            .First();

        string? audioUrl = null;
        string? audioId = null;
        if (!HasCodec(picked.AudioCodec))
        {
            if (!string.IsNullOrEmpty(cached.AudioUrl))
            {
                audioUrl = cached.AudioUrl;
                audioId = cached.AudioFormatId;
            }
            else if (bestAudio is not null)
            {
                audioUrl = bestAudio.Url;
                audioId = bestAudio.FormatId;
            }
        }

        return cached with
        {
            StreamUrl = picked.Url,
            AudioUrl = audioUrl,
            VideoFormatId = picked.FormatId,
            AudioFormatId = audioId,
            HttpHeaders = NonEmptyHeaders(picked.HttpHeaders) ?? cached.HttpHeaders,
        };
    }

    private static StreamFormat? SelectBestAudio(IReadOnlyList<StreamFormat> formats)
    {
        StreamFormat? best = null;
        foreach (StreamFormat format in formats)
        {
            if (!IsPlayable(format) || HasCodec(format.VideoCodec) || !HasCodec(format.AudioCodec))
            {
                continue;
            }

            if (best is null || CompareAudio(format, best) > 0)
            {
                best = format;
            }
        }
        return best;
    }

    private static int CompareAudio(StreamFormat left, StreamFormat right)
    {
        int bitrateCmp = (left.Bitrate ?? 0).CompareTo(right.Bitrate ?? 0);
        if (bitrateCmp != 0)
        {
            return bitrateCmp;
        }

        return IsDirectHttp(left.Protocol).CompareTo(IsDirectHttp(right.Protocol));
    }

    private static bool IsPlayable(StreamFormat format) =>
        !format.HasDrm
        && !string.IsNullOrWhiteSpace(format.Url)
        && IsPlayableProtocol(format.Protocol);

    private static bool IsPlayableProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol)
        || string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase)
        || string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase)
        || string.Equals(protocol, "m3u8", StringComparison.OrdinalIgnoreCase)
        || string.Equals(protocol, "m3u8_native", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectHttp(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol)
        || string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase)
        || string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase);

    private static bool HasCodec(string? codec) =>
        !string.IsNullOrWhiteSpace(codec) && !string.Equals(codec, "none", StringComparison.OrdinalIgnoreCase);

    private static bool IsAv1(string? codec) =>
        codec is not null && codec.StartsWith("av01", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string>? NonEmptyHeaders(IReadOnlyDictionary<string, string>? headers) =>
        headers is { Count: > 0 } ? headers : null;

    private static int ParseStreamingQualityMaxHeight(string? streamingQuality)
    {
        if (string.IsNullOrWhiteSpace(streamingQuality))
        {
            return 0;
        }

        string trimmed = streamingQuality.Trim();
        if ((trimmed.EndsWith('p') || trimmed.EndsWith('P'))
            && int.TryParse(trimmed.AsSpan(0, trimmed.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int height)
            && height > 0)
        {
            return height;
        }

        return 0;
    }
}
