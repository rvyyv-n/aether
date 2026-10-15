using System.Globalization;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Video;

/// <summary>
/// What the video flyout (screen 9.3) has set for the open file: zoom, rotation, aspect ratio and the four colour
/// sliders. Values are kept in the units the flyout shows (percent, degrees, -100..100) and turned into mpv's
/// properties only at the edge, so the clamping lives in one place.
/// </summary>
/// <param name="ZoomPercent">100 is the video at its own size; mpv's <c>video-zoom</c> is the base-2 logarithm of this over 100.</param>
/// <param name="Rotation">Degrees clockwise: 0, 90, 180 or 270.</param>
/// <param name="Aspect"><see cref="AutoAspect"/> or one of <see cref="AspectOptions"/>.</param>
public sealed record VideoAdjustments(
    int ZoomPercent = VideoAdjustments.DefaultZoom,
    int Rotation = 0,
    string Aspect = VideoAdjustments.AutoAspect,
    int Brightness = 0,
    int Contrast = 0,
    int Gamma = 0,
    int Saturation = 0)
{
    public const int DefaultZoom = 100;
    public const int MinZoom = 25;
    public const int MaxZoom = 400;
    public const int ColourLimit = 100;
    public const string AutoAspect = "auto";

    public static IReadOnlyList<int> Rotations { get; } = [0, 90, 180, 270];

    /// <summary>"Auto" first, then the same ratios as Settings → Video, so both lists agree.</summary>
    public static IReadOnlyList<string> AspectOptions { get; } = [AutoAspect, "16:9", "4:3", "21:9", "1:1", "2.35:1"];

    /// <summary>What a file starts with: no zoom or rotation, and the aspect ratio and colours from Settings → Video.</summary>
    public static VideoAdjustments FromSettings(AetherSettings settings) => new VideoAdjustments(
        Aspect: AspectOptions.Contains(settings.AspectRatio) ? settings.AspectRatio : AutoAspect,
        Brightness: settings.Brightness,
        Contrast: settings.Contrast,
        Gamma: settings.Gamma,
        Saturation: settings.Saturation).Clamped();

    public static int ClampZoom(double percent) => (int)Math.Clamp(Math.Round(percent), MinZoom, MaxZoom);

    public static int ClampColour(double value) => (int)Math.Clamp(Math.Round(value), -ColourLimit, ColourLimit);

    /// <summary>The nearest quarter turn, so 450 or -90 land on 90 and 270 rather than being refused.</summary>
    public static int NormalizeRotation(int degrees)
    {
        int quarter = (int)Math.Round(degrees / 90.0);
        return (quarter % 4 + 4) % 4 * 90;
    }

    /// <summary>The same values pulled inside what the flyout can show and mpv takes.</summary>
    public VideoAdjustments Clamped() => this with
    {
        ZoomPercent = ClampZoom(ZoomPercent),
        Rotation = NormalizeRotation(Rotation),
        Aspect = AspectOptions.Contains(Aspect) ? Aspect : AutoAspect,
        Brightness = ClampColour(Brightness),
        Contrast = ClampColour(Contrast),
        Gamma = ClampColour(Gamma),
        Saturation = ClampColour(Saturation),
    };

    public VideoAdjustments WithColoursOf(VideoAdjustments other) => this with
    {
        Brightness = other.Brightness,
        Contrast = other.Contrast,
        Gamma = other.Gamma,
        Saturation = other.Saturation,
    };

    /// <summary>The mpv properties to set, in a fixed order. Always all of them, so one pass puts the whole state right.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Properties()
    {
        VideoAdjustments v = Clamped();
        return
        [
            new("video-zoom", ZoomForMpv(v.ZoomPercent)),
            new("video-rotate", v.Rotation.ToString(CultureInfo.InvariantCulture)),
            new("video-aspect-override", AspectForMpv(v.Aspect)),
            new("brightness", v.Brightness.ToString(CultureInfo.InvariantCulture)),
            new("contrast", v.Contrast.ToString(CultureInfo.InvariantCulture)),
            new("gamma", v.Gamma.ToString(CultureInfo.InvariantCulture)),
            new("saturation", v.Saturation.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    /// <summary>The size the picture takes on screen: mpv's <c>dwidth</c> and <c>dheight</c> ignore the flyout's rotation, so a quarter turn swaps them.</summary>
    public static (int Width, int Height) DisplaySize(int width, int height, int rotation) =>
        NormalizeRotation(rotation) % 180 == 90 ? (height, width) : (width, height);

    public static string ZoomForMpv(int percent) =>
        Math.Log2(ClampZoom(percent) / 100.0).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>"auto" is <c>-1</c> to mpv, which means the file's own ratio.</summary>
    public static string AspectForMpv(string aspect) => aspect == AutoAspect ? "-1" : aspect;

    public static string FormatZoom(int percent) => string.Create(CultureInfo.CurrentCulture, $"{percent}%");

    /// <summary>A colour value with a sign, "+10", "−25" or "0", for the slider's readout.</summary>
    public static string FormatColour(int value) =>
        value > 0 ? $"+{value}" : value < 0 ? $"−{-value}" : "0";
}
