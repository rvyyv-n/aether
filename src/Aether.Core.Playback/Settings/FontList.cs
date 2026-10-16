namespace Aether.Core.Playback.Settings;

/// <summary>Turns the installed font families into the list the subtitle font picker shows.</summary>
public static class FontList
{
    /// <summary>Sorted, without duplicates (ignoring case), and always containing <paramref name="current"/> when it is not blank.</summary>
    public static List<string> Build(IEnumerable<string> installed, string? current)
    {
        // Fonts can be installed twice (per user and for everyone); the first spelling wins.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> result = [];
        foreach (string name in installed.Append(current ?? ""))
        {
            string trimmed = name.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }
        result.Sort(StringComparer.CurrentCultureIgnoreCase);
        return result;
    }

    /// <summary>Names containing <paramref name="text"/> anywhere, those that start with it first; everything when it is blank.</summary>
    public static List<string> Filter(IReadOnlyList<string> fonts, string? text)
    {
        string query = (text ?? "").Trim();
        if (query.Length == 0)
        {
            return [.. fonts];
        }
        return
        [
            .. fonts.Where(f => f.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)),
            .. fonts.Where(f => !f.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)
                && f.Contains(query, StringComparison.CurrentCultureIgnoreCase)),
        ];
    }
}
