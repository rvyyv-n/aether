using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Aether.PvP.Review.Clipping;

public sealed record ClipResult(string OutputPath, long SizeBytes, TimeSpan Elapsed, VideoEncoder? EncoderUsed = null);

public sealed class ClipExportService : IDisposable
{
    private static readonly TimeSpan PreWarmTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _lifetime = new();
    private volatile string _ffmpegPath;
    private volatile bool _isFfmpegAvailable;
    private readonly IProcessRunner _runner;
    private IHardwareEncoderDetector? _detector;
    private string? _detectorPath;

    public ClipExportService(
        string ffmpegPath = "ffmpeg.exe",
        IProcessRunner? runner = null,
        IHardwareEncoderDetector? detector = null)
    {
        _ffmpegPath = ffmpegPath;
        _runner = runner ?? new SystemProcessRunner();
        _detector = detector;
    }

    public bool IsFfmpegAvailable => _isFfmpegAvailable;

    // Running the binary once pulls it into the OS file cache so the first real export doesn't
    // pay for a cold disk read or the first-run security scan.
    public async Task PreWarmAsync(string ffmpegPath = "ffmpeg.exe", CancellationToken ct = default)
    {
        _ffmpegPath = ffmpegPath;

        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-version");

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            process = null;
        }

        if (process is null)
        {
            _isFfmpegAvailable = false;
            return;
        }

