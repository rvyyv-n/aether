using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aether.Core.Interop.Mpv;

/// <summary>
/// Owns an <c>mpv_handle*</c>. Release runs <c>mpv_terminate_destroy</c>, which stops playback
/// and joins mpv's threads. Render contexts AddRef this handle (<see cref="SafeHandle.DangerousAddRef"/>)
/// so the core is never destroyed while a render context created from it is still alive.
/// </summary>
public sealed class MpvHandle : SafeHandle
{
    private int _shutdownTracked;

    public MpvHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    internal MpvHandle(nint handle) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(handle);

    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>Counts this core in <see cref="MpvCoreShutdown.Shared"/> until it has been destroyed. Once only.</summary>
    internal void TrackShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownTracked, 1) == 0)
        {
            MpvCoreShutdown.Shared.Begin();
        }
    }

    protected override bool ReleaseHandle()
    {
        try
        {
            MpvApi.TerminateDestroy(handle);
        }
        finally
        {
            if (Volatile.Read(ref _shutdownTracked) != 0)
            {
                MpvCoreShutdown.Shared.End();
            }
        }
        return true;
    }
}
