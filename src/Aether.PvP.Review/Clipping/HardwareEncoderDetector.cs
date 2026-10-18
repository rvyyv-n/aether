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
    private readonly TimeSpan _probeTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<VideoEncoder> _failed = [];
    private List<VideoEncoder>? _detected;

    public FfmpegEncoderDetector(IProcessRunner runner, string ffmpegPath, TimeSpan? probeTimeout = null)
    {
        _runner = runner;
        _ffmpegPath = ffmpegPath;
        _probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<IReadOnlyList<VideoEncoder>> GetAvailableEncodersAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _detected ??= await DetectAsync(ct).ConfigureAwait(false);
            lock (_failed) return _detected.Where(e => !_failed.Contains(e)).ToList();
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

    private async Task<List<VideoEncoder>> DetectAsync(CancellationToken ct)
    {
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
        if (outcome.ExitCode != 0) return result;

        foreach (var encoder in Enum.GetValues<VideoEncoder>())
        {
            // A driver can list an encoder it can't open, so each one must encode a frame to count.
            if (listed.Contains(encoder.FfmpegName()) && await CanEncodeAsync(encoder, ct).ConfigureAwait(false))
                result.Add(encoder);
        }
        return result;
    }

    private async Task<bool> CanEncodeAsync(VideoEncoder encoder, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_probeTimeout);
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
            return false;
        }
    }
}
