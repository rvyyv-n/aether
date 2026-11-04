namespace Aether.Core.Playback;

/// <summary>One row of the queue drawer: where it is in the queue, what it is called, and whether it is playing.</summary>
public readonly record struct QueueRow(int Index, string Item, string Title, bool IsPlaying);

/// <summary>What the queue drawer (13.3) shows and how its controls read, apart from the strings file.</summary>
public static class QueueRows
{
    /// <summary>
    /// The queue as rows, narrowed to names containing <paramref name="filter"/> (ignoring case and the spaces around
    /// it). <paramref name="current"/> is the playing item's index, or -1.
    /// </summary>
    public static IReadOnlyList<QueueRow> Build(IReadOnlyList<string> items, int current, string? filter)
    {
        ArgumentNullException.ThrowIfNull(items);
        string needle = filter?.Trim() ?? "";
        var rows = new List<QueueRow>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            string title = Title(items[i]);
            if (needle.Length == 0 || title.Contains(needle, StringComparison.CurrentCultureIgnoreCase))
            {
                rows.Add(new QueueRow(i, items[i], title, i == current));
            }
        }
        return rows;
    }

    /// <summary>A file's name with its extension; a link's site and path ("youtube.com/watch?v=…").</summary>
    public static string Title(string item)
    {
        if (WebLink.Host(item) is { } host && Uri.TryCreate(item.Trim(), UriKind.Absolute, out Uri? uri))
        {
            string rest = uri.PathAndQuery.TrimEnd('/');
            return host + rest;
        }
        return Path.GetFileName(item) is { Length: > 0 } name ? name : item;
    }

    /// <summary>The repeat button steps Off, All, One and back to Off.</summary>
    public static RepeatMode NextRepeat(RepeatMode mode) => mode switch
    {
        RepeatMode.None => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.None,
    };

    /// <summary>
    /// The single move that turns <paramref name="before"/> into <paramref name="after"/>, as a list's drag to reorder
    /// leaves it; null when nothing moved or it took more than one move.
    /// </summary>
    public static (int From, int To)? FindMove<T>(IReadOnlyList<T> before, IReadOnlyList<T> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        EqualityComparer<T> same = EqualityComparer<T>.Default;
        if (before.Count != after.Count)
        {
            return null;
        }
        int first = 0;
        while (first < before.Count && same.Equals(before[first], after[first]))
        {
            first++;
        }
        if (first == before.Count)
        {
            return null;
        }
        int last = before.Count - 1;
        while (same.Equals(before[last], after[last]))
        {
            last--;
        }
        if (same.Equals(after[last], before[first]) && Shifted(before, after, first + 1, first, last - first, same))
        {
            return (first, last);
        }
        if (same.Equals(after[first], before[last]) && Shifted(before, after, first, first + 1, last - first, same))
        {
            return (last, first);
        }
        return null;
    }

    private static bool Shifted<T>(IReadOnlyList<T> before, IReadOnlyList<T> after, int from, int to, int count, EqualityComparer<T> same)
    {
        for (int i = 0; i < count; i++)
        {
            if (!same.Equals(before[from + i], after[to + i]))
            {
                return false;
            }
        }
        return true;
    }
}
