using System;
using QRCoder;

namespace Aether.Server.LanHost;

public static class QrPairingService
{
    public static string CreateSvg(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(8, "#FFFFFF", "#00000000");
    }

    public static byte[] CreatePng(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(8);
    }

    /// <summary>
    /// Black on white with its quiet zone, a whole number of pixels per module so it stays sharp when shown at
    /// one image pixel per screen pixel, and as large as fits in <paramref name="maxPixels"/> (at least one per module).
    /// </summary>
    public static (byte[] Png, int Pixels) CreatePngToFit(string url, int maxPixels)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        int modules = data.ModuleMatrix.Count; // includes the quiet zone
        int pixelsPerModule = Math.Max(1, maxPixels / modules);
        return (new PngByteQRCode(data).GetGraphic(pixelsPerModule), pixelsPerModule * modules);
    }
}
