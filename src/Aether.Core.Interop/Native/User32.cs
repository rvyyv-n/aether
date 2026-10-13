using System.Runtime.InteropServices;
using Aether.Core.Interop.Windowing;

namespace Aether.Core.Interop.Native;

internal static unsafe partial class User32
{
    public const int GWLP_WNDPROC = -4;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint WM_SIZING = 0x0214;

    /// <summary>Adjusts the calling thread's cursor display counter; returns the new count (shown while >= 0).</summary>
    [LibraryImport("user32.dll")]
    public static partial int ShowCursor(int show);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowRect(nint hwnd, PixelRect* rect);

    [LibraryImport("user32.dll")]
    public static partial int GetClientRect(nint hwnd, PixelRect* rect);

    /// <summary>The window's DPI (96 = 100% scale).</summary>
    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    public static partial nint CallWindowProc(nint previous, nint hwnd, uint message, nint wParam, nint lParam);

    // 64-bit user32 exports the *Ptr functions; on 32-bit they are macros over the *Long ones.
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr64(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong32(nint hwnd, int index, int value);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr64(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong32(nint hwnd, int index);

    public static nint SetWindowLongPtr(nint hwnd, int index, nint value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : SetWindowLong32(hwnd, index, (int)value);

    public static nint GetWindowLongPtr(nint hwnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);

    /// <summary>Win32 <c>MINMAXINFO</c>; only the minimum tracking size is used.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MinMaxInfo
    {
        public int ReservedX, ReservedY;
        public int MaxSizeX, MaxSizeY;
        public int MaxPositionX, MaxPositionY;
        public int MinTrackSizeX, MinTrackSizeY;
        public int MaxTrackSizeX, MaxTrackSizeY;
    }
}
