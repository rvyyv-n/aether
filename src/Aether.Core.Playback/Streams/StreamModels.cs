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
    TimeSpan? Timeout = null,
    string? FormatSelectorOverride = null);

public sealed record StreamChapter(double Start, double End, string Title);

public sealed record StreamFormat(
    string FormatId,
    int? Height,
    int? Width,
    double? Fps,
    string? VideoCodec,
    string? AudioCodec,
    string? Ext,
    double? Bitrate,
    string Url,
    IReadOnlyDictionary<string, string>? HttpHeaders);

public sealed record StreamSubtitle(string Language, string? Name, string Url, string Ext, bool IsAutomatic);

/// <summary>
/// The list members (Chapters, Formats, Subtitles) are null when yt-dlp's JSON lacks the key
/// and empty when the key is present but holds nothing usable.
/// </summary>
public sealed record ResolvedStream(
    string StreamUrl,
    string Title,
    double? DurationSeconds = null,
    string? WebpageUrl = null,
    IReadOnlyDictionary<string, string>? HttpHeaders = null,
    string? AudioUrl = null,
    IReadOnlyList<StreamChapter>? Chapters = null,
    IReadOnlyList<StreamFormat>? Formats = null,
    IReadOnlyList<StreamSubtitle>? Subtitles = null,
    string? VideoFormatId = null,
    string? AudioFormatId = null);

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
