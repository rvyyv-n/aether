using System.Diagnostics;
using Aether.Core.Playback.Streams;
using Xunit;

namespace Aether.Core.Tests;

public class YtDlpStreamResolverTests
{
    private sealed class MockProcessRunner : IProcessRunner
    {
        public Func<ProcessStartInfo, CancellationToken, Task<ProcessRunResult>>? Handler { get; set; }
        public ProcessStartInfo? LastStartInfo { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
        {
            LastStartInfo = startInfo;
            if (Handler is not null)
            {
                return Handler(startInfo, ct);
            }
            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty));
        }
    }

    [Fact]
    public async Task ResolveAsync_ValidJson_ReturnsResolvedStream()
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
    public async Task ResolveAsync_NonZeroExitCode_ThrowsClassifiedException()
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
}
