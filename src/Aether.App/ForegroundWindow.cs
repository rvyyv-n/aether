using System.Runtime.InteropServices;

namespace Aether_App;

/// <summary>Windows only lets the process that has the user's attention move a window to the front.</summary>
internal static partial class ForegroundWindow
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);

    /// <summary>Called by the launching process, which was just started by the user, to let <paramref name="processId"/> take the front.</summary>
    public static void Allow(uint processId) => AllowSetForegroundWindow(processId);

    /// <summary>Best effort: when Windows refuses, the taskbar button flashes instead.</summary>
    public static void Bring(nint hwnd) => SetForegroundWindow(hwnd);
}
