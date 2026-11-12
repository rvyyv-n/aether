namespace Aether.Core.Playback.Tracks;

/// <summary>
/// What the user picked in the tracks flyout for the open file, so an engine restart can put it back: mpv starts the new
/// engine on its default tracks. Ids alone are not enough for subtitle files, which get their ids in load order, so a
/// chosen file is found again by name.
/// </summary>
/// <param name="AudioId">The audio track in use, or null when the file has none.</param>
/// <param name="SubtitleId">The embedded subtitle track in use; null with no <paramref name="SubtitleFile"/> means Off.</param>
/// <param name="SubtitleFile">The subtitle file in use, or null.</param>
/// <param name="ExternalFiles">Every subtitle file that was loaded at the time.</param>
/// <param name="SecondaryId">The embedded second subtitle (15.2); null with no <paramref name="SecondaryFile"/> means none.</param>
/// <param name="SecondaryFile">The second subtitle when it is a file, or null.</param>
public sealed record TrackChoices(
    int? AudioId,
    int? SubtitleId,
    string? SubtitleFile,
    double AudioDelay,
    double SubtitleDelay,
    IReadOnlyList<string> ExternalFiles,
    int? SecondaryId = null,
    string? SecondaryFile = null)
{
    private const double DelayTolerance = 0.001;

    public static TrackChoices From(TrackSnapshot snapshot)
    {
        MediaTrack? audio = snapshot.Tracks.Audio.FirstOrDefault(t => t.Selected);
        MediaTrack? subtitle = snapshot.Tracks.SelectedSubtitle;
        MediaTrack? secondary = snapshot.Tracks.SecondarySubtitle;
        return new TrackChoices(
            audio?.Id,
            subtitle is { ExternalFile: null } ? subtitle.Id : null,
            subtitle?.ExternalFile,
            snapshot.AudioDelay,
            snapshot.SubtitleDelay,
            [.. snapshot.Tracks.Subtitles.Select(t => t.ExternalFile).OfType<string>()],
            secondary is { ExternalFile: null } ? secondary.Id : null,
            secondary?.ExternalFile);
    }

    /// <summary>The subtitle files to load again: those that are not on <paramref name="current"/> already (mpv loads matching files itself).</summary>
    public IReadOnlyList<string> MissingFiles(TrackList current) =>
        [.. ExternalFiles.Where(file => !current.Subtitles.Any(t => t.ExternalFile is { } loaded && SameFile(loaded, file)))];

    /// <summary>The mpv commands that bring <paramref name="current"/> to these choices; none for what already matches.</summary>
    public IReadOnlyList<string[]> Select(TrackSnapshot current)
    {
        var commands = new List<string[]>();

        if (AudioId is { } audioId && current.Tracks.Audio.FirstOrDefault(t => t.Id == audioId) is { Selected: false })
        {
            commands.Add(["set", "aid", audioId.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        MediaTrack? wanted = SubtitleFile is { } file
            ? current.Tracks.Subtitles.FirstOrDefault(t => t.ExternalFile is { } loaded && SameFile(loaded, file))
            : SubtitleId is { } id
                ? current.Tracks.Subtitles.FirstOrDefault(t => t.Id == id && t.ExternalFile is null)
                : null;
        if (wanted is { Selected: false })
        {
            commands.Add(["set", "sid", wanted.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }
        else if (wanted is null && SubtitleFile is null && SubtitleId is null && current.Tracks.SelectedSubtitle is not null)
        {
            commands.Add(["set", "sid", "no"]);
        }

        MediaTrack? second = SecondaryFile is { } secondFile
            ? current.Tracks.Subtitles.FirstOrDefault(t => t.ExternalFile is { } loaded && SameFile(loaded, secondFile))
            : SecondaryId is { } secondId
                ? current.Tracks.Subtitles.FirstOrDefault(t => t.Id == secondId && t.ExternalFile is null)
                : null;
        if (second is { Secondary: false })
        {
            commands.Add(["set", "secondary-sid", second.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }
        else if (second is null && SecondaryFile is null && SecondaryId is null && current.Tracks.SecondarySubtitle is not null)
        {
            commands.Add(["set", "secondary-sid", "no"]);
        }

        if (Math.Abs(AudioDelay - current.AudioDelay) > DelayTolerance)
        {
            commands.Add(["set", "audio-delay", TrackDelay.ForMpv(AudioDelay)]);
        }
        if (Math.Abs(SubtitleDelay - current.SubtitleDelay) > DelayTolerance)
        {
            commands.Add(["set", "sub-delay", TrackDelay.ForMpv(SubtitleDelay)]);
        }
        return commands;
    }

    // mpv reports the path it was given, which can be spelled with forward slashes.
    private static bool SameFile(string a, string b) =>
        string.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
}
