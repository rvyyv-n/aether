using System.Runtime.InteropServices;
using Aether.Core.Interop.Windowing;

namespace Aether.Core.Interop.Native;

internal static unsafe partial class User32
{
    public const int GWLP_WNDPROC = -4;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint WM_SIZING = 0x0214;
    public const uint WM_DISPLAYCHANGE = 0x007E;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint ENUM_CURRENT_SETTINGS = 0xFFFFFFFF;

    /// <summary>The monitor the window is mostly on; the nearest one when it is on none (minimised, off-screen).</summary>
    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    public static partial int GetMonitorInfo(nint monitor, MonitorInfoEx* info);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW")]
    public static partial int EnumDisplaySettings(char* deviceName, uint modeNumber, DevMode* mode);

    public const uint QDC_ONLY_ACTIVE_PATHS = 2;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;

    [LibraryImport("user32.dll")]
    public static partial int GetDisplayConfigBufferSizes(uint flags, uint* pathCount, uint* modeCount);

    [LibraryImport("user32.dll")]
    public static partial int QueryDisplayConfig(uint flags, uint* pathCount, DisplayConfigPathInfo* paths, uint* modeCount, DisplayConfigModeInfo* modes, nint currentTopologyId);

    [LibraryImport("user32.dll")]
    public static partial int DisplayConfigGetDeviceInfo(DisplayConfigSourceDeviceName* request);

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

    /// <summary>Win32 <c>MONITORINFOEXW</c>; only the device name is used.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfoEx
    {
        public uint Size;
        public PixelRect Monitor, Work;
        public uint Flags;
        public fixed char DeviceName[32];
    }

    /// <summary>Win32 <c>DEVMODEW</c> (220 bytes) in its display form; only the size and the refresh rate are used.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    public struct DevMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(184)] public uint DisplayFrequency;
    }

    /// <summary>Win32 <c>DISPLAYCONFIG_PATH_INFO</c> (72 bytes); only the source id and the target's refresh rate are read.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct DisplayConfigPathInfo
    {
        [FieldOffset(0)] public long SourceAdapterId;
        [FieldOffset(8)] public uint SourceId;
        [FieldOffset(48)] public uint RefreshNumerator;
        [FieldOffset(52)] public uint RefreshDenominator;
    }

    /// <summary>Win32 <c>DISPLAYCONFIG_MODE_INFO</c> (64 bytes); only a buffer for <c>QueryDisplayConfig</c>.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DisplayConfigModeInfo
    {
    }

    /// <summary>Win32 <c>DISPLAYCONFIG_SOURCE_DEVICE_NAME</c> (84 bytes): the GDI name (\.\DISPLAY1) of a path's source.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 84)]
    public struct DisplayConfigSourceDeviceName
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Size;
        [FieldOffset(8)] public long AdapterId;
        [FieldOffset(16)] public uint Id;
        [FieldOffset(20)] public fixed char GdiDeviceName[32];
    }

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
