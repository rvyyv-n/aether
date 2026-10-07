using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Mpv;

/// <summary>
/// Owns an <c>mpv_handle*</c>. Release runs <c>mpv_terminate_destroy</c>, which stops playback
/// and joins mpv's threads. Render contexts AddRef this handle (<see cref="SafeHandle.DangerousAddRef"/>)
/// so the core is never destroyed while a render context created from it is still alive.
/// </summary>
public sealed class MpvHandle : SafeHandle
{
    public MpvHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    internal MpvHandle(nint handle) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(handle);

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        MpvApi.TerminateDestroy(handle);
        return true;
    }
}
