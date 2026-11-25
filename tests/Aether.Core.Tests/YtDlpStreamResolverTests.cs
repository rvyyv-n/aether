using System.Diagnostics;
using Aether.Core.Playback.Streams;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class YtDlpStreamResolverTests
{
    private sealed class MockProcessRunner : IProcessRunner
    {
        public Func<ProcessStartInfo, CancellationToken, Task<ProcessRunResult>>? Handler { get; set; }
        public ProcessStartInfo? LastStartInfo { get; private set; }
        public int CallCount { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
        {
            CallCount++;
            LastStartInfo = startInfo;
            if (Handler is not null)
            {
                return Handler(startInfo, ct);
            }
            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty));
        }
    }

    private static string MultiFormatJson(
        bool isLive = false,
        long? expireUnix = null,
        string? rootHeadersJson = null,
        string? videoEntryHeadersJson = null)
    {
        string suffix = expireUnix is { } exp ? $"?expire={exp}" : string.Empty;
        string rootHeaders = rootHeadersJson is not null ? $",\"http_headers\":{rootHeadersJson}" : string.Empty;
        string entryHeaders = videoEntryHeadersJson is not null ? $",\"http_headers\":{videoEntryHeadersJson}" : string.Empty;
        return $$"""
            {
              "title": "Sample Video",
              "duration": 240.0,
              "webpage_url": "https://www.youtube.com/watch?v=abc",
              "is_live": {{(isLive ? "true" : "false")}}
              {{rootHeaders}},
              "requested_formats": [
                {"format_id":"137","url":"https://cdn.example/v1080{{suffix}}","vcodec":"avc1","acodec":"none"{{entryHeaders}}},
                {"format_id":"140-es","url":"https://cdn.example/a-es{{suffix}}","vcodec":"none","acodec":"mp4a"}
              ],
              "formats": [
                {"format_id":"140-es","url":"https://cdn.example/a-es{{suffix}}","vcodec":"none","acodec":"mp4a","ext":"m4a","tbr":128,"protocol":"https","has_drm":false},
                {"format_id":"136","url":"https://cdn.example/v720{{suffix}}","vcodec":"avc1","acodec":"none","ext":"mp4","height":720,"width":1280,"fps":30,"tbr":2200,"protocol":"https","has_drm":false},
                {"format_id":"137","url":"https://cdn.example/v1080{{suffix}}","vcodec":"avc1","acodec":"none","ext":"mp4","height":1080,"width":1920,"fps":30,"tbr":4200,"protocol":"https","has_drm":false},
                {"format_id":"315","url":"https://cdn.example/v2160{{suffix}}","vcodec":"vp9","acodec":"none","ext":"webm","height":2160,"width":3840,"fps":60,"tbr":12000,"protocol":"m3u8_native","has_drm":true}
              ]
            }
            """.ReplaceLineEndings(" ");
    }

    [Fact]
    public async Task ResolveAsync_ValidJson_ReturnsResolvedStreamAndUsesArgumentList()
    {
        string json = string.Concat(
            (char)123,
            (char)34, "url", (char)34, ": ", (char)34, "https://stream.example.com/video.mp4", (char)34, ", ",
            (char)34, "title", (char)34, ": ", (char)34, "Example Title", (char)34, ", ",
            (char)34, "duration", (char)34, ": 150.5, ",
            (char)34, "webpage_url", (char)34, ": ", (char)34, "https://youtube.com/watch?v=123", (char)34, ", ",
            (char)34, "http_headers", (char)34, ": ", (char)123, (char)34, "User-Agent", (char)34, ": ", (char)34, "Test/1.0", (char)34, (char)125,
            (char)125);
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(0, json, string.Empty))
        };
        var resolver = new YtDlpStreamResolver(runner);
        var options = new StreamResolutionOptions(YtDlpPath: "yt-dlp.exe");

        ResolvedStream stream = await resolver.ResolveAsync("https://youtube.com/watch?v=123", options);

        Assert.Equal("https://stream.example.com/video.mp4", stream.StreamUrl);
        Assert.Equal("Example Title", stream.Title);
        Assert.Equal(150.5, stream.DurationSeconds);
        Assert.Equal("https://youtube.com/watch?v=123", stream.WebpageUrl);
        Assert.NotNull(stream.HttpHeaders);
        Assert.Equal("Test/1.0", stream.HttpHeaders!["User-Agent"]);

        Assert.NotNull(runner.LastStartInfo);
        Assert.Equal(string.Empty, runner.LastStartInfo!.Arguments);
        Assert.Contains("--dump-json", runner.LastStartInfo.ArgumentList);
        Assert.Contains("--", runner.LastStartInfo.ArgumentList);
        Assert.Equal("https://youtube.com/watch?v=123", runner.LastStartInfo.ArgumentList[^1]);
    }

    [Fact]
    public async Task ResolveAsync_RequestedFormatsJson_ExtractsFormatUrl()
    {
        string json = string.Concat(
            (char)123,
            (char)34, "title", (char)34, ": ", (char)34, "DASH Video", (char)34, ", ",
            (char)34, "requested_formats", (char)34, ": [",
            (char)123, (char)34, "url", (char)34, ": ", (char)34, "https://dash.example.com/video.mp4", (char)34, (char)125, ", ",
            (char)123, (char)34, "url", (char)34, ": ", (char)34, "https://dash.example.com/audio.m4a", (char)34, (char)125,
            "]", (char)125);
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(0, json, string.Empty))
        };
        var resolver = new YtDlpStreamResolver(runner);
        var options = new StreamResolutionOptions();

        ResolvedStream stream = await resolver.ResolveAsync("https://youtu.be/dash", options);

        Assert.Equal("https://dash.example.com/video.mp4", stream.StreamUrl);
        Assert.Equal("DASH Video", stream.Title);
    }

    [Fact]
    public async Task ResolveAsync_NonZeroExitCode_ThrowsClassifiedExceptionAndDoesNotCache()
    {
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(1, string.Empty, "ERROR: [youtube] Private video"))
        };
        var resolver = new YtDlpStreamResolver(runner);
        var options = new StreamResolutionOptions();

        StreamResolutionException ex = await Assert.ThrowsAsync<StreamResolutionException>(() =>
            resolver.ResolveAsync("https://youtube.com/watch?v=private", options));
        Assert.Equal(StreamFailureReason.PrivateVideo, ex.Reason);

        await Assert.ThrowsAsync<StreamResolutionException>(() =>
            resolver.ResolveAsync("https://youtube.com/watch?v=private", options));
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_MissingExecutable_ThrowsYtDlpMissingException()
    {
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => throw new StreamResolutionException(StreamFailureReason.YtDlpMissing, "yt-dlp not found")
        };
        var resolver = new YtDlpStreamResolver(runner);
        var options = new StreamResolutionOptions();

        StreamResolutionException ex = await Assert.ThrowsAsync<StreamResolutionException>(() =>
            resolver.ResolveAsync("https://youtu.be/test", options));

        Assert.Equal(StreamFailureReason.YtDlpMissing, ex.Reason);
    }

    [Fact]
    public async Task ResolveAsync_InternalTimeout_ThrowsOfflineException()
    {
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => throw new OperationCanceledException()
        };
        var resolver = new YtDlpStreamResolver(runner);
        var options = new StreamResolutionOptions(Timeout: TimeSpan.FromMilliseconds(10));

        StreamResolutionException ex = await Assert.ThrowsAsync<StreamResolutionException>(() =>
            resolver.ResolveAsync("https://youtu.be/slow", options, CancellationToken.None));

        Assert.Equal(StreamFailureReason.Offline, ex.Reason);
    }

    [Fact]
    public async Task ResolveAsync_CallerCanceled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new MockProcessRunner
        {
            Handler = (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty)); }
        };
        var resolver = new YtDlpStreamResolver(runner);
        var options = new StreamResolutionOptions();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveAsync("https://youtu.be/test", options, cts.Token));
    }

    [Fact]
    public async Task ResolveAsync_WarningBeforeJson_ParsesJsonLineCorrectly()
    {
        string stdout = string.Concat(
            "WARNING: Deprecated format", (char)10,
            (char)123, (char)34, "url", (char)34, ": ", (char)34, "https://ok.com/v.mp4", (char)34, ", ",
            (char)34, "title", (char)34, ": ", (char)34, "Clean Title", (char)34, (char)125);
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(0, stdout, string.Empty))
        };
        var resolver = new YtDlpStreamResolver(runner);
        var options = new StreamResolutionOptions();

        ResolvedStream stream = await resolver.ResolveAsync("https://youtu.be/test", options);

        Assert.Equal("https://ok.com/v.mp4", stream.StreamUrl);
        Assert.Equal("Clean Title", stream.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not valid json")]
    public async Task ResolveAsync_InvalidOrEmptyOutput_ThrowsUnknownException(string output)
    {
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(0, output, string.Empty))
        };
        var resolver = new YtDlpStreamResolver(runner);
        var options = new StreamResolutionOptions();

        StreamResolutionException ex = await Assert.ThrowsAsync<StreamResolutionException>(() =>
            resolver.ResolveAsync("https://youtu.be/test", options));

        Assert.Equal(StreamFailureReason.Unknown, ex.Reason);
    }

    [Fact]
    public void ParseStreamJson_ParsesProtocolHasDrmAndFallsBackToVideoEntryHeaders()
    {
        ResolvedStream missingRootHeaders = YtDlpStreamResolver.ParseStreamJson(
            MultiFormatJson(rootHeadersJson: null, videoEntryHeadersJson: """{"User-Agent":"EntryUA"}"""),
            "https://fallback");
        Assert.Equal("EntryUA", missingRootHeaders.HttpHeaders!["User-Agent"]);

        ResolvedStream emptyRootHeaders = YtDlpStreamResolver.ParseStreamJson(
            MultiFormatJson(rootHeadersJson: "{}", videoEntryHeadersJson: """{"User-Agent":"EntryUA2"}"""),
            "https://fallback");
        Assert.Equal("EntryUA2", emptyRootHeaders.HttpHeaders!["User-Agent"]);

        Assert.Equal("https", missingRootHeaders.Formats![1].Protocol);
        Assert.False(missingRootHeaders.Formats[1].HasDrm);
        Assert.Equal("m3u8_native", missingRootHeaders.Formats[3].Protocol);
        Assert.True(missingRootHeaders.Formats[3].HasDrm);
    }

    [Fact]
    public async Task ResolveAsync_Tier1AndTier2CacheHits_AvoidExtraProcessRuns()
    {
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(0, MultiFormatJson(), string.Empty))
        };
        var resolver = new YtDlpStreamResolver(runner, new ManualTimeProvider());
        string url = "https://www.youtube.com/watch?v=abc";

        ResolvedStream initial = await resolver.ResolveAsync(url, new StreamResolutionOptions(StreamingQuality: "1080p"));
        Assert.Equal("https://cdn.example/v1080", initial.StreamUrl);
        Assert.Equal(1, runner.CallCount);

        // Tier 1 exact hit (even with surrounding spaces on URL)
        ResolvedStream tier1 = await resolver.ResolveAsync($"  {url}  ", new StreamResolutionOptions(StreamingQuality: "1080p"));
        Assert.Equal(initial.StreamUrl, tier1.StreamUrl);
        Assert.Equal(1, runner.CallCount);

        // Tier 2 metadata hit for a different quality -> StreamFormatSelector selects 720p + keeps audio-es
        var opt720 = new StreamResolutionOptions(
            RequestedQuality: QualityOptions.ForKey("h720"),
            FormatSelectorOverride: "bv*[height<=720]+ba/b[height<=720]");
        ResolvedStream tier2To720 = await resolver.ResolveAsync(url, opt720);
        Assert.Equal("https://cdn.example/v720", tier2To720.StreamUrl);
        Assert.Equal("https://cdn.example/a-es", tier2To720.AudioUrl);
        Assert.Equal("136", tier2To720.VideoFormatId);
        Assert.Equal("140-es", tier2To720.AudioFormatId);
        Assert.Equal(1, runner.CallCount);

        // Tier 2 -> AudioOnly
        ResolvedStream tier2Audio = await resolver.ResolveAsync(
            url,
            new StreamResolutionOptions(RequestedQuality: QualityOptions.AudioOnly, FormatSelectorOverride: "ba/b"));
        Assert.Equal("https://cdn.example/a-es", tier2Audio.StreamUrl);
        Assert.Null(tier2Audio.AudioUrl);
        Assert.Equal("140-es", tier2Audio.VideoFormatId);
        Assert.Equal(1, runner.CallCount);

        // BypassCache forces fresh process run
        await resolver.ResolveAsync(url, new StreamResolutionOptions(StreamingQuality: "1080p", BypassCache: true));
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_Tier2MissesOnCustomSelectorOverrideWithoutRequestedQuality()
    {
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(0, MultiFormatJson(), string.Empty))
        };
        var resolver = new YtDlpStreamResolver(runner, new ManualTimeProvider());
        string url = "https://www.youtube.com/watch?v=abc";

        await resolver.ResolveAsync(url, new StreamResolutionOptions());
        await resolver.ResolveAsync(url, new StreamResolutionOptions(FormatSelectorOverride: "best[height<=480]", RequestedQuality: null));

        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_RespectsVodAndLiveTtlAndInvalidate()
    {
        var time = new ManualTimeProvider();
        bool live = false;
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(0, MultiFormatJson(isLive: live), string.Empty))
        };
        var resolver = new YtDlpStreamResolver(runner, time);
        var opts = new StreamResolutionOptions();

        // VOD: 20 minutes
        await resolver.ResolveAsync("https://example.com/vod", opts);
        time.Advance(TimeSpan.FromMinutes(19));
        await resolver.ResolveAsync("https://example.com/vod", opts);
        Assert.Equal(1, runner.CallCount);

        time.Advance(TimeSpan.FromMinutes(1));
        await resolver.ResolveAsync("https://example.com/vod", opts);
        Assert.Equal(2, runner.CallCount);

        // Invalidate clears cached entry
        resolver.Invalidate("https://example.com/vod");
        await resolver.ResolveAsync("https://example.com/vod", opts);
        Assert.Equal(3, runner.CallCount);

        // Live: 60 seconds
        live = true;
        await resolver.ResolveAsync("https://example.com/live", opts);
        time.Advance(TimeSpan.FromSeconds(59));
        await resolver.ResolveAsync("https://example.com/live", opts);
        Assert.Equal(4, runner.CallCount);

        time.Advance(TimeSpan.FromSeconds(1));
        await resolver.ResolveAsync("https://example.com/live", opts);
        Assert.Equal(5, runner.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_CapsCacheExpiryByUrlExpireParamMinus5Minutes()
    {
        var time = new ManualTimeProvider();
        DateTimeOffset startUtc = time.GetUtcNow();
        long expireIn12Min = (startUtc + TimeSpan.FromMinutes(12)).ToUnixTimeSeconds();
        long expireIn4Min = (startUtc + TimeSpan.FromMinutes(4)).ToUnixTimeSeconds();

        long currentExpire = expireIn12Min;
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(0, MultiFormatJson(expireUnix: currentExpire), string.Empty))
        };
        var resolver = new YtDlpStreamResolver(runner, time);
        var opts = new StreamResolutionOptions();

        // expire in 12m -> cached for 12m - 5m = 7m
        await resolver.ResolveAsync("https://example.com/exp", opts);
        time.Advance(TimeSpan.FromMinutes(6));
        await resolver.ResolveAsync("https://example.com/exp", opts);
        Assert.Equal(1, runner.CallCount);

        time.Advance(TimeSpan.FromMinutes(1));
        await resolver.ResolveAsync("https://example.com/exp", opts);
        Assert.Equal(2, runner.CallCount);

        // expire in 4m -> already inside 5m margin -> never cached
        currentExpire = (time.GetUtcNow() + TimeSpan.FromMinutes(4)).ToUnixTimeSeconds();
        await resolver.ResolveAsync("https://example.com/soon", opts);
        await resolver.ResolveAsync("https://example.com/soon", opts);
        Assert.Equal(4, runner.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_LruEvictsOldestBeyond32Entries()
    {
        var runner = new MockProcessRunner
        {
            Handler = (_, _) => Task.FromResult(new ProcessRunResult(0, MultiFormatJson(), string.Empty))
        };
        var resolver = new YtDlpStreamResolver(runner, new ManualTimeProvider());
        var opts = new StreamResolutionOptions();

        for (int i = 0; i < 32; i++)
        {
            await resolver.ResolveAsync($"https://example.com/v{i}", opts);
        }
        Assert.Equal(32, runner.CallCount);

        // Touch v0 so v1 becomes LRU
        await resolver.ResolveAsync("https://example.com/v0", opts);
        Assert.Equal(32, runner.CallCount);

        // Add 33rd URL -> evicts v1, keeps v0
        await resolver.ResolveAsync("https://example.com/v32", opts);
        Assert.Equal(33, runner.CallCount);

        await resolver.ResolveAsync("https://example.com/v0", opts);
        Assert.Equal(33, runner.CallCount);

        await resolver.ResolveAsync("https://example.com/v1", opts);
        Assert.Equal(34, runner.CallCount);
    }
}
