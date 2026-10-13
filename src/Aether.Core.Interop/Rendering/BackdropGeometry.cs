using System;

namespace Aether.Core.Interop.Rendering;

/// <summary>
/// The player card's rectangle in swap-chain pixels (top-left origin, relative to the video
/// surface), with the surface size it was measured against, so the render thread can rescale it
/// when the swap chain has already moved on to a newer size.
/// </summary>
/// <param name="Radius">Corner radius in pixels.</param>
/// <param name="Scale">Composition scale: pixels per DIP, for the shadow and the blur margin.</param>
public readonly record struct BackdropRegion(
    float X, float Y, float Width, float Height, float Radius, float Scale, uint SurfaceWidth, uint SurfaceHeight)
{
    public bool IsEmpty =>
        !(float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Radius) && float.IsFinite(Scale)
          && float.IsFinite(Width) && float.IsFinite(Height) && Width >= 1 && Height >= 1 && Scale > 0);
}

/// <summary>What to draw this frame: where, and how strongly (0..1).</summary>
public readonly record struct BackdropFrame(BackdropRegion Region, float Opacity);

/// <summary>
/// <see cref="BackdropRegion"/> mapped onto the current back buffer, in GL's bottom-left pixel
/// space, plus the rectangle copied for blurring.
/// </summary>
public readonly record struct BackdropLayout(
    float CardX0, float CardY0, float CardX1, float CardY1, float Radius, float Scale,
    int CopyX, int CopyY, int CopyWidth, int CopyHeight);

public static class BackdropGeometry
{
    /// <summary>Design values for the card's shadow: offset 0,8, blur 24, black 32%.</summary>
    public const float ShadowOffsetDips = 8f;

    public const float ShadowBlurDips = 24f;

    public const float ShadowOpacity = 0.32f;

    /// <summary>Video around the card that the blur may pull in, so its edge doesn't clamp.</summary>
    public const float CopyMarginDips = 24f;

    /// <summary>
    /// Maps the card onto a back buffer of <paramref name="bufferWidth"/> × <paramref name="bufferHeight"/>
    /// pixels. No Y flip: the back buffer is a client-buffer pbuffer, so GL row 0 is the top row
    /// (see <see cref="Angle.AngleBridge.RenderFlipY"/>). False when there is nothing to draw.
    /// </summary>
    public static bool TryLayout(in BackdropRegion region, uint bufferWidth, uint bufferHeight, out BackdropLayout layout)
    {
        layout = default;
        if (region.IsEmpty || bufferWidth == 0 || bufferHeight == 0)
        {
            return false;
        }

        float sx = region.SurfaceWidth == 0 ? 1f : (float)bufferWidth / region.SurfaceWidth;
        float sy = region.SurfaceHeight == 0 ? 1f : (float)bufferHeight / region.SurfaceHeight;
        float uniform = (sx + sy) * 0.5f;

        float x0 = region.X * sx;
        float x1 = x0 + (region.Width * sx);
        float y0 = region.Y * sy;
        float y1 = y0 + (region.Height * sy);

        float scale = region.Scale * uniform;
        float margin = MathF.Ceiling(CopyMarginDips * scale);
        int copyX0 = (int)Math.Max(0, MathF.Floor(x0 - margin));
        int copyY0 = (int)Math.Max(0, MathF.Floor(y0 - margin));
        int copyX1 = (int)Math.Min(bufferWidth, MathF.Ceiling(x1 + margin));
        int copyY1 = (int)Math.Min(bufferHeight, MathF.Ceiling(y1 + margin));
        if (copyX1 - copyX0 < 1 || copyY1 - copyY0 < 1)
        {
            return false;
        }

        layout = new BackdropLayout(x0, y0, x1, y1, region.Radius * uniform, scale, copyX0, copyY0, copyX1 - copyX0, copyY1 - copyY0);
        return true;
    }

    /// <summary>
    /// Opacity part-way through a fade, on the ease-out curve of the XAML card's own fade. A zero
    /// duration is instant.
    /// </summary>
    public static float FadeOpacity(float from, float to, double elapsedMilliseconds, double durationMilliseconds)
    {
        if (!(durationMilliseconds > 0) || elapsedMilliseconds >= durationMilliseconds)
        {
            return to;
        }
        if (elapsedMilliseconds <= 0)
        {
            return from;
        }
        return from + ((to - from) * (float)EaseOut(elapsedMilliseconds / durationMilliseconds));
    }

    // Same control points as ChromeFader in the app (0.1, 0.9, 0.2, 1.0).
    private const double X1 = 0.1, Y1 = 0.9, X2 = 0.2, Y2 = 1.0;

    /// <summary>The chrome's cubic-bezier ease-out at time fraction <paramref name="t"/> (0..1).</summary>
    public static double EaseOut(double t)
    {
        if (t <= 0)
        {
            return 0;
        }
        if (t >= 1)
        {
            return 1;
        }
        // x(s) rises monotonically on 0..1 for these control points, so bisection finds s for x = t.
        double low = 0, high = 1, s = t;
        for (int i = 0; i < 24; i++)
        {
            s = (low + high) / 2;
            if (Bezier(s, X1, X2) < t)
            {
                low = s;
            }
            else
            {
                high = s;
            }
        }
        return Bezier(s, Y1, Y2);
    }

    private static double Bezier(double s, double p1, double p2)
    {
        double u = 1 - s;
        return (3 * u * u * s * p1) + (3 * u * s * s * p2) + (s * s * s);
    }
}
