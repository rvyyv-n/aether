using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aether.Core.Interop.Native;

namespace Aether.Core.Interop.Windowing;

/// <summary>
/// Keeps a window's client area at a fixed aspect ratio while the user drags its edges, by
/// subclassing the window procedure and adjusting the rectangle in <c>WM_SIZING</c> (see
/// <see cref="WindowGeometry.ConstrainToAspect"/>), and keeps the client area at least
/// <see cref="MinimumClientSize"/> through <c>WM_GETMINMAXINFO</c>. Programmatic moves and resizes
/// keep the minimum but not the aspect.
/// <para>
/// Threading: create, set <see cref="AspectRatio"/> and dispose on the window's thread; the window
/// procedure runs there too. One lock per process, since the procedure finds its state through a
/// static field (the shell has a single window).
/// </para>
/// </summary>
public sealed unsafe class AspectRatioLock : IDisposable
{
    private static nint s_hwnd;
    private static nint s_previous;
    private static double s_aspect;
    private static double s_minWidthDips;
    private static double s_minHeightDips;

    private readonly nint _hwnd;
    private bool _disposed;

    public AspectRatioLock(nint hwnd)
    {
        if (hwnd == 0)
        {
            throw new ArgumentException("A window handle is required.", nameof(hwnd));
        }
        if (s_hwnd != 0)
        {
            throw new InvalidOperationException("Only one window can be aspect-locked at a time.");
        }

        nint procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProcedure;
        nint previous = User32.SetWindowLongPtr(hwnd, User32.GWLP_WNDPROC, procedure);
        if (previous == 0)
        {
            throw new InvalidOperationException($"SetWindowLongPtr(GWLP_WNDPROC) failed: {Marshal.GetLastPInvokeError()}.");
        }
        _hwnd = hwnd;
        s_hwnd = hwnd;
        s_previous = previous;
        s_aspect = 0;
    }

    /// <summary>Client width / height to keep while dragging; 0 (or any non-positive value) turns the lock off.</summary>
    public double AspectRatio
    {
        get => s_aspect;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            s_aspect = double.IsFinite(value) && value > 0 ? value : 0;
        }
    }

    /// <summary>
    /// Smallest client area the user can drag the window to, in device-independent pixels (scaled by
    /// the window's current DPI each time Windows asks). (0, 0) turns the minimum off.
    /// </summary>
    public (double Width, double Height) MinimumClientSize
    {
        get => (s_minWidthDips, s_minHeightDips);
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            s_minWidthDips = double.IsFinite(value.Width) ? Math.Max(0, value.Width) : 0;
            s_minHeightDips = double.IsFinite(value.Height) ? Math.Max(0, value.Height) : 0;
        }
    }

    /// <summary>Restores the original window procedure, unless something has subclassed the window after us.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        s_aspect = 0;
        s_minWidthDips = s_minHeightDips = 0;
        if (s_hwnd != _hwnd)
        {
            return; // already unhooked by WM_NCDESTROY
        }
        nint ours = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProcedure;
        if (User32.GetWindowLongPtr(_hwnd, User32.GWLP_WNDPROC) == ours)
        {
            User32.SetWindowLongPtr(_hwnd, User32.GWLP_WNDPROC, s_previous);
            s_hwnd = 0;
        }
        // Otherwise leave the chain intact: with s_aspect = 0 our procedure only forwards.
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        nint previous = s_previous;
        nint result = User32.CallWindowProc(previous, hwnd, message, wParam, lParam);

        if (message == User32.WM_SIZING && s_aspect > 0 && lParam != 0)
        {
            PixelRect window, client;
            if (User32.GetWindowRect(hwnd, &window) != 0 && User32.GetClientRect(hwnd, &client) != 0)
            {
                // The frame (invisible resize borders) does not change during a drag.
                (int minWidth, int minHeight) = MinimumClientPixels(hwnd);
                PixelRect* proposed = (PixelRect*)lParam;
                *proposed = WindowGeometry.ConstrainToAspect(
                    *proposed, (SizingEdge)(int)wParam, s_aspect,
                    window.Width - client.Width, window.Height - client.Height, minWidth, minHeight);
                return 1;
            }
        }
        else if (message == User32.WM_GETMINMAXINFO && lParam != 0 && hwnd == s_hwnd)
        {
            (int minWidth, int minHeight) = MinimumClientPixels(hwnd);
            PixelRect window, client;
            if ((minWidth > 0 || minHeight > 0)
                && User32.GetWindowRect(hwnd, &window) != 0 && User32.GetClientRect(hwnd, &client) != 0
                && client.Width > 0 && client.Height > 0) // a minimised window has no client area to measure the frame by
            {
                User32.MinMaxInfo* info = (User32.MinMaxInfo*)lParam;
                info->MinTrackSizeX = Math.Max(info->MinTrackSizeX, minWidth + window.Width - client.Width);
                info->MinTrackSizeY = Math.Max(info->MinTrackSizeY, minHeight + window.Height - client.Height);
                return 0;
            }
        }
        else if (message == User32.WM_NCDESTROY && hwnd == s_hwnd)
        {
            User32.SetWindowLongPtr(hwnd, User32.GWLP_WNDPROC, previous);
            s_hwnd = 0;
            s_aspect = 0;
        }
        return result;
    }

    private static (int Width, int Height) MinimumClientPixels(nint hwnd)
    {
        uint dpi = User32.GetDpiForWindow(hwnd);
        double scale = (dpi == 0 ? 96 : dpi) / 96.0;
        return ((int)Math.Ceiling(s_minWidthDips * scale), (int)Math.Ceiling(s_minHeightDips * scale));
    }
}
