using System.Diagnostics;
using System.Text.Json;

namespace Aether.Core.Playback.Streams;

public sealed class YtDlpStreamResolver : IYtDlpStreamResolver
{
    private readonly IProcessRunner _processRunner;

    public YtDlpStreamResolver(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new DefaultProcessRunner();
    }

    public async Task<ResolvedStream> ResolveAsync(string url, StreamResolutionOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(options);

        string exePath = options.YtDlpPath ?? "yt-dlp";

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = YtDlpArgumentsBuilder.BuildArguments(url, options),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (options.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            cts.CancelAfter(timeout);
        }

        ProcessRunResult result;
        try
        {
            result = await _processRunner.RunAsync(startInfo, cts.Token).ConfigureAwait(false);
        }
        catch (StreamResolutionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new StreamResolutionException(StreamFailureReason.Offline, "yt-dlp stream resolution timed out.");
        }

        if (result.ExitCode != 0)
        {
            StreamFailureReason failure = YtDlpErrorClassifier.Classify(result.StandardError, result.ExitCode);
            string msg = string.Concat("yt-dlp failed with exit code ", result.ExitCode.ToString(), ": ", result.StandardError);
            throw new StreamResolutionException(failure, msg, result.StandardError);
        }

        return ParseStreamJson(result.StandardOutput, url);
    }

    public static ResolvedStream ParseStreamJson(string jsonOutput, string fallbackUrl)
    {
        if (string.IsNullOrWhiteSpace(jsonOutput))
        {
            throw new StreamResolutionException(StreamFailureReason.Unknown, "yt-dlp returned empty output.");
        }

        try
        {
            string json = ExtractJsonLine(jsonOutput);
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string? streamUrl = null;
            if (root.TryGetProperty("url", out JsonElement urlElement) && urlElement.ValueKind == JsonValueKind.String)
            {
                streamUrl = urlElement.GetString();
            }

            if (string.IsNullOrEmpty(streamUrl) && root.TryGetProperty("requested_formats", out JsonElement formats) && formats.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement fmt in formats.EnumerateArray())
                {
                    if (fmt.TryGetProperty("url", out JsonElement fmtUrl) && fmtUrl.ValueKind == JsonValueKind.String)
                    {
                        streamUrl = fmtUrl.GetString();
                        break;
                    }
                }
            }

            string title = root.TryGetProperty("title", out JsonElement titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString() ?? string.Empty
                : string.Empty;

            double? duration = null;
            if (root.TryGetProperty("duration", out JsonElement durElement) && durElement.TryGetDouble(out double d))
            {
                duration = d;
            }

            string? webpageUrl = root.TryGetProperty("webpage_url", out JsonElement wpElement) && wpElement.ValueKind == JsonValueKind.String
                ? wpElement.GetString()
                : fallbackUrl;

            Dictionary<string, string>? headers = null;
            if (root.TryGetProperty("http_headers", out JsonElement headersElement) && headersElement.ValueKind == JsonValueKind.Object)
            {
                headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty prop in headersElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        headers[prop.Name] = prop.Value.GetString()!;
                    }
                }
            }

            return new ResolvedStream(
                StreamUrl: streamUrl ?? fallbackUrl,
                Title: title,
                DurationSeconds: duration,
                WebpageUrl: webpageUrl ?? fallbackUrl,
                HttpHeaders: headers);
        }
        catch (JsonException ex)
        {
            throw new StreamResolutionException(StreamFailureReason.Unknown, "Failed to parse yt-dlp JSON output.", jsonOutput, ex);
        }
    }

    private static string ExtractJsonLine(string output)
    {
        string[] lines = output.Split('\n');
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == (char)123 && trimmed[^1] == (char)125)
            {
                return trimmed;
            }
        }
        return output.Trim();
    }
}
