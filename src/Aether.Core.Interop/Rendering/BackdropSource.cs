using System;
using System.Diagnostics;
using System.Threading;

namespace Aether.Core.Interop.Rendering;

/// <summary>
/// What the UI thread tells the render thread about the card to blur behind: its rectangle, whether
/// the effect is on at all, and a fade. The render thread reads one consistent snapshot per frame
/// under a tiny lock (a struct copy, no allocation). Outlives render loops, so a restarted loop
/// picks up where the last one left off.
/// </summary>
public sealed class BackdropSource
{
    // A fade that nobody renders (another backend) must not keep the loop redrawing forever.
    private const double GraceMilliseconds = 100;

    private readonly Lock _lock = new();
    private BackdropRegion _region;
    private bool _active;
    private float _from = 1f, _to = 1f;
    private long _startTimestamp;
    private double _durationMilliseconds;
    private bool _finalFramePending;

    /// <summary>Any thread. Moves or resizes the card; the caller then asks the loop for a redraw.</summary>
    public void Post(in BackdropRegion region)
    {
        lock (_lock)
        {
            _region = region;
        }
    }

    /// <summary>Any thread. Off when there is no video, the page is settings, or the window is picture in picture.</summary>
    public void SetActive(bool active)
    {
        lock (_lock)
        {
            _active = active;
        }
    }

    /// <summary>Any thread. Fades towards shown or hidden from wherever the fade is now; zero duration jumps there.</summary>
    public void Fade(bool visible, double durationMilliseconds) =>
        Fade(visible, durationMilliseconds, Stopwatch.GetTimestamp());

    internal void Fade(bool visible, double durationMilliseconds, long timestamp)
    {
        lock (_lock)
        {
            _from = OpacityAt(timestamp);
            _to = visible ? 1f : 0f;
            _startTimestamp = timestamp;
            _durationMilliseconds = durationMilliseconds;
            _finalFramePending = durationMilliseconds > 0;
        }
    }

    /// <summary>
    /// True while the loop should keep redrawing without a new video frame: a fade is running, or
    /// its last frame has not been drawn yet. Render thread.
    /// </summary>
    public bool NeedsFrame => NeedsFrameAt(Stopwatch.GetTimestamp());

    internal bool NeedsFrameAt(long timestamp)
    {
        lock (_lock)
        {
            return _finalFramePending && ElapsedMilliseconds(timestamp) < _durationMilliseconds + GraceMilliseconds;
        }
    }

    /// <summary>
    /// Render thread. The frame to draw at <paramref name="timestamp"/>; false when the effect is off,
    /// fully faded out or has no card.
    /// </summary>
    public bool TryGetFrame(long timestamp, out BackdropFrame frame)
    {
        lock (_lock)
        {
            double elapsed = ElapsedMilliseconds(timestamp);
            float opacity = BackdropGeometry.FadeOpacity(_from, _to, elapsed, _durationMilliseconds);
            if (elapsed >= _durationMilliseconds)
            {
                _finalFramePending = false;
            }
            frame = new BackdropFrame(_region, opacity);
            return _active && opacity > 0f && !_region.IsEmpty;
        }
    }

    private float OpacityAt(long timestamp) =>
        BackdropGeometry.FadeOpacity(_from, _to, ElapsedMilliseconds(timestamp), _durationMilliseconds);

    private double ElapsedMilliseconds(long timestamp) =>
        (timestamp - _startTimestamp) * 1000.0 / Stopwatch.Frequency;
}
