using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Aether.Core.Interop.Native;

internal static unsafe partial class Kernel32
{
    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_TIMEOUT = 0x102;
    public const uint WAIT_FAILED = 0xFFFFFFFF;
    public const uint INFINITE = 0xFFFFFFFF;

    /// <summary>Unnamed event; auto-reset unless <paramref name="manualReset"/>.</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
    public static partial SafeWaitHandle CreateEvent(nint attributes, int manualReset, int initialState, char* name);

    /// <summary>Raw handle on purpose: called from mpv's update callback, which must not touch managed objects.</summary>
    [LibraryImport("kernel32.dll")]
    public static partial int SetEvent(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForMultipleObjectsEx(uint count, nint* handles, int waitAll, uint milliseconds, int alertable);

    public static SafeWaitHandle CreateAutoResetEvent()
    {
        SafeWaitHandle handle = CreateEvent(0, 0, 0, null);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new System.ComponentModel.Win32Exception(error);
        }
        return handle;
    }
}
