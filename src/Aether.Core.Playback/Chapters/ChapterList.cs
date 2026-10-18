using System.Globalization;
using System.Text.Json;

namespace Aether.Core.Playback.Chapters;

/// <summary>
/// The chapters of the open file, sorted by start. A chapter ends where the next one starts; the last one ends at
/// the media duration, which callers pass in because the list does not know it.
/// </summary>
public sealed class ChapterList
{
    /// <summary>Going back this far or less into a chapter jumps to the chapter before it, like most players.</summary>
    public const double RestartThresholdSeconds = 3;

    public static ChapterList Empty { get; } = new([]);

    private readonly Chapter[] _chapters;

    private ChapterList(Chapter[] chapters) => _chapters = chapters;

    public int Count => _chapters.Length;

    public Chapter this[int index] => _chapters[index];

    public IReadOnlyList<Chapter> Items => _chapters;

    /// <summary>Sorts by start, clamps negative or non-finite starts to 0 and keeps the first of any chapters that share a start.</summary>
    public static ChapterList Create(IEnumerable<(double Start, string Title)> chapters)
    {
        var cleaned = chapters
            .Select(c => new Chapter(double.IsFinite(c.Start) ? Math.Max(0, c.Start) : 0, c.Title ?? ""))
            .OrderBy(c => c.Start)
            .ToList();

        var result = new List<Chapter>(cleaned.Count);
        foreach (Chapter chapter in cleaned)
        {
            if (result.Count == 0 || result[^1].Start != chapter.Start)
            {
                result.Add(chapter);
            }
        }
        return result.Count == 0 ? Empty : new ChapterList([.. result]);
    }

    /// <summary>Reads mpv's <c>chapter-list</c> property as JSON: <c>[{"title":"…","time":12.5}, …]</c>. Anything unreadable gives an empty list.</summary>
    public static ChapterList Parse(string? json)
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
            var found = new List<(double, string)>();
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("time", out JsonElement time)
                    || time.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }
                string title = item.TryGetProperty("title", out JsonElement t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() ?? ""
                    : "";
                found.Add((time.GetDouble(), title));
            }
            return Create(found);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>The title to show: the chapter's own, or "Chapter N" (1-based) built from the localised <paramref name="untitledFormat"/> when it has none.</summary>
    public string DisplayTitle(int index, string untitledFormat) =>
        string.IsNullOrWhiteSpace(_chapters[index].Title)
            ? string.Format(CultureInfo.CurrentCulture, untitledFormat, index + 1)
            : _chapters[index].Title;

    /// <summary>Where the chapter ends: the next chapter's start, or <paramref name="duration"/> for the last one.</summary>
    public double EndOf(int index, double duration) =>
        index + 1 < _chapters.Length ? _chapters[index + 1].Start : duration;

    /// <summary>The index of the chapter containing <paramref name="position"/>, or -1 before the first chapter or when there are none.</summary>
    public int IndexAt(double position)
    {
        int index = -1;
        for (int i = 0; i < _chapters.Length && _chapters[i].Start <= position; i++)
        {
            index = i;
        }
        return index;
    }

    /// <summary>The start of the chapter after the current one, or null in the last chapter (or with no chapters).</summary>
    public double? NextStart(double position)
    {
        int next = IndexAt(position) + 1;
        return next < _chapters.Length ? _chapters[next].Start : null;
    }

    /// <summary>
    /// Where "previous chapter" goes: the start of the current chapter when more than <see cref="RestartThresholdSeconds"/>
    /// into it, otherwise the start of the one before. In the first chapter, or before it, that is 0.
    /// </summary>
    public double PreviousStart(double position)
    {
        int index = IndexAt(position);
        if (index < 0)
        {
            return 0;
        }
        if (position - _chapters[index].Start > RestartThresholdSeconds)
        {
            return _chapters[index].Start;
        }
        return index > 0 ? _chapters[index - 1].Start : 0;
    }

    /// <summary>Chapter starts as 0..1 fractions of <paramref name="duration"/> for the seek bar marks. A chapter at 0 or at/after the end has no mark.</summary>
    public IReadOnlyList<double> Fractions(double duration)
    {
        if (!(duration > 0))
        {
            return [];
        }
        return [.. _chapters.Where(c => c.Start > 0 && c.Start < duration).Select(c => c.Start / duration)];
    }
}
