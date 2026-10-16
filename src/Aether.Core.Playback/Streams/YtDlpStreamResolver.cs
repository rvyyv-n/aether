using System.Diagnostics;
using System.Text.Json;

namespace Aether.Core.Playback.Streams;

public sealed class YtDlpStreamResolver : IYtDlpStreamResolver
{
    private readonly IProcessRunner _processRunner;

    public YtDlpStreamResolver(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new DefaultProcessRunner();
    }

    public async Task<ResolvedStream> ResolveAsync(string url, StreamResolutionOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(options);

        string exePath = options.YtDlpPath ?? "yt-dlp";

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = YtDlpArgumentsBuilder.BuildArguments(url, options),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (options.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            cts.CancelAfter(timeout);
        }

        ProcessRunResult result;
        try
        {
            result = await _processRunner.RunAsync(startInfo, cts.Token).ConfigureAwait(false);
        }
        catch (StreamResolutionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new StreamResolutionException(StreamFailureReason.Offline, "yt-dlp stream resolution timed out.");
        }

        if (result.ExitCode != 0)
        {
            StreamFailureReason failure = YtDlpErrorClassifier.Classify(result.StandardError, result.ExitCode);
            string msg = string.Concat("yt-dlp failed with exit code ", result.ExitCode.ToString(), ": ", result.StandardError);
            throw new StreamResolutionException(failure, msg, result.StandardError);
        }

        return ParseStreamJson(result.StandardOutput, url);
    }

    public static ResolvedStream ParseStreamJson(string jsonOutput, string fallbackUrl)
    {
        if (string.IsNullOrWhiteSpace(jsonOutput))
        {
            throw new StreamResolutionException(StreamFailureReason.Unknown, "yt-dlp returned empty output.");
        }

        try
        {
            string json = ExtractJsonLine(jsonOutput);
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string? streamUrl = null;
            if (root.TryGetProperty("url", out JsonElement urlElement) && urlElement.ValueKind == JsonValueKind.String)
            {
                streamUrl = urlElement.GetString();
            }

            string? audioUrl = null;
            string? videoFormatId = null;
            string? audioFormatId = null;

            if (root.TryGetProperty("requested_formats", out JsonElement requested) && requested.ValueKind == JsonValueKind.Array)
            {
                // YouTube splits video and audio; the audio-only entry has vcodec "none".
                JsonElement? videoEntry = null;
                JsonElement? audioEntry = null;
                foreach (JsonElement fmt in requested.EnumerateArray())
                {
                    if (fmt.ValueKind != JsonValueKind.Object || GetString(fmt, "url") is not { Length: > 0 })
                    {
                        continue;
                    }

                    if (GetString(fmt, "vcodec") == "none")
                    {
                        audioEntry ??= fmt;
                    }
                    else
                    {
                        videoEntry ??= fmt;
                    }
                }

                if (string.IsNullOrEmpty(streamUrl))
                {
                    if (videoEntry is { } v)
                    {
                        streamUrl = GetString(v, "url");
                        videoFormatId = GetString(v, "format_id");
                    }
                    else if (audioEntry is { } onlyAudio)
                    {
                        streamUrl = GetString(onlyAudio, "url");
                        videoFormatId = GetString(onlyAudio, "format_id");
                        audioEntry = null;
                    }

                    if (audioEntry is { } a && GetString(a, "url") != streamUrl)
                    {
                        audioUrl = GetString(a, "url");
                        audioFormatId = GetString(a, "format_id");
                    }
                }
            }

            if (videoFormatId is null && audioUrl is null)
            {
                videoFormatId = GetString(root, "format_id");
            }

            string title = root.TryGetProperty("title", out JsonElement titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString() ?? string.Empty
                : string.Empty;

            double? duration = null;
            if (root.TryGetProperty("duration", out JsonElement durElement) && durElement.TryGetDouble(out double d))
            {
                duration = d;
            }

            string? webpageUrl = root.TryGetProperty("webpage_url", out JsonElement wpElement) && wpElement.ValueKind == JsonValueKind.String
                ? wpElement.GetString()
                : fallbackUrl;

            Dictionary<string, string>? headers = ParseHeaders(root);

            return new ResolvedStream(
                StreamUrl: streamUrl ?? fallbackUrl,
                Title: title,
                DurationSeconds: duration,
                WebpageUrl: webpageUrl ?? fallbackUrl,
                HttpHeaders: headers,
                AudioUrl: audioUrl,
                Chapters: ParseChapters(root),
                Formats: ParseFormats(root),
                Subtitles: ParseSubtitles(root),
                VideoFormatId: videoFormatId,
                AudioFormatId: audioFormatId);
        }
        catch (JsonException ex)
        {
            throw new StreamResolutionException(StreamFailureReason.Unknown, "Failed to parse yt-dlp JSON output.", jsonOutput, ex);
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetDouble(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double d)
            ? d
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        GetDouble(element, name) is { } d && d >= 0 && d <= int.MaxValue ? (int)d : null;

    // yt-dlp writes "none" for a missing codec.
    private static string? GetCodec(JsonElement element, string name)
    {
        string? codec = GetString(element, name);
        return string.IsNullOrEmpty(codec) || codec == "none" ? null : codec;
    }

    private static Dictionary<string, string>? ParseHeaders(JsonElement element)
    {
        if (!element.TryGetProperty("http_headers", out JsonElement headersElement) || headersElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty prop in headersElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                headers[prop.Name] = prop.Value.GetString()!;
            }
        }
        return headers;
    }

    private static IReadOnlyList<StreamChapter>? ParseChapters(JsonElement root)
    {
        if (!root.TryGetProperty("chapters", out JsonElement chapters))
        {
            return null;
        }

        var result = new List<StreamChapter>();
        if (chapters.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement chapter in chapters.EnumerateArray())
            {
                if (GetDouble(chapter, "start_time") is { } start
                    && GetDouble(chapter, "end_time") is { } end
                    && end > start)
                {
                    result.Add(new StreamChapter(start, end, GetString(chapter, "title") ?? string.Empty));
                }
            }
            result.Sort((a, b) => a.Start.CompareTo(b.Start));
        }
        return result;
    }

    private static IReadOnlyList<StreamFormat>? ParseFormats(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out JsonElement formats))
        {
            return null;
        }

