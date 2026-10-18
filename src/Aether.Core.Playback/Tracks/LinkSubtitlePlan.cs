using Aether.Core.Playback.Streams;

namespace Aether.Core.Playback.Tracks;

/// <summary>One subtitle track of a link to hand to mpv's <c>sub-add</c>. <see cref="Select"/> is true for the one that matches the preferred language.</summary>
public sealed record LinkSubtitleTrack(string Url, string Title, string Language, bool Select);

/// <summary>
/// Which of a link's subtitles to add to the player, and which one to turn on. YouTube lists an automatic caption for
/// every language it can translate to, so automatic ones are only added for the preferred language; written ones are
/// all added. Nothing is selected when no track matches, so subtitles are never turned on unasked.
/// </summary>
public static class LinkSubtitlePlan
{
    public const string AutomaticSuffix = " (auto)";

    public static IReadOnlyList<LinkSubtitleTrack> Create(IReadOnlyList<StreamSubtitle>? subtitles, string? preferredLanguages)
    {
        if (subtitles is not { Count: > 0 })
        {
            return [];
        }
        string[] preferred = (preferredLanguages ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<StreamSubtitle> kept = [.. subtitles.Where(s => !string.IsNullOrWhiteSpace(s.Url) && (!s.IsAutomatic || Matches(s.Language, preferred)))];
        StreamSubtitle? selected = kept.FirstOrDefault(s => !s.IsAutomatic && Matches(s.Language, preferred))
            ?? kept.FirstOrDefault(s => s.IsAutomatic && Matches(s.Language, preferred));

        return [.. kept.Select(s => new LinkSubtitleTrack(s.Url, Title(s), s.Language, ReferenceEquals(s, selected)))];
    }

    /// <summary>True when <paramref name="language"/> is a preferred one or a regional form of it ("en-US" for "en").</summary>
    public static bool Matches(string language, IReadOnlyList<string> preferred) =>
        preferred.Any(p => language.Equals(p, StringComparison.OrdinalIgnoreCase)
            || language.StartsWith(p + "-", StringComparison.OrdinalIgnoreCase));

    private static string Title(StreamSubtitle subtitle)
    {
        string name = string.IsNullOrWhiteSpace(subtitle.Name) ? subtitle.Language : subtitle.Name.Trim();
        return subtitle.IsAutomatic ? name + AutomaticSuffix : name;
    }
}
