using System.Text.Json;
using Aether.Core.Playback.Input;

namespace Aether.Core.Playback.Settings;

/// <summary>Persisted keys of <see cref="AetherSettings"/>.</summary>
public static class SettingKeys
{
    public const string BatterySaver = "general.batterySaver";
    public const string OpenInRunningWindow = "general.openInRunningWindow";
    public const string PreciseSeeking = "playback.preciseSeeking";
    public const string ResumePlayback = "playback.resume";
    public const string LoopByDefault = "playback.loop";
    public const string PlayNextInFolder = "playback.playNext";
    public const string RememberWindowSize = "playback.rememberWindowSize";
    public const string AlwaysOnTop = "playback.alwaysOnTop";
    public const string PreventSleep = "playback.preventSleep";
    public const string QualityPreset = "video.qualityPreset";
    public const string HardwareDecoding = "video.hwdec";
    public const string SmoothMotion = "video.smoothMotion";
    public const string Upscaling = "video.upscaling";
    public const string Downscaling = "video.downscaling";
    public const string Debanding = "video.debanding";
    public const string HdrToneMapping = "video.hdrToneMapping";
    public const string Brightness = "video.brightness";
    public const string Contrast = "video.contrast";
    public const string Gamma = "video.gamma";
    public const string Saturation = "video.saturation";
    public const string Deinterlace = "video.deinterlace";
    public const string DeinterlaceMode = "video.deinterlaceMode";
    public const string AspectRatio = "video.aspectRatio";
    public const string ScreenshotDirectory = "video.screenshotDirectory";
    public const string ScreenshotFormat = "video.screenshotFormat";
    public const string ScreenshotTemplate = "video.screenshotTemplate";
    public const string AudioOutputDevice = "audio.outputDevice";
    public const string ExclusiveMode = "audio.exclusive";
    public const string Passthrough = "audio.passthrough";
    public const string VolumeNormalisation = "audio.normalise";
    public const string PitchCorrection = "audio.pitchCorrection";
    public const string StereoDownmix = "audio.stereoDownmix";
    public const string ShowSubtitles = "subtitles.show";
    public const string PreferredAudioLanguage = "subtitles.audioLanguage";
    public const string PreferredSubtitleLanguage = "subtitles.language";
    public const string SubtitleFont = "subtitles.font";
    public const string SubtitleSize = "subtitles.size";
    public const string SubtitleColor = "subtitles.color";
    public const string SubtitlePosition = "subtitles.position";
    public const string StreamingQuality = "streaming.quality";
    public const string PreferGpuDecodableFormats = "streaming.preferGpuDecodable";
    public const string YtDlpAutoUpdate = "streaming.ytdlpAutoUpdate";
    public const string NetworkCacheMegabytes = "streaming.cacheMegabytes";
    public const string HttpProxy = "streaming.httpProxy";
    public const string RtspTransport = "streaming.rtspTransport";
    public const string RepairAviIndex = "advanced.repairAviIndex";
    public const string SkipDeblocking = "advanced.skipDeblocking";
    public const string CustomMpvOptions = "advanced.customMpvOptions";
    public const string MotionStyle = "advanced.motionStyle";
    public const string ShortcutOpenFile = "shortcuts.openFile";
    public const string ShortcutOpenUrl = "shortcuts.openUrl";
    public const string ShortcutSkipBack = "shortcuts.skipBack";
    public const string ShortcutSkipForward = "shortcuts.skipForward";
    public const string ShortcutSetIn = "shortcuts.setIn";
    public const string ShortcutSetOut = "shortcuts.setOut";
    public const string ShortcutPlayPause = "shortcuts.playPause";
    public const string ShortcutFullScreen = "shortcuts.fullScreen";
    public const string ShortcutMute = "shortcuts.mute";
    public const string ShortcutLoop = "shortcuts.loop";
    public const string ShortcutAlwaysOnTop = "shortcuts.alwaysOnTop";
    public const string ShortcutDiagnostics = "shortcuts.diagnostics";
    public const string ShortcutSettings = "shortcuts.settings";
    public const string ShortcutCopyFrame = "shortcuts.copyFrame";
    public const string ShortcutSaveFrame = "shortcuts.saveFrame";
}

/// <summary>One setting: key, restart flag, validation and (de)serialisation. No reflection, so it is AOT-safe.</summary>
internal abstract class SettingDefinition
{
    public required string Key { get; init; }
    public bool RequiresRestart { get; init; }
    public abstract object GetValue(AetherSettings settings);
    /// <summary>Returns the settings with this value applied, or null when the value is the wrong type or not allowed.</summary>
    public abstract AetherSettings? TryWith(AetherSettings settings, object? value);
    public abstract AetherSettings WithDefault(AetherSettings settings);
    public abstract void Write(Utf8JsonWriter writer, AetherSettings settings);
    public abstract AetherSettings? TryRead(AetherSettings settings, JsonElement element);
}

