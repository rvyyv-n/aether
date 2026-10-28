using System.Globalization;

namespace Aether.Core.Playback.Tracks;

/// <summary>What a picked track looks like, which survives into another file where its id does not.</summary>
public readonly record struct TrackLook(string? Language, string? Title, bool Forced);

/// <summary>
/// The audio and subtitle the user picked, carried to the next file of the queue (13.4). Matched by language, then
/// title and forced flag, since another file numbers its tracks its own way; a language it lacks leaves its default.
/// A loaded subtitle file belongs to its video and is not carried.
/// </summary>
public sealed record TrackPreference(TrackLook? Audio, TrackLook? Subtitle, bool SubtitlesOff)
{
    public static TrackPreference From(TrackList tracks)
    {
        MediaTrack? audio = tracks.Audio.FirstOrDefault(t => t.Selected);
        MediaTrack? subtitle = tracks.SelectedSubtitle;
        return new TrackPreference(
            Look(audio),
            subtitle is { ExternalFile: null } ? Look(subtitle) : null,
            SubtitlesOff: subtitle is null);
    }

    /// <summary>The mpv commands that pick the matching tracks in <paramref name="next"/>; none for what already matches.</summary>
    public IReadOnlyList<string[]> Select(TrackList next)
    {
        var commands = new List<string[]>();
        if (Audio is { } audio && Best(next.Audio, audio) is { Selected: false } a)
        {
            commands.Add(["set", "aid", a.Id.ToString(CultureInfo.InvariantCulture)]);
        }
        if (SubtitlesOff)
        {
            if (next.SelectedSubtitle is not null)
            {
                commands.Add(["set", "sid", "no"]);
            }
        }
        else if (Subtitle is { } subtitle && Best(next.Subtitles.Where(t => t.ExternalFile is null), subtitle) is { Selected: false } s)
        {
            commands.Add(["set", "sid", s.Id.ToString(CultureInfo.InvariantCulture)]);
        }
        return commands;
    }

    private static TrackLook? Look(MediaTrack? track) => track is null ? null : new TrackLook(track.Language, track.Title, track.Forced);

    // Same language first (or same title for untagged tracks); among those, a matching title, then forced flag.
    private static MediaTrack? Best(IEnumerable<MediaTrack> candidates, TrackLook wanted)
    {
        MediaTrack? best = null;
        int bestScore = 0;
        foreach (MediaTrack track in candidates)
        {
            bool sameTitle = Same(track.Title, wanted.Title);
            if (wanted.Language is null ? !sameTitle : !Same(track.Language, wanted.Language))
            {
                continue;
            }
            int score = 1 + (sameTitle ? 2 : 0) + (track.Forced == wanted.Forced ? 1 : 0);
            if (score > bestScore)
            {
                (best, bestScore) = (track, score);
            }
        }
        return best;
    }

    private static bool Same(string? a, string? b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
