using System.Runtime.InteropServices;

namespace Aether_App;

/// <summary>
/// Windows 11 draws a 1 px border just inside a window's edge, over the client area. Over video it reads as a grey
/// line between the picture and the edge, so it is turned off while a video is open; the rounded corners stay.
/// </summary>
internal static partial class WindowBorder
{
    private const uint DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
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

    /// <summary>
    /// Windows paints the 1 px resize frame along the top edge light unless the window is in dark mode, which shows
    /// as a white line over video in the light theme. Best effort, like <see cref="SetShown"/>.
    /// </summary>
    public static void SetDarkFrame(nint hwnd, bool dark)
    {
        uint value = dark ? 1u : 0u;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(uint));
    }
}
