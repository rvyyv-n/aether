using System.Globalization;
using Aether.PvP.Review.Clipping;

namespace Aether.PvP.Tests;

public sealed class KeyframeProbeTests
{
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

    [Fact]
    public async Task Ffprobe_TakesLastKeyframeAtOrBeforeIn()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult((0, "88.5\n91.25\n95.0\n", "")));
        var probe = new KeyframeProbe(runner);

        double? found = await probe.FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None);

        Assert.Equal(91.25, found);
        Assert.Equal("84%94.05", IntervalOf(runner.Calls[0].Args));
    }

    [Fact]
    public async Task Ffprobe_KeyframeExactlyOnIn_IsUsed()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult((0, "90\n94.0\n", "")));

        Assert.Equal(94.0, await new KeyframeProbe(runner).FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None));
    }

    [Fact]
    public async Task EmptyFirstWindow_WidensOnceTo30Seconds()
    {
        var runner = new FakeRunner((_, args) =>
            Task.FromResult(IntervalOf(args).StartsWith("70%", StringComparison.Ordinal) ? (0, "75.5\n", "") : (0, "", "")));
        var probe = new KeyframeProbe(runner);

        double? found = await probe.FindAtOrBeforeAsync("v.mp4", 100.0, CancellationToken.None);

        Assert.Equal(75.5, found);
        Assert.Equal(["90%100.05", "70%100.05"], runner.Calls.Select(c => IntervalOf(c.Args)));
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
        Assert.Equal("0%4.05", IntervalOf(runner.Calls[0].Args));
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
    public async Task SameQuestion_IsAnsweredFromCache()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult((0, "91.0\n", "")));
        var probe = new KeyframeProbe(runner);

        await probe.FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None);
        await probe.FindAtOrBeforeAsync("v.mp4", 94.0, CancellationToken.None);

        Assert.Single(runner.Calls);
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
        await WaitUntil(() => results.Contains(19));

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
