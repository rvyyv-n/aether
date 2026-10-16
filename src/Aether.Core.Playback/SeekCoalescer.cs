using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback;

/// <summary>
/// Coalesces rapid seek bar scrubbing into at most one in-flight seek command at a time.
/// Intermediate drag seeks are collapsed to the latest target, and the final release seek
/// is guaranteed to dispatch as an exact frame seek without being dropped.
/// <para>
/// Thread safety: <see cref="Seek"/> is called on the UI thread; <see cref="OnCommandReply"/>
/// arrives on the mpv event thread. Synchronization is minimal and lock-protected with zero allocations.
/// </para>
/// </summary>
public sealed class SeekCoalescer : IMpvCommandReplyHandler
{
    private const ulong SequenceMask = 0x0000_FFFF_FFFF_FFFF;
    private const ulong TagMask = ~SequenceMask;

    private readonly ISeekCommandSender _sender;
    private readonly TimeProvider _time;
    private readonly SeekCoalescerOptions _options;
    private readonly long _epoch;
    private readonly object _gate = new();

    private ulong _inFlightId;
    private TimeSpan _inFlightSince;
    private ulong _sequence;

    private double? _pendingPosition;
    private bool _pendingDragging;
    private double? _pendingSkip;

    private long _seeksSent;
    private long _seeksCoalesced;
    private long _repliesHandled;
    private long _seekFailures;

