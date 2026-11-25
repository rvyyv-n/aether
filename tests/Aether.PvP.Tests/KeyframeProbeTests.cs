using System.Globalization;
using System.Text;
using Aether.PvP.Review.Clipping;

namespace Aether.PvP.Tests;

public sealed class KeyframeProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-kfprobe-" + Guid.NewGuid().ToString("N"));

    public KeyframeProbeTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class FakeRunner(Func<string, IReadOnlyList<string>, Task<(int Exit, string Out, string Err)>> behaviour)
        : IProcessRunner
    {
        public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = [];

        public async Task<ProcessOutcome> RunAsync(
            string fileName, IReadOnlyList<string> arguments, Func<TextReader, Task>? stdoutConsumer, CancellationToken ct)
        {
            lock (Calls) Calls.Add((fileName, arguments));
            var (exit, output, error) = await behaviour(fileName, arguments);
            ct.ThrowIfCancellationRequested();
            if (stdoutConsumer is not null) await stdoutConsumer(new StringReader(output));
            return new ProcessOutcome(exit, error);
        }
    }

    private static string IntervalOf(IReadOnlyList<string> args) => args[args.ToList().IndexOf("-read_intervals") + 1];

    private static string Packets(params (double? Pts, double? Dts, string Flags)[] packets)
    {
        var sb = new StringBuilder();
        foreach (var (pts, dts, flags) in packets)
        {
            sb.Append("pts_time=").AppendLine(pts is { } p ? p.ToString(CultureInfo.InvariantCulture) : "N/A");
            sb.Append("dts_time=").AppendLine(dts is { } d ? d.ToString(CultureInfo.InvariantCulture) : "N/A");
            sb.Append("flags=").AppendLine(flags);
        }
        return sb.ToString();
    }

    [Fact]
    public async Task Ffprobe_UsesPacketHeadersAndTakesLastKeyframeAtOrBeforeIn()
    {
        string output = Packets(
            (88.5, 88.5, "K__"),
            (91.25, 91.25, "K__"),
            (93.5, 93.5, "___"),
            (95.0, 95.0, "K__"));
        var runner = new FakeRunner((_, _) => Task.FromResult((0, output, "")));
        var probe = new KeyframeProbe(runner);

        double? found = await probe.FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None);

        Assert.Equal(91.25, found);
        IReadOnlyList<string> args = runner.Calls[0].Args;
        Assert.Equal("84%99.05", IntervalOf(args));
        Assert.DoesNotContain("-skip_frame", args);
        Assert.Contains("packet=pts_time,dts_time,flags", args);
        Assert.Contains("default=noprint_wrappers=1", args);
    }

    [Fact]
    public async Task Ffprobe_KeyframeExactlyOnIn_IsUsed()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult((0, Packets((90.0, 90.0, "K_"), (94.0, 94.0, "K_")), "")));

        Assert.Equal(94.0, await new KeyframeProbe(runner).FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None));
    }

    [Fact]
    public async Task Ffprobe_FallsBackToDtsTimeWhenPtsIsNA_AndParsesBracketedBlocks()
    {
        string output = """
            [PACKET]
            flags=K__
            pts_time=N/A
            dts_time=91.75
            [/PACKET]
            [PACKET]
            pts_time=93.0
            dts_time=93.0
            flags=___
            [/PACKET]
            """;
        var runner = new FakeRunner((_, _) => Task.FromResult((0, output, "")));

        Assert.Equal(91.75, await new KeyframeProbe(runner).FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None));
    }

    [Fact]
    public async Task EmptyFirstWindow_WidensOnceTo30SecondsUsingIncrementalGap()
    {
        var runner = new FakeRunner((_, args) =>
            Task.FromResult(IntervalOf(args).StartsWith("70%", StringComparison.Ordinal)
                ? (0, Packets((75.5, 75.5, "K_")), "")
                : (0, "", "")));
        var probe = new KeyframeProbe(runner);

        double? found = await probe.FindAtOrBeforeAsync("v.mp4", 100.0, CancellationToken.None);

        Assert.Equal(75.5, found);
        Assert.Equal(["90%105.05", "70%90.05"], runner.Calls.Select(c => IntervalOf(c.Args)));
    }

    [Fact]
    public async Task NoKeyframeInEitherWindow_ReturnsNullAfterTwoProbes()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult((0, "", "")));

        Assert.Null(await new KeyframeProbe(runner).FindAtOrBeforeAsync("v.mp4", 100.0, CancellationToken.None));
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task WindowReachingFileStart_DoesNotWiden()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult((0, "", "")));

        Assert.Null(await new KeyframeProbe(runner).FindAtOrBeforeAsync("v.mp4", 4.0, CancellationToken.None));
        Assert.Single(runner.Calls);
        Assert.Equal("0%9.05", IntervalOf(runner.Calls[0].Args));
    }

    [Fact]
    public async Task FailedTool_ReturnsNullWithoutWidening()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult((1, "", "boom")));

        Assert.Null(await new KeyframeProbe(runner).FindAtOrBeforeAsync("v.mp4", 100.0, CancellationToken.None));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task MissingFfprobe_FallsBackToFfmpegShowinfo()
    {
        var runner = new FakeRunner((file, _) => file.Contains("ffprobe")
            ? throw new FileNotFoundException("no ffprobe")
            : Task.FromResult((0, "", "[Parsed_showinfo_0 @ 0x1] n:   0 pts:  15360 pts_time:1.5 pos: 1\n[Parsed_showinfo_0 @ 0x1] n:   1 pts: 61440 pts_time:6.0 pos: 2\n")));
        var probe = new KeyframeProbe(runner, @"C:\tools\ffmpeg.exe");

        double? found = await probe.FindAtOrBeforeAsync("v.mp4", 100.0, CancellationToken.None);

        Assert.Equal(@"C:\tools\ffprobe.exe", runner.Calls[0].File);
        Assert.Equal(@"C:\tools\ffmpeg.exe", runner.Calls[1].File);
        Assert.Equal(90.0 + 6.0, found); // showinfo times are relative to the -ss seek
    }

    [Fact]
    public async Task SameQuestionAndNearbyNudges_AreAnsweredFromIntervalCache()
    {
        string output = Packets((88.5, 88.5, "K_"), (91.25, 91.25, "K_"), (96.0, 96.0, "K_"));
        var runner = new FakeRunner((_, _) => Task.FromResult((0, output, "")));
        var probe = new KeyframeProbe(runner);

        Assert.Equal(91.25, await probe.FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None));
        Assert.Equal(91.25, await probe.FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None));

        // Backward nudge within covered range hits cache
        Assert.True(probe.TryGetCached("v.mp4", 90.0, out double? backCached));
        Assert.Equal(88.5, backCached);
        Assert.Equal(88.5, await probe.FindAtOrBeforeAsync("v.mp4", 90.0, CancellationToken.None));

        // Forward nudge inside +5s lookahead hits cache (including keyframe at 96.0)
        Assert.True(probe.TryGetCached("v.mp4", 98.0, out double? fwdCached));
        Assert.Equal(96.0, fwdCached);
        Assert.Equal(96.0, await probe.FindAtOrBeforeAsync("v.mp4", 98.0, CancellationToken.None));

        Assert.Single(runner.Calls);

        // Stepping past covered end (99.0) probes only the uncovered tail [99.0, 107.0]
        await probe.FindAtOrBeforeAsync("v.mp4", 102.0, CancellationToken.None);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("99%107.05", IntervalOf(runner.Calls[1].Args));
    }

    [Fact]
    public async Task FileModification_InvalidatesCachedEntry()
    {
        string file = Path.Combine(_dir, "clip.mp4");
        File.WriteAllText(file, "first");
        string output = Packets((91.0, 91.0, "K_"));
        var runner = new FakeRunner((_, _) => Task.FromResult((0, output, "")));
        var probe = new KeyframeProbe(runner);

        Assert.Equal(91.0, await probe.FindAtOrBeforeAsync(file, 94.0, CancellationToken.None));
        Assert.True(probe.TryGetCached(file, 94.0, out _));

        File.WriteAllText(file, "modified-content-longer");
        Assert.False(probe.TryGetCached(file, 94.0, out _));

        await probe.FindAtOrBeforeAsync(file, 94.0, CancellationToken.None);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task IntervalCache_EvictsLeastRecentlyUsedPast8Files()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult((0, Packets((10.0, 10.0, "K_")), "")));
        var probe = new KeyframeProbe(runner);

        for (int i = 0; i < 8; i++)
        {
            await probe.FindAtOrBeforeAsync($"file{i}.mp4", 12.0, CancellationToken.None);
        }

        // Touch file0 so file1 becomes LRU
        Assert.True(probe.TryGetCached("file0.mp4", 12.0, out _));

        // Add 9th file -> evicts file1, keeps file0
        await probe.FindAtOrBeforeAsync("file8.mp4", 12.0, CancellationToken.None);

        Assert.True(probe.TryGetCached("file0.mp4", 12.0, out _));
        Assert.False(probe.TryGetCached("file1.mp4", 12.0, out _));
    }

    [Fact]
    public async Task CancelledProbe_Throws()
    {
        using var cts = new CancellationTokenSource(30);
        var probe = new KeyframeProbe(new CancelAwareRunner());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.FindAtOrBeforeAsync("v.mp4", 50, cts.Token));
    }

    private sealed class CancelAwareRunner : IProcessRunner
    {
        public async Task<ProcessOutcome> RunAsync(
            string fileName, IReadOnlyList<string> arguments, Func<TextReader, Task>? stdoutConsumer, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new ProcessOutcome(0, "");
        }
    }

    // ---- tracker ----

    private sealed class FakeProbe : IKeyframeProbe
    {
        public List<double> Started { get; } = [];
        public List<double> Cancelled { get; } = [];
        public Func<double, double?> Answer { get; set; } = s => s - 1;
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        public async Task<double?> FindAtOrBeforeAsync(string path, double seconds, CancellationToken ct)
        {
            lock (Started) Started.Add(seconds);
            try
            {
                if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            }
            catch (OperationCanceledException)
            {
                lock (Cancelled) Cancelled.Add(seconds);
                throw;
            }
            return Answer(seconds);
        }
    }

    [Fact]
    public async Task Tracker_NewInWhileProbing_CancelsStaleProbeAndReportsOnlyNewest()
    {
        var probe = new FakeProbe { Delay = TimeSpan.FromSeconds(30) };
        using var tracker = new CutStartTracker(probe, TimeSpan.Zero);
        var results = new List<double?>();
        tracker.Changed += r => { lock (results) results.Add(r); };

        tracker.Track("v.mp4", 10);
        await WaitUntil(() => probe.Started.Count == 1);
        probe.Delay = TimeSpan.Zero;
        tracker.Track("v.mp4", 20);
        // The stale probe records its cancellation on its own continuation, which can land after the new result.
        await WaitUntil(() => { lock (results) lock (probe.Cancelled) return results.Contains(19) && probe.Cancelled.Count > 0; });

        Assert.Equal([10.0], probe.Cancelled);
        Assert.DoesNotContain(9.0, results.OfType<double>());
    }

    [Fact]
    public async Task Tracker_DragWithinDebounce_ProbesOnlyTheLastValue()
    {
        var probe = new FakeProbe();
        using var tracker = new CutStartTracker(probe, TimeSpan.FromMilliseconds(150));
        var results = new List<double?>();
        tracker.Changed += r => { lock (results) results.Add(r); };

        for (int i = 1; i <= 5; i++) tracker.Track("v.mp4", i * 10);
        await WaitUntil(() => results.Contains(49));

        Assert.Equal([50.0], probe.Started);
    }

    [Fact]
    public async Task Tracker_ClearingReportsNullAndStopsProbing()
    {
        var probe = new FakeProbe();
        using var tracker = new CutStartTracker(probe, TimeSpan.FromMilliseconds(100));
        var results = new List<double?>();
        tracker.Changed += r => { lock (results) results.Add(r); };

        tracker.Track("v.mp4", 10);
        tracker.Track(null, null);
        await Task.Delay(300);

        Assert.Empty(probe.Started);
        Assert.All(results, r => Assert.Null(r));
    }

    [Fact]
    public async Task Tracker_SamePoint_IsNotProbedTwice()
    {
        var probe = new FakeProbe();
        using var tracker = new CutStartTracker(probe, TimeSpan.Zero);
        var results = new List<double?>();
        tracker.Changed += r => { lock (results) results.Add(r); };

        tracker.Track("v.mp4", 10);
        await WaitUntil(() => results.Contains(9));
        tracker.Track("v.mp4", 10);
        await Task.Delay(100);

        Assert.Single(probe.Started);
    }

    [Fact]
    public async Task Tracker_CachedHit_EmitsImmediatelyWithZeroDebounceAndZeroNullFlicker()
    {
        string output = Packets((88.5, 88.5, "K_"), (91.25, 91.25, "K_"), (96.0, 96.0, "K_"));
        var runner = new FakeRunner((_, _) => Task.FromResult((0, output, "")));
        var probe = new KeyframeProbe(runner);
        await probe.FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None);

        // Long debounce proves cached path bypasses timer completely.
        using var tracker = new CutStartTracker(probe, TimeSpan.FromSeconds(30));
        var results = new List<double?>();
        tracker.Changed += r => { lock (results) results.Add(r); };

        tracker.Track("v.mp4", 97.0);

        Assert.Equal([96.0], results);
        Assert.Single(runner.Calls);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "timed out waiting");
    }

    // ---- hint ----

    [Theory]
    [InlineData(100.0, 98.6, 1.4)]
    [InlineData(10.0, 0.0, 10.0)]
    public void Lead_IsHowFarBeforeIn(double inPoint, double keyframe, double expected) =>
        Assert.Equal(expected, CutStart.Lead(inPoint, keyframe)!.Value, 6);

    [Theory]
    [InlineData(100.0, 99.97)]
    [InlineData(100.0, 100.0)]
    [InlineData(100.0, null)]
    public void Lead_AboutOneFrameOrUnknown_ShowsNothing(double inPoint, double? keyframe) =>
        Assert.Null(CutStart.Lead(inPoint, keyframe));

    [Fact]
    public void Format_OneDecimalAndUnit()
    {
        Assert.Equal("1.4 s", CutStart.Format(1.4, CultureInfo.InvariantCulture));
        Assert.Equal("1,4 s", CutStart.Format(1.4, new CultureInfo("de-DE")));
        Assert.Equal("12.0 s", CutStart.Format(11.96, CultureInfo.InvariantCulture));
    }
}
