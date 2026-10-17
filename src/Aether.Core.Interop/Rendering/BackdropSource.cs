using System;
using System.Diagnostics;
using System.Threading;

namespace Aether.Core.Interop.Rendering;

/// <summary>Which card a region, fade or switch belongs to. A fixed set, so the render thread loops over a plain array.</summary>
public enum BackdropSlot
{
    /// <summary>The transport card.</summary>
    Bar = 0,

    /// <summary>The floating sidebar.</summary>
    Sidebar = 1,
}

/// <summary>
/// What the UI thread tells the render thread about the cards to blur behind: for each <see cref="BackdropSlot"/>
/// its rectangle, whether the effect is on at all, and a fade. The render thread reads one consistent snapshot per
/// frame under a tiny lock (struct copies, no allocation). Outlives render loops, so a restarted loop
/// picks up where the last one left off.
/// </summary>
public sealed class BackdropSource
{
    /// <summary>The number of <see cref="BackdropSlot"/> values; size the buffer given to <see cref="TryGetFrames"/> with it.</summary>
    public const int SlotCount = 2;

    // A fade that nobody renders (another backend) must not keep the loop redrawing forever.
    private const double GraceMilliseconds = 100;

    private struct Slot
    {
        public BackdropRegion Region;
        public bool Active;
        public float From, To;
        public long StartTimestamp;
        public double DurationMilliseconds;
        public bool FinalFramePending;
    }

    private readonly Lock _lock = new();
    private readonly Slot[] _slots = CreateSlots();

    private static Slot[] CreateSlots()
    {
        var slots = new Slot[SlotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].From = slots[i].To = 1f;
        }
        return slots;
    }

    /// <summary>Any thread. Moves or resizes the card; the caller then asks the loop for a redraw.</summary>
    public void Post(BackdropSlot slot, in BackdropRegion region)
    {
        lock (_lock)
        {
            _slots[(int)slot].Region = region;
        }
    }

    /// <summary>Any thread. Off when there is no video, the page is settings, or the window is picture in picture.</summary>
    public void SetActive(BackdropSlot slot, bool active)
    {
        lock (_lock)
        {
            _slots[(int)slot].Active = active;
        }
    }

    /// <summary>Any thread. Fades towards shown or hidden from wherever the fade is now; zero duration jumps there.</summary>
    public void Fade(BackdropSlot slot, bool visible, double durationMilliseconds) =>
        Fade(slot, visible, durationMilliseconds, Stopwatch.GetTimestamp());

    internal void Fade(BackdropSlot slot, bool visible, double durationMilliseconds, long timestamp)
    {
        lock (_lock)
        {
            ref Slot s = ref _slots[(int)slot];
            s.From = OpacityAt(in s, timestamp);
            s.To = visible ? 1f : 0f;
            s.StartTimestamp = timestamp;
            s.DurationMilliseconds = durationMilliseconds;
            s.FinalFramePending = durationMilliseconds > 0;
        }
    }

    /// <summary>
    /// True while the loop should keep redrawing without a new video frame: a fade is running in either
    /// slot, or its last frame has not been drawn yet. Render thread.
    /// </summary>
    public bool NeedsFrame => NeedsFrameAt(Stopwatch.GetTimestamp());

    internal bool NeedsFrameAt(long timestamp)
    {
        lock (_lock)
        {
            foreach (ref readonly Slot s in _slots.AsSpan())
            {
                if (s.FinalFramePending && ElapsedMilliseconds(in s, timestamp) < s.DurationMilliseconds + GraceMilliseconds)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Render thread. Writes the frames to draw at <paramref name="timestamp"/> into <paramref name="frames"/>
    /// (at least <see cref="SlotCount"/> long) and returns how many: a slot that is off, fully faded out or
    /// has no card adds none.
    /// </summary>
    public int TryGetFrames(long timestamp, Span<BackdropFrame> frames)
    {
        int count = 0;
        lock (_lock)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot s = ref _slots[i];
                double elapsed = ElapsedMilliseconds(in s, timestamp);
                float opacity = BackdropGeometry.FadeOpacity(s.From, s.To, elapsed, s.DurationMilliseconds);
                if (elapsed >= s.DurationMilliseconds)
                {
                    s.FinalFramePending = false;
                }
                if (s.Active && opacity > 0f && !s.Region.IsEmpty)
                {
                    frames[count++] = new BackdropFrame(s.Region, opacity, i);
                }
            }
        }
        return count;
    }

    private static float OpacityAt(in Slot s, long timestamp) =>
        BackdropGeometry.FadeOpacity(s.From, s.To, ElapsedMilliseconds(in s, timestamp), s.DurationMilliseconds);

    private static double ElapsedMilliseconds(in Slot s, long timestamp) =>
        (timestamp - s.StartTimestamp) * 1000.0 / Stopwatch.Frequency;
}