    public SeekCoalescer(ISeekCommandSender sender, TimeProvider? time = null, SeekCoalescerOptions? options = null)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _time = time ?? TimeProvider.System;
        _options = options ?? new SeekCoalescerOptions();
        _options.Validate();
        _epoch = _time.GetTimestamp();
    }

    /// <summary>Raised when the underlying sender rejects a seek because the core is shutting down.</summary>
    public event EventHandler<Exception>? SendFailed;

    /// <summary>True while an async seek is in flight awaiting its command reply.</summary>
    public bool IsSeekInFlight
    {
        get
        {
            lock (_gate)
            {
                return _inFlightId != 0;
            }
        }
    }

    /// <summary>True if a subsequent seek was coalesced and is waiting for the in-flight seek to finish.</summary>
    public bool HasPendingSeek
    {
        get
        {
            lock (_gate)
            {
                return _pendingPosition.HasValue || _pendingSkip.HasValue;
            }
        }
    }

    /// <summary>The summed skip waiting to dispatch (see <see cref="SeekBy"/>), or null.</summary>
    public double? PendingSkip
    {
        get
        {
            lock (_gate)
            {
                return _pendingSkip;
            }
        }
    }

    /// <summary>The latest pending seek position waiting to dispatch, or null.</summary>
    public double? PendingPosition
    {
        get
        {
            lock (_gate)
            {
                return _pendingPosition;
            }
        }
    }

    /// <summary>True if the pending seek is a keyframe drag seek, false if an exact release seek, or null if none pending.</summary>
    public bool? PendingDragging
    {
        get
        {
            lock (_gate)
            {
                return _pendingPosition.HasValue ? _pendingDragging : null;
            }
        }
    }

    /// <summary>Total seek commands dispatched to the sender.</summary>
    public long SeeksSent => Volatile.Read(ref _seeksSent);

    /// <summary>Total redundant intermediate seek requests collapsed/coalesced.</summary>
    public long SeeksCoalesced => Volatile.Read(ref _seeksCoalesced);

    /// <summary>Total command replies processed.</summary>
    public long RepliesHandled => Volatile.Read(ref _repliesHandled);

    /// <summary>Total replies that reported an error.</summary>
    public long SeekFailures => Volatile.Read(ref _seekFailures);

    /// <summary>
    /// UI thread: requests a seek. If a previous seek is still in flight, coalesces into a pending slot.
    /// A release seek (<paramref name="dragging"/> = false) is never downgraded and will land as exact.
    /// </summary>
    public void Seek(double seconds, bool dragging)
    {
        double target = Math.Max(0, double.IsFinite(seconds) ? seconds : 0);
        TimeSpan now = Now();

        lock (_gate)
        {
            if (_inFlightId != 0)
            {
                if (now - _inFlightSince >= _options.InFlightTimeout)
                {
                    // Prior seek timed out (lost reply or slow core). Reset and proceed.
                    _inFlightId = 0;
                }
                else
                {
                    // A seek is already in flight. Coalesce this request; an absolute target replaces a queued skip.
                    _pendingSkip = null;
                    if (!_pendingPosition.HasValue)
                    {
                        _pendingDragging = dragging;
                    }
                    else if (!dragging)
                    {
                        // A release seek always overrides prior drag seeks to become exact.
                        _pendingDragging = false;
                    }
                    _pendingPosition = target;
                    Interlocked.Increment(ref _seeksCoalesced);
                    return;
                }
            }

            DispatchSeekLocked(target, dragging, now);
        }
    }

    /// <summary>
    /// UI thread: skips by <paramref name="deltaSeconds"/> from the current position. Skips requested while a seek is
    /// in flight add up, and move a queued seek-bar target by the same amount, so a held arrow key sends one seek at a time.
    /// </summary>
    public void SeekBy(double deltaSeconds)
    {
        double delta = double.IsFinite(deltaSeconds) ? deltaSeconds : 0;
        TimeSpan now = Now();

        lock (_gate)
        {
            if (_inFlightId != 0)
            {
                if (now - _inFlightSince >= _options.InFlightTimeout)
                {
                    _inFlightId = 0;
                }
                else
                {
                    if (_pendingPosition is { } target)
                    {
                        _pendingPosition = Math.Max(0, target + delta);
                    }
                    else
                    {
                        _pendingSkip = (_pendingSkip ?? 0) + delta;
                    }
                    Interlocked.Increment(ref _seeksCoalesced);
                    return;
                }
            }

            DispatchSkipLocked(delta, now);
        }
    }

    /// <summary>
    /// Event thread: handles <c>MPV_EVENT_COMMAND_REPLY</c>. If a pending seek was queued, dispatches it immediately.
    /// </summary>
    public bool OnCommandReply(ulong replyUserdata, int error)
    {
        if ((replyUserdata & TagMask) != IMpvCommandReplyHandler.SeekReplyTag)
        {
            return false;
        }

        lock (_gate)
        {
            if (_inFlightId != replyUserdata)
            {
                return true;
            }

            _inFlightId = 0;
            Interlocked.Increment(ref _repliesHandled);
            if (error < 0)
            {
                Interlocked.Increment(ref _seekFailures);
            }

            if (_pendingPosition is { } nextTarget)
            {
                bool nextDragging = _pendingDragging;
                _pendingPosition = null;
                DispatchSeekLocked(nextTarget, nextDragging, Now());
            }
            else if (_pendingSkip is { } nextSkip)
            {
                _pendingSkip = null;
                DispatchSkipLocked(nextSkip, Now());
            }

            return true;
        }
    }

    /// <summary>Resets any active in-flight or pending seek state.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _inFlightId = 0;
            _pendingPosition = null;
            _pendingSkip = null;
        }
    }

    private void DispatchSkipLocked(double delta, TimeSpan now)
    {
        ulong id = IMpvCommandReplyHandler.SeekReplyTag | (++_sequence & SequenceMask);
        _inFlightId = id;
        _inFlightSince = now;
        Interlocked.Increment(ref _seeksSent);

        try
        {
            _sender.SendSeekBy(delta, id);
        }
        catch (MpvException ex)
        {
            _inFlightId = 0;
            _pendingPosition = null;
            _pendingSkip = null;
            SendFailed?.Invoke(this, ex);
        }
    }

    private void DispatchSeekLocked(double seconds, bool dragging, TimeSpan now)
    {
        ulong id = IMpvCommandReplyHandler.SeekReplyTag | (++_sequence & SequenceMask);
        _inFlightId = id;
        _inFlightSince = now;
        Interlocked.Increment(ref _seeksSent);

        try
        {
            _sender.SendSeek(seconds, dragging, id);
        }
        catch (MpvException ex)
        {
            _inFlightId = 0;
            _pendingPosition = null;
            _pendingSkip = null;
            SendFailed?.Invoke(this, ex);
        }
    }

    private TimeSpan Now() => _time.GetElapsedTime(_epoch);
}
