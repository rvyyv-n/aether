using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Streams;

public sealed class YtDlpStreamResolver : IYtDlpStreamResolver
{
    private const int MaxCacheEntries = 32;
    private static readonly TimeSpan VodTtl = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan LiveTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UrlExpirySafetyMargin = TimeSpan.FromMinutes(5);

    private readonly IProcessRunner _processRunner;
    private readonly TimeProvider _timeProvider;
    private readonly object _cacheLock = new();
    private readonly LruCache<Tier1Key> _tier1Cache = new(MaxCacheEntries);
    private readonly LruCache<Tier2Key> _tier2Cache = new(MaxCacheEntries);

    public YtDlpStreamResolver() : this(null, null)
    {
    }

    public YtDlpStreamResolver(IProcessRunner? processRunner, TimeProvider? timeProvider = null)
    {
        _processRunner = processRunner ?? new DefaultProcessRunner();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Invalidate(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string normalized = url.Trim();
        lock (_cacheLock)
        {
            _tier1Cache.RemoveWhere(k => string.Equals(k.NormalizedUrl, normalized, StringComparison.Ordinal));
            _tier2Cache.RemoveWhere(k => string.Equals(k.NormalizedUrl, normalized, StringComparison.Ordinal));
        }
    }

    public async Task<ResolvedStream> ResolveAsync(string url, StreamResolutionOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        string normalizedUrl = url.Trim();
        string proxy = string.IsNullOrWhiteSpace(options.HttpProxy) ? string.Empty : options.HttpProxy.Trim();
        string exePath = string.IsNullOrWhiteSpace(options.YtDlpPath) ? "yt-dlp" : options.YtDlpPath.Trim();
        string effectiveSelector = EffectiveFormatSelector(options);

        var tier1Key = new Tier1Key(normalizedUrl, effectiveSelector, options.PreferGpuDecodableFormats, proxy, exePath);
        var tier2Key = new Tier2Key(normalizedUrl, proxy, exePath);

        if (!options.BypassCache)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            lock (_cacheLock)
            {
                if (_tier1Cache.TryGet(tier1Key, now, out CacheEntry tier1Hit))
                {
                    _tier2Cache.TryGet(tier2Key, now, out _);
                    return tier1Hit.Stream;
                }

                if (_tier2Cache.TryGet(tier2Key, now, out CacheEntry tier2Hit))
                {
                    ResolvedStream? selected = StreamFormatSelector.TrySelect(tier2Hit.Stream, options);
                    if (selected is not null)
                    {
                        DateTimeOffset selectedExp = CapExpiryWithUrls(tier2Hit.ExpiresAtUtc, selected.StreamUrl, selected.AudioUrl);
                        if (selectedExp > now)
                        {
                            _tier1Cache.Set(tier1Key, new CacheEntry(selected, selectedExp));
                            return selected;
                        }
                    }
                }
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in YtDlpArgumentsBuilder.BuildArgumentList(url, options))
        {
            startInfo.ArgumentList.Add(arg);
        }

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
            string msg = string.Concat("yt-dlp failed with exit code ", result.ExitCode.ToString(CultureInfo.InvariantCulture), ": ", result.StandardError);
            throw new StreamResolutionException(failure, msg, result.StandardError);
        }

        ResolvedStream resolved = ParseStreamJson(result.StandardOutput, url);

        DateTimeOffset afterNow = _timeProvider.GetUtcNow();
        TimeSpan ttl = resolved.IsLive ? LiveTtl : VodTtl;
        DateTimeOffset cappedExpiry = CapExpiryWithUrls(afterNow + ttl, resolved.StreamUrl, resolved.AudioUrl);
        if (cappedExpiry > afterNow)
        {
            var entry = new CacheEntry(resolved, cappedExpiry);
            lock (_cacheLock)
            {
                _tier1Cache.Set(tier1Key, entry);
                _tier2Cache.Set(tier2Key, entry);
            }
        }

        return resolved;
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
            JsonElement? primaryEntry = null;

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

                primaryEntry = videoEntry ?? audioEntry;

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
            if ((headers is null || headers.Count == 0) && primaryEntry is { } entry && ParseHeaders(entry) is { Count: > 0 } entryHeaders)
            {
                headers = entryHeaders;
            }

            bool live = (root.TryGetProperty("is_live", out JsonElement isLive) && isLive.ValueKind == JsonValueKind.True)
                || GetString(root, "live_status") == "is_live";

            string? thumbnailUrl = GetString(root, "thumbnail");
            if (!Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out Uri? thumbUri)
                || (thumbUri.Scheme != Uri.UriSchemeHttp && thumbUri.Scheme != Uri.UriSchemeHttps))
            {
                thumbnailUrl = null;
            }

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
                AudioFormatId: audioFormatId,
                IsLive: live,
                Storyboards: ParseStoryboards(root),
                ThumbnailUrl: thumbnailUrl);
        }
        catch (JsonException ex)
        {
            throw new StreamResolutionException(StreamFailureReason.Unknown, "Failed to parse yt-dlp JSON output.", jsonOutput, ex);
        }
    }

    private static string EffectiveFormatSelector(StreamResolutionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.FormatSelectorOverride))
        {
            return options.FormatSelectorOverride.Trim();
        }

        if (options.RequestedQuality is { IsAuto: false } requested && requested.FormatSelector() is { } qualitySelector)
        {
            return qualitySelector;
        }

        return MpvSettingsMap.YtdlFormat(options.StreamingQuality, options.PreferGpuDecodableFormats);
    }

    private static DateTimeOffset CapExpiryWithUrls(DateTimeOffset baseExpiry, string? streamUrl, string? audioUrl)
    {
        DateTimeOffset capped = baseExpiry;
        if (TryParseUrlExpiryUtc(streamUrl) is { } streamExp)
        {
            DateTimeOffset cap = streamExp - UrlExpirySafetyMargin;
            if (cap < capped)
            {
                capped = cap;
            }
        }
        if (TryParseUrlExpiryUtc(audioUrl) is { } audioExp)
        {
            DateTimeOffset cap = audioExp - UrlExpirySafetyMargin;
            if (cap < capped)
            {
                capped = cap;
            }
        }
        return capped;
    }

    private static DateTimeOffset? TryParseUrlExpiryUtc(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        int q = url.IndexOf('?');
        if (q < 0 || q + 1 >= url.Length)
        {
            return null;
        }

        int hash = url.IndexOf('#', q + 1);
        ReadOnlySpan<char> query = hash >= 0 ? url.AsSpan(q + 1, hash - q - 1) : url.AsSpan(q + 1);

        while (query.Length > 0)
        {
            int amp = query.IndexOf('&');
            ReadOnlySpan<char> part = amp >= 0 ? query[..amp] : query;
            query = amp >= 0 ? query[(amp + 1)..] : ReadOnlySpan<char>.Empty;

            if (part.StartsWith("expire=", StringComparison.OrdinalIgnoreCase))
            {
                ReadOnlySpan<char> raw = part["expire=".Length..];
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSeconds)
                    && unixSeconds >= -62135596800
                    && unixSeconds <= 253402300799)
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                }
            }
        }

        return null;
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

    private static bool GetBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

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
                ParseHeaders(fmt),
                GetString(fmt, "protocol"),
                GetBool(fmt, "has_drm")));
        }
        return result;
    }

    private static IReadOnlyList<StreamStoryboard>? ParseStoryboards(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out JsonElement formats))
        {
            return null;
        }

        var result = new List<StreamStoryboard>();
        if (formats.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement fmt in formats.EnumerateArray())
        {
            if (fmt.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? formatNote = GetString(fmt, "format_note");
            string? ext = GetString(fmt, "ext");
            string? protocol = GetString(fmt, "protocol");
            if (!string.Equals(formatNote, "storyboard", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ext, "mhtml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(protocol, "mhtml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? formatId = GetString(fmt, "format_id");
            int? width = GetInt(fmt, "width");
            int? height = GetInt(fmt, "height");
            int? rows = GetInt(fmt, "rows");
            int? columns = GetInt(fmt, "columns");
            if (string.IsNullOrEmpty(formatId)
                || width is not > 0
                || height is not > 0
                || rows is not > 0
                || columns is not > 0
                || !fmt.TryGetProperty("fragments", out JsonElement fragments)
                || fragments.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var parsedFragments = new List<StoryboardFragment>();
            foreach (JsonElement fragment in fragments.EnumerateArray())
            {
                string? fragUrl = GetString(fragment, "url");
                double? fragDur = GetDouble(fragment, "duration");
                if (fragUrl is { Length: > 0 }
                    && Uri.TryCreate(fragUrl, UriKind.Absolute, out Uri? uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    && fragDur is > 0 && double.IsFinite(fragDur.Value))
                {
                    parsedFragments.Add(new StoryboardFragment(fragUrl, fragDur.Value));
                }
            }

            if (parsedFragments.Count == 0)
            {
                continue;
            }

            double? fps = GetDouble(fmt, "fps");
            if (fps is not > 0 || !double.IsFinite(fps.Value))
            {
                fps = null;
            }

            result.Add(new StreamStoryboard(
                FormatId: formatId,
                TileWidth: width.Value,
                TileHeight: height.Value,
                Rows: rows.Value,
                Columns: columns.Value,
                Fps: fps,
                Fragments: parsedFragments,
                HttpHeaders: ParseHeaders(fmt)));
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

    private readonly record struct Tier1Key(
        string NormalizedUrl,
        string EffectiveFormatSelector,
        bool PreferGpuDecodableFormats,
        string HttpProxy,
        string YtDlpPath);

    private readonly record struct Tier2Key(
        string NormalizedUrl,
        string HttpProxy,
        string YtDlpPath);

    private readonly record struct CacheEntry(ResolvedStream Stream, DateTimeOffset ExpiresAtUtc);

    private sealed class LruCache<TKey> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, CacheEntry Entry)>> _map = [];
        private readonly LinkedList<(TKey Key, CacheEntry Entry)> _lru = [];

        public LruCache(int capacity) => _capacity = capacity;

        public bool TryGet(TKey key, DateTimeOffset now, out CacheEntry entry)
        {
            if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, CacheEntry Entry)>? node))
            {
                if (node.Value.Entry.ExpiresAtUtc <= now)
                {
                    _lru.Remove(node);
                    _map.Remove(key);
                    entry = default;
                    return false;
                }

                _lru.Remove(node);
                _lru.AddFirst(node);
                entry = node.Value.Entry;
                return true;
            }

            entry = default;
            return false;
        }

        public void Set(TKey key, CacheEntry entry)
        {
            if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, CacheEntry Entry)>? existing))
            {
                existing.Value = (key, entry);
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return;
            }

            if (_map.Count >= _capacity && _lru.Last is { } oldest)
            {
                _lru.RemoveLast();
                _map.Remove(oldest.Value.Key);
            }

            var node = new LinkedListNode<(TKey Key, CacheEntry Entry)>((key, entry));
            _lru.AddFirst(node);
            _map[key] = node;
        }

        public void RemoveWhere(Func<TKey, bool> predicate)
        {
            LinkedListNode<(TKey Key, CacheEntry Entry)>? node = _lru.First;
            while (node is not null)
            {
                LinkedListNode<(TKey Key, CacheEntry Entry)>? next = node.Next;
                if (predicate(node.Value.Key))
                {
                    _lru.Remove(node);
                    _map.Remove(node.Value.Key);
                }
                node = next;
            }
        }
    }
}
