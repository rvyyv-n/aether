namespace Aether.Core.Playback.Streams;

public enum StreamFailureReason
{
    None,
    Unknown,
    YtDlpMissing,
    PrivateVideo,
    Offline,
    SignInRequired,
    RegionBlocked,
    LiveNotStarted,
    SiteNotSupported,
}

public sealed record StreamResolutionOptions(
    string? YtDlpPath = null,
    string StreamingQuality = "1080p",
    bool PreferGpuDecodableFormats = true,
    string? HttpProxy = null,
    TimeSpan? Timeout = null);

public sealed record ResolvedStream(
    string StreamUrl,
    string Title,
    double? DurationSeconds = null,
    string? WebpageUrl = null,
    IReadOnlyDictionary<string, string>? HttpHeaders = null);

public sealed class StreamResolutionException : Exception
{
    public StreamFailureReason Reason { get; }
    public string? RawOutput { get; }

    public StreamResolutionException(StreamFailureReason reason, string message, string? rawOutput = null, Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
        RawOutput = rawOutput;
    }
}

public interface IYtDlpStreamResolver
{
    Task<ResolvedStream> ResolveAsync(string url, StreamResolutionOptions options, CancellationToken ct = default);
}
