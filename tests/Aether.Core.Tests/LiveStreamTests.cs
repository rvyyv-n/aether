using System.Diagnostics;
using Aether.Core.Playback.Streams;

namespace Aether.Core.Tests;

public class LiveStreamTests
{
    private sealed class Runner(string json) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct) =>
            Task.FromResult(new ProcessRunResult(0, json, ""));
    }

    private static Task<ResolvedStream> Resolve(string json) =>
        new YtDlpStreamResolver(new Runner(json)).ResolveAsync("https://www.youtube.com/watch?v=live", new StreamResolutionOptions(YtDlpPath: "yt-dlp.exe"));

    [Theory]
    [InlineData("""{"url":"https://x/live.m3u8","title":"t","is_live":true}""", true)]
    [InlineData("""{"url":"https://x/live.m3u8","title":"t","live_status":"is_live"}""", true)]
    [InlineData("""{"url":"https://x/v.mp4","title":"t","is_live":false,"live_status":"was_live"}""", false)]
    [InlineData("""{"url":"https://x/v.mp4","title":"t"}""", false)]
    public async Task yt_dlp_Says_Whether_A_Stream_Is_Live(string json, bool live) =>
        Assert.Equal(live, (await Resolve(json)).IsLive);

    private const string CacheState = """
        {"bof-cached":false,"eof-cached":false,"fw-bytes":123,"cache-end":1012.5,"reader-pts":1003.2,
         "seekable-ranges":[{"start":880.0,"end":940.0},{"start":950.25,"end":1012.5}]}
        """;

    [Fact]
    public void The_Window_Runs_From_The_Earliest_Cached_Point_To_The_Newest()
    {
        LiveWindow window = LiveWindow.Parse(CacheState)!.Value;

        Assert.Equal(880, window.Start);
        Assert.Equal(1012.5, window.End);
        Assert.Equal(132.5, window.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"seekable-ranges":[]}""")]
    [InlineData("""{"cache-end":10}""")]
    [InlineData("""{"seekable-ranges":[{"start":5,"end":5}]}""")]
    public void No_Seekable_Range_Is_No_Window(string json) => Assert.Null(LiveWindow.Parse(json));

    [Fact]
    public void Near_The_Newest_Point_Counts_As_Live_And_Further_Back_Says_How_Far()
    {
        var window = new LiveWindow(880, 1012.5);

        Assert.True(window.IsAtEdge(1010));
        Assert.False(window.IsAtEdge(990));
        Assert.Equal(22.5, window.BehindBy(990));
        Assert.Equal(0, window.BehindBy(1020));
        Assert.Equal(1011.5, window.EdgeTarget);
        Assert.Equal(880, window.Clamp(800));
        Assert.Equal(1011.5, window.Clamp(2000));
    }

    [Fact]
    public void The_Seek_Bar_Shows_The_Window_And_Maps_Back_To_The_Stream_Clock()
    {
        var window = new LiveWindow(880, 1012.5);

        Assert.Equal(110, window.ToBar(990));
        Assert.Equal(990, window.FromBar(110));
        Assert.Equal(0, window.ToBar(870));
    }
}
