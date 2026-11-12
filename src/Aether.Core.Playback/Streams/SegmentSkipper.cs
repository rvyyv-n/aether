namespace Aether.Core.Playback.Streams;

/// <summary>A SponsorBlock skip: the segment playing and where the jump lands, past any segments touching it.</summary>
public readonly record struct SegmentSkip(SponsorSegment Segment, double Target);

/// <summary>
/// Decides when a SponsorBlock segment is skipped (16.3). Feed it every position update. A segment is skipped the first
/// time playback is found inside it, even on a resume, unless the user seeked into it; segments that touch or overlap
/// are jumped over as one. Each is skipped at most once per video. UI thread only.
/// </summary>
public sealed class SegmentSkipper
{
    /// <summary>Segments closer than this are one stretch to skip.</summary>
    public const double JoinSeconds = 0.5;

    /// <summary>Inside this of a segment's end, a jump would gain nothing.</summary>
    public const double TailSeconds = 0.25;

    private IReadOnlyList<SponsorSegment> _segments = [];
    private readonly HashSet<SponsorSegment> _done = [];

    public void Reset(IReadOnlyList<SponsorSegment> segments)
    {
        _segments = [.. segments.OrderBy(s => s.Start)];
        _done.Clear();
    }

    /// <summary>The user jumped to <paramref name="target"/>: a segment there plays.</summary>
    public void OnUserSeek(double target)
    {
        foreach (SponsorSegment segment in _segments.Where(s => s.Start <= target && target < s.End))
        {
            _done.Add(segment);
        }
    }

    /// <summary>The skip to make now, if any; it is used up.</summary>
    public SegmentSkip? OnPosition(double position)
    {
        if (Offer(position) is not { } skip)
        {
            return null;
        }
        Done(skip);
        return skip;
    }

    /// <summary>The segment playing at <paramref name="position"/> that is still to skip, for a Skip button; nothing is used up.</summary>
    public SegmentSkip? Offer(double position)
    {
        SponsorSegment? playing = _segments.FirstOrDefault(s => !_done.Contains(s) && s.Start <= position && position < s.End - TailSeconds);
        if (playing is null)
        {
            return null;
        }
        double end = playing.End;
        foreach (SponsorSegment next in _segments.Where(s => s.Start > playing.Start))
        {
            if (next.Start > end + JoinSeconds)
            {
                break;
            }
            end = Math.Max(end, next.End);
        }
        return new SegmentSkip(playing, end);
    }

    /// <summary>Marks the skip's segments as done, after a skip from the button.</summary>
    public void Done(SegmentSkip skip)
    {
        foreach (SponsorSegment segment in _segments.Where(s => s.Start >= skip.Segment.Start && s.Start < skip.Target))
        {
            _done.Add(segment);
        }
    }
}
