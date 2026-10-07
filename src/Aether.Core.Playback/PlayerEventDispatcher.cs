using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback;

/// <summary>Ids the player properties are observed under (<c>reply_userdata</c>); dispatch is on these, never on names.</summary>
public enum PlayerProperty : ulong
{
    Position = 1,
    Duration,
    Speed,
    Volume,
    ContainerFps,
    Pause,
    EofReached,
    IdleActive,
}

/// <summary>
/// Turns mpv events into <see cref="PlayerViewModel"/> updates without ever touching the UI thread
/// from the event thread.
/// <para>
/// Event thread (<see cref="OnEvent"/>): decodes in place into ONE pending <see cref="PlayerState"/>
/// under a small lock and calls <see cref="IUiFlushScheduler.RequestFlush"/> only on the clean-to-dirty
/// transition. No managed allocation per event: no string decoding, no boxing, no closures.
/// UI thread (<see cref="Flush"/>): takes the newest pending state and applies it. Intermediate values
/// are dropped on purpose, so the UI sees at most one update per flush however fast mpv reports.
/// </para>
/// </summary>
public sealed class PlayerEventDispatcher : IMpvEventSink
{
    private readonly PlayerViewModel _viewModel;
    private readonly IUiFlushScheduler _scheduler;
    private readonly Lock _gate = new();

    // Guarded by _gate. Written on the event thread, read by Flush.
    private PlayerState _pending = PlayerState.Initial;
    private bool _idleActive = true; // idle-active; true until mpv says otherwise (no file yet)
    private bool _loading;           // between StartFile and FileLoaded
    private bool _fileEnded;         // EndFile seen and no StartFile since

    private int _dirty;              // 1 while a flush is requested and has not started yet
    private Exception? _fault;
    private volatile IMpvCommandReplyHandler? _commandReplies;

    public PlayerEventDispatcher(PlayerViewModel viewModel, IUiFlushScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(scheduler);
        _viewModel = viewModel;
        _scheduler = scheduler;
    }

    /// <summary>Receives every <see cref="MpvEventId.CommandReply"/> on the event thread. Must not block.</summary>
    public IMpvCommandReplyHandler? CommandReplies
    {
        get => _commandReplies;
        set => _commandReplies = value;
    }

    /// <summary>
    /// Raised on the UI thread (from <see cref="Flush"/>) after the sink faulted. If several faults pile
    /// up before a flush, only the latest is reported.
    /// </summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>Observes the player properties under their <see cref="PlayerProperty"/> ids. Call before starting the event loop.</summary>
    public void ObserveAll(MpvContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ObserveProperty((ulong)PlayerProperty.Position, "time-pos", MpvFormat.Double);
        context.ObserveProperty((ulong)PlayerProperty.Duration, "duration", MpvFormat.Double);
        context.ObserveProperty((ulong)PlayerProperty.Speed, "speed", MpvFormat.Double);
        context.ObserveProperty((ulong)PlayerProperty.Volume, "volume", MpvFormat.Double);
        context.ObserveProperty((ulong)PlayerProperty.ContainerFps, "container-fps", MpvFormat.Double);
        context.ObserveProperty((ulong)PlayerProperty.Pause, "pause", MpvFormat.Flag);
        context.ObserveProperty((ulong)PlayerProperty.EofReached, "eof-reached", MpvFormat.Flag);
        context.ObserveProperty((ulong)PlayerProperty.IdleActive, "idle-active", MpvFormat.Flag);
    }

    /// <summary>Event thread.</summary>
    public void OnEvent(MpvEventView mpvEvent)
    {
        switch (mpvEvent.Id)
        {
            case MpvEventId.PropertyChange:
                if (mpvEvent.TryGetProperty(out MpvPropertyChange change))
                {
                    OnProperty(change);
                }
                break;

            case MpvEventId.CommandReply:
                _commandReplies?.OnCommandReply(mpvEvent.ReplyUserdata, mpvEvent.Error);
                break;

            case MpvEventId.StartFile:
                OnFileState(loading: true, fileEnded: false);
                break;

            case MpvEventId.FileLoaded:
                OnFileState(loading: false, fileEnded: false);
                break;

            case MpvEventId.EndFile:
            case MpvEventId.Shutdown:
                OnFileState(loading: false, fileEnded: true);
                break;

            case MpvEventId.QueueOverflow:
                // mpv dropped events, so the pending state may be stale. Re-reading the observed
                // properties needs a command channel (out of scope here); for now just make sure the
                // UI refreshes. Values correct themselves at the next property change.
                MarkDirty();
                break;
        }
    }

