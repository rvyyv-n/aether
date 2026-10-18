using System.Globalization;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Video;

public enum DenoiseLevel
{
    Off,
    Light,
    Medium,
    Strong,
}

/// <summary>
/// The Look under the video flyout's colour sliders (screen 9.3): a colour grade, sharpen, film grain and denoise. One
/// recipe serves playback (mpv's <c>lavfi</c> filter) and, later, the Precise clip export (ffmpeg's <c>-vf</c>), so what
/// is seen is what is saved. Unlike <see cref="VideoAdjustments"/> it is kept across files and saved as last used.
/// </summary>
/// <param name="Grade"><see cref="NoGrade"/>, a built-in id from <see cref="LookLibrary.BuiltIn"/>, or <c>user:</c> and a file name.</param>
/// <param name="Sharpen">0 (off) to 100.</param>
/// <param name="Grain">0 (off) to 100.</param>
public sealed record VideoLook(
    string Grade = VideoLook.NoGrade,
    int Sharpen = 0,
    int Grain = 0,
    DenoiseLevel Denoise = DenoiseLevel.Off)
{
    public const string NoGrade = "none";
    public const int MaxAmount = 100;

    private const string CopyBackDecoder = "d3d11va-copy";

    public static VideoLook Off { get; } = new();

    public static VideoLook FromSettings(AetherSettings settings) => new VideoLook(
        settings.LookGrade,
        settings.LookSharpen,
        settings.LookGrain,
        DenoiseFromName(settings.LookDenoise)).Clamped();

    public static int ClampAmount(double value) => (int)Math.Clamp(Math.Round(value), 0, MaxAmount);

    public static string DenoiseName(DenoiseLevel level) => level switch
    {
        DenoiseLevel.Light => "light",
        DenoiseLevel.Medium => "medium",
        DenoiseLevel.Strong => "strong",
        _ => "off",
    };

    public static DenoiseLevel DenoiseFromName(string? name) => name switch
    {
        "light" => DenoiseLevel.Light,
        "medium" => DenoiseLevel.Medium,
        "strong" => DenoiseLevel.Strong,
        _ => DenoiseLevel.Off,
    };

    public VideoLook Clamped() => this with
    {
        Grade = string.IsNullOrWhiteSpace(Grade) ? NoGrade : Grade,
        Sharpen = ClampAmount(Sharpen),
        Grain = ClampAmount(Grain),
        Denoise = Enum.IsDefined(Denoise) ? Denoise : DenoiseLevel.Off,
    };

    /// <summary>True when nothing is set, so playback and export are left exactly as they were.</summary>
    public bool IsOff => Clamped() is { Grade: NoGrade, Sharpen: 0, Grain: 0, Denoise: DenoiseLevel.Off };

    /// <summary>
    /// The lavfi chain in a fixed order: denoise, grade, sharpen, grain. Off parts are left out, so an all-off look gives
    /// an empty string. A grade whose <paramref name="lutPath"/> is null (file gone) is left out too.
    /// </summary>
    public string FilterChain(string? lutPath)
    {
        VideoLook look = Clamped();
        List<string> parts = [];
        if (DenoiseParameters(look.Denoise) is { } denoise)
        {
            parts.Add($"hqdn3d={denoise}");
        }
        if (look.Grade != NoGrade && !string.IsNullOrEmpty(lutPath))
        {
            parts.Add($"lut3d=file={QuoteFilterPath(lutPath)}");
        }
        if (look.Sharpen > 0)
        {
            parts.Add($"cas=strength={Number(look.Sharpen / 100.0)}");
        }
        if (look.Grain > 0)
        {
            parts.Add($"noise=alls={Number(look.Grain * 0.3)}:allf=t");
        }
        return string.Join(",", parts);
    }

    /// <summary>The value for mpv's <c>vf</c> property: the chain inside <c>lavfi</c>, or empty to clear the filters.</summary>
    public string MpvFilter(string? lutPath) =>
        FilterChain(lutPath) is { Length: > 0 } chain ? $"lavfi=[{chain}]" : "";

    /// <summary>The same chain as ffmpeg's <c>-vf</c> argument; null when the look is off. The export adds its <c>eq</c> colour filter to this.</summary>
    public string? FfmpegFilter(string? lutPath) =>
        FilterChain(lutPath) is { Length: > 0 } chain ? chain : null;

    /// <summary>
    /// The decoder to ask mpv for. The filters run on the CPU, so while any is in use a hardware decoder must hand its
    /// frames back to memory; with no filters the user's own choice is restored. "no" and the copy form stay as chosen.
    /// </summary>
    public static string HardwareDecoder(string configured, bool filtersActive) =>
        filtersActive && configured is not ("no" or CopyBackDecoder) ? CopyBackDecoder : configured;

    /// <summary>
    /// lavfi wants backslashes as slashes, the drive colon escaped and the path in single quotes. A quote inside is closed,
    /// escaped twice and reopened: the filter graph and then the option are each unquoted, and a single escape loses the quote.
    /// </summary>
    internal static string QuoteFilterPath(string path)
    {
        string slashed = path.Replace('\\', '/');
        if (slashed.Length >= 2 && slashed[1] == ':')
        {
            slashed = string.Concat(slashed.AsSpan(0, 1), "\\:", slashed.AsSpan(2));
        }
        return "'" + slashed.Replace("'", "'\\\\\\''") + "'";
    }

    /// <summary>
    /// The chain for a Precise clip: this look, then the colour sliders as an <c>eq</c> filter. Null when both are at rest,
    /// so the export is left as it was. The look runs first, as in playback, where mpv adds the sliders after its filters.
    /// </summary>
    public string? ExportFilter(string? lutPath, VideoAdjustments colours)
    {
        string look = FilterChain(lutPath);
        string? eq = colours.FfmpegColourFilter();
        return look.Length == 0 ? eq : eq is null ? look : $"{look},{eq}";
    }

    private static string? DenoiseParameters(DenoiseLevel level) => level switch
    {
        DenoiseLevel.Light => "2:1.5:3:2.25",
        DenoiseLevel.Medium => "4:3:6:4.5",
        DenoiseLevel.Strong => "8:6:12:9",
        _ => null,
    };

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
