using System.Text.Json;

namespace Aether.Core.Playback.Tracks;

/// <summary>The audio and subtitle tracks of the open file, parsed from the JSON of mpv's <c>track-list</c> property.</summary>
public sealed class TrackList
{
    public static TrackList Empty { get; } = new([], []);

    public TrackList(IReadOnlyList<MediaTrack> audio, IReadOnlyList<MediaTrack> subtitles)
    {
        Audio = audio;
        Subtitles = subtitles;
    }

    public IReadOnlyList<MediaTrack> Audio { get; }

    public IReadOnlyList<MediaTrack> Subtitles { get; }

    /// <summary>The selected subtitle, or null when subtitles are off.</summary>
    public MediaTrack? SelectedSubtitle => Subtitles.FirstOrDefault(t => t.Selected);

    /// <summary>Video tracks are ignored. Anything unreadable gives an empty list rather than an error.</summary>
    public static TrackList Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Empty;
            }
            var audio = new List<MediaTrack>();
            var subtitles = new List<MediaTrack>();
            foreach (JsonElement entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || Text(entry, "type") is not { } type)
                {
                    continue;
                }
                TrackKind? kind = type switch
                {
                    "audio" => TrackKind.Audio,
                    "sub" => TrackKind.Subtitle,
                    _ => null,
                };
                if (kind is { } k && Read(entry, k) is { } track)
                {
                    (k == TrackKind.Audio ? audio : subtitles).Add(track);
                }
            }
            return new TrackList(audio, subtitles);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static MediaTrack? Read(JsonElement entry, TrackKind kind)
    {
        if (!entry.TryGetProperty("id", out JsonElement id) || !id.TryGetInt32(out int trackId))
        {
            return null;
        }
        int channels = entry.TryGetProperty("demux-channel-count", out JsonElement count) && count.TryGetInt32(out int n) ? n : 0;
        return new MediaTrack(
            trackId,
            kind,
            Text(entry, "lang"),
            Text(entry, "title"),
            Text(entry, "demux-channels"),
            channels,
            Flag(entry, "hearing-impaired"),
            Flag(entry, "forced"),
            Flag(entry, "selected"),
            Flag(entry, "external") ? Text(entry, "external-filename") : null);
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static bool Flag(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
