using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Native;

internal static unsafe partial class Gdi32
{
    public const int TRANSPARENT = 1;
    public const uint ANTIALIASED_QUALITY = 4;
    public const uint DEFAULT_CHARSET = 1;
    public const int FW_NORMAL = 400;

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial int DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial int DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint hdc, BitmapInfoHeader* info, uint usage, byte** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte* bits);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFont(
        int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut,
        uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "TextOutW")]
    public static partial int TextOut(nint hdc, int x, int y, char* text, int length);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W")]
    public static partial int GetTextExtentPoint32(nint hdc, char* text, int length, Size* size);

    [StructLayout(LayoutKind.Sequential)]
    public struct Size
    {
        public int Width, Height;
    }

    /// <summary>Win32 <c>BITMAPINFOHEADER</c>; a 32-bit image needs no colour table, so it stands in for <c>BITMAPINFO</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }
}
