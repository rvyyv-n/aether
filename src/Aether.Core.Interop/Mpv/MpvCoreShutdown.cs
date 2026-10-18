using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aether.Core.Interop.Mpv;

/// <summary>
/// Cores whose destruction has started. <c>mpv_terminate_destroy</c> joins mpv's threads and can take seconds (a
/// stalled network read, an audio device that is slow to close), so <see cref="MpvContext.Dispose"/> never runs it on
/// the caller's thread, and app exit waits here, bounded, so the process does not end with mpv half torn down.
/// </summary>
internal sealed class MpvCoreShutdown
{
    public const string ThreadName = "Aether.MpvDestroy";

    private readonly object _gate = new();
    private int _pending;

    public static MpvCoreShutdown Shared { get; } = new();

    public int Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    public void Begin()
    {
        lock (_gate)
        {
            _pending++;
        }
    }

    public void End()
    {
        lock (_gate)
        {
            _pending--;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Waits until every destruction that has begun has finished. False on timeout.</summary>
    public bool WaitForAll(int timeoutMilliseconds)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        lock (_gate)
        {
            while (_pending > 0)
            {
                long left = deadline - Environment.TickCount64;
                if (left <= 0)
                {
                    return false;
                }
                Monitor.Wait(_gate, (int)left);
            }
            return true;
        }
    }

    /// <summary>
    /// Drops one reference taken with <see cref="SafeHandle.DangerousAddRef"/> on a background thread, so if it is
    /// the last one the handle is released there. A background thread does not keep the process alive past
    /// <see cref="WaitForAll"/>.
    /// </summary>
    public void ReleaseOnWorker(SafeHandle handle)
    {
        var thread = new Thread(static state => ((SafeHandle)state!).DangerousRelease())
        {
            IsBackground = true,
            Name = ThreadName,
        };
        try
        {
            thread.Start(handle);
        }
        catch (Exception)
        {
            // No thread to be had: blocking the caller beats leaking the core.
            handle.DangerousRelease();
        }
    }
}