internal sealed class SettingDefinition<T> : SettingDefinition where T : notnull
{
    public required Func<AetherSettings, T> Get { get; init; }
    public required Func<AetherSettings, T, AetherSettings> With { get; init; }
    public Func<T, bool>? IsValid { get; init; }

    public override object GetValue(AetherSettings settings) => Get(settings);

    public override AetherSettings? TryWith(AetherSettings settings, object? value) =>
        value is T typed && (IsValid?.Invoke(typed) ?? true) ? With(settings, typed) : null;

    public override AetherSettings WithDefault(AetherSettings settings) => With(settings, Get(SettingDefinitions.Defaults));

    public override void Write(Utf8JsonWriter writer, AetherSettings settings)
    {
        switch (Get(settings))
        {
            case bool b: writer.WriteBoolean(Key, b); break;
            case int i: writer.WriteNumber(Key, i); break;
            case string s: writer.WriteString(Key, s); break;
            default: throw new InvalidOperationException($"Unsupported setting type {typeof(T)}.");
        }
    }

    public override AetherSettings? TryRead(AetherSettings settings, JsonElement element)
    {
        object? value = null;
        if (typeof(T) == typeof(bool) && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
        }
        else if (typeof(T) == typeof(int) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int i))
        {
            value = i;
        }
        else if (typeof(T) == typeof(string) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
        }
        return TryWith(settings, value);
    }
}

/// <summary>The table of every setting. Order is the order written to disk.</summary>
public static class SettingDefinitions
{
    internal static readonly AetherSettings Defaults = new();

    private const int MaxText = 8192;

    private static SettingDefinition<bool> Flag(string key, Func<AetherSettings, bool> get, Func<AetherSettings, bool, AetherSettings> with, bool restart = false) =>
        new() { Key = key, Get = get, With = with, RequiresRestart = restart };

    private static SettingDefinition<string> Choice(string key, Func<AetherSettings, string> get, Func<AetherSettings, string, AetherSettings> with, bool restart, params string[] allowed) =>
        new() { Key = key, Get = get, With = with, RequiresRestart = restart, IsValid = v => Array.IndexOf(allowed, v) >= 0 };

    private static SettingDefinition<string> Text(string key, Func<AetherSettings, string> get, Func<AetherSettings, string, AetherSettings> with, bool restart = false) =>
        new() { Key = key, Get = get, With = with, RequiresRestart = restart, IsValid = v => v.Length <= MaxText };

    private static SettingDefinition<string> Shortcut(string key, Func<AetherSettings, string> get, Func<AetherSettings, string, AetherSettings> with) =>
        new() { Key = key, Get = get, With = with, IsValid = v => v.Length <= MaxText && ShortcutMap.IsValidText(v) };

    private static SettingDefinition<int> Adjust(string key, Func<AetherSettings, int> get, Func<AetherSettings, int, AetherSettings> with) =>
        new() { Key = key, Get = get, With = with, IsValid = v => v is >= -100 and <= 100 };

    private static SettingDefinition<int> Range(string key, Func<AetherSettings, int> get, Func<AetherSettings, int, AetherSettings> with, int min, int max, bool restart = false) =>
        new() { Key = key, Get = get, With = with, RequiresRestart = restart, IsValid = v => v >= min && v <= max };

