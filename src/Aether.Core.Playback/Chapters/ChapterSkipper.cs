namespace Aether.Core.Playback.Chapters;

/// <summary>Where to jump and which chapter was skipped (the first one, when several in a row are).</summary>
/// <param name="Target">Seconds to seek to. Equals the duration when the skipped chapter runs to the end of the file.</param>
public readonly record struct ChapterSkipDecision(double Target, string Title);

/// <summary>
/// Decides when chapter auto-skip jumps. Feed it every position update; it answers when playback has just entered a
/// chapter whose title matches. A chapter is skipped at most once per file, never when the user seeked into it, and
/// never when playback resumed part-way into it. An Intro at 0 is skipped because playback starts inside it.
/// A matching chapter that runs to the end (an Outro) is skipped with a target equal to the duration, which the
/// app should treat as the end of the file.
/// </summary>
public sealed class ChapterSkipper
{
    /// <summary>Playback that starts later than this into a file is a resume, not a start.</summary>
    public const double StartWindowSeconds = 1;

    /// <summary>A chapter ending within this of the duration counts as running to the end.</summary>
    public const double EndWindowSeconds = 1;

    private readonly HashSet<int> _done = [];
    private ChapterList _chapters = ChapterList.Empty;
    private bool[] _matches = [];
    private int _lastIndex = -1;
    private bool _first = true;

    /// <summary>Starts over for a new file. Empty <paramref name="names"/> turns auto-skip off.</summary>
    public void Reset(ChapterList chapters, IReadOnlyList<string> names)
    {
        _chapters = chapters;
        _matches = names.Count == 0
            ? new bool[chapters.Count]
            : [.. chapters.Items.Select(c => ChapterSkipRules.Matches(c.Title, names))];
        _done.Clear();
        _lastIndex = -1;
        _first = true;
    }

    /// <summary>The user jumped to <paramref name="target"/>: the chapter there plays even if it matches.</summary>
    public void OnUserSeek(double target)
    {
        int index = _chapters.IndexAt(target);
        _lastIndex = index;
        _first = false;
        if (index >= 0)
        {
            _done.Add(index);
        }
    }

    public ChapterSkipDecision? OnPosition(double position, double duration)
    {
        int index = _chapters.IndexAt(position);
        bool wasFirst = _first;
        int previous = _lastIndex;
        _first = false;
        _lastIndex = index;

        if (index < 0 || !_matches[index] || (!wasFirst && index == previous))
        {
            return null;
        }
        if (wasFirst && position > StartWindowSeconds)
        {
            _done.Add(index);
            return null;
        }
        if (!_done.Add(index))
        {
            return null;
        }

        int last = index;
        while (last + 1 < _chapters.Count && _matches[last + 1])
        {
            last++;
            _done.Add(last);
        }

        double end = _chapters.EndOf(last, duration);
        if (last + 1 >= _chapters.Count || end >= duration - EndWindowSeconds)
        {
            if (!(duration > 0))
            {
                return null;
            }
            end = duration;
        }
        _lastIndex = last;
        return new ChapterSkipDecision(end, _chapters[index].Title);
    }
}
