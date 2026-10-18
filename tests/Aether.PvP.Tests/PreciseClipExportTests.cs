using Aether.PvP.Review.Clipping;

namespace Aether.PvP.Tests;

public sealed class PreciseClipExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-precise-" + Guid.NewGuid().ToString("N"));

    public PreciseClipExportTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string MakeSource(string name = "src.mp4")
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, "not a real video");
        return path;
    }

    private ClipRequest Request(string source, long? target = null, VideoEncoder? preferred = null) =>
        new(source, 1.0, 11.0, Path.Combine(_dir, "out"), "Clip", ExportMode.Precise, target, preferred);

    private string[] OutputFiles() =>
        Directory.Exists(Path.Combine(_dir, "out")) ? Directory.GetFiles(Path.Combine(_dir, "out")) : [];

    private sealed class FakeDetector(params VideoEncoder[] available) : IHardwareEncoderDetector
    {
        public List<VideoEncoder> Failed { get; } = [];

        public Task<IReadOnlyList<VideoEncoder>> GetAvailableEncodersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VideoEncoder>>(available.Where(e => !Failed.Contains(e)).ToList());

        public void ReportFailure(VideoEncoder encoder) => Failed.Add(encoder);
    }

    private sealed class FakeRunner(Func<IReadOnlyList<string>, Func<TextReader, Task>?, CancellationToken, Task<ProcessOutcome>> behaviour)
        : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<ProcessOutcome> RunAsync(
            string fileName, IReadOnlyList<string> arguments, Func<TextReader, Task>? stdoutConsumer, CancellationToken ct)
        {
            Calls.Add(arguments);
            return behaviour(arguments, stdoutConsumer, ct);
        }
    }

    private static string EncoderOf(IReadOnlyList<string> args) => args[args.ToList().IndexOf("-c:v") + 1];

    private static string OutputOf(IReadOnlyList<string> args) => args[^1];

    private static ProcessOutcome Ok => new(0, "");

    // ---- encoder detection ----

    private static FakeRunner EncoderListRunner(string listed, Func<string, bool> canEncode) => new(async (args, consumer, _) =>
    {
        if (args.Contains("-encoders"))
        {
            await consumer!(new StringReader(listed));
            return Ok;
        }
        return canEncode(EncoderOf(args)) ? Ok : new ProcessOutcome(1, "driver failed");
    });

    private const string AllListed = """
         V....D libx264              libx264 H.264
         V....D h264_nvenc           NVIDIA NVENC H.264 encoder
         V....D h264_qsv             H.264 (Intel Quick Sync Video)
         V....D h264_amf             AMD AMF H.264 Encoder
        """;

    [Fact]
    public async Task Detector_ReturnsLadderOrder()
    {
        var detector = new FfmpegEncoderDetector(EncoderListRunner(AllListed, _ => true), "ffmpeg");

        var found = await detector.GetAvailableEncodersAsync();

        Assert.Equal([VideoEncoder.Nvenc, VideoEncoder.Qsv, VideoEncoder.Amf, VideoEncoder.X264], found);
    }

    [Fact]
    public async Task Detector_SkipsListedEncoderThatFailsTestEncode()
    {
        var detector = new FfmpegEncoderDetector(
            EncoderListRunner(AllListed, name => name != "h264_nvenc"), "ffmpeg");

        var found = await detector.GetAvailableEncodersAsync();

        Assert.DoesNotContain(VideoEncoder.Nvenc, found);
        Assert.Equal(VideoEncoder.Qsv, found[0]);
    }

    [Fact]
    public async Task Detector_SkipsEncoderMissingFromList()
    {
        var detector = new FfmpegEncoderDetector(
            EncoderListRunner(" V....D libx264   libx264 H.264", _ => true), "ffmpeg");

        Assert.Equal([VideoEncoder.X264], await detector.GetAvailableEncodersAsync());
    }

    [Fact]
    public async Task Detector_CachesResultsAndDropsReportedFailures()
    {
        var runner = EncoderListRunner(AllListed, _ => true);
        var detector = new FfmpegEncoderDetector(runner, "ffmpeg");

        await detector.GetAvailableEncodersAsync();
        int callsAfterFirst = runner.Calls.Count;
        detector.ReportFailure(VideoEncoder.Nvenc);
        var second = await detector.GetAvailableEncodersAsync();

        Assert.Equal(callsAfterFirst, runner.Calls.Count);
        Assert.DoesNotContain(VideoEncoder.Nvenc, second);
    }

    [Fact]
    public async Task Detector_WhenEncoderListFails_ReturnsNothing()
    {
        var runner = new FakeRunner((_, _, _) => Task.FromResult(new ProcessOutcome(1, "boom")));
        var detector = new FfmpegEncoderDetector(runner, "ffmpeg");

        Assert.Empty(await detector.GetAvailableEncodersAsync());
    }

    [Fact]
    public async Task Detector_TestEncodeThatHangs_CountsAsFailure()
    {
        var runner = new FakeRunner(async (args, consumer, ct) =>
        {
            if (args.Contains("-encoders"))
            {
                await consumer!(new StringReader(" V....D h264_amf   AMD"));
                return Ok;
            }
            await Task.Delay(Timeout.Infinite, ct);
            return Ok;
        });
        var detector = new FfmpegEncoderDetector(runner, "ffmpeg", TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // The probe times out on its own; the caller's token is not what ends it.
        Assert.Empty(await detector.GetAvailableEncodersAsync(cts.Token));
    }

    [Fact]
    public async Task Detector_DoesNotCacheEmptyOrFailedResults()
    {
        bool broken = true;
        var runner = new FakeRunner(async (args, consumer, _) =>
        {
            if (args.Contains("-encoders"))
            {
                if (broken) return new ProcessOutcome(1, "cold start");
                await consumer!(new StringReader(" V....D libx264   libx264 H.264"));
            }
            return Ok;
        });
        var detector = new FfmpegEncoderDetector(runner, "ffmpeg");

        Assert.Empty(await detector.GetAvailableEncodersAsync());
        broken = false;

        Assert.Equal([VideoEncoder.X264], await detector.GetAvailableEncodersAsync());
    }

    [Fact]
    public async Task Detector_DoesNotCacheAProbeTimeout()
    {
        bool hang = true;
        var runner = new FakeRunner(async (args, consumer, ct) =>
        {
            if (args.Contains("-encoders"))
            {
                await consumer!(new StringReader(" V....D h264_nvenc   NVIDIA\n V....D libx264   libx264"));
                return Ok;
            }
            if (hang && EncoderOf(args) == "h264_nvenc") await Task.Delay(Timeout.Infinite, ct);
            return Ok;
        });
        var detector = new FfmpegEncoderDetector(runner, "ffmpeg", TimeSpan.FromMilliseconds(100));

        Assert.Equal([VideoEncoder.X264], await detector.GetAvailableEncodersAsync());
        hang = false;

        Assert.Equal([VideoEncoder.Nvenc, VideoEncoder.X264], await detector.GetAvailableEncodersAsync());
    }

    [Fact]
    public void Order_PutsPreferredFirstOnlyWhenAvailable()
    {
        VideoEncoder[] available = [VideoEncoder.Nvenc, VideoEncoder.Amf, VideoEncoder.X264];

        Assert.Equal([VideoEncoder.Amf, VideoEncoder.Nvenc, VideoEncoder.X264],
            ClipExportService.Order(available, VideoEncoder.Amf));
        Assert.Equal(available, ClipExportService.Order(available, VideoEncoder.Qsv));
        Assert.Equal(available, ClipExportService.Order(available, null));
    }

    // ---- fallback in the service ----

    [Fact]
    public async Task Export_UsesFirstEncoderInLadder()
    {
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllText(OutputOf(args), "data");
            return Task.FromResult(Ok);
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.Nvenc, VideoEncoder.X264));

        var result = await service.ExportAsync(Request(MakeSource()));

        Assert.Equal(VideoEncoder.Nvenc, result.EncoderUsed);
        Assert.Single(runner.Calls);
        Assert.True(File.Exists(result.OutputPath));
        Assert.EndsWith(".mp4", result.OutputPath);
        Assert.Single(OutputFiles());
    }

    [Fact]
    public async Task Export_WhenEncoderFails_FallsDownTheLadderAndReportsIt()
    {
        var detector = new FakeDetector(VideoEncoder.Nvenc, VideoEncoder.Qsv, VideoEncoder.X264);
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllText(OutputOf(args), "partial");
            return Task.FromResult(EncoderOf(args) == "libx264" ? Ok : new ProcessOutcome(1, "no device"));
        });
        using var service = new ClipExportService("ffmpeg", runner, detector);

        var result = await service.ExportAsync(Request(MakeSource()));

        Assert.Equal(VideoEncoder.X264, result.EncoderUsed);
        Assert.Equal(["h264_nvenc", "h264_qsv", "libx264"], runner.Calls.Select(EncoderOf));
        Assert.Equal([VideoEncoder.Nvenc, VideoEncoder.Qsv], detector.Failed);
        Assert.Single(OutputFiles());
    }

    [Fact]
    public async Task Export_WhenEveryEncoderFails_ThrowsAndLeavesNoFiles()
    {
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllText(OutputOf(args), "partial");
            return Task.FromResult(new ProcessOutcome(1, "nope"));
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.Qsv, VideoEncoder.X264));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(Request(MakeSource())));

        Assert.Contains("nope", ex.Message);
        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_WithNoUsableEncoder_Throws()
    {
        var runner = new FakeRunner((_, _, _) => Task.FromResult(Ok));
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(Request(MakeSource())));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Export_HonoursPreferredEncoder()
    {
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllText(OutputOf(args), "data");
            return Task.FromResult(Ok);
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.Nvenc, VideoEncoder.X264));

        var result = await service.ExportAsync(Request(MakeSource(), preferred: VideoEncoder.X264));

        Assert.Equal(VideoEncoder.X264, result.EncoderUsed);
    }

    [Fact]
    public async Task Export_KeepsMkvForMkvSourcesAndMovesOthersToMp4()
    {
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllText(OutputOf(args), "data");
            return Task.FromResult(Ok);
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.X264));

        var mkv = await service.ExportAsync(Request(MakeSource("a.mkv")));
        var webm = await service.ExportAsync(Request(MakeSource("b.webm")));

        Assert.EndsWith(".mkv", mkv.OutputPath);
        Assert.EndsWith(".mp4", webm.OutputPath);
        Assert.NotEqual(mkv.OutputPath, webm.OutputPath);
    }

    [Fact]
    public async Task Export_SameSecondTwice_GetsDistinctNames()
    {
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllText(OutputOf(args), "data");
            return Task.FromResult(Ok);
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.X264));
        string source = MakeSource();

        var first = await service.ExportAsync(Request(source));
        var second = await service.ExportAsync(Request(source));

        Assert.NotEqual(first.OutputPath, second.OutputPath);
        Assert.Equal(2, OutputFiles().Length);
    }

    [Fact]
    public async Task Export_ImpossibleSizeLimit_ThrowsClearErrorBeforeRunning()
    {
        var runner = new FakeRunner((_, _, _) => Task.FromResult(Ok));
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.X264));

        var ex = await Assert.ThrowsAsync<ClipTargetTooSmallException>(
            () => service.ExportAsync(Request(MakeSource(), target: 10_000)));

        Assert.True(ex.MinimumSizeBytes > 10_000);
        Assert.Empty(runner.Calls);
        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_WhenEveryEncoderFails_DoesNotCrossAnyEncoderOff()
    {
        var detector = new FakeDetector(VideoEncoder.Nvenc, VideoEncoder.X264);
        var runner = new FakeRunner((_, _, _) => Task.FromResult(new ProcessOutcome(1, "disk full")));
        using var service = new ClipExportService("ffmpeg", runner, detector);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(Request(MakeSource())));

        Assert.Empty(detector.Failed);
    }

    [Fact]
    public async Task Export_NeverCrossesOffX264()
    {
        var detector = new FakeDetector(VideoEncoder.X264, VideoEncoder.Nvenc);
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllText(OutputOf(args), "data");
            return Task.FromResult(EncoderOf(args) == "libx264" ? new ProcessOutcome(1, "bad") : Ok);
        });
        using var service = new ClipExportService("ffmpeg", runner, detector);

        var result = await service.ExportAsync(Request(MakeSource(), preferred: VideoEncoder.X264));

        Assert.Equal(VideoEncoder.Nvenc, result.EncoderUsed);
        Assert.Empty(detector.Failed);
    }

    [Fact]
    public async Task Export_Cancel_WhileEncoderExitsNonZero_DoesNotCrossItOff()
    {
        var detector = new FakeDetector(VideoEncoder.Nvenc, VideoEncoder.X264);
        using var cts = new CancellationTokenSource();
        var runner = new FakeRunner((args, _, _) =>
        {
            cts.Cancel();
            return Task.FromResult(new ProcessOutcome(1, "killed"));
        });
        using var service = new ClipExportService("ffmpeg", runner, detector);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(Request(MakeSource()), cts.Token));

        Assert.Empty(detector.Failed);
        Assert.Single(runner.Calls);
        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_SizeLimitOvershoot_RetriesOnceAtLowerRate()
    {
        var sizes = new Queue<int>([1_300_000, 900_000]);
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllBytes(OutputOf(args), new byte[sizes.Dequeue()]);
            return Task.FromResult(Ok);
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.X264));

        var result = await service.ExportAsync(Request(MakeSource(), target: 1_000_000));

        Assert.Equal(2, runner.Calls.Count);
        long first = long.Parse(runner.Calls[0][runner.Calls[0].ToList().IndexOf("-b:v") + 1]);
        long second = long.Parse(runner.Calls[1][runner.Calls[1].ToList().IndexOf("-b:v") + 1]);
        Assert.Equal((long)Math.Floor(first * 1_000_000.0 / 1_300_000 * 0.97), second);
        Assert.Equal(900_000, result.SizeBytes);
        Assert.Single(OutputFiles());
    }

    [Fact]
    public async Task Export_SizeLimitStillOverAfterRetry_FailsClearlyAndLeavesNoFiles()
    {
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllBytes(OutputOf(args), new byte[1_300_000]);
            return Task.FromResult(Ok);
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.X264));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExportAsync(Request(MakeSource(), target: 1_000_000)));

        Assert.Contains("limit", ex.Message);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_UnderTheSizeLimit_RunsOnlyOnce()
    {
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllBytes(OutputOf(args), new byte[500_000]);
            return Task.FromResult(Ok);
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.X264));

        await service.ExportAsync(Request(MakeSource(), target: 1_000_000));

        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Export_ReservesNameBeforeEncoderDetectionFinishes()
    {
        var gate = new TaskCompletionSource();
        var detector = new SlowDetector(gate.Task, VideoEncoder.X264);
        var runner = new FakeRunner((args, _, _) =>
        {
            File.WriteAllText(OutputOf(args), "data");
            return Task.FromResult(Ok);
        });
        using var service = new ClipExportService("ffmpeg", runner, detector);
        string source = MakeSource();

        var first = service.ExportAsync(Request(source));
        var second = service.ExportAsync(Request(source));
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.NotEqual(results[0].OutputPath, results[1].OutputPath);
        Assert.Equal(2, OutputFiles().Length);
    }

    [Fact]
    public void Reserve_SameSecondTwice_GetsDistinctNamesAndPartFiles()
    {
        var request = new ClipRequest(MakeSource(), 1.0, 11.0, Path.Combine(_dir, "out"), "Clip");
        Directory.CreateDirectory(request.DestinationDirectory);
        string first = ClipExportService.ReserveOutputPath(request);
        string second = ClipExportService.ReserveOutputPath(request);

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first + ".part"));
        Assert.True(File.Exists(second + ".part"));
    }

    [Fact]
    public async Task Export_SharesOneDetectorAcrossConcurrentExports()
    {
        int probes = 0;
        var runner = new FakeRunner(async (args, consumer, _) =>
        {
            if (args.Contains("-encoders"))
            {
                Interlocked.Increment(ref probes);
                await Task.Delay(50);
                await consumer!(new StringReader(" V....D libx264   libx264 H.264"));
                return Ok;
            }
            if (args.Contains("lavfi")) return Ok;
            File.WriteAllText(OutputOf(args), "data");
            return Ok;
        });
        using var service = new ClipExportService("ffmpeg", runner);
        string source = MakeSource();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.ExportAsync(Request(source))));

        Assert.Equal(1, probes);
    }

    private sealed class SlowDetector(Task gate, params VideoEncoder[] available) : IHardwareEncoderDetector
    {
        public async Task<IReadOnlyList<VideoEncoder>> GetAvailableEncodersAsync(CancellationToken ct = default)
        {
            await gate;
            return available;
        }

        public void ReportFailure(VideoEncoder encoder) { }
    }

    [Fact]
    public async Task Export_Cancel_RemovesPartFile()
    {
        var started = new TaskCompletionSource();
        var runner = new FakeRunner(async (args, _, ct) =>
        {
            File.WriteAllText(OutputOf(args), "partial");
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Ok;
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.X264));
        using var cts = new CancellationTokenSource();

        var task = service.ExportAsync(Request(MakeSource()), cts.Token);
        await started.Task;
        Assert.Single(OutputFiles());

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_DisposeService_CancelsPreciseExportAndCleansPart()
    {
        var started = new TaskCompletionSource();
        var runner = new FakeRunner(async (args, _, ct) =>
        {
            File.WriteAllText(OutputOf(args), "partial");
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Ok;
        });
        var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.X264));

        var task = service.ExportAsync(Request(MakeSource()));
        await started.Task;
        service.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_ReportsProgressFromFfmpegOutput()
    {
        var runner = new FakeRunner(async (args, consumer, _) =>
        {
            await consumer!(new StringReader("out_time_us=5000000\nspeed=2.0x\nprogress=continue\nprogress=end\n"));
            File.WriteAllText(OutputOf(args), "data");
            return Ok;
        });
        using var service = new ClipExportService("ffmpeg", runner, new FakeDetector(VideoEncoder.X264));
        var reports = new List<ClipProgress>();

        await service.ExportAsync(Request(MakeSource()), progress: new SyncProgress(reports.Add));

        Assert.Equal(0.5, reports[0].Fraction, 3);
        Assert.Equal(1.0, reports[^1].Fraction);
    }

    private sealed class SyncProgress(Action<ClipProgress> onReport) : IProgress<ClipProgress>
    {
        public void Report(ClipProgress value) => onReport(value);
    }

    [Fact]
    public async Task Export_FastMode_StillUsesStreamCopyPathUntouched()
    {
        var runner = new FakeRunner((_, _, _) => throw new InvalidOperationException("runner must not be used"));
        using var service = new ClipExportService("definitely-missing-ffmpeg", runner, new FakeDetector(VideoEncoder.X264));
        var fast = new ClipRequest(MakeSource(), 1, 2, Path.Combine(_dir, "out"));

        Assert.Equal(ExportMode.Fast, fast.Mode);
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ExportAsync(fast));
        Assert.Empty(runner.Calls);
    }

    // ---- bitrate math ----

    [Fact]
    public void Bitrate_SubtractsAudioAndAppliesMargin()
    {
        // 10 MB over 10 s: 8 Mbit/s total before the margin.
        var plan = BitrateCalculator.Plan(10_000_000, 10);

        Assert.True(plan.IsFeasible);
        long expected = (long)Math.Floor(10_000_000 * 8.0 * BitrateCalculator.SafetyMargin / 10) - BitrateCalculator.AudioBitsPerSecond;
        Assert.Equal(expected, plan.VideoBitsPerSecond);
    }

    [Fact]
    public void Bitrate_ResultNeverExceedsTarget()
    {
        var plan = BitrateCalculator.Plan(25_000_000, 37.3);

        double projectedBytes = (plan.VideoBitsPerSecond + BitrateCalculator.AudioBitsPerSecond) * 37.3 / 8;
        Assert.True(projectedBytes <= 25_000_000 * 0.95);
    }

    [Fact]
    public void Bitrate_TooSmallTarget_IsNotFeasibleAndReportsMinimum()
    {
        var plan = BitrateCalculator.Plan(50_000, 60);

        Assert.False(plan.IsFeasible);
        Assert.Equal(0, plan.VideoBitsPerSecond);
        Assert.True(plan.MinimumSizeBytes > 50_000);
    }

    [Fact]
    public void Bitrate_ExactlyAtMinimum_IsFeasible()
    {
        var minimum = BitrateCalculator.Plan(1, 10).MinimumSizeBytes;

        Assert.True(BitrateCalculator.Plan(minimum, 10).IsFeasible);
        Assert.False(BitrateCalculator.Plan(minimum / 2, 10).IsFeasible);
    }

    [Fact]
    public void Bitrate_LeavesRoomForContainerOverhead()
    {
        var plan = BitrateCalculator.Plan(25_000_000, 37.3);

        double projectedBytes = (plan.VideoBitsPerSecond + BitrateCalculator.AudioBitsPerSecond) * 37.3 / 8;
        Assert.True(projectedBytes <= 25_000_000 * 0.92);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(1_000_000, 0)]
    [InlineData(1_000_000, -3)]
    public void Bitrate_NonsenseInputs_AreNotFeasible(long target, double duration)
    {
        Assert.False(BitrateCalculator.Plan(target, duration).IsFeasible);
    }

    // ---- arguments ----

    [Fact]
    public void Args_ConstantQuality_UsesQualitySettingPerEncoder()
    {
        var req = Request("in.mp4");

        var nvenc = ReEncodeArgumentsBuilder.Build(req, "o.mp4.part", VideoEncoder.Nvenc, null);
        var qsv = ReEncodeArgumentsBuilder.Build(req, "o.mp4.part", VideoEncoder.Qsv, null);
        var amf = ReEncodeArgumentsBuilder.Build(req, "o.mp4.part", VideoEncoder.Amf, null);
        var x264 = ReEncodeArgumentsBuilder.Build(req, "o.mp4.part", VideoEncoder.X264, null);

        Assert.Contains("-cq", nvenc);
        Assert.Contains("-global_quality", qsv);
        Assert.Contains("-qp_i", amf);
        Assert.Contains("-crf", x264);
        Assert.DoesNotContain("-maxrate", x264);
    }

    [Fact]
    public void Args_SizeLimited_SetsBitrateMaxrateAndBufsize()
    {
        var args = ReEncodeArgumentsBuilder.Build(Request("in.mp4"), "o.mp4.part", VideoEncoder.X264, 2_000_000).ToList();

        Assert.Equal("2000000", args[args.IndexOf("-b:v") + 1]);
        Assert.Equal("2000000", args[args.IndexOf("-maxrate") + 1]);
        Assert.Equal("4000000", args[args.IndexOf("-bufsize") + 1]);
        Assert.DoesNotContain("-crf", args);
    }

    [Fact]
    public void Args_SizeLimited_ShortClipGetsOneSecondBuffer()
    {
        var short3s = Request("in.mp4") with { StartTimeSeconds = 0, EndTimeSeconds = 3 };

        var args = ReEncodeArgumentsBuilder.Build(short3s, "o.mp4.part", VideoEncoder.X264, 2_000_000).ToList();

        Assert.Equal("2000000", args[args.IndexOf("-bufsize") + 1]);
    }

    [Fact]
    public void Args_CutIsAccurateAndAudioIsAac()
    {
        var args = ReEncodeArgumentsBuilder.Build(Request("in.mp4"), "o.mp4.part", VideoEncoder.X264, null).ToList();

        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
        Assert.Equal("1.000", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("10.000", args[args.IndexOf("-t") + 1]);
        Assert.Equal("libx264", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal("128k", args[args.IndexOf("-b:a") + 1]);
        Assert.DoesNotContain("copy", args);
    }

    [Fact]
    public void Args_Mp4GetsFaststartAndMkvDoesNot()
    {
        var mp4 = ReEncodeArgumentsBuilder.Build(Request("in.mp4"), "o.mp4.part", VideoEncoder.X264, null).ToList();
        var mkv = ReEncodeArgumentsBuilder.Build(Request("in.mkv"), "o.mkv.part", VideoEncoder.X264, null).ToList();

        Assert.Equal("+faststart", mp4[mp4.IndexOf("-movflags") + 1]);
        Assert.Equal("mp4", mp4[mp4.IndexOf("-f") + 1]);
        Assert.DoesNotContain("-movflags", mkv);
        Assert.Equal("matroska", mkv[mkv.IndexOf("-f") + 1]);
    }

    [Fact]
    public void Args_PipeProgressToStdoutAndWriteToTempPath()
    {
        var args = ReEncodeArgumentsBuilder.Build(Request("in.mp4"), "o.mp4.part", VideoEncoder.X264, null).ToList();

        Assert.Equal("pipe:1", args[args.IndexOf("-progress") + 1]);
        Assert.Equal("o.mp4.part", args[^1]);
    }

    [Fact]
    public void Args_KeepOptionalAudioAndFirstVideoOnly()
    {
        var args = ReEncodeArgumentsBuilder.Build(Request("in.mp4"), "o.mp4.part", VideoEncoder.X264, null).ToList();

        Assert.Contains("0:v:0", args);
        Assert.Contains("0:a:0?", args);
    }

    // ---- progress parser ----

    [Fact]
    public void Parser_BlockGivesFractionSpeedAndRemaining()
    {
        var parser = new FfmpegProgressParser(10);

        Assert.Null(parser.Feed("frame=120"));
        Assert.Null(parser.Feed("out_time_us=2500000"));
        Assert.Null(parser.Feed("speed=2.5x"));
        var update = parser.Feed("progress=continue");

        Assert.NotNull(update);
        Assert.Equal(0.25, update.Fraction, 3);
        Assert.Equal(2.5, update.Speed, 3);
        Assert.Equal(3.0, update.Remaining!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void Parser_EndBlockIsComplete()
    {
        var parser = new FfmpegProgressParser(10);
        parser.Feed("out_time_us=9000000");

        var update = parser.Feed("progress=end");

        Assert.Equal(1.0, update!.Fraction);
        Assert.Equal(TimeSpan.Zero, update.Remaining);
    }

    [Fact]
    public void Parser_StartupNotAvailableValuesGiveZeroProgressAndUnknownEta()
    {
        var parser = new FfmpegProgressParser(10);
        parser.Feed("out_time_us=N/A");
        parser.Feed("out_time_ms=-9223372036854775807");
        parser.Feed("speed=N/A");

        var update = parser.Feed("progress=continue");

        Assert.Equal(0, update!.Fraction);
        Assert.Equal(0, update.Speed);
        Assert.Null(update.Remaining);
    }

    [Fact]
    public void Parser_ClampsOvershootPastDuration()
    {
        var parser = new FfmpegProgressParser(10);
        parser.Feed("out_time_us=12000000");
        parser.Feed("speed=1x");

        var update = parser.Feed("progress=continue");

        Assert.Equal(1.0, update!.Fraction);
        Assert.Equal(TimeSpan.Zero, update.Remaining);
    }

    [Fact]
    public void Parser_IgnoresGarbageLines()
    {
        var parser = new FfmpegProgressParser(10);

        Assert.Null(parser.Feed(""));
        Assert.Null(parser.Feed("no equals sign"));
        Assert.Null(parser.Feed("=leading"));
    }

    [Fact]
    public async Task Parser_ReadAsyncReportsEveryBlock()
    {
        var parser = new FfmpegProgressParser(4);
        var reports = new List<ClipProgress>();
        string text = "out_time_us=1000000\nspeed=1x\nprogress=continue\nout_time_us=3000000\nspeed=1x\nprogress=continue\nprogress=end\n";

        await parser.ReadAsync(new StringReader(text), new SyncProgress(reports.Add));

        Assert.Equal([0.25, 0.75, 1.0], reports.Select(r => r.Fraction));
    }

    // ---- real process runner ----

    private string MakeChattyScript()
    {
        string path;
        if (OperatingSystem.IsWindows())
        {
            path = Path.Combine(_dir, "chatty.cmd");
            File.WriteAllText(path,
                "@echo off\r\nfor /l %%i in (1,1,3000) do (echo stdout line %%i padding padding padding padding padding& echo stderr line %%i padding padding padding padding padding 1>&2)\r\nexit /b 0\r\n");
        }
        else
        {
            path = Path.Combine(_dir, "chatty.sh");
            File.WriteAllText(path,
                "#!/bin/sh\ni=0\nwhile [ $i -lt 3000 ]; do echo \"stdout line $i padding padding padding padding padding\"; echo \"stderr line $i padding padding padding padding padding\" >&2; i=$((i+1)); done\nexit 0\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    [Fact]
    public async Task SystemProcessRunner_DrainsLargeOutputOnBothPipes()
    {
        string path = MakeChattyScript();

        int lines = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await new SystemProcessRunner().RunAsync(path, [], async reader =>
        {
            while (await reader.ReadLineAsync(cts.Token) is not null) lines++;
        }, cts.Token);

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(3000, lines);
        Assert.Contains("stderr line 2999", outcome.StandardError);
    }

    [Fact]
    public async Task SystemProcessRunner_ConsumerThatThrows_StillDrainsAndFinishes()
    {
        string path = MakeChattyScript();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await new SystemProcessRunner().RunAsync(path, [], reader =>
        {
            reader.ReadLine();
            throw new InvalidOperationException("progress UI blew up");
        }, cts.Token);

        Assert.Equal(0, outcome.ExitCode);
        Assert.Contains("stderr line 2999", outcome.StandardError);
    }

    [Fact]
    public async Task SystemProcessRunner_AlreadyCancelled_DoesNotStartTheProcess()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SystemProcessRunner().RunAsync(Path.Combine(_dir, "does-not-exist.exe"), [], null, cts.Token));
    }
}
