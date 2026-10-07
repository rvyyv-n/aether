using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback;

/// <summary>
/// Frame stepping for a held key: steps at once on press, repeats with acceleration while held, and
/// never has more than one step in flight, so releasing the key stops stepping immediately.
/// <para>
/// Clock-driven and UI-agnostic: the caller forwards key events to <see cref="Press"/> /
/// <see cref="Release"/> and calls <see cref="Tick"/> from a timer, re-arming it with the returned delay.
/// Time comes from the injected <see cref="TimeProvider"/>.
/// </para>
/// <para>
/// Threading: everything except <see cref="OnCommandReply"/> runs on the UI thread and is not
/// thread-safe against itself. The only state shared with the mpv event thread is the id of the step in
/// flight, accessed with <see cref="Volatile"/> / <see cref="Interlocked"/>.
/// </para>
/// </summary>
public sealed class FrameStepper : IMpvCommandReplyHandler
{
    private const ulong SequenceMask = 0x0000_FFFF_FFFF_FFFF;
    private const ulong TagMask = ~SequenceMask;

    private readonly IFrameStepCommandSender _sender;
    private readonly TimeProvider _time;
    private readonly FrameStepperOptions _options;
    private readonly long _epoch;

    // Shared with the event thread: id of the step awaiting its reply, 0 when none. Never 0 for a real id (the tag is non-zero).
    private ulong _inFlightId;

    // UI thread only.
    private ulong _sequence;
    private TimeSpan _inFlightSince;
    private int _held; // 0 = nothing, otherwise (int)FrameStepDirection
    private TimeSpan _nextDue;
    private TimeSpan _firstRepeatDue;
    private bool _firstRepeatPending;
    private bool _atStart;
    private bool _atEnd;
    private long _stepsSent;
    private long _repeatsDropped;
    private long _stepFailures;
    private int _pendingTap; // 0 = none, otherwise (int)FrameStepDirection: a press that landed on a step in flight
    private long _tapsQueued;

    /// <summary>How often <see cref="Tick"/> asks to be called while a queued tap waits for the step in flight.</summary>
    private static readonly TimeSpan PendingTapPoll = TimeSpan.FromMilliseconds(2);

