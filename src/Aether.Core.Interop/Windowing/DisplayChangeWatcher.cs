using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aether.Core.Interop.Native;

namespace Aether.Core.Interop.Windowing;

/// <summary>
/// Raises <see cref="DisplayChanged"/> when Windows sends <c>WM_DISPLAYCHANGE</c> (resolution, refresh
/// rate or monitor layout changed), by subclassing the window procedure.
/// <para>
/// Threading: create and dispose on the window's thread; the event is raised there too. It chains to
/// whatever procedure was installed before it (the window may also carry an <see cref="AspectRatioLock"/>), and
/// like it keeps its state in static fields, so one watcher per process.
/// </para>
/// </summary>
public sealed unsafe class DisplayChangeWatcher : IDisposable
{
    private static nint s_hwnd;
    private static nint s_previous;
    private static Action? s_callback;

    private readonly nint _hwnd;
    private bool _disposed;

    public DisplayChangeWatcher(nint hwnd, Action displayChanged)
    {
        ArgumentNullException.ThrowIfNull(displayChanged);
        if (hwnd == 0)
        {
            throw new ArgumentException("A window handle is required.", nameof(hwnd));
        }
        if (s_hwnd != 0)
        {
            throw new InvalidOperationException("Only one window can be watched for display changes at a time.");
        }

        nint procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProcedure;
        nint previous = User32.SetWindowLongPtr(hwnd, User32.GWLP_WNDPROC, procedure);
        if (previous == 0)
        {
            throw new InvalidOperationException($"SetWindowLongPtr(GWLP_WNDPROC) failed: {Marshal.GetLastPInvokeError()}.");
        }
        _hwnd = hwnd;
        s_callback = displayChanged;
        s_previous = previous;
        s_hwnd = hwnd;
    }

    /// <summary>Restores the original window procedure, unless something has subclassed the window after us.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        s_callback = null;
        if (s_hwnd != _hwnd)
        {
            return; // the window is already gone
        }
        nint ours = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProcedure;
        if (User32.GetWindowLongPtr(_hwnd, User32.GWLP_WNDPROC) == ours)
        {
            User32.SetWindowLongPtr(_hwnd, User32.GWLP_WNDPROC, s_previous);
            s_hwnd = 0;
        }
        // Otherwise leave the chain intact: with no callback our procedure only forwards.
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        nint result = User32.CallWindowProc(s_previous, hwnd, message, wParam, lParam);

        if (message == User32.WM_DISPLAYCHANGE && hwnd == s_hwnd)
        {
            try
            {
                s_callback?.Invoke();
            }
            catch
            {
                // An exception must not unwind into native code.
            }
        }
        else if (message == User32.WM_NCDESTROY && hwnd == s_hwnd)
        {
            // The window is going away, and a procedure beneath us may already have unhooked itself.
            s_callback = null;
            s_hwnd = 0;
        }
        return result;
    }
}
