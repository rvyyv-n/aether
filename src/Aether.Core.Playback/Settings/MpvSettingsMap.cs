using System.Globalization;

namespace Aether.Core.Playback.Settings;

/// <summary>What to change on a running core: mpv properties in order, whether the volume-normalising filter is on, and the deinterlacing filter.</summary>
/// <param name="DeinterlaceFilter">Software filter to add for the chosen deinterlacer (<c>yadif</c> or <c>bwdif</c>), or null for none.</param>
public sealed record MpvRuntimePlan(IReadOnlyList<KeyValuePair<string, string>> Properties, bool NormaliseVolume, string? DeinterlaceFilter = null);

/// <summary>
/// Turns an <see cref="AetherSettings"/> snapshot into mpv options. Pure, so it is tested without libmpv.
/// Hardware decoding is not here: it is the engine's own <c>HardwareDecoder</c> option.
/// </summary>
public static class MpvSettingsMap
{
    /// <summary>Label of the filter we add, so removing it leaves any other audio filter alone.</summary>
    public const string NormaliseFilterLabel = "aether-norm";

    /// <summary>Label of the deinterlacing filter we add.</summary>
    public const string DeinterlaceFilterLabel = "aether-deint";

    private const string NormaliseFilter = "dynaudnorm";

    /// <summary>
    /// Options that only count before the core starts: passthrough, the network cache size, AVI index repair and the
    /// deblocking skip, then the custom options (which come last, so they win over everything Aether sets). Lines the
    /// validator rejects are skipped, so a reserved key never gets through.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> StartOptions(AetherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var options = new List<KeyValuePair<string, string>>();
        if (settings.Passthrough)
        {
            options.Add(new("audio-spdif", "ac3,eac3,dts-hd,truehd"));
        }
        if (settings.NetworkCacheMegabytes != new AetherSettings().NetworkCacheMegabytes)
        {
            options.Add(new("demuxer-max-bytes", $"{settings.NetworkCacheMegabytes.ToString(CultureInfo.InvariantCulture)}MiB"));
        }
        if (settings.RepairAviIndex)
        {
            options.Add(new("index", "recreate"));
        }
        if (settings.SkipDeblocking != "default")
        {
            options.Add(new("vd-lavc-skiploopfilter", settings.SkipDeblocking));
        }
        options.AddRange(CustomOptions(settings.CustomMpvOptions));
        return options;
    }

    /// <summary>
    /// <c>demuxer-max-back-bytes</c> for the engine: half the network cache, between 8 and 512 MiB. mpv raises the
    /// cache to at least the back buffer, so a fixed 512 MiB would override any cache setting below that.
    /// </summary>
    public static string BackBufferSize(AetherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return $"{Math.Clamp(settings.NetworkCacheMegabytes / 2, 8, 512).ToString(CultureInfo.InvariantCulture)}MiB";
    }

    /// <summary>
    /// Whether to start the core with <c>video-latency-hacks</c>. The hacks stop mpv decoding a frame ahead; a GPU
    /// decoder keeps up regardless, but a software decoder then delivers every slow frame late and playback stutters.
    /// </summary>
    public static bool VideoLatencyHacks(AetherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Review finding R2: restrict video-latency-hacks to explicit d3d11va; under auto-safe or
        // software decoding, disabling the 1-frame readahead queue starves the decoder and causes dropped frames.
        return settings.HardwareDecoding == "d3d11va";
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
            new("sub-color", SubtitleColor(settings.SubtitleColor)),
            new("sub-pos", SubtitlePosition(settings.SubtitlePosition)),
            new("sub-bold", YesNo(settings.SubtitleBold)),
            new("sub-border-size", SubtitleOutlineSize(settings.SubtitleOutline)),
            new("sub-border-color", SubtitleOutlineColor(settings.SubtitleOutlineColor)),
            new("sub-border-style", settings.SubtitleBackground == "none" ? "outline-and-shadow" : "background-box"),
            new("sub-back-color", SubtitleBackColor(settings.SubtitleBackground)),
            // Without this, a subtitle file that carries its own styling keeps its own font, colour and size.
            new("sub-ass-override", settings.SubtitleOverrideStyled ? "force" : "scale"),
            new("ytdl-format", YtdlFormat(settings.StreamingQuality, settings.PreferGpuDecodableFormats)),
            new("deinterlace", YesNo(settings.Deinterlace && settings.DeinterlaceMode == "auto")),
            new("video-aspect-override", settings.AspectRatio == "auto" ? "-1" : settings.AspectRatio),
            new("screenshot-directory", settings.ScreenshotDirectory),
            new("screenshot-format", settings.ScreenshotFormat),
            new("screenshot-template", settings.ScreenshotTemplate),
            new("audio-pitch-correction", YesNo(settings.PitchCorrection)),
            new("audio-channels", settings.StereoDownmix ? "stereo" : "auto-safe"),
            new("http-proxy", settings.HttpProxy),
            new("rtsp-transport", settings.RtspTransport),
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
        string? deinterlacer = settings.Deinterlace && settings.DeinterlaceMode != "auto" && !custom.Contains("vf") ? settings.DeinterlaceMode : null;
        return new MpvRuntimePlan(properties, settings.VolumeNormalisation && !custom.Contains("af"), deinterlacer);
    }

    /// <summary>Text size in mpv's scaled pixels (it is 55 at a 720-pixel-high window).</summary>
    public static string SubtitleFontSize(string size) => size switch
    {
        "small" => "40",
        "large" => "75",
        "xlarge" => "95",
        _ => "55",
    };

    /// <summary>Outline width in the same scaled pixels; mpv's own is 3.</summary>
    public static string SubtitleOutlineSize(string outline) => outline switch
    {
        "none" => "0",
        "thin" => "1.5",
        "thick" => "5",
        _ => "3",
    };

    public static string SubtitleOutlineColor(string color) => color switch
    {
        "gray" => "#404040",
        "white" => "#FFFFFF",
        _ => "#000000",
    };

    /// <summary>Colour of the box behind the text (mpv reads <c>#AARRGGBB</c>); fully clear when there is no box.</summary>
    public static string SubtitleBackColor(string background) => background switch
    {
        "soft" => "#99000000",
        "solid" => "#FF000000",
        _ => "#00000000",
    };

    public static string SubtitleColor(string color) => color switch
    {
        "yellow" => "#FFFF00",
        "green" => "#00FF00",
        "cyan" => "#00FFFF",
        _ => "#FFFFFF",
    };

    /// <summary>Percent of the picture height from the top, where the last line sits (mpv's default is 100, the bottom edge).</summary>
    public static string SubtitlePosition(string position) => position switch
    {
        "raised" => "90",
        "top" => "10",
        _ => "100",
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

    /// <summary>The commands that switch the deinterlacing filter; <paramref name="filter"/> null removes it.</summary>
    public static IReadOnlyList<string[]> DeinterlaceCommands(string? filter) => filter is null
        ? [["vf", "remove", "@" + DeinterlaceFilterLabel]]
        : [["vf", "remove", "@" + DeinterlaceFilterLabel], ["vf", "add", $"@{DeinterlaceFilterLabel}:{filter}"]];

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
