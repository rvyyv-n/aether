namespace Aether.Core.Playback.Windowing;

/// <summary>Keeps a remembered window on a monitor that is still there.</summary>
public static class WindowPlacement
{
    /// <summary>A window with less than this much showing on any monitor is treated as off-screen.</summary>
    public const int MinVisibleWidth = 120;
    public const int MinVisibleHeight = 60;

    /// <summary>
    /// <paramref name="saved"/> moved fully inside the work area it overlaps most, and shrunk if it no longer
    /// fits there. When it overlaps none by <see cref="MinVisibleWidth"/> by <see cref="MinVisibleHeight"/>
    /// (a monitor was unplugged or the layout changed), it is centred on <paramref name="primary"/> instead.
    /// </summary>
    public static WindowRect Fit(WindowRect saved, IReadOnlyList<WindowRect> workAreas, WindowRect primary)
    {
        WindowRect target = primary;
        long best = 0;
        foreach (WindowRect area in workAreas)
        {
            int width = Math.Min(saved.Right, area.Right) - Math.Max(saved.X, area.X);
            int height = Math.Min(saved.Bottom, area.Bottom) - Math.Max(saved.Y, area.Y);
            if (width < MinVisibleWidth || height < MinVisibleHeight)
            {
                continue;
            }
            long overlap = (long)width * height;
            if (overlap > best)
            {
                best = overlap;
                target = area;
            }
        }

        int fittedWidth = Math.Min(saved.Width, target.Width);
        int fittedHeight = Math.Min(saved.Height, target.Height);
        if (best == 0)
        {
            return new WindowRect(
                target.X + (target.Width - fittedWidth) / 2,
                target.Y + (target.Height - fittedHeight) / 2,
                fittedWidth,
                fittedHeight);
        }
        return new WindowRect(
            Math.Clamp(saved.X, target.X, target.Right - fittedWidth),
            Math.Clamp(saved.Y, target.Y, target.Bottom - fittedHeight),
            fittedWidth,
            fittedHeight);
    }
}