        using (process)
        {
            Task output = process.StandardOutput.ReadToEndAsync();
            Task errors = process.StandardError.ReadToEndAsync();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            linked.CancelAfter(PreWarmTimeout);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                await Task.WhenAll(output, errors).ConfigureAwait(false);
                _isFfmpegAvailable = process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                _isFfmpegAvailable = false;
                if (ct.IsCancellationRequested) throw;
            }
        }
    }

    // progress is only reported in Precise mode; a stream copy has nothing worth showing.
    public async Task<ClipResult> ExportAsync(
        ClipRequest request, CancellationToken ct = default, IProgress<ClipProgress>? progress = null)
    {
        Validate(request);
        Directory.CreateDirectory(request.DestinationDirectory);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var token = linked.Token;

        var stopwatch = Stopwatch.StartNew();
        if (request.Mode == ExportMode.Precise)
            return await ExportPreciseAsync(request, progress, stopwatch, token).ConfigureAwait(false);

        string outputPath = ReserveOutputPath(request);
        string tempPath = outputPath + ".part";

        var startInfo = new ProcessStartInfo(_ffmpegPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in FfmpegArguments.Build(request, tempPath))
            startInfo.ArgumentList.Add(a);

        bool committed = false;
        try
        {
            Process process;
            try
            {
                process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            }
            catch (Win32Exception ex)
            {
                _isFfmpegAvailable = false;
                throw new FileNotFoundException($"FFmpeg could not be started from '{_ffmpegPath}'.", _ffmpegPath, ex);
            }

            using (process)
            {
                Task drainOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();

                try
                {
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Kill(process);
                    // The .part can't be deleted while ffmpeg still holds it open.
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                await drainOutput.ConfigureAwait(false);
                string errors = await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"FFmpeg stream copy failed with exit code {process.ExitCode}: {errors}");
            }

            if (!File.Exists(tempPath))
                throw new InvalidOperationException("FFmpeg finished without writing an output file.");

            // The final name was chosen before ffmpeg ran, so a clip made in the same second
            // gets its own name and the rename never overwrites an earlier export.
            File.Move(tempPath, outputPath, overwrite: false);
            committed = true;

            stopwatch.Stop();
            return new ClipResult(outputPath, new FileInfo(outputPath).Length, stopwatch.Elapsed);
        }
        finally
        {
            if (!committed) TryDelete(tempPath);
        }
    }

    private async Task<ClipResult> ExportPreciseAsync(
        ClipRequest request, IProgress<ClipProgress>? progress, Stopwatch stopwatch, CancellationToken token)
    {
        double duration = request.EndTimeSeconds - request.StartTimeSeconds;

        long? videoBitrate = null;
        if (request.TargetSizeBytes is { } target)
        {
            var plan = BitrateCalculator.Plan(target, duration);
            if (!plan.IsFeasible) throw new ClipTargetTooSmallException(plan.MinimumSizeBytes);
            videoBitrate = plan.VideoBitsPerSecond;
        }

        var detector = GetDetector();
        var ladder = Order(await detector.GetAvailableEncodersAsync(token).ConfigureAwait(false), request.PreferredEncoder);
        if (ladder.Count == 0)
            throw new InvalidOperationException("No H.264 encoder is available in this copy of FFmpeg.");

        string outputPath = ReserveOutputPath(request, PreciseExtension(request.SourceFilePath));
        string tempPath = outputPath + ".part";

        bool committed = false;
        try
        {
            string lastError = "";
            foreach (var encoder in ladder)
            {
                var parser = new FfmpegProgressParser(duration);
                var outcome = await _runner.RunAsync(
                    _ffmpegPath,
                    ReEncodeArgumentsBuilder.Build(request, tempPath, encoder, videoBitrate),
                    reader => parser.ReadAsync(reader, progress, token),
                    token).ConfigureAwait(false);

                if (outcome.ExitCode == 0 && File.Exists(tempPath))
                {
                    // The final name was chosen before ffmpeg ran, so the rename never overwrites an earlier export.
                    File.Move(tempPath, outputPath, overwrite: false);
                    committed = true;
                    stopwatch.Stop();
                    return new ClipResult(outputPath, new FileInfo(outputPath).Length, stopwatch.Elapsed, encoder);
                }

                // Drivers can pass the test encode and still fail on a real clip: skip to the next encoder.
                lastError = $"{encoder.FfmpegName()} exited with code {outcome.ExitCode}: {outcome.StandardError}";
                detector.ReportFailure(encoder);
                TryDelete(tempPath);
            }

            throw new InvalidOperationException($"FFmpeg re-encode failed with every available encoder. Last error: {lastError}");
        }
        finally
        {
            if (!committed) TryDelete(tempPath);
        }
    }

    private IHardwareEncoderDetector GetDetector()
    {
        // The path can change in PreWarmAsync, and a detector caches results for one binary.
        if (_detector is null || (_detectorPath is not null && _detectorPath != _ffmpegPath))
        {
            _detector = new FfmpegEncoderDetector(_runner, _ffmpegPath);
            _detectorPath = _ffmpegPath;
        }
        return _detector;
    }

    // The preferred encoder goes first when it is usable; the rest keep the ladder order as fallbacks.
    public static List<VideoEncoder> Order(IReadOnlyList<VideoEncoder> available, VideoEncoder? preferred) =>
        preferred is { } p && available.Contains(p)
            ? [p, .. available.Where(e => e != p)]
            : [.. available];

    // H.264 and AAC go in MP4 or MKV; a WebM or MOV source can't carry them as it is.
    internal static string PreciseExtension(string sourcePath) =>
        Path.GetExtension(sourcePath).Equals(".mkv", StringComparison.OrdinalIgnoreCase) ? ".mkv" : ".mp4";

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    internal static void Validate(ClipRequest request)
    {
        if (!File.Exists(request.SourceFilePath))
            throw new FileNotFoundException("Source video file not found", request.SourceFilePath);

        if (request.StartTimeSeconds < 0)
            throw new ArgumentException("Start timestamp must not be negative.");

        if (request.EndTimeSeconds <= request.StartTimeSeconds)
            throw new ArgumentException("End timestamp must be strictly greater than start timestamp.");
    }

    internal static string ReserveOutputPath(ClipRequest request, string? extension = null)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        extension ??= Path.GetExtension(request.SourceFilePath);
        string baseName = $"{request.ClipNamePrefix}_{timestamp}";

        string candidate = Path.Combine(request.DestinationDirectory, baseName + extension);
        for (int i = 1; File.Exists(candidate) || File.Exists(candidate + ".part"); i++)
            candidate = Path.Combine(request.DestinationDirectory, $"{baseName}_{i}{extension}");
        return candidate;
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
