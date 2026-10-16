using System.Text.RegularExpressions;

namespace Aether.Core.Playback.Chapters;

/// <summary>Which chapter titles auto-skip (9.7) should jump over.</summary>
public static class ChapterSkipRules
{
    private static readonly char[] Separators = [',', '\n', '\r'];

    /// <summary>Splits the setting text on commas and line breaks, trims, and drops empty and repeated (case-insensitive) names.</summary>
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in text.Split(Separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Add(part))
            {
                names.Add(part);
            }
        }
        return names;
    }

    /// <summary>
    /// True when a name appears in <paramref name="title"/> as a whole word or word sequence, ignoring case, so "Sponsor"
    /// matches "Sponsor: NordVPN" but not "Sponsorship talk", and "Intro" does not match "Introduction".
    /// </summary>
    public static bool Matches(string title, IReadOnlyList<string> names)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }
        foreach (string name in names)
        {
            if (name.Length > 0 && Regex.IsMatch(title, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }
        return false;
    }
}
