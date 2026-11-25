using Aether.PvP.Review.Clipping;

namespace Aether.PvP.Tests;

public sealed class EncoderCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-enccache-" + Guid.NewGuid().ToString("N"));

    public EncoderCacheTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class FakeRunner(Func<IReadOnlyList<string>, Func<TextReader, Task>?, CancellationToken, Task<ProcessOutcome>> behaviour)
        : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<ProcessOutcome> RunAsync(
            string fileName, IReadOnlyList<string> arguments, Func<TextReader, Task>? stdoutConsumer, CancellationToken ct)
        {
            lock (Calls) Calls.Add(arguments);
            return behaviour(arguments, stdoutConsumer, ct);
        }
    }

    private sealed class MemoryEncoderCache : IEncoderCache
    {
        private string? _fingerprint;
        private List<VideoEncoder>? _encoders;
        public int WriteCount { get; private set; }
        public int InvalidateCount { get; private set; }

        public IReadOnlyList<VideoEncoder>? TryRead(string fingerprint) =>
            _encoders is not null && _fingerprint == fingerprint ? [.. _encoders] : null;

        public void Write(string fingerprint, IReadOnlyList<VideoEncoder> encoders)
        {
            WriteCount++;
            _fingerprint = fingerprint;
            _encoders = [.. encoders];
        }

        public void Invalidate()
        {
            InvalidateCount++;
            _fingerprint = null;
            _encoders = null;
        }
    }

    [Fact]
    public void FileEncoderCache_RoundTripAndFingerprintValidation()
    {
        string path = Path.Combine(_dir, "nested", "encoder-cache.json");
        var cache = new FileEncoderCache(path);

        Assert.Null(cache.TryRead("fp-1"));

        cache.Write("fp-1", [VideoEncoder.Nvenc, VideoEncoder.X264]);

        Assert.Equal([VideoEncoder.Nvenc, VideoEncoder.X264], cache.TryRead("fp-1"));
        Assert.Null(cache.TryRead("fp-other"));

        cache.Invalidate();
        Assert.False(File.Exists(path));
        Assert.Null(cache.TryRead("fp-1"));
        cache.Invalidate(); // no throw when absent
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"version":2,"fingerprint":"fp","encoders":["libx264"]}""")]
    [InlineData("""{"version":1,"fingerprint":"fp","encoders":[]}""")]
    [InlineData("""{"version":1,"fingerprint":"fp","encoders":["unknown_enc"]}""")]
    [InlineData("""{"version":1,"fingerprint":"fp","encoders":[123]}""")]
    public void FileEncoderCache_RejectsCorruptOrWrongSchema(string contents)
    {
        string path = Path.Combine(_dir, "encoder-cache.json");
        File.WriteAllText(path, contents);
        var cache = new FileEncoderCache(path);

        Assert.Null(cache.TryRead("fp"));
    }

    [Fact]
    public void HardwareFingerprint_TracksBinaryChangesAndIsDeterministic()
    {
        string fakeBin = Path.Combine(_dir, "ffmpeg.exe");
        File.WriteAllText(fakeBin, "v1");

        string fp1 = HardwareFingerprint.Compute(fakeBin);
        string fp2 = HardwareFingerprint.Compute(fakeBin);
        Assert.Equal(fp1, fp2);
        Assert.Equal(64, fp1.Length);

        File.WriteAllText(fakeBin, "v2-with-different-length");
        string fp3 = HardwareFingerprint.Compute(fakeBin);
        Assert.NotEqual(fp1, fp3);

        string missingA = HardwareFingerprint.Compute(Path.Combine(_dir, "missing-a.exe"));
        string missingB = HardwareFingerprint.Compute(Path.Combine(_dir, "missing-b.exe"));
        Assert.NotEqual(missingA, missingB);
    }

    [Fact]
    public async Task Detector_UsesWarmCacheWithoutRunningFfmpeg()
    {
        var cache = new MemoryEncoderCache();
        cache.Write("fp-1", [VideoEncoder.Qsv, VideoEncoder.X264]);
        var runner = new FakeRunner((_, _, _) => throw new InvalidOperationException("must not run ffmpeg"));
        var detector = new FfmpegEncoderDetector(runner, "ffmpeg", cache: cache, fingerprintProvider: () => "fp-1");

        var encoders = await detector.GetAvailableEncodersAsync();

        Assert.Equal([VideoEncoder.Qsv, VideoEncoder.X264], encoders);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Detector_ProbesInParallelPreservingEnumOrderAndWritesCache()
    {
        var cache = new MemoryEncoderCache();
        var nvencGate = new TaskCompletionSource();
        var runner = new FakeRunner(async (args, consumer, _) =>
        {
            if (args.Contains("-encoders"))
            {
                await consumer!(new StringReader(" V....D libx264 x\n V....D h264_nvenc n\n V....D h264_amf a\n"));
                return new ProcessOutcome(0, "");
            }

            string enc = args[args.ToList().IndexOf("-c:v") + 1];
            if (enc == "h264_nvenc")
            {
                await nvencGate.Task;
            }
            else if (enc == "libx264")
            {
                nvencGate.TrySetResult(); // proves x264 started without waiting for nvenc
            }
            return new ProcessOutcome(0, "");
        });
        var detector = new FfmpegEncoderDetector(runner, "ffmpeg", cache: cache, fingerprintProvider: () => "fp-1");

        var encoders = await detector.GetAvailableEncodersAsync();

        Assert.Equal([VideoEncoder.Nvenc, VideoEncoder.Amf, VideoEncoder.X264], encoders);
        Assert.Equal(1, cache.WriteCount);
        Assert.Equal([VideoEncoder.Nvenc, VideoEncoder.Amf, VideoEncoder.X264], cache.TryRead("fp-1"));
    }

    [Fact]
    public async Task Detector_ReportFailureInvalidatesPersistentCache()
    {
        var cache = new MemoryEncoderCache();
        cache.Write("fp-1", [VideoEncoder.Nvenc, VideoEncoder.X264]);
        var runner = new FakeRunner((_, _, _) => Task.FromResult(new ProcessOutcome(0, "")));
        var detector = new FfmpegEncoderDetector(runner, "ffmpeg", cache: cache, fingerprintProvider: () => "fp-1");

        await detector.GetAvailableEncodersAsync();
        detector.ReportFailure(VideoEncoder.Nvenc);

        Assert.Equal(1, cache.InvalidateCount);
        Assert.Null(cache.TryRead("fp-1"));
        Assert.Equal([VideoEncoder.X264], await detector.GetAvailableEncodersAsync());
    }

    [Fact]
    public async Task ClipExportService_WithEncoderCache_UsesCachedEncodersOnPreciseExport()
    {
        string source = Path.Combine(_dir, "src.mp4");
        File.WriteAllText(source, "data");
        string fakeFfmpeg = Path.Combine(_dir, "ffmpeg.exe");
        File.WriteAllText(fakeFfmpeg, "bin");

        var cache = new MemoryEncoderCache();
        cache.Write(HardwareFingerprint.Compute(fakeFfmpeg), [VideoEncoder.Qsv, VideoEncoder.X264]);

        var runner = new FakeRunner((args, _, _) =>
        {
            Assert.DoesNotContain("-encoders", args);
            File.WriteAllText(args[^1], "encoded");
            return Task.FromResult(new ProcessOutcome(0, ""));
        });
        using var service = new ClipExportService(fakeFfmpeg, runner, encoderCache: cache);

        ClipResult result = await service.ExportAsync(
            new ClipRequest(source, 1.0, 4.0, Path.Combine(_dir, "out"), "Clip", ExportMode.Precise));

        Assert.Equal(VideoEncoder.Qsv, result.EncoderUsed);
        Assert.Single(runner.Calls);
    }
}
