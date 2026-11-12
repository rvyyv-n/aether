using System.Globalization;
using System.Text.RegularExpressions;

namespace Aether.Core.Playback.Tracks;

/// <summary>A subtitle file found beside a video, with what its name says about it.</summary>
/// <param name="Score">3 for the video's own name, 2 for the same episode code, 1 for the only video in the folder.</param>
public sealed record SubtitleFile(string Path, string? Language, bool Forced, bool HearingImpaired, int Score);

/// <summary>The language and flags written at the end of a subtitle file's name (<c>Film.en.forced.srt</c>).</summary>
public readonly record struct SubtitleTags(string Stem, string? Language, bool Forced, bool HearingImpaired);

/// <summary>
/// Finds a local video's subtitle files (15.1) in its folder and a Subs or Subtitles folder beside it: the same name
/// whatever the case and separators, else the same episode code (S01E02, 1x02), else any subtitle when the video is the
/// only one there. mpv's own matching only takes the exact name, so it is off and these are added instead.
/// </summary>
public static partial class SubtitleFiles
{
    private static readonly string[] Extensions = [".srt", ".vtt", ".ass", ".ssa", ".sub"];
    private static readonly string[] FolderNames = ["subs", "sub", "subtitles", "subtitle"];

    // ISO 639-2 bibliographic codes, which files use and .NET's cultures do not list.
    private static readonly string[] BibliographicCodes =
        ["alb", "arm", "baq", "bur", "chi", "cze", "dut", "fre", "geo", "ger", "gre", "ice", "mac", "mao", "may", "per", "rum", "slo", "tib", "wel"];

    private static readonly HashSet<string> ThreeLetterCodes = new(
        CultureInfo.GetCultures(CultureTypes.NeutralCultures).Select(c => c.ThreeLetterISOLanguageName).Concat(BibliographicCodes),
        StringComparer.OrdinalIgnoreCase);

    public static bool IsSubtitle(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsSubtitleFolder(string path) =>
        FolderNames.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The subtitle files among <paramref name="files"/> that belong to <paramref name="video"/>, best first.
    /// <paramref name="videos"/> are the folder's videos, the video itself among them.
    /// </summary>
    public static IReadOnlyList<SubtitleFile> Match(string video, IEnumerable<string> files, IEnumerable<string> videos)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(videos);
        string own = Key(Path.GetFileNameWithoutExtension(video));
        string? episode = Episode(own);
        string[] others = videos.Select(v => Key(Path.GetFileNameWithoutExtension(v))).Where(k => k != own).ToArray();
        bool alone = others.Length == 0;

        var found = new List<SubtitleFile>();
        foreach (string file in files.Where(IsSubtitle))
        {
            SubtitleTags tags = Tags(Path.GetFileName(file));
            string key = Key(tags.Stem);
            int score = key == own ? 3
                : others.Contains(key) ? 0
                : episode is not null && Episode(key) == episode ? 2
                : alone ? 1
                : 0;
            if (score > 0)
            {
                found.Add(new SubtitleFile(file, tags.Language, tags.Forced, tags.HearingImpaired, score));
            }
        }
        return [.. found.OrderByDescending(f => f.Score).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Reads language, forced and SDH tags off the end of <paramref name="fileName"/>; the rest is the stem.</summary>
    public static SubtitleTags Tags(string fileName)
    {
        List<string> parts = [.. Path.GetFileNameWithoutExtension(fileName).Split('.')];
        string? language = null;
        bool forced = false;
        bool hearingImpaired = false;
        while (parts.Count > 1)
        {
            string last = parts[^1];
            if (last.Equals("forced", StringComparison.OrdinalIgnoreCase))
            {
                forced = true;
            }
            else if (last.Equals("sdh", StringComparison.OrdinalIgnoreCase) || last.Equals("cc", StringComparison.OrdinalIgnoreCase))
            {
                hearingImpaired = true;
            }
            else if (language is null && IsLanguage(last))
            {
                language = last;
            }
            else
            {
                break;
            }
            parts.RemoveAt(parts.Count - 1);
        }
        return new SubtitleTags(string.Join('.', parts), language, forced, hearingImpaired);
    }

    /// <summary>
    /// The <c>sub-add</c> commands for <paramref name="files"/>: each is added with its name as the title and its language,
    /// and with <paramref name="select"/> the preferred language, else an untagged file of the video's own name, is turned on.
    /// The rest are added with <c>auto</c>: mpv's <c>cached</c> selects the track too.
    /// </summary>
    public static IReadOnlyList<string[]> Commands(IReadOnlyList<SubtitleFile> files, string preferredLanguage, bool select)
    {
        ArgumentNullException.ThrowIfNull(files);
        SubtitleFile? chosen = select
            ? files.FirstOrDefault(f => !f.Forced && SameLanguage(f.Language, preferredLanguage))
                ?? files.FirstOrDefault(f => !f.Forced && f.Language is null && f.Score == 3)
            : null;
        return [.. files.Select(f => (string[])["sub-add", f.Path, f == chosen ? "select" : "auto", Path.GetFileName(f.Path), f.Language ?? ""])];
    }

    private static bool SameLanguage(string? tag, string preferred)
    {
        if (tag is null || preferred.Length == 0)
        {
            return false;
        }
        static string? Two(string code)
        {
            try
            {
                return CultureInfo.GetCultureInfo(code).TwoLetterISOLanguageName;
            }
            catch (CultureNotFoundException)
            {
                return BibliographicCodes.Contains(code, StringComparer.OrdinalIgnoreCase) ? code : null;
            }
        }
        return string.Equals(Two(tag) ?? tag, Two(preferred) ?? preferred, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLanguage(string tag)
    {
        if (tag.Length == 3 && tag.All(char.IsAsciiLetter))
        {
            return ThreeLetterCodes.Contains(tag);
        }
        if (!LanguageShape().IsMatch(tag))
        {
            return false;
        }
        try
        {
            return CultureInfo.GetCultureInfo(tag, predefinedOnly: true) is { Name.Length: > 0 };
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Lower case, with every run of punctuation, brackets and spaces as one space.</summary>
    private static string Key(string name) => Separators().Replace(name.ToLowerInvariant(), " ").Trim();

    private static string? Episode(string key)
    {
        Match match = EpisodeCode().Match(key);
        if (!match.Success)
        {
            return null;
        }
        string season = match.Groups["s1"].Success ? match.Groups["s1"].Value : match.Groups["s2"].Value;
        string episode = match.Groups["e1"].Success ? match.Groups["e1"].Value : match.Groups["e2"].Value;
        return $"{int.Parse(season, CultureInfo.InvariantCulture)}x{int.Parse(episode, CultureInfo.InvariantCulture)}";
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"\bs(?<s1>\d{1,2}) ?e(?<e1>\d{1,3})\b|\b(?<s2>\d{1,2})x(?<e2>\d{2,3})\b")]
    private static partial Regex EpisodeCode();

    [GeneratedRegex(@"^[A-Za-z]{2}(-[A-Za-z]{2,4})?$")]
    private static partial Regex LanguageShape();
}
