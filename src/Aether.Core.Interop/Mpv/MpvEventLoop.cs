using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aether.Core.Interop.Mpv;

/// <summary>
/// Receives events on the event thread ("Aether.MpvEvents"). Implementations must not block and must
/// not wait on the UI thread: copy what is needed into preallocated state and hand off.
/// </summary>
public interface IMpvEventSink
{
    /// <summary>
    /// One event. <paramref name="mpvEvent"/> is only valid during the call. Includes
    /// <see cref="MpvEventId.Shutdown"/> (the core is quitting; the loop stops after this call) and
    /// <see cref="MpvEventId.QueueOverflow"/> (mpv dropped events; re-read any state you track).
    /// </summary>
    void OnEvent(MpvEventView mpvEvent);

    /// <summary><see cref="OnEvent"/> threw, or the pump itself failed. The loop keeps running after a sink fault.</summary>
    void OnFault(Exception exception);
}

/// <summary>The native side of the loop. Internal seam so the pump can be tested without libmpv.</summary>
internal unsafe interface IMpvEventSource
{
    /// <summary>Pins the core for the life of the loop; false if it is already closed.</summary>
    bool TryAcquire();

    /// <summary>Drops the pin taken by <see cref="TryAcquire"/>. May destroy the core if it was the last reference.</summary>
    void Release();

    /// <summary>The owner disposed the core; the loop must stop and release it.</summary>
    bool IsClosed { get; }

    /// <summary><c>mpv_wait_event</c>. Called only between <see cref="TryAcquire"/> and <see cref="Release"/>, on the event thread.</summary>
    MpvEvent* Wait(double timeoutSeconds);

    /// <summary><c>mpv_wakeup</c> from any thread; false if the core is already gone.</summary>
    bool TryWakeup();
}

/// <summary>
/// The dedicated event thread ("Aether.MpvEvents", a <see cref="TaskCreationOptions.LongRunning"/> task)
/// pumping <c>mpv_wait_event</c> for one core and handing each event to an <see cref="IMpvEventSink"/>.
/// <para>
/// Hot path: no managed allocation. Each event reaches the sink as an <see cref="MpvEventView"/> over
/// mpv's own memory; properties are dispatched by the numeric id they were observed under.
/// </para>
/// <para>
/// Lifetime: the loop holds a reference on the core's <see cref="MpvHandle"/> from <see cref="Start"/>
/// until the thread exits, so the core can never be destroyed under <c>mpv_wait_event</c>.
/// <see cref="Dispose"/> and <see cref="MpvContext.Dispose"/> both wake the thread with
/// <c>mpv_wakeup</c>; it then sees the stop request (or the closed handle), drops its reference and
/// exits. If that was the last reference, the core is destroyed on the event thread, not the caller's.
/// </para>
/// </summary>
public sealed unsafe class MpvEventLoop : IDisposable
{
    public const string ThreadName = "Aether.MpvEvents";

    private readonly IMpvEventSource _source;
    private readonly IMpvEventSink _sink;
    private Task _completion = Task.CompletedTask;
    private volatile bool _stop;
    private int _started;
    private int _disposed;
    private int _threadId;
    private long _eventsDispatched;

    /// <param name="context">The core to pump. The loop never disposes it.</param>
    public MpvEventLoop(MpvContext context, IMpvEventSink sink)
        : this(new MpvHandleEventSource(context?.Handle ?? throw new ArgumentNullException(nameof(context))), sink)
    {
    }

    internal MpvEventLoop(IMpvEventSource source, IMpvEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);
        _source = source;
        _sink = sink;
    }

    /// <summary>Completes when the event thread has exited and released the core.</summary>
    public Task Completion => _completion;

    public bool IsRunning => !_completion.IsCompleted;

    /// <summary>Events handed to the sink (excluding the empty wake-ups).</summary>
    public long EventsDispatched => Interlocked.Read(ref _eventsDispatched);

    /// <summary>True on the event thread itself.</summary>
    public bool IsEventThread => Environment.CurrentManagedThreadId == Volatile.Read(ref _threadId);

    /// <summary>Starts the event thread. Once only.</summary>
    /// <exception cref="ObjectDisposedException">The loop or the core was already disposed.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The event loop was already started.");
        }
        if (!_source.TryAcquire())
        {
            throw new ObjectDisposedException(nameof(MpvContext));
        }

        try
        {
            _completion = Task.Factory.StartNew(
                static state => ((MpvEventLoop)state!).Run(),
                this,
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }
        catch
        {
            _source.Release();
            throw;
        }
    }

    /// <summary>
    /// Requests stop and waits up to <paramref name="timeoutMilliseconds"/> for the thread to exit.
    /// Does not wait when called on the event thread (it exits after the current event).
    /// </summary>
    public bool Stop(int timeoutMilliseconds)
    {
        RequestStop();
        if (IsEventThread)
        {
            return false;
        }
        return _completion.Wait(timeoutMilliseconds);
    }

    /// <summary>Requests stop and returns without waiting, so it is safe on the UI thread and on the event thread.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            RequestStop();
        }
    }

    private void RequestStop()
    {
        _stop = true;
        // Stop flag first, then wake: mpv latches a wake-up that arrives while the thread is not
        // waiting, so the next mpv_wait_event returns at once and the flag is seen.
        _source.TryWakeup();
    }

    private void Run()
    {
        Thread.CurrentThread.Name ??= ThreadName;
        Volatile.Write(ref _threadId, Environment.CurrentManagedThreadId);
        try
        {
            while (!_stop && !_source.IsClosed)
            {
                MpvEvent* e = _source.Wait(-1);
                if (e == null || e->EventId == (int)MpvEventId.None)
                {
                    continue; // mpv_wakeup (or a spurious return); re-check the flags
                }
                if (_stop || _source.IsClosed)
                {
                    break; // don't hand out events after Dispose returned
                }

                Interlocked.Increment(ref _eventsDispatched);
                try
                {
                    _sink.OnEvent(new MpvEventView(e));
                }
                catch (Exception ex)
                {
                    ReportFault(ex);
                }

                if (e->EventId == (int)MpvEventId.Shutdown)
                {
                    break; // mpv keeps returning Shutdown from here on
                }
            }
        }
        catch (Exception ex)
        {
            ReportFault(ex); // the pump itself failed; nothing to retry against
        }
        finally
        {
            Volatile.Write(ref _threadId, 0);
            _source.Release();
        }
    }

    private void ReportFault(Exception exception)
    {
        try
        {
            _sink.OnFault(exception);
        }
        catch
        {
            // A sink that throws from OnFault has nowhere left to report; keep the thread alive.
        }
    }

    /// <summary>Production source: libmpv on an <see cref="MpvHandle"/>.</summary>
    private sealed class MpvHandleEventSource(MpvHandle handle) : IMpvEventSource
    {
        private nint _raw;

        public bool IsClosed => handle.IsClosed;

        public bool TryAcquire()
        {
            bool added = false;
            try
            {
                handle.DangerousAddRef(ref added);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            _raw = handle.DangerousGetHandle();
            return added;
        }

        public void Release() => handle.DangerousRelease();

        public MpvEvent* Wait(double timeoutSeconds) => MpvApi.WaitEvent(_raw, timeoutSeconds);

        public bool TryWakeup()
        {
            bool added = false;
            try
            {
                handle.DangerousAddRef(ref added);
                MpvApi.Wakeup(handle.DangerousGetHandle());
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false; // closed: MpvContext.Dispose already woke the thread
            }
            finally
            {
                if (added)
                {
                    handle.DangerousRelease();
                }
            }
        }
    }
}
