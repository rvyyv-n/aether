namespace Aether.Core.Playback.Settings;

/// <summary>
/// Immutable snapshot of every user setting. Defaults live here; <see cref="SettingDefinitions"/> adds the
/// persisted key, allowed values and restart flag for each property.
/// </summary>
public sealed record AetherSettings
{
    // General
    public bool BatterySaver { get; init; } = true;
    public bool OpenInRunningWindow { get; init; } = true;

    // Playback
    public bool PreciseSeeking { get; init; }
    public bool ResumePlayback { get; init; } = true;
    public bool LoopByDefault { get; init; }
    public bool PlayNextInFolder { get; init; } = true;
    public bool RememberWindowSize { get; init; } = true;
    public bool AlwaysOnTop { get; init; }

    // Video
    /// <summary><c>efficient</c>, <c>balanced</c> or <c>high</c>.</summary>
    public string QualityPreset { get; init; } = "balanced";
    /// <summary>mpv <c>hwdec</c> value: <c>auto-safe</c>, <c>no</c>, <c>d3d11va</c> or <c>d3d11va-copy</c>.</summary>
    public string HardwareDecoding { get; init; } = "auto-safe";
    public bool SmoothMotion { get; init; } = true;
    /// <summary>mpv <c>scale</c> value: <c>bilinear</c>, <c>spline36</c>, <c>lanczos</c> or <c>ewa_lanczos</c>.</summary>
    public string Upscaling { get; init; } = "spline36";
    /// <summary>mpv <c>dscale</c> value: <c>bilinear</c>, <c>hermite</c>, <c>mitchell</c> or <c>lanczos</c>.</summary>
    public string Downscaling { get; init; } = "mitchell";
    public bool Debanding { get; init; } = true;
    /// <summary>mpv <c>tone-mapping</c> value: <c>auto</c>, <c>bt.2390</c>, <c>reinhard</c>, <c>hable</c> or <c>clip</c>.</summary>
    public string HdrToneMapping { get; init; } = "auto";
    /// <summary>Colour adjustments, -100..100 (mpv's own range).</summary>
    public int Brightness { get; init; }
    public int Contrast { get; init; }
    public int Gamma { get; init; }
    public int Saturation { get; init; }

    // Audio
    /// <summary>mpv <c>audio-device</c> name; empty means the system default.</summary>
    public string AudioOutputDevice { get; init; } = "";
    public bool ExclusiveMode { get; init; }
    public bool Passthrough { get; init; }
    public bool VolumeNormalisation { get; init; }

    // Subtitles
    public bool ShowSubtitles { get; init; } = true;
    /// <summary>ISO 639-1 code, e.g. <c>en</c>; empty means no preference.</summary>
    public string PreferredAudioLanguage { get; init; } = "en";
    public string PreferredSubtitleLanguage { get; init; } = "en";
    public string SubtitleFont { get; init; } = "Segoe UI Variable";
    /// <summary><c>small</c>, <c>medium</c> or <c>large</c>.</summary>
    public string SubtitleSize { get; init; } = "medium";

    // Streaming
    /// <summary>Maximum stream height: <c>480p</c>, <c>720p</c>, <c>1080p</c>, <c>1440p</c>, <c>2160p</c> or <c>best</c>.</summary>
    public string StreamingQuality { get; init; } = "1080p";
    public bool PreferGpuDecodableFormats { get; init; } = true;
    public bool YtDlpAutoUpdate { get; init; } = true;

    // Advanced
    /// <summary>Multi-line text, one <c>key=value</c> mpv option per line (see <see cref="MpvOptionsValidator"/>).</summary>
    public string CustomMpvOptions { get; init; } = "";

    // Shortcuts: empty means the default keys, "None" unbound, otherwise e.g. "Ctrl+O" or "F, F11" (see ShortcutMap).
    public string ShortcutOpenFile { get; init; } = "";
    public string ShortcutOpenUrl { get; init; } = "";
    public string ShortcutSkipBack { get; init; } = "";
    public string ShortcutSkipForward { get; init; } = "";
    public string ShortcutSetIn { get; init; } = "";
    public string ShortcutSetOut { get; init; } = "";
    public string ShortcutPlayPause { get; init; } = "";
    public string ShortcutFullScreen { get; init; } = "";
    public string ShortcutMute { get; init; } = "";
    public string ShortcutLoop { get; init; } = "";
    public string ShortcutAlwaysOnTop { get; init; } = "";
    public string ShortcutDiagnostics { get; init; } = "";
}