    internal static readonly IReadOnlyList<SettingDefinition> All =
    [
        Flag(SettingKeys.BatterySaver, s => s.BatterySaver, (s, v) => s with { BatterySaver = v }),
        Flag(SettingKeys.OpenInRunningWindow, s => s.OpenInRunningWindow, (s, v) => s with { OpenInRunningWindow = v }),

        Flag(SettingKeys.PreciseSeeking, s => s.PreciseSeeking, (s, v) => s with { PreciseSeeking = v }),
        Flag(SettingKeys.ResumePlayback, s => s.ResumePlayback, (s, v) => s with { ResumePlayback = v }),
        Flag(SettingKeys.LoopByDefault, s => s.LoopByDefault, (s, v) => s with { LoopByDefault = v }),
        Flag(SettingKeys.PlayNextInFolder, s => s.PlayNextInFolder, (s, v) => s with { PlayNextInFolder = v }),
        Flag(SettingKeys.RememberWindowSize, s => s.RememberWindowSize, (s, v) => s with { RememberWindowSize = v }),
        Flag(SettingKeys.AlwaysOnTop, s => s.AlwaysOnTop, (s, v) => s with { AlwaysOnTop = v }),
        Flag(SettingKeys.PreventSleep, s => s.PreventSleep, (s, v) => s with { PreventSleep = v }),

        Choice(SettingKeys.QualityPreset, s => s.QualityPreset, (s, v) => s with { QualityPreset = v }, false, "efficient", "balanced", "high"),
        Choice(SettingKeys.HardwareDecoding, s => s.HardwareDecoding, (s, v) => s with { HardwareDecoding = v }, true, "auto-safe", "no", "d3d11va", "d3d11va-copy"),
        Flag(SettingKeys.SmoothMotion, s => s.SmoothMotion, (s, v) => s with { SmoothMotion = v }),
        Choice(SettingKeys.Upscaling, s => s.Upscaling, (s, v) => s with { Upscaling = v }, false, "bilinear", "spline36", "lanczos", "ewa_lanczos"),
        Choice(SettingKeys.Downscaling, s => s.Downscaling, (s, v) => s with { Downscaling = v }, false, "bilinear", "hermite", "mitchell", "lanczos"),
        Flag(SettingKeys.Debanding, s => s.Debanding, (s, v) => s with { Debanding = v }),
        Choice(SettingKeys.HdrToneMapping, s => s.HdrToneMapping, (s, v) => s with { HdrToneMapping = v }, false, "auto", "bt.2390", "reinhard", "hable", "clip"),
        Adjust(SettingKeys.Brightness, s => s.Brightness, (s, v) => s with { Brightness = v }),
        Adjust(SettingKeys.Contrast, s => s.Contrast, (s, v) => s with { Contrast = v }),
        Adjust(SettingKeys.Gamma, s => s.Gamma, (s, v) => s with { Gamma = v }),
        Adjust(SettingKeys.Saturation, s => s.Saturation, (s, v) => s with { Saturation = v }),
        Flag(SettingKeys.Deinterlace, s => s.Deinterlace, (s, v) => s with { Deinterlace = v }),
        Choice(SettingKeys.DeinterlaceMode, s => s.DeinterlaceMode, (s, v) => s with { DeinterlaceMode = v }, false, "auto", "yadif", "bwdif"),
        Choice(SettingKeys.AspectRatio, s => s.AspectRatio, (s, v) => s with { AspectRatio = v }, false, "auto", "16:9", "4:3", "21:9", "1:1", "2.35:1"),
        Text(SettingKeys.ScreenshotDirectory, s => s.ScreenshotDirectory, (s, v) => s with { ScreenshotDirectory = v }),
        Choice(SettingKeys.ScreenshotFormat, s => s.ScreenshotFormat, (s, v) => s with { ScreenshotFormat = v }, false, "png", "jpg", "webp"),
        Text(SettingKeys.ScreenshotTemplate, s => s.ScreenshotTemplate, (s, v) => s with { ScreenshotTemplate = v }),

        Text(SettingKeys.AudioOutputDevice, s => s.AudioOutputDevice, (s, v) => s with { AudioOutputDevice = v }),
        Flag(SettingKeys.ExclusiveMode, s => s.ExclusiveMode, (s, v) => s with { ExclusiveMode = v }),
        Flag(SettingKeys.Passthrough, s => s.Passthrough, (s, v) => s with { Passthrough = v }, restart: true),
        Flag(SettingKeys.VolumeNormalisation, s => s.VolumeNormalisation, (s, v) => s with { VolumeNormalisation = v }),
        Flag(SettingKeys.PitchCorrection, s => s.PitchCorrection, (s, v) => s with { PitchCorrection = v }),
        Flag(SettingKeys.StereoDownmix, s => s.StereoDownmix, (s, v) => s with { StereoDownmix = v }),

        Flag(SettingKeys.ShowSubtitles, s => s.ShowSubtitles, (s, v) => s with { ShowSubtitles = v }),
        Text(SettingKeys.PreferredAudioLanguage, s => s.PreferredAudioLanguage, (s, v) => s with { PreferredAudioLanguage = v }),
        Text(SettingKeys.PreferredSubtitleLanguage, s => s.PreferredSubtitleLanguage, (s, v) => s with { PreferredSubtitleLanguage = v }),
        Text(SettingKeys.SubtitleFont, s => s.SubtitleFont, (s, v) => s with { SubtitleFont = v }),
        Choice(SettingKeys.SubtitleSize, s => s.SubtitleSize, (s, v) => s with { SubtitleSize = v }, false, "small", "medium", "large"),
        Choice(SettingKeys.SubtitleColor, s => s.SubtitleColor, (s, v) => s with { SubtitleColor = v }, false, "white", "yellow", "green", "cyan"),
        Choice(SettingKeys.SubtitlePosition, s => s.SubtitlePosition, (s, v) => s with { SubtitlePosition = v }, false, "bottom", "raised", "top"),

        Choice(SettingKeys.StreamingQuality, s => s.StreamingQuality, (s, v) => s with { StreamingQuality = v }, false, "480p", "720p", "1080p", "1440p", "2160p", "best"),
        Flag(SettingKeys.PreferGpuDecodableFormats, s => s.PreferGpuDecodableFormats, (s, v) => s with { PreferGpuDecodableFormats = v }),
        Flag(SettingKeys.YtDlpAutoUpdate, s => s.YtDlpAutoUpdate, (s, v) => s with { YtDlpAutoUpdate = v }),
        Range(SettingKeys.NetworkCacheMegabytes, s => s.NetworkCacheMegabytes, (s, v) => s with { NetworkCacheMegabytes = v }, 16, 2048, restart: true),
        Text(SettingKeys.HttpProxy, s => s.HttpProxy, (s, v) => s with { HttpProxy = v }),
        Choice(SettingKeys.RtspTransport, s => s.RtspTransport, (s, v) => s with { RtspTransport = v }, false, "tcp", "udp", "http", "udp_multicast"),

        Flag(SettingKeys.RepairAviIndex, s => s.RepairAviIndex, (s, v) => s with { RepairAviIndex = v }, restart: true),
        Choice(SettingKeys.SkipDeblocking, s => s.SkipDeblocking, (s, v) => s with { SkipDeblocking = v }, true, "default", "nonref", "nonkey", "all"),
        Text(SettingKeys.CustomMpvOptions, s => s.CustomMpvOptions, (s, v) => s with { CustomMpvOptions = v }, restart: true),
        Choice(SettingKeys.MotionStyle, s => s.MotionStyle, (s, v) => s with { MotionStyle = v }, false, "quick", "springy"),

        Shortcut(SettingKeys.ShortcutOpenFile, s => s.ShortcutOpenFile, (s, v) => s with { ShortcutOpenFile = v }),
        Shortcut(SettingKeys.ShortcutOpenUrl, s => s.ShortcutOpenUrl, (s, v) => s with { ShortcutOpenUrl = v }),
        Shortcut(SettingKeys.ShortcutSkipBack, s => s.ShortcutSkipBack, (s, v) => s with { ShortcutSkipBack = v }),
        Shortcut(SettingKeys.ShortcutSkipForward, s => s.ShortcutSkipForward, (s, v) => s with { ShortcutSkipForward = v }),
        Shortcut(SettingKeys.ShortcutSetIn, s => s.ShortcutSetIn, (s, v) => s with { ShortcutSetIn = v }),
        Shortcut(SettingKeys.ShortcutSetOut, s => s.ShortcutSetOut, (s, v) => s with { ShortcutSetOut = v }),
        Shortcut(SettingKeys.ShortcutPlayPause, s => s.ShortcutPlayPause, (s, v) => s with { ShortcutPlayPause = v }),
        Shortcut(SettingKeys.ShortcutFullScreen, s => s.ShortcutFullScreen, (s, v) => s with { ShortcutFullScreen = v }),
        Shortcut(SettingKeys.ShortcutMute, s => s.ShortcutMute, (s, v) => s with { ShortcutMute = v }),
        Shortcut(SettingKeys.ShortcutLoop, s => s.ShortcutLoop, (s, v) => s with { ShortcutLoop = v }),
        Shortcut(SettingKeys.ShortcutAlwaysOnTop, s => s.ShortcutAlwaysOnTop, (s, v) => s with { ShortcutAlwaysOnTop = v }),
        Shortcut(SettingKeys.ShortcutDiagnostics, s => s.ShortcutDiagnostics, (s, v) => s with { ShortcutDiagnostics = v }),
        Shortcut(SettingKeys.ShortcutSettings, s => s.ShortcutSettings, (s, v) => s with { ShortcutSettings = v }),
        Shortcut(SettingKeys.ShortcutCopyFrame, s => s.ShortcutCopyFrame, (s, v) => s with { ShortcutCopyFrame = v }),
        Shortcut(SettingKeys.ShortcutSaveFrame, s => s.ShortcutSaveFrame, (s, v) => s with { ShortcutSaveFrame = v }),
    ];

    private static readonly Dictionary<string, SettingDefinition> ByKey = All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    internal static bool TryGet(string key, out SettingDefinition definition) => ByKey.TryGetValue(key, out definition!);

    /// <summary>All persisted keys, in file order.</summary>
    public static IEnumerable<string> Keys => All.Select(d => d.Key);
}
