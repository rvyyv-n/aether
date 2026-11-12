using System.Globalization;

namespace Aether.Core.Playback.Audio;

/// <summary>One audio track's place in the mix: mpv's track id, its level in percent and whether it is muted.</summary>
public sealed record MixLevel(int TrackId, int Percent, bool Muted)
{
    public static int Clamp(int percent) => Math.Clamp(percent, 0, TrackMix.MaxPercent);
}

/// <summary>
/// The multi-track mixer (14.4): plays every audio track of a recording at once (game, mic, chat and music from OBS or
/// ShadowPlay), each at its own level, as an mpv <c>lavfi-complex</c> graph. <c>af-command</c> cannot reach that graph,
/// so a change sends the whole graph again. A muted track stays in the graph at zero, which keeps the track count, and
/// with it the graph's shape, the same.
/// </summary>
public static class TrackMix
{
    public const int MaxPercent = 200;

    /// <summary>
    /// What starts or changes the mix (<paramref name="graph"/>), or with null ends it, after which mpv plays
    /// <paramref name="restoreTrack"/> again, or its own pick when that is null: the mix leaves it on no single track.
    /// </summary>
    public static IReadOnlyList<string[]> Commands(string? graph, bool mixing, int? restoreTrack)
    {
        if (graph is not null)
        {
            return [["set", "lavfi-complex", graph]];
        }
        return mixing
            ? [["set", "lavfi-complex", ""], ["set", "aid", restoreTrack?.ToString(CultureInfo.InvariantCulture) ?? "auto"]]
            : [];
    }

    public static IReadOnlyList<MixLevel> Start(IEnumerable<int> trackIds) => trackIds.Select(id => new MixLevel(id, 100, false)).ToArray();

    /// <summary>The <c>lavfi-complex</c> value for <paramref name="levels"/>; the mix ends in <c>[ao]</c>, the audio output.</summary>
    public static string Graph(IReadOnlyList<MixLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count < 2)
        {
            throw new ArgumentException("a mix needs at least two tracks", nameof(levels));
        }
        IEnumerable<string> inputs = levels.Select(level =>
        {
            string id = level.TrackId.ToString(CultureInfo.InvariantCulture);
            double gain = level.Muted ? 0 : MixLevel.Clamp(level.Percent) / 100.0;
            return $"[aid{id}]volume@m{id}=volume={gain.ToString("0.##", CultureInfo.InvariantCulture)}[a{id}]";
        });
        string mixed = string.Concat(levels.Select(level => $"[a{level.TrackId.ToString(CultureInfo.InvariantCulture)}]"));
        return $"{string.Join(';', inputs)};{mixed}amix=inputs={levels.Count.ToString(CultureInfo.InvariantCulture)}:normalize=0[ao]";
    }
}