        var result = new List<StreamFormat>();
        if (formats.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement fmt in formats.EnumerateArray())
        {
            string? url = GetString(fmt, "url");
            string? formatId = GetString(fmt, "format_id");
            if (string.IsNullOrEmpty(url) || formatId is null)
            {
                continue;
            }

            string? videoCodec = GetCodec(fmt, "vcodec");
            string? audioCodec = GetCodec(fmt, "acodec");
            string? ext = GetString(fmt, "ext");
            if (GetString(fmt, "format_note") == "storyboard"
                || ext == "mhtml"
                || (GetString(fmt, "vcodec") == "none" && GetString(fmt, "acodec") == "none"))
            {
                continue;
            }

            result.Add(new StreamFormat(
                formatId,
                GetInt(fmt, "height"),
                GetInt(fmt, "width"),
                GetDouble(fmt, "fps"),
                videoCodec,
                audioCodec,
                ext,
                GetDouble(fmt, "tbr"),
                url,
                ParseHeaders(fmt)));
        }
        return result;
    }

    private static readonly string[] SubtitleExtPreference = ["vtt", "srt", "ass"];

    private static IReadOnlyList<StreamSubtitle>? ParseSubtitles(JsonElement root)
    {
        bool hasManual = root.TryGetProperty("subtitles", out JsonElement manual) && manual.ValueKind == JsonValueKind.Object;
        bool hasAuto = root.TryGetProperty("automatic_captions", out JsonElement auto) && auto.ValueKind == JsonValueKind.Object;
        if (!hasManual && !hasAuto)
        {
            return null;
        }

        var result = new List<StreamSubtitle>();
        if (hasManual)
        {
            foreach (JsonProperty lang in manual.EnumerateObject())
            {
                if (lang.Name != "live_chat" && PickSubtitle(lang.Name, lang.Value, isAutomatic: false) is { } sub)
                {
                    result.Add(sub);
                }
            }
        }

        if (hasAuto)
        {
            // YouTube offers an auto-translated track for every language; keep only the spoken one.
            // When an "-orig" track exists the plain language key holds the same captions, so list it once.
            string? videoLanguage = GetString(root, "language");
            bool hasOriginal = auto.EnumerateObject().Any(l => l.Name.EndsWith("-orig", StringComparison.OrdinalIgnoreCase));
            foreach (JsonProperty lang in auto.EnumerateObject())
            {
                bool wanted = lang.Name.EndsWith("-orig", StringComparison.OrdinalIgnoreCase)
                    || (!hasOriginal && videoLanguage is not null && string.Equals(lang.Name, videoLanguage, StringComparison.OrdinalIgnoreCase));
                if (lang.Name != "live_chat" && wanted && PickSubtitle(lang.Name, lang.Value, isAutomatic: true) is { } sub)
                {
                    result.Add(sub);
                }
            }
        }
        return result;
    }

    private static StreamSubtitle? PickSubtitle(string language, JsonElement entries, bool isAutomatic)
    {
        if (entries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (string wantedExt in SubtitleExtPreference)
        {
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (GetString(entry, "ext") == wantedExt && GetString(entry, "url") is { Length: > 0 } url)
                {
                    return new StreamSubtitle(language, GetString(entry, "name"), url, wantedExt, isAutomatic);
                }
            }
        }
        return null;
    }

    private static string ExtractJsonLine(string output)
    {
        string[] lines = output.Split('\n');
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == (char)123 && trimmed[^1] == (char)125)
            {
                return trimmed;
            }
        }
        return output.Trim();
    }
}
