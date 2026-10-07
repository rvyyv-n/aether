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

    /// <summary>Replies that reported an mpv error (e.g. stepping while nothing is loaded).</summary>
    public long StepFailures => Volatile.Read(ref _stepFailures);

    /// <summary>
    /// UI thread: a step key went down. Steps immediately (if allowed) and starts the repeat timing. A press
    /// of the already-held direction does nothing (key auto-repeat); the opposite direction takes over.
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
        TryStep(direction, now);
    }

    /// <summary>UI thread: a step key went up. Stops repeating at once if it is the held direction.</summary>
    public void Release(FrameStepDirection direction)
    {
        if (_held == Validated(direction))
        {
            _held = 0;
        }
    }

    /// <summary>
    /// UI thread: the caller's timer fired (or any time). Steps if a repeat is due.
    /// </summary>
    /// <returns>The delay until the next repeat is due, or null if no key is held (the timer can stop).</returns>
    public TimeSpan? Tick()
    {
        if (_held == 0)
        {
            return null;
        }

        TimeSpan now = Now();
        if (now < _nextDue)
        {
            return _nextDue - now;
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
            return null;
        }

        // Schedule from the due time to keep the cadence; if the UI stalled for more than an interval,
        // resynchronise instead of bursting to catch up.
        TimeSpan interval = FrameRepeatCurve.IntervalAt(due - _firstRepeatDue, _options);
        TimeSpan next = due + interval;
        _nextDue = next > now ? next : now + interval;
        return _nextDue - now;
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

    private void TryStep(FrameStepDirection direction, TimeSpan now)
    {
        if ((direction == FrameStepDirection.Backward && _atStart) || (direction == FrameStepDirection.Forward && _atEnd))
        {
            return;
        }

        ulong inFlight = Volatile.Read(ref _inFlightId);
        if (inFlight != 0)
        {
            if (now - _inFlightSince < _options.InFlightTimeout)
            {
                Volatile.Write(ref _repeatsDropped, _repeatsDropped + 1);
                return;
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
        }
        catch (MpvException ex)
        {
            Interlocked.CompareExchange(ref _inFlightId, 0, id);
            _held = 0; // the core is going away: stop repeating instead of failing on every tick
            SendFailed?.Invoke(this, ex);
        }
    }

    private TimeSpan Now() => _time.GetElapsedTime(_epoch);

    private static int Validated(FrameStepDirection direction) =>
        direction is FrameStepDirection.Backward or FrameStepDirection.Forward
            ? (int)direction
            : throw new ArgumentOutOfRangeException(nameof(direction));
}
