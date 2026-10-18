using System.Runtime.InteropServices;

namespace Aether_App;

/// <summary>
/// Windows 11 draws a 1 px border just inside a window's edge, over the client area. Over video it reads as a grey
/// line between the picture and the edge, so it is turned off while a video is open; the rounded corners stay.
/// </summary>
internal static partial class WindowBorder
{
    private const uint DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, ref uint value, uint size);

    /// <summary>Best effort: before Windows 11 the attribute does not exist and the call does nothing.</summary>
    public static void SetShown(nint hwnd, bool shown)
    {
        uint colour = shown ? DWMWA_COLOR_DEFAULT : DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colour, sizeof(uint));
    }
}
