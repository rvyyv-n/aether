namespace Aether.Core.Playback.Diagnostics;

/// <summary>Pixel-level diagnostics for a captured BGRA8 frame, used to detect solid/blank captures.</summary>
public readonly record struct VisualImageStats(
    int Width,
    int Height,
    double MeanLuma,
    double NonBlackRatio,
    int DistinctSampledColors,
    bool IsLikelyBlank);

/// <summary>
/// Pure software BGRA8 compositor that combines a D3D11 back-buffer plane with premultiplied-alpha
/// WinUI <c>RenderTargetBitmap</c> layers (main shell and open popups).
/// </summary>
public static class BgraCompositor
{
    /// <summary>Allocates an opaque BGRA8 canvas filled with the given sRGB colour.</summary>
    public static byte[] CreateCanvas(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        byte[] buffer = new byte[checked(width * height * 4)];
        for (int i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = b;
            buffer[i + 1] = g;
            buffer[i + 2] = r;
            buffer[i + 3] = a;
        }
        return buffer;
    }

    /// <summary>
    /// Copies an opaque BGRA8 source plane into <paramref name="dst"/> at the target rectangle,
    /// scaling cleanly with nearest-pixel resampling if the source and target sizes differ slightly.
    /// </summary>
    public static void BlitOpaque(
        byte[] dst,
        int dstWidth,
        int dstHeight,
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        int offsetX,
        int offsetY,
        int targetWidth,
        int targetHeight)
    {
        if (dstWidth <= 0 || dstHeight <= 0 || srcWidth <= 0 || srcHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            return;
        }
        if (dst.Length < dstWidth * dstHeight * 4 || src.Length < srcWidth * srcHeight * 4)
        {
            return;
        }

        int startX = Math.Max(0, offsetX);
        int startY = Math.Max(0, offsetY);
        int endX = Math.Min(dstWidth, offsetX + targetWidth);
        int endY = Math.Min(dstHeight, offsetY + targetHeight);
        if (startX >= endX || startY >= endY)
        {
            return;
        }

        if (srcWidth == targetWidth && srcHeight == targetHeight)
        {
            int copyBytes = (endX - startX) * 4;
            int srcOffsetX = startX - offsetX;
            for (int y = startY; y < endY; y++)
            {
                int srcY = y - offsetY;
                int srcRow = (srcY * srcWidth + srcOffsetX) * 4;
                int dstRow = (y * dstWidth + startX) * 4;
                src.Slice(srcRow, copyBytes).CopyTo(dst.AsSpan(dstRow, copyBytes));
                for (int i = 3; i < copyBytes; i += 4)
                {
                    dst[dstRow + i] = 0xFF;
                }
            }
            return;
        }

        for (int y = startY; y < endY; y++)
        {
            int srcY = Math.Clamp((int)((long)(y - offsetY) * srcHeight / targetHeight), 0, srcHeight - 1);
            int dstRowStart = y * dstWidth * 4;
            int srcRowStart = srcY * srcWidth * 4;
            for (int x = startX; x < endX; x++)
            {
                int srcX = Math.Clamp((int)((long)(x - offsetX) * srcWidth / targetWidth), 0, srcWidth - 1);
                int s = srcRowStart + srcX * 4;
                int d = dstRowStart + x * 4;
                dst[d] = src[s];
                dst[d + 1] = src[s + 1];
                dst[d + 2] = src[s + 2];
                dst[d + 3] = 0xFF;
            }
        }
    }

    /// <summary>
    /// Porter-Duff Source-Over blend of a premultiplied-alpha BGRA8 layer (<c>RenderTargetBitmap</c>)
    /// onto <paramref name="dst"/> at <c>(offsetX, offsetY)</c>.
    /// </summary>
    public static void BlendPremultipliedOver(
        byte[] dst,
        int dstWidth,
        int dstHeight,
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        int offsetX,
        int offsetY)
    {
        if (dstWidth <= 0 || dstHeight <= 0 || srcWidth <= 0 || srcHeight <= 0)
        {
            return;
        }
        if (dst.Length < dstWidth * dstHeight * 4 || src.Length < srcWidth * srcHeight * 4)
        {
            return;
        }

        int startX = Math.Max(0, offsetX);
        int startY = Math.Max(0, offsetY);
        int endX = Math.Min(dstWidth, offsetX + srcWidth);
        int endY = Math.Min(dstHeight, offsetY + srcHeight);
        if (startX >= endX || startY >= endY)
        {
            return;
        }

        for (int y = startY; y < endY; y++)
        {
            int srcY = y - offsetY;
            int dstRow = y * dstWidth * 4;
            int srcRow = srcY * srcWidth * 4;
            for (int x = startX; x < endX; x++)
            {
                int srcX = x - offsetX;
                int s = srcRow + srcX * 4;
                byte sa = src[s + 3];
                if (sa == 0)
                {
                    continue;
                }

                int d = dstRow + x * 4;
                if (sa == 255)
                {
                    dst[d] = src[s];
                    dst[d + 1] = src[s + 1];
                    dst[d + 2] = src[s + 2];
                    dst[d + 3] = 255;
                    continue;
                }

                int invA = 255 - sa;
                dst[d] = ClampByte(src[s] + ((dst[d] * invA + 127) / 255));
                dst[d + 1] = ClampByte(src[s + 1] + ((dst[d + 1] * invA + 127) / 255));
                dst[d + 2] = ClampByte(src[s + 2] + ((dst[d + 2] * invA + 127) / 255));
                dst[d + 3] = ClampByte(sa + ((dst[d + 3] * invA + 127) / 255));
            }
        }
    }

    /// <summary>Samples a BGRA8 image to report luminance, non-black coverage, and color variety.</summary>
    public static VisualImageStats AnalyzePixels(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return new VisualImageStats(width, height, 0, 0, 0, true);
        }

        int stepX = Math.Max(1, width / 64);
        int stepY = Math.Max(1, height / 64);
        long samples = 0;
        long nonBlack = 0;
        double lumaSum = 0;
        var colors = new HashSet<int>();

        for (int y = 0; y < height; y += stepY)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x += stepX)
            {
                int i = row + x * 4;
                byte b = bgra[i];
                byte g = bgra[i + 1];
                byte r = bgra[i + 2];
                double luma = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
                lumaSum += luma;
                if (r > 4 || g > 4 || b > 4)
                {
                    nonBlack++;
                }
                // Quantize slightly (6 bits per channel) so anti-aliased noise doesn't mask flat captures.
                int key = ((r >> 2) << 12) | ((g >> 2) << 6) | (b >> 2);
                colors.Add(key);
                samples++;
            }
        }

        if (samples == 0)
        {
            return new VisualImageStats(width, height, 0, 0, 0, true);
        }

        double meanLuma = lumaSum / samples;
        double nonBlackRatio = (double)nonBlack / samples;
        bool likelyBlank = colors.Count <= 2 || nonBlackRatio < 0.005;
        return new VisualImageStats(width, height, Math.Round(meanLuma, 2), Math.Round(nonBlackRatio, 4), colors.Count, likelyBlank);
    }

    private static byte ClampByte(int value) => (byte)(value > 255 ? 255 : value);
}