    /// <summary>Event thread: the sink or the pump failed. Reported to the UI through <see cref="Flush"/>.</summary>
    public void OnFault(Exception exception)
    {
        Interlocked.Exchange(ref _fault, exception);
        MarkDirty();
    }

    /// <summary>
    /// UI thread, called by the scheduler after <see cref="IUiFlushScheduler.RequestFlush"/>: applies
    /// the newest pending state, then raises <see cref="Faulted"/> if the sink faulted.
    /// </summary>
    public void Flush()
    {
        PlayerState state;
        lock (_gate)
        {
            state = _pending;
            // Cleared under the lock, after the read: a later write sees 0 and requests a new flush.
            Interlocked.Exchange(ref _dirty, 0);
        }

        Exception? fault = Interlocked.Exchange(ref _fault, null);
        _viewModel.Apply(state);
        if (fault is not null)
        {
            Faulted?.Invoke(this, fault);
        }
    }

    private void OnProperty(in MpvPropertyChange change)
    {
        // Unavailable (no file) and unexpected formats both fall back to the field's default.
        bool changed;
        lock (_gate)
        {
            PlayerState before = _pending;
            PlayerState next = before;
            switch ((PlayerProperty)change.Id)
            {
                case PlayerProperty.Position:
                    next = next with { Position = change.TryGetDouble(out double position) ? position : 0 };
                    break;
                case PlayerProperty.Duration:
                    next = next with { Duration = change.TryGetDouble(out double duration) ? duration : 0 };
                    break;
                case PlayerProperty.Speed:
                    next = next with { Speed = change.TryGetDouble(out double speed) ? speed : 1 };
                    break;
                case PlayerProperty.Volume:
                    next = next with { Volume = change.TryGetDouble(out double volume) ? volume : 100 };
                    break;
                case PlayerProperty.ContainerFps:
                    next = next with { FramesPerSecond = change.TryGetDouble(out double fps) ? fps : 0 };
                    break;
                case PlayerProperty.Pause:
                    next = next with { IsPaused = change.TryGetFlag(out bool paused) && paused };
                    break;
                case PlayerProperty.EofReached:
                    next = next with { IsAtEnd = change.TryGetFlag(out bool atEnd) && atEnd };
                    break;
                case PlayerProperty.IdleActive:
                    // Unavailable can't happen for idle-active; treat it as idle.
                    _idleActive = !change.TryGetFlag(out bool idle) || idle;
                    break;
                default:
                    return;
            }

            next = next with { Status = DeriveStatus(next) };
            changed = !next.Equals(before);
            _pending = next;
        }

        if (changed)
        {
            MarkDirty();
        }
    }

    private void OnFileState(bool loading, bool fileEnded)
    {
        bool changed;
        lock (_gate)
        {
            _loading = loading;
            _fileEnded = fileEnded;
            PlayerState before = _pending;
            PlayerState next = before with { Status = DeriveStatus(before) };
            changed = !next.Equals(before);
            _pending = next;
        }

        if (changed)
        {
            MarkDirty();
        }
    }

    /// <summary>Loading, then Idle, then Ended, else Paused/Playing. Caller holds <see cref="_gate"/>.</summary>
    private PlaybackStatus DeriveStatus(in PlayerState state)
    {
        if (_loading)
        {
            return PlaybackStatus.Loading;
        }
        if (_idleActive || _fileEnded)
        {
            return PlaybackStatus.Idle;
        }
        if (state.IsAtEnd)
        {
            return PlaybackStatus.Ended;
        }
        return state.IsPaused ? PlaybackStatus.Paused : PlaybackStatus.Playing;
    }

    /// <summary>Requests a flush only when none is outstanding. Never called under <see cref="_gate"/>.</summary>
    private void MarkDirty()
    {
        if (Interlocked.Exchange(ref _dirty, 1) != 0)
        {
            return;
        }

        try
        {
            _scheduler.RequestFlush();
        }
        catch
        {
            // No flush is coming; let the next event ask again instead of staying dirty forever.
            Interlocked.Exchange(ref _dirty, 0);
            throw;
        }
    }
}
