using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aether.Core.Interop.DirectX.Native;

/// <summary>
/// Raw IUnknown helpers. DirectX objects are held as bare COM pointers and
/// called through their vtables, so the hot path never touches an RCW or
/// allocates. Every pointer obtained here carries one reference that the
/// caller must release.
/// </summary>
internal static unsafe class Com
{
    public static void** Vtbl(nint p) => *(void***)p;

    public static int QueryInterface(nint p, Guid iid, out nint result)
    {
        nint r = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Vtbl(p)[0])(p, &iid, &r);
        result = hr >= 0 ? r : 0;
        return hr;
    }

    public static uint AddRef(nint p) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(p)[1])(p);

    public static uint Release(nint p) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(p)[2])(p);

    /// <summary>Releases and clears <paramref name="p"/>; safe to call twice.</summary>
    public static void Release(ref nint p)
    {
        nint v = Interlocked.Exchange(ref p, 0);
        if (v != 0)
        {
            Release(v);
        }
    }

    public static void ThrowIfFailed(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }
}
