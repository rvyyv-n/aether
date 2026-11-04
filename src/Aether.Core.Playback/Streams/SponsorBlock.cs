using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aether.Core.Playback.Streams;

/// <summary>The SponsorBlock categories Aether skips (16.1).</summary>
public enum SponsorCategory
{
    Sponsor,
    SelfPromo,
    Interaction,
    Intro,
    Outro,
}

/// <summary>A part of a video to skip, in seconds.</summary>
public sealed record SponsorSegment(double Start, double End, SponsorCategory Category);

/// <summary>
/// The SponsorBlock client (16.1): for a YouTube link, the community's skip segments. Off unless the user turns it on.
/// It asks by the first four hex digits of the video id's SHA-256, so the service learns a bucket of many videos and
/// never which one is playing; this video's segments are picked out of the reply here.
/// </summary>
public static partial class SponsorBlock
{
    private const string Endpoint = "https://sponsor.ajay.app/api/skipSegments/";

    private static readonly string[] YouTubeHosts = ["youtube.com", "m.youtube.com", "music.youtube.com", "youtube-nocookie.com"];

    /// <summary>The 11-character video id of a YouTube watch, short, embed, live or youtu.be link; null for anything else.</summary>
    public static string? VideoId(string? link)
    {
        if (!WebLink.IsValid(link) || !Uri.TryCreate(link!.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return null;
        }
        string host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        string? id = null;
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            id = uri.AbsolutePath.Trim('/');
        }
        else if (YouTubeHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            string[] path = uri.AbsolutePath.Trim('/').Split('/');
            id = path is ["watch"]
                ? Query(uri, "v")
                : path is ["shorts" or "embed" or "live", { } rest] ? rest : null;
        }
        return id is not null && Id().IsMatch(id) ? id : null;
    }

    public static string HashPrefix(string videoId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(videoId)))[..4];

    public static Uri RequestUri(string videoId, IEnumerable<SponsorCategory> categories)
    {
        string list = "[" + string.Join(',', categories.Select(c => $"\"{Key(c)}\"")) + "]";
        return new Uri($"{Endpoint}{HashPrefix(videoId)}?categories={Uri.EscapeDataString(list)}&actionType=skip");
    }

    /// <summary>The segments for <paramref name="videoId"/>; none when the service has none, cannot be reached or errs.</summary>
    public static async Task<IReadOnlyList<SponsorSegment>> FetchAsync(
        HttpClient http, string videoId, IReadOnlyList<SponsorCategory> categories, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (categories.Count == 0)
        {
            return [];
        }
        try
        {
            using HttpResponseMessage response = await http.GetAsync(RequestUri(videoId, categories), ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), videoId)
                : [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>This video's skip segments in a reply to the prefix request, in time order; unreadable parts are left out.</summary>
    public static IReadOnlyList<SponsorSegment> Parse(string json, string videoId)
    {
        var segments = new List<SponsorSegment>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            foreach (JsonElement video in document.RootElement.EnumerateArray())
            {
                if (video.ValueKind != JsonValueKind.Object
                    || !video.TryGetProperty("videoID", out JsonElement id) || id.GetString() != videoId
                    || !video.TryGetProperty("segments", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (JsonElement entry in list.EnumerateArray())
                {
                    if (Read(entry) is { } segment)
                    {
                        segments.Add(segment);
                    }
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }
        return [.. segments.OrderBy(s => s.Start)];
    }

    public static IReadOnlyList<SponsorCategory> ParseCategories(string setting) =>
        [.. setting.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(FromKey).OfType<SponsorCategory>().Distinct()];

    public static string FormatCategories(IEnumerable<SponsorCategory> categories) => string.Join(',', categories.Select(Key));

    /// <summary>The name SponsorBlock gives a category.</summary>
    public static string Key(SponsorCategory category) => category switch
    {
        SponsorCategory.Sponsor => "sponsor",
        SponsorCategory.SelfPromo => "selfpromo",
        SponsorCategory.Interaction => "interaction",
        SponsorCategory.Intro => "intro",
        SponsorCategory.Outro => "outro",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    private static SponsorCategory? FromKey(string key) =>
        Enum.GetValues<SponsorCategory>().Cast<SponsorCategory?>().FirstOrDefault(c => Key(c!.Value) == key);

    private static SponsorSegment? Read(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || (entry.TryGetProperty("actionType", out JsonElement action) && action.GetString() != "skip")
            || !entry.TryGetProperty("category", out JsonElement name) || name.ValueKind != JsonValueKind.String
            || FromKey(name.GetString()!) is not { } category
            || !entry.TryGetProperty("segment", out JsonElement times) || times.ValueKind != JsonValueKind.Array || times.GetArrayLength() != 2
            || times[0].ValueKind != JsonValueKind.Number || times[1].ValueKind != JsonValueKind.Number)
        {
            return null;
        }
        double start = times[0].GetDouble();
        double end = times[1].GetDouble();
        return end > start && start >= 0 ? new SponsorSegment(start, end, category) : null;
    }

    private static string? Query(Uri uri, string name)
    {
        foreach (string pair in uri.Query.TrimStart('?').Split('&'))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0 && pair[..equals] == name)
            {
                return Uri.UnescapeDataString(pair[(equals + 1)..]);
            }
        }
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex Id();
}
