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
    /// <summary>The first-run tour (7.26) has been shown; Settings › About can show it again.</summary>
    public bool TourSeen { get; init; }

    // Playback
    public bool PreciseSeeking { get; init; }
    public bool ResumePlayback { get; init; } = true;
    public bool LoopByDefault { get; init; }
    public bool PlayNextInFolder { get; init; } = true;
    public bool RememberWindowSize { get; init; } = true;
    public bool AlwaysOnTop { get; init; }
    /// <summary>Keep the display awake while a file plays.</summary>
    public bool PreventSleep { get; init; } = true;

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
    public bool Deinterlace { get; init; }
    /// <summary><c>auto</c> (mpv's own deinterlacer), <c>yadif</c> or <c>bwdif</c>.</summary>
    public string DeinterlaceMode { get; init; } = "auto";
    /// <summary>mpv <c>video-aspect-override</c>: <c>auto</c> or a ratio such as <c>16:9</c>.</summary>
    public string AspectRatio { get; init; } = "auto";
    /// <summary>mpv <c>screenshot-directory</c>; empty means mpv's default.</summary>
    public string ScreenshotDirectory { get; init; } = "";
    /// <summary>mpv <c>screenshot-format</c>: <c>png</c>, <c>jpg</c> or <c>webp</c>.</summary>
    public string ScreenshotFormat { get; init; } = "png";
    /// <summary>mpv <c>screenshot-template</c>.</summary>
    public string ScreenshotTemplate { get; init; } = "Aether-%F-%P";

    // Audio
    /// <summary>mpv <c>audio-device</c> name; empty means the system default.</summary>
    public string AudioOutputDevice { get; init; } = "";
    public bool ExclusiveMode { get; init; }
    public bool Passthrough { get; init; }
    public bool VolumeNormalisation { get; init; }
    /// <summary>Keeps the pitch when the speed changes.</summary>
    public bool PitchCorrection { get; init; } = true;
    /// <summary>Mixes multi-channel tracks down to stereo.</summary>
    public bool StereoDownmix { get; init; }

    // Subtitles
    public bool ShowSubtitles { get; init; } = true;
    /// <summary>ISO 639-1 code, e.g. <c>en</c>; empty means no preference.</summary>
    public string PreferredAudioLanguage { get; init; } = "en";
    public string PreferredSubtitleLanguage { get; init; } = "en";
    public string SubtitleFont { get; init; } = "Segoe UI Variable";
    /// <summary><c>small</c>, <c>medium</c> or <c>large</c>.</summary>
    public string SubtitleSize { get; init; } = "medium";
    /// <summary><c>white</c>, <c>yellow</c>, <c>green</c> or <c>cyan</c>.</summary>
    public string SubtitleColor { get; init; } = "white";
    /// <summary><c>bottom</c>, <c>raised</c> or <c>top</c>.</summary>
    public string SubtitlePosition { get; init; } = "bottom";

    // Streaming
    /// <summary>Maximum stream height: <c>480p</c>, <c>720p</c>, <c>1080p</c>, <c>1440p</c>, <c>2160p</c> or <c>best</c>.</summary>
    public string StreamingQuality { get; init; } = "1080p";
    public bool PreferGpuDecodableFormats { get; init; } = true;
    public bool YtDlpAutoUpdate { get; init; } = true;
    /// <summary>Most data mpv buffers ahead (<c>demuxer-max-bytes</c>), in MiB.</summary>
    public int NetworkCacheMegabytes { get; init; } = 150;
    /// <summary>mpv <c>http-proxy</c>, e.g. <c>http://host:3128</c>; empty means none.</summary>
    public string HttpProxy { get; init; } = "";
    /// <summary>mpv <c>rtsp-transport</c>: <c>tcp</c>, <c>udp</c>, <c>http</c> or <c>udp_multicast</c>.</summary>
    public string RtspTransport { get; init; } = "tcp";

    // Advanced
    /// <summary>Multi-line text, one <c>key=value</c> mpv option per line (see <see cref="MpvOptionsValidator"/>).</summary>
    public string CustomMpvOptions { get; init; } = "";
    /// <summary>Rebuilds the index of a damaged AVI file when it opens (<c>index=recreate</c>).</summary>
    public bool RepairAviIndex { get; init; }
    /// <summary>mpv <c>vd-lavc-skiploopfilter</c>: <c>default</c>, <c>nonref</c>, <c>nonkey</c> or <c>all</c>.</summary>
    public string SkipDeblocking { get; init; } = "default";
    /// <summary>Motion style preview (7.15): <c>quick</c> or <c>springy</c>. Temporary, until the owner picks one.</summary>
    public string MotionStyle { get; init; } = "quick";

    // Shortcuts: empty means the default keys, "None" unbound, otherwise e.g. "Ctrl+O" or "F, F11" (see ShortcutMap).
    public string ShortcutOpenFile { get; init; } = "";
    public string ShortcutOpenUrl { get; init; } = "";
    public string ShortcutSkipBack { get; init; } = "";
    public string ShortcutSkipForward { get; init; } = "";
    public string ShortcutSkipBackFine { get; init; } = "";
    public string ShortcutSkipForwardFine { get; init; } = "";
    public string ShortcutSkipBackLong { get; init; } = "";
    public string ShortcutSkipForwardLong { get; init; } = "";
    public string ShortcutSetIn { get; init; } = "";
    public string ShortcutSetOut { get; init; } = "";
    public string ShortcutPlayPause { get; init; } = "";
    public string ShortcutFullScreen { get; init; } = "";
    public string ShortcutMute { get; init; } = "";
    public string ShortcutLoop { get; init; } = "";
    public string ShortcutAlwaysOnTop { get; init; } = "";
    public string ShortcutDiagnostics { get; init; } = "";
    public string ShortcutSettings { get; init; } = "";
    public string ShortcutCopyFrame { get; init; } = "";
    public string ShortcutSaveFrame { get; init; } = "";
    public string ShortcutExportClip { get; init; } = "";
}
