namespace Aether.PvP.Review.Clipping;

public interface IHardwareEncoderDetector
{
    // Encoders that listed and passed a test encode, in fallback order (NVENC, QSV, AMF, x264).
    Task<IReadOnlyList<VideoEncoder>> GetAvailableEncodersAsync(CancellationToken ct = default);

    // Drops an encoder that failed during a real export so later exports skip it.
    void ReportFailure(VideoEncoder encoder);
}

public sealed class FfmpegEncoderDetector : IHardwareEncoderDetector
{
    private readonly IProcessRunner _runner;
    private readonly string _ffmpegPath;
    private readonly TimeSpan? _probeTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<VideoEncoder> _failed = [];
    private List<VideoEncoder>? _detected;
    private bool _probedBefore;

    public FfmpegEncoderDetector(IProcessRunner runner, string ffmpegPath, TimeSpan? probeTimeout = null)
    {
        _runner = runner;
        _ffmpegPath = ffmpegPath;
        _probeTimeout = probeTimeout;
    }

    public async Task<IReadOnlyList<VideoEncoder>> GetAvailableEncodersAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var detected = _detected;
            if (detected is null)
            {
                var (found, cacheable) = await DetectAsync(ct).ConfigureAwait(false);
                // A timeout or an empty list is more likely a slow cold start than the final answer.
                if (cacheable) _detected = found;
                detected = found;
            }
            lock (_failed) return detected.Where(e => !_failed.Contains(e)).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void ReportFailure(VideoEncoder encoder)
    {
        lock (_failed) _failed.Add(encoder);
    }

    private async Task<(List<VideoEncoder> Encoders, bool Cacheable)> DetectAsync(CancellationToken ct)
    {
        // The first probe may be the first time the driver or ffmpeg is loaded, which can be slow.
        var timeout = _probeTimeout ?? (_probedBefore ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(30));
        _probedBefore = true;

        var listed = new HashSet<string>(StringComparer.Ordinal);
        var outcome = await _runner.RunAsync(
            _ffmpegPath,
            ["-hide_banner", "-encoders"],
            async reader =>
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                {
                    // " V....D h264_nvenc    NVIDIA NVENC H.264 encoder"
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) listed.Add(parts[1]);
                }
            },
            ct).ConfigureAwait(false);

        var result = new List<VideoEncoder>();
        if (outcome.ExitCode != 0) return (result, false);

        bool timedOut = false;
        foreach (var encoder in Enum.GetValues<VideoEncoder>())
        {
            // A driver can list an encoder it can't open, so each one must encode a frame to count.
            if (!listed.Contains(encoder.FfmpegName())) continue;
            var probe = await CanEncodeAsync(encoder, timeout, ct).ConfigureAwait(false);
            if (probe is null) timedOut = true;
            else if (probe.Value) result.Add(encoder);
        }
        return (result, !timedOut && result.Count > 0);
    }

    // null means the probe timed out, which says nothing about whether the encoder works.
    private async Task<bool?> CanEncodeAsync(VideoEncoder encoder, TimeSpan probeTimeout, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(probeTimeout);
        try
        {
            var outcome = await _runner.RunAsync(
                _ffmpegPath,
                [
                    "-hide_banner", "-nostdin", "-loglevel", "error",
                    "-f", "lavfi", "-i", "color=black:s=320x240:d=0.2:r=10",
                    "-frames:v", "2", "-c:v", encoder.FfmpegName(), "-f", "null", "-"
                ],
                null,
                timeout.Token).ConfigureAwait(false);
            return outcome.ExitCode == 0;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
