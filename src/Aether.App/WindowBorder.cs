using System.Runtime.InteropServices;

namespace Aether_App;

/// <summary>
/// Windows 11 draws a 1 px border just inside a window's edge, over the client area. Over video it reads as a grey
/// line between the picture and the edge, so it is painted black while a video is on screen; the rounded corners stay.
/// </summary>
internal static partial class WindowBorder
{
    private const uint DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;
    private const uint Black = 0x00000000;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, ref uint value, uint size);

    /// <summary>Best effort: before Windows 11 the attribute does not exist and the call does nothing.</summary>
    /// <remarks>Hiding the border outright still leaves a pale line along the top edge in either theme, so it is painted black.
    /// Over Settings and the home screen the normal border comes back, because black there reads as a frame.</remarks>
    public static void SetShown(nint hwnd, bool shown)
    {
        uint colour = shown ? DWMWA_COLOR_DEFAULT : Black;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colour, sizeof(uint));
    }
}
