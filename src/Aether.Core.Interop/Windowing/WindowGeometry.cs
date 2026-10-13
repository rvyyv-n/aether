using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Windowing;

/// <summary>A rectangle in physical pixels, laid out like Win32 <c>RECT</c> so a <c>RECT*</c> can be read as one.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>The edge or corner being dragged, as in <c>WM_SIZING</c>'s <c>WMSZ_*</c> values.</summary>
public enum SizingEdge
{
    Left = 1,
    Right = 2,
    Top = 3,
    TopLeft = 4,
    TopRight = 5,
    Bottom = 6,
    BottomLeft = 7,
    BottomRight = 8,
}

/// <summary>
/// Window geometry for a player window that follows its video: the size to open a video at, and the
/// drag-resize constraint that keeps the client area at the video's aspect ratio. All values are
/// physical pixels. The frame is the part of the window rectangle outside the client area (on
/// Windows 11, mostly the invisible resize borders).
/// </summary>
public static class WindowGeometry
{
    /// <summary>
    /// The client size for a video of <paramref name="videoWidth"/> × <paramref name="videoHeight"/>:
    /// one video pixel per screen pixel, scaled down to fit <paramref name="maxFraction"/> of the work
    /// area, and scaled up to at least <paramref name="minClientWidth"/> when that still fits.
    /// </summary>
    public static (int Width, int Height) FitClient(
        int videoWidth, int videoHeight, PixelRect workArea, int frameWidth, int frameHeight,
        double maxFraction, int minClientWidth)
    {
        if (videoWidth <= 0 || videoHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(videoWidth), "The video size must be positive.");
        }
        double maxWidth = Math.Max(1, workArea.Width * maxFraction - frameWidth);
        double maxHeight = Math.Max(1, workArea.Height * maxFraction - frameHeight);
        double fitScale = Math.Min(maxWidth / videoWidth, maxHeight / videoHeight);

        double scale = Math.Min(1, fitScale);
        if (videoWidth * scale < minClientWidth)
        {
            scale = Math.Min((double)minClientWidth / videoWidth, fitScale);
        }
        return (Math.Max(1, (int)Math.Round(videoWidth * scale)), Math.Max(1, (int)Math.Round(videoHeight * scale)));
    }

    /// <summary>
    /// Where to put a window of the given outer size: centred on <paramref name="current"/>'s centre,
    /// then moved inside <paramref name="workArea"/> (its top-left corner wins when it does not fit).
    /// </summary>
    public static (int X, int Y) Recentre(PixelRect current, int windowWidth, int windowHeight, PixelRect workArea)
    {
        int x = (current.Left + current.Right) / 2 - windowWidth / 2;
        int y = (current.Top + current.Bottom) / 2 - windowHeight / 2;
        x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - windowWidth));
        y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - windowHeight));
        return (x, y);
    }

    /// <summary>
    /// Adjusts a proposed window rectangle during a drag so its client area has <paramref name="aspect"/>
    /// (width / height). Side drags keep the dragged dimension and move the far side of the other
    /// one; corner drags follow whichever dimension the pointer asks to make larger and keep the
    /// opposite corner fixed. The client area then grows, still at the aspect, until it is at least
    /// <paramref name="minClientWidth"/> × <paramref name="minClientHeight"/>. Returns
    /// <paramref name="proposed"/> unchanged when there is nothing to keep.
    /// </summary>
    public static PixelRect ConstrainToAspect(
        PixelRect proposed, SizingEdge edge, double aspect, int frameWidth, int frameHeight,
        int minClientWidth = 0, int minClientHeight = 0)
    {
        int clientWidth = proposed.Width - frameWidth;
        int clientHeight = proposed.Height - frameHeight;
        if (!double.IsFinite(aspect) || aspect <= 0 || clientWidth <= 0 || clientHeight <= 0)
        {
            return proposed;
        }

        bool widthDrives = edge switch
        {
            SizingEdge.Left or SizingEdge.Right => true,
            SizingEdge.Top or SizingEdge.Bottom => false,
            _ => clientWidth / aspect >= clientHeight,
        };
        if (widthDrives)
        {
            clientHeight = Math.Max(1, (int)Math.Round(clientWidth / aspect));
        }
        else
        {
            clientWidth = Math.Max(1, (int)Math.Round(clientHeight * aspect));
        }

        // Growing to the minimum width can only make the height larger, so the height check comes second.
        if (clientWidth < minClientWidth)
        {
            clientWidth = minClientWidth;
            clientHeight = Math.Max(1, (int)Math.Round(clientWidth / aspect));
        }
        if (clientHeight < minClientHeight)
        {
            clientHeight = minClientHeight;
            clientWidth = Math.Max(1, (int)Math.Round(clientHeight * aspect));
        }

        int width = clientWidth + frameWidth;
        int height = clientHeight + frameHeight;
        int left = proposed.Left, top = proposed.Top, right = proposed.Right, bottom = proposed.Bottom;
        if (edge is SizingEdge.Left or SizingEdge.TopLeft or SizingEdge.BottomLeft)
        {
            left = right - width;
        }
        else
        {
            right = left + width;
        }
        if (edge is SizingEdge.Top or SizingEdge.TopLeft or SizingEdge.TopRight)
        {
            top = bottom - height;
        }
        else
        {
            bottom = top + height;
        }
        return new PixelRect(left, top, right, bottom);
    }
}
