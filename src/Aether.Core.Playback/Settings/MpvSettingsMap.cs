using System.Globalization;

namespace Aether.Core.Playback.Settings;

/// <summary>What to change on a running core: mpv properties in order, and whether the volume-normalising filter is on.</summary>
public sealed record MpvRuntimePlan(IReadOnlyList<KeyValuePair<string, string>> Properties, bool NormaliseVolume);

/// <summary>
/// Turns an <see cref="AetherSettings"/> snapshot into mpv options. Pure, so it is tested without libmpv.
/// Hardware decoding is not here: it is the engine's own <c>HardwareDecoder</c> option.
/// </summary>
public static class MpvSettingsMap
{
    /// <summary>Label of the filter we add, so removing it leaves any other audio filter alone.</summary>
    public const string NormaliseFilterLabel = "aether-norm";

    private const string NormaliseFilter = "dynaudnorm";

    /// <summary>
    /// Options that only count before the core starts: passthrough, then the custom options (which come last, so they
    /// win over everything Aether sets). Lines the validator rejects are skipped, so a reserved key never gets through.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> StartOptions(AetherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var options = new List<KeyValuePair<string, string>>();
        if (settings.Passthrough)
        {
            options.Add(new("audio-spdif", "ac3,eac3,dts-hd,truehd"));
        }
        options.AddRange(CustomOptions(settings.CustomMpvOptions));
        return options;
    }

    /// <summary>
    /// Whether to start the core with <c>video-latency-hacks</c>. The hacks stop mpv decoding a frame ahead; a GPU
    /// decoder keeps up regardless, but a software decoder then delivers every slow frame late and playback stutters.
    /// </summary>
    public static bool VideoLatencyHacks(AetherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.HardwareDecoding != "no";
    }

    /// <summary>The valid <c>key=value</c> lines of <paramref name="text"/>, in order, without a leading <c>--</c>.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> CustomOptions(string? text)
    {
        var options = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(text))
        {
            return options;
        }

        var rejected = new HashSet<int>();
        foreach (MpvOptionError error in MpvOptionsValidator.Validate(text))
        {
            rejected.Add(error.Line);
        }

        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#' || rejected.Contains(i + 1))
            {
                continue;
            }
            int eq = line.IndexOf('=');
            string key = line[..eq].Trim();
            if (key.StartsWith("--", StringComparison.Ordinal))
            {
                key = key[2..];
            }
            options.Add(new(key, line[(eq + 1)..].Trim()));
        }
        return options;
    }

    /// <summary>
    /// The preset in force. Battery saver acts as <c>efficient</c> while the PC runs on battery.
    /// </summary>
    public static string EffectivePreset(AetherSettings settings, bool onBattery)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.BatterySaver && onBattery ? "efficient" : settings.QualityPreset;
    }

    /// <summary>
    /// Properties that can change on a running core. <c>balanced</c> uses the individual scaler, debanding and smooth
    /// motion settings; <c>efficient</c> and <c>high</c> replace them with fixed cheap or best-quality choices. Keys the
    /// user set in the custom options are left out, so those keep winning.
    /// </summary>
    public static MpvRuntimePlan RuntimePlan(AetherSettings settings, bool onBattery)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string preset = EffectivePreset(settings, onBattery);
        (string scale, string dscale, bool deband, bool smooth) = preset switch
        {
            "efficient" => ("bilinear", "bilinear", false, false),
            "high" => ("ewa_lanczos", "lanczos", true, settings.SmoothMotion),
            _ => (settings.Upscaling, settings.Downscaling, settings.Debanding, settings.SmoothMotion),
        };

        var properties = new List<KeyValuePair<string, string>>
        {
            new("scale", scale),
            new("dscale", dscale),
            new("deband", YesNo(deband)),
            new("video-sync", smooth ? "display-resample" : "audio"),
            new("interpolation", YesNo(smooth)),
            new("tone-mapping", settings.HdrToneMapping),
            new("brightness", Number(settings.Brightness)),
            new("contrast", Number(settings.Contrast)),
            new("gamma", Number(settings.Gamma)),
            new("saturation", Number(settings.Saturation)),
            new("hr-seek", settings.PreciseSeeking ? "yes" : "default"),
            new("audio-device", settings.AudioOutputDevice.Length == 0 ? "auto" : settings.AudioOutputDevice),
            new("audio-exclusive", YesNo(settings.ExclusiveMode)),
            new("sub-visibility", YesNo(settings.ShowSubtitles)),
            new("alang", settings.PreferredAudioLanguage),
            new("slang", settings.PreferredSubtitleLanguage),
            new("sub-font", settings.SubtitleFont),
            new("sub-font-size", SubtitleFontSize(settings.SubtitleSize)),
            new("ytdl-format", YtdlFormat(settings.StreamingQuality, settings.PreferGpuDecodableFormats)),
        };

        var custom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> option in CustomOptions(settings.CustomMpvOptions))
        {
            custom.Add(option.Key);
        }
        if (custom.Count > 0)
        {
            properties.RemoveAll(p => custom.Contains(p.Key));
        }
        return new MpvRuntimePlan(properties, settings.VolumeNormalisation && !custom.Contains("af"));
    }

    /// <summary>Text size in mpv's scaled pixels (it is 55 at a 720-pixel-high window).</summary>
    public static string SubtitleFontSize(string size) => size switch
    {
        "small" => "40",
        "large" => "75",
        _ => "55",
    };

    /// <summary>
    /// yt-dlp format selector: best video plus audio up to the chosen height, falling back to a single combined stream.
    /// With <paramref name="avoidAv1"/>, AV1 is tried last because many GPUs cannot decode it.
    /// </summary>
    public static string YtdlFormat(string quality, bool avoidAv1)
    {
        string height = quality.EndsWith('p') && int.TryParse(quality.AsSpan(0, quality.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int h)
            ? $"[height<=?{h.ToString(CultureInfo.InvariantCulture)}]"
            : "";
        string video = $"bv*{height}";
        string combined = $"b{height}";
        return avoidAv1
            ? $"{video}[vcodec!^=av01]+ba/{video}+ba/{combined}"
            : $"{video}+ba/{combined}";
    }

    /// <summary>The commands that switch the volume-normalising filter. Removing first keeps repeat adds from failing.</summary>
    public static IReadOnlyList<string[]> NormaliseCommands(bool on) => on
        ? [["af", "remove", "@" + NormaliseFilterLabel], ["af", "add", $"@{NormaliseFilterLabel}:{NormaliseFilter}"]]
        : [["af", "remove", "@" + NormaliseFilterLabel]];

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
