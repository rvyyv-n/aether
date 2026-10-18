using System.Buffers;
using System.Text;
using System.Text.Unicode;
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
    DisplayWidth,
    DisplayHeight,
    HwdecCurrent,
    VideoFormat,
    VideoParamsWidth,
    VideoParamsHeight,
    EstimatedVfFps,
    DisplayFps,
    DecoderFrameDropCount,
    FrameDropCount,
    DemuxerCacheDuration,
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
    private volatile IMpvPropertyRequester? _propertyRequests;

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

    /// <summary>What <see cref="ObserveAll"/> observes and a queue overflow re-reads, under the <see cref="PlayerProperty"/> ids.</summary>
    private static readonly (PlayerProperty Id, string Name, MpvFormat Format)[] Observed =
    [
        (PlayerProperty.Position, "time-pos", MpvFormat.Double),
        (PlayerProperty.Duration, "duration", MpvFormat.Double),
        (PlayerProperty.Speed, "speed", MpvFormat.Double),
        (PlayerProperty.Volume, "volume", MpvFormat.Double),
        (PlayerProperty.ContainerFps, "container-fps", MpvFormat.Double),
        (PlayerProperty.Pause, "pause", MpvFormat.Flag),
        (PlayerProperty.EofReached, "eof-reached", MpvFormat.Flag),
        (PlayerProperty.IdleActive, "idle-active", MpvFormat.Flag),
        (PlayerProperty.DisplayWidth, "dwidth", MpvFormat.Int64),
        (PlayerProperty.DisplayHeight, "dheight", MpvFormat.Int64),
        (PlayerProperty.HwdecCurrent, "hwdec-current", MpvFormat.String),
        (PlayerProperty.VideoFormat, "video-format", MpvFormat.String),
        (PlayerProperty.VideoParamsWidth, "video-params/w", MpvFormat.Int64),
        (PlayerProperty.VideoParamsHeight, "video-params/h", MpvFormat.Int64),
        (PlayerProperty.EstimatedVfFps, "estimated-vf-fps", MpvFormat.Double),
        (PlayerProperty.DisplayFps, "display-fps", MpvFormat.Double),
        (PlayerProperty.DecoderFrameDropCount, "decoder-frame-drop-count", MpvFormat.Int64),
        (PlayerProperty.FrameDropCount, "frame-drop-count", MpvFormat.Int64),
        (PlayerProperty.DemuxerCacheDuration, "demuxer-cache-duration", MpvFormat.Double),
    ];

    /// <summary>
    /// Re-reads every observed property after <see cref="MpvEventId.QueueOverflow"/>; without it the state stays as
    /// it was until each property happens to change again.
    /// </summary>
    public IMpvPropertyRequester? PropertyRequests
    {
        get => _propertyRequests;
        set => _propertyRequests = value;
    }

    /// <summary>Observes the player properties under their <see cref="PlayerProperty"/> ids. Call before starting the event loop.</summary>
    public void ObserveAll(MpvContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach ((PlayerProperty id, string name, MpvFormat format) in Observed)
        {
            context.ObserveProperty((ulong)id, name, format);
        }
    }

    /// <summary>Event thread.</summary>
    public void OnEvent(MpvEventView mpvEvent)
    {
        switch (mpvEvent.Id)
        {
            case MpvEventId.PropertyChange:
            case MpvEventId.GetPropertyReply:
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
                OnQueueOverflow();
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

    /// <summary>
    /// mpv dropped events, so file-state events may be among them: a lost FileLoaded would leave the player loading
    /// for good. Forget the file state (idle-active, re-read with the rest, says whether a file is open) and ask for
    /// every observed value again; the replies arrive as ordinary events. Rare, so the requests' marshalling is fine.
    /// </summary>
    private void OnQueueOverflow()
    {
        lock (_gate)
        {
            _loading = false;
            _fileEnded = false;
            _pending = _pending with { Status = DeriveStatus(_pending) };
        }

        if (_propertyRequests is { } requests)
        {
            try
            {
                foreach ((PlayerProperty id, string name, MpvFormat format) in Observed)
                {
                    requests.Request((ulong)id, name, format);
                }
            }
            catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
            {
                // The core is shutting down; there is nothing left to resync.
            }
        }
        MarkDirty();
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
                case PlayerProperty.DisplayWidth:
                    next = next with { VideoWidth = ToDimension(change) };
                    break;
                case PlayerProperty.DisplayHeight:
                    next = next with { VideoHeight = ToDimension(change) };
                    break;
                case PlayerProperty.HwdecCurrent:
                    next = next with { Decoder = ToText(change, before.Decoder) };
                    break;
                case PlayerProperty.VideoFormat:
                    next = next with { VideoFormat = ToText(change, before.VideoFormat) };
                    break;
                case PlayerProperty.VideoParamsWidth:
                    next = next with { CodedWidth = ToDimension(change) };
                    break;
                case PlayerProperty.VideoParamsHeight:
                    next = next with { CodedHeight = ToDimension(change) };
                    break;
                case PlayerProperty.EstimatedVfFps:
                    next = next with { EstimatedFps = change.TryGetDouble(out double estimated) ? estimated : 0 };
                    break;
                case PlayerProperty.DisplayFps:
                    next = next with { DisplayFps = change.TryGetDouble(out double refresh) ? refresh : 0 };
                    break;
                case PlayerProperty.DecoderFrameDropCount:
                    next = next with { DecoderDroppedFrames = change.TryGetInt64(out long decoderDrops) ? decoderDrops : 0 };
                    break;
                case PlayerProperty.FrameDropCount:
                    next = next with { DroppedFrames = change.TryGetInt64(out long drops) ? drops : 0 };
                    break;
                case PlayerProperty.DemuxerCacheDuration:
                    next = next with { CacheDuration = change.TryGetDouble(out double cache) ? cache : -1 };
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

    /// <summary>
    /// A string property, reusing <paramref name="current"/> when the text is unchanged so a repeated
    /// report allocates nothing; null when unavailable or empty.
    /// </summary>
    private static string? ToText(in MpvPropertyChange change, string? current)
    {
        if (!change.TryGetStringUtf8(out ReadOnlySpan<byte> utf8) || utf8.IsEmpty)
        {
            return null;
        }

        if (current is not null && utf8.Length <= 128)
        {
            Span<char> buffer = stackalloc char[128];
            if (Utf8.ToUtf16(utf8, buffer, out _, out int written) == OperationStatus.Done
                && buffer[..written].SequenceEqual(current))
            {
                return current;
            }
        }
        return Encoding.UTF8.GetString(utf8);
    }

    /// <summary>A display dimension in pixels; 0 when unavailable or out of range.</summary>
    private static int ToDimension(in MpvPropertyChange change) =>
        change.TryGetInt64(out long value) && value > 0 && value <= int.MaxValue ? (int)value : 0;

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
