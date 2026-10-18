namespace Aether.Core.Playback.Windowing;

/// <summary>
/// Where the picture-in-picture window opens (8.1). The first time: the area of a 400×225 picture at the video's
/// aspect (exactly 400×225 for 16:9; a portrait video gets a tall window of the same area rather than a sliver),
/// in the bottom-right corner of the work area. After that: the remembered window, given the new video's aspect at
/// the same area and centre, kept on a connected monitor. All rectangles are outer window bounds in screen pixels;
/// the frame is the window's invisible resize border, half of <c>frameWidth</c> each side and all of
/// <c>frameHeight</c> at the bottom (the title bar is drawn by the app).
/// </summary>
public static class PipPlacement
{
    public const double DefaultWidth = 400;
    public const double DefaultHeight = 225;

    /// <summary>Smallest picture the user can drag it to (DIPs): the 32 px Back and 46 px Close still fit the top strip.</summary>
    public const double MinWidth = 256;
    public const double MinHeight = 144;

    /// <summary>Gap between the picture and the work area's edges on first open (DIPs).</summary>
    public const double EdgeMargin = 16;

    private const double DefaultAspect = DefaultWidth / DefaultHeight;

    /// <summary>Picture size in DIPs at <paramref name="aspect"/> (width / height) with the area of 400×225, never below the minimum.</summary>
    public static (double Width, double Height) PictureSize(double aspect) =>
        PictureSize(aspect, DefaultWidth * DefaultHeight);

    private static (double Width, double Height) PictureSize(double aspect, double area)
    {
        if (!double.IsFinite(aspect) || aspect <= 0)
        {
            aspect = DefaultAspect;
        }
        double width = Math.Sqrt(area * aspect);
        double height = width / aspect;
        // Grow, keeping the aspect, until both sides clear the minimum.
        double grow = Math.Max(1, Math.Max(MinWidth / width, MinHeight / height));
        return (width * grow, height * grow);
    }

    /// <summary>First open: the default picture in the bottom-right of <paramref name="workArea"/>, <see cref="EdgeMargin"/> in.</summary>
    public static WindowRect Initial(WindowRect workArea, double aspect, double scale, int frameWidth, int frameHeight)
    {
        scale = Scale(scale);
        (double w, double h) = PictureSize(aspect);
        int width = (int)Math.Round(w * scale) + frameWidth;
        int height = (int)Math.Round(h * scale) + frameHeight;
        int margin = (int)Math.Round(EdgeMargin * scale);
        // The visible edges sit inside the invisible border: frameWidth / 2 on the right, frameHeight at the bottom.
        int x = workArea.Right - margin - width + frameWidth / 2;
        int y = workArea.Bottom - margin - height + frameHeight;
        WindowRect placed = new(x, y, width, height);
        return WindowPlacement.Fit(placed, [workArea], workArea);
    }

    /// <summary>
    /// The remembered window with the new video's aspect, at the same picture area and centre, refitted onto a
    /// connected monitor (or centred on <paramref name="primary"/> when its monitor is gone).
    /// </summary>
    public static WindowRect Remembered(
        WindowRect saved, double aspect, double scale, int frameWidth, int frameHeight,
        IReadOnlyList<WindowRect> workAreas, WindowRect primary)
    {
        scale = Scale(scale);
        double savedWidth = Math.Max(1, saved.Width - frameWidth) / scale;
        double savedHeight = Math.Max(1, saved.Height - frameHeight) / scale;
        (double w, double h) = PictureSize(aspect, savedWidth * savedHeight);
        int width = (int)Math.Round(w * scale) + frameWidth;
        int height = (int)Math.Round(h * scale) + frameHeight;
        int centreX = saved.X + saved.Width / 2;
        int centreY = saved.Y + saved.Height / 2;
        return WindowPlacement.Fit(new WindowRect(centreX - width / 2, centreY - height / 2, width, height), workAreas, primary);
    }

    private static double Scale(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1;
}