    /// <param name="sender">Delivers the steps.</param>
    /// <param name="time">Clock; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="options">Timing; defaults to <see cref="FrameStepperOptions"/> defaults.</param>
    public FrameStepper(IFrameStepCommandSender sender, TimeProvider? time = null, FrameStepperOptions? options = null)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _time = time ?? TimeProvider.System;
        _options = options ?? new FrameStepperOptions();
        _options.Validate();
        _epoch = _time.GetTimestamp();
    }

    /// <summary>
    /// Raised on the calling (UI) thread when the sender rejects a step because the core is shutting down.
    /// The held key is released first, so a failure is reported once per press, not once per repeat.
    /// </summary>
    public event EventHandler<Exception>? SendFailed;

    /// <summary>True while a step key is held (also while stepping is suppressed at a boundary).</summary>
    public bool IsHeld => _held != 0;

    /// <summary>The held direction, or null.</summary>
    public FrameStepDirection? HeldDirection => _held == 0 ? null : (FrameStepDirection)_held;

    /// <summary>True from sending a step until its reply (or the safety timeout, which is applied on the next press or tick).</summary>
    public bool IsStepInFlight => Volatile.Read(ref _inFlightId) != 0;

    /// <summary>Steps handed to the sender, including ones it then rejected.</summary>
    public long StepsSent => Volatile.Read(ref _stepsSent);

    /// <summary>Steps (repeats, or a press landing on a step still in flight) dropped because a step was in flight.</summary>
    public long RepeatsDropped => Volatile.Read(ref _repeatsDropped);

    /// <summary>Presses that landed on a step in flight and were queued (one slot, newest wins) instead of dropped.</summary>
    public long TapsQueued => Volatile.Read(ref _tapsQueued);

    /// <summary>Replies that reported an mpv error (e.g. stepping while nothing is loaded).</summary>
    public long StepFailures => Volatile.Read(ref _stepFailures);

    /// <summary>
    /// UI thread: a step key went down. Steps immediately (if allowed) and starts the repeat timing. A press
    /// of the already-held direction does nothing (key auto-repeat); the opposite direction takes over.
    /// A press that lands on a step still in flight is queued in a single slot (the newest tap wins) and sent
    /// from <see cref="Tick"/> once the reply arrives, so fast taps are not lost; held repeats are still dropped.
    /// </summary>
    public void Press(FrameStepDirection direction)
    {
        int value = Validated(direction);
        if (_held == value)
        {
            return;
        }

        TimeSpan now = Now();
        _held = value;
        _nextDue = now + _options.InitialRepeatDelay;
        _firstRepeatPending = true;
        if (!TryStep(direction, now) && IsStepInFlight)
        {
            _pendingTap = value;
            Volatile.Write(ref _tapsQueued, _tapsQueued + 1);
        }
    }

    /// <summary>UI thread: a step key went up. Stops repeating at once if it is the held direction.</summary>
    public void Release(FrameStepDirection direction)
    {
        // A queued tap survives its key-up on purpose: it was a real press that has not been sent yet.
        if (_held == Validated(direction))
        {
            _held = 0;
        }
    }

    /// <summary>UI thread: window blur or focus loss. Releases the held key and discards any queued tap.</summary>
    public void ReleaseAll()
    {
        _held = 0;
        _pendingTap = 0;
    }

    /// <summary>
    /// UI thread: the caller's timer fired (or any time). Steps if a repeat is due.
    /// </summary>
    /// <returns>The delay until the next repeat is due, or null if no key is held (the timer can stop).</returns>
    public TimeSpan? Tick()
    {
        TimeSpan now = Now();
        bool tapWaiting = FlushPendingTap(now);

        if (_held == 0)
        {
            return tapWaiting ? PendingTapPoll : null;
        }

        if (now < _nextDue)
        {
            TimeSpan wait = _nextDue - now;
            return tapWaiting && PendingTapPoll < wait ? PendingTapPoll : wait;
        }

        TimeSpan due = _nextDue;
        if (_firstRepeatPending)
        {
            _firstRepeatPending = false;
            _firstRepeatDue = due;
        }
        TryStep((FrameStepDirection)_held, now);

        // A failed send releases the held key.
        if (_held == 0)
        {
            return tapWaiting ? PendingTapPoll : null;
        }

        // Schedule from the due time to keep the cadence; if the UI stalled for more than an interval,
        // resynchronise instead of bursting to catch up.
        TimeSpan interval = FrameRepeatCurve.IntervalAt(due - _firstRepeatDue, _options);
        TimeSpan next = due + interval;
        _nextDue = next > now ? next : now + interval;
        TimeSpan delay = _nextDue - now;
        return tapWaiting && PendingTapPoll < delay ? PendingTapPoll : delay;
    }

    /// <summary>
    /// UI thread: where playback currently sits. Backward steps are suppressed at the start and forward
    /// steps at the end; a held key stays held and resumes when the bounds change.
    /// </summary>
    public void SetBounds(bool atStart, bool atEnd)
    {
        _atStart = atStart;
        _atEnd = atEnd;
    }

    /// <summary>
    /// Event thread: a command reply arrived. Clears the in-flight step if the id matches; never blocks or allocates.
    /// </summary>
    /// <returns>False if <paramref name="replyUserdata"/> does not carry the frame-step tag.</returns>
    public bool OnCommandReply(ulong replyUserdata, int error)
    {
        if ((replyUserdata & TagMask) != IMpvCommandReplyHandler.FrameStepReplyTag)
        {
            return false;
        }

        // A reply for a step that already timed out (or was superseded) no longer matches and changes nothing.
        if (Interlocked.CompareExchange(ref _inFlightId, 0, replyUserdata) == replyUserdata && error < 0)
        {
            Interlocked.Increment(ref _stepFailures);
        }
        return true;
    }

    /// <summary>Sends a queued tap once nothing is in flight. Returns true while one is still waiting.</summary>
    private bool FlushPendingTap(TimeSpan now)
    {
        if (_pendingTap == 0)
        {
            return false;
        }

        var direction = (FrameStepDirection)_pendingTap;
        if (TryStep(direction, now))
        {
            _pendingTap = 0;
            return false;
        }
        // Still in flight keeps it waiting; a boundary (or a failed send) discards it.
        if (IsStepInFlight)
        {
            return true;
        }
        _pendingTap = 0;
        return false;
    }

    /// <returns>True if a step was handed to the sender.</returns>
    private bool TryStep(FrameStepDirection direction, TimeSpan now)
    {
        if ((direction == FrameStepDirection.Backward && _atStart) || (direction == FrameStepDirection.Forward && _atEnd))
        {
            return false;
        }

        ulong inFlight = Volatile.Read(ref _inFlightId);
        if (inFlight != 0)
        {
            if (now - _inFlightSince < _options.InFlightTimeout)
            {
                Volatile.Write(ref _repeatsDropped, _repeatsDropped + 1);
                return false;
            }
            // Lost reply: forget it. If it arrives after all, it no longer matches.
            Interlocked.CompareExchange(ref _inFlightId, 0, inFlight);
        }

        ulong id = IMpvCommandReplyHandler.FrameStepReplyTag | (++_sequence & SequenceMask);
        _inFlightSince = now;
        // Published before Send: the reply can arrive on the event thread before Send returns.
        Volatile.Write(ref _inFlightId, id);
        Volatile.Write(ref _stepsSent, _stepsSent + 1);
        try
        {
            _sender.Send(direction, id);
            return true;
        }
        catch (MpvException ex)
        {
            Interlocked.CompareExchange(ref _inFlightId, 0, id);
            _held = 0; // the core is going away: stop repeating instead of failing on every tick
            _pendingTap = 0;
            SendFailed?.Invoke(this, ex);
            return false;
        }
    }

    private TimeSpan Now() => _time.GetElapsedTime(_epoch);

    private static int Validated(FrameStepDirection direction) =>
        direction is FrameStepDirection.Backward or FrameStepDirection.Forward
            ? (int)direction
            : throw new ArgumentOutOfRangeException(nameof(direction));
}
