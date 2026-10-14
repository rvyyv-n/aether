using Aether.Core.Interop.Native;

namespace Aether.Core.Interop.Windowing;

/// <summary>Reads the refresh rate of the monitor a window is on. Any thread; no allocation.</summary>
public static unsafe class DisplayRefresh
{
    /// <summary>The monitor the window is mostly on, or 0 when Windows reports none.</summary>
    public static nint MonitorFor(nint hwnd) =>
        hwnd == 0 ? 0 : User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);

    /// <summary>
    /// The monitor's current refresh rate in whole hertz, or 0 when it can't be read. Windows reports 0 or 1 for
    /// "hardware default", which callers should treat as unknown too.
    /// </summary>
    public static int QueryHertz(nint monitor)
    {
        if (monitor == 0)
        {
            return 0;
        }

        User32.MonitorInfoEx info = default;
        info.Size = (uint)sizeof(User32.MonitorInfoEx);
        if (User32.GetMonitorInfo(monitor, &info) == 0)
        {
            return 0;
        }

        User32.DevMode mode = default;
        mode.Size = (ushort)sizeof(User32.DevMode);
        // The device name is the null-terminated buffer inside info (e.g. \\.\DISPLAY1).
        if (User32.EnumDisplaySettings(info.DeviceName, User32.ENUM_CURRENT_SETTINGS, &mode) == 0)
        {
            return 0;
        }
        return (int)Math.Min(mode.DisplayFrequency, int.MaxValue);
    }
}
