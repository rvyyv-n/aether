using Aether.Core.Playback.Settings;
using Xunit;

namespace Aether.Core.Tests;

public class MpvSettingsMapTests
{
    private static Dictionary<string, string> Runtime(AetherSettings settings, bool onBattery = false) =>
        MpvSettingsMap.RuntimePlan(settings, onBattery).Properties.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Defaults_Map_To_The_Expected_Values()
    {
        Dictionary<string, string> p = Runtime(new AetherSettings());

        Assert.Equal("spline36", p["scale"]);
        Assert.Equal("mitchell", p["dscale"]);
        Assert.Equal("yes", p["deband"]);
        Assert.Equal("display-resample", p["video-sync"]);
        Assert.Equal("yes", p["interpolation"]);
        Assert.Equal("auto", p["tone-mapping"]);
        Assert.Equal("0", p["brightness"]);
        Assert.Equal("default", p["hr-seek"]);
        Assert.Equal("auto", p["audio-device"]);
        Assert.Equal("no", p["audio-exclusive"]);
        Assert.Equal("yes", p["sub-visibility"]);
        Assert.Equal("en", p["alang"]);
        Assert.Equal("en", p["slang"]);
        Assert.Equal("Segoe UI Variable", p["sub-font"]);
        Assert.Equal("55", p["sub-font-size"]);
        Assert.Equal("#FFFFFF", p["sub-color"]);
        Assert.Equal("100", p["sub-pos"]);
    }

    [Fact]
    public void Individual_Settings_Reach_Their_Properties()
    {
        var settings = new AetherSettings
        {
            Upscaling = "ewa_lanczos",
            Downscaling = "hermite",
            Debanding = false,
            SmoothMotion = false,
            HdrToneMapping = "bt.2390",
            Brightness = -10,
            Contrast = 20,
            Gamma = -30,
            Saturation = 40,
            PreciseSeeking = true,
            AudioOutputDevice = "wasapi/{abc}",
            ExclusiveMode = true,
            ShowSubtitles = false,
            PreferredAudioLanguage = "ja",
            PreferredSubtitleLanguage = "",
            SubtitleFont = "Arial",
            SubtitleSize = "large",
        };

        Dictionary<string, string> p = Runtime(settings);

        Assert.Equal("ewa_lanczos", p["scale"]);
        Assert.Equal("hermite", p["dscale"]);
        Assert.Equal("no", p["deband"]);
        Assert.Equal("audio", p["video-sync"]);
        Assert.Equal("no", p["interpolation"]);
        Assert.Equal("bt.2390", p["tone-mapping"]);
        Assert.Equal("-10", p["brightness"]);
        Assert.Equal("20", p["contrast"]);
        Assert.Equal("-30", p["gamma"]);
        Assert.Equal("40", p["saturation"]);
        Assert.Equal("yes", p["hr-seek"]);
        Assert.Equal("wasapi/{abc}", p["audio-device"]);
        Assert.Equal("yes", p["audio-exclusive"]);
        Assert.Equal("no", p["sub-visibility"]);
        Assert.Equal("ja", p["alang"]);
        Assert.Equal("", p["slang"]);
        Assert.Equal("Arial", p["sub-font"]);
        Assert.Equal("75", p["sub-font-size"]);
    }

    [Theory]
    [InlineData("auto-safe", false)]
    [InlineData("d3d11va", true)]
    [InlineData("no", false)]
    public void Software_Decoding_Keeps_The_Frame_Decoded_Ahead(string hwdec, bool expected) =>
        Assert.Equal(expected, MpvSettingsMap.VideoLatencyHacks(new AetherSettings { HardwareDecoding = hwdec }));

    [Theory]
    [InlineData("small", "40")]
    [InlineData("medium", "55")]
    [InlineData("large", "75")]
    public void Subtitle_Size_Maps_To_Font_Size(string size, string expected) =>
        Assert.Equal(expected, Runtime(new AetherSettings { SubtitleSize = size })["sub-font-size"]);

    [Fact]
    public void Efficient_Preset_Overrides_The_Individual_Video_Settings()
    {
        Dictionary<string, string> p = Runtime(new AetherSettings { QualityPreset = "efficient", Upscaling = "ewa_lanczos", Downscaling = "lanczos" });

        Assert.Equal("bilinear", p["scale"]);
        Assert.Equal("bilinear", p["dscale"]);
        Assert.Equal("no", p["deband"]);
        Assert.Equal("audio", p["video-sync"]);
        Assert.Equal("no", p["interpolation"]);
    }

    [Fact]
    public void High_Preset_Raises_Scalers_And_Debanding_But_Leaves_Smooth_Motion_To_The_User()
    {
        var settings = new AetherSettings { QualityPreset = "high", Upscaling = "bilinear", Downscaling = "bilinear", Debanding = false, SmoothMotion = false };

        Dictionary<string, string> p = Runtime(settings);

        Assert.Equal("ewa_lanczos", p["scale"]);
        Assert.Equal("lanczos", p["dscale"]);
        Assert.Equal("yes", p["deband"]);
        Assert.Equal("no", p["interpolation"]);
    }

    [Fact]
    public void Balanced_Preset_Uses_The_Individual_Settings() =>
        Assert.Equal("lanczos", Runtime(new AetherSettings { QualityPreset = "balanced", Upscaling = "lanczos" })["scale"]);

    [Fact]
    public void Battery_Saver_On_Battery_Acts_As_Efficient()
    {
        var settings = new AetherSettings { BatterySaver = true, QualityPreset = "high" };

        Assert.Equal("efficient", MpvSettingsMap.EffectivePreset(settings, onBattery: true));
        Assert.Equal("bilinear", Runtime(settings, onBattery: true)["scale"]);
    }

    [Fact]
    public void Battery_Saver_Does_Nothing_On_Mains_Or_When_Off()
    {
        var on = new AetherSettings { BatterySaver = true, QualityPreset = "high" };
        var off = new AetherSettings { BatterySaver = false, QualityPreset = "high" };

        Assert.Equal("high", MpvSettingsMap.EffectivePreset(on, onBattery: false));
        Assert.Equal("high", MpvSettingsMap.EffectivePreset(off, onBattery: true));
        Assert.Equal("ewa_lanczos", Runtime(off, onBattery: true)["scale"]);
    }

    [Theory]
    [InlineData("480p", false, "bv*[height<=?480]+ba/b[height<=?480]")]
    [InlineData("1080p", false, "bv*[height<=?1080]+ba/b[height<=?1080]")]
    [InlineData("2160p", false, "bv*[height<=?2160]+ba/b[height<=?2160]")]
    [InlineData("best", false, "bv*+ba/b")]
    [InlineData("720p", true, "bv*[height<=?720][vcodec!^=av01]+ba/bv*[height<=?720]+ba/b[height<=?720]")]
    [InlineData("best", true, "bv*[vcodec!^=av01]+ba/bv*+ba/b")]
    public void Streaming_Quality_Builds_The_Format_Selector(string quality, bool avoidAv1, string expected)
    {
        Assert.Equal(expected, MpvSettingsMap.YtdlFormat(quality, avoidAv1));
        Assert.Equal(
            expected,
            Runtime(new AetherSettings { StreamingQuality = quality, PreferGpuDecodableFormats = avoidAv1 })["ytdl-format"]);
    }

    [Fact]
    public void Volume_Normalisation_Follows_The_Setting()
    {
        Assert.False(MpvSettingsMap.RuntimePlan(new AetherSettings(), false).NormaliseVolume);
        Assert.True(MpvSettingsMap.RuntimePlan(new AetherSettings { VolumeNormalisation = true }, false).NormaliseVolume);
    }

    [Fact]
    public void Normalisation_Commands_Touch_Only_Our_Labelled_Filter()
    {
        Assert.Equal([["af", "remove", "@aether-norm"]], MpvSettingsMap.NormaliseCommands(false));
        Assert.Equal(
            [["af", "remove", "@aether-norm"], ["af", "add", "@aether-norm:dynaudnorm"]],
            MpvSettingsMap.NormaliseCommands(true));
    }

    [Fact]
    public void Passthrough_Is_A_Start_Option_Only_When_On()
    {
        Assert.Empty(MpvSettingsMap.StartOptions(new AetherSettings()));
        Assert.Equal(
            [new KeyValuePair<string, string>("audio-spdif", "ac3,eac3,dts-hd,truehd")],
            MpvSettingsMap.StartOptions(new AetherSettings { Passthrough = true }));
    }

    [Fact]
    public void Custom_Options_Keep_Order_And_Drop_Reserved_And_Malformed_Lines()
    {
        const string text = "# my tweaks\r\n"
            + "cache=yes\r\n"
            + "\r\n"
            + "hwdec=no\r\n"
            + "--demuxer-max-bytes = 200MiB\r\n"
            + "no equals here\r\n"
            + "=value\r\n"
            + "bad key=1\r\n"
            + "VO=gpu\r\n"
            + "cache=no\r\n"
            + "sub-delay=0.5\r\n";

        IReadOnlyList<KeyValuePair<string, string>> options = MpvSettingsMap.StartOptions(new AetherSettings { CustomMpvOptions = text });

        Assert.Equal(
            [
                new KeyValuePair<string, string>("cache", "yes"),
                new KeyValuePair<string, string>("demuxer-max-bytes", "200MiB"),
                new KeyValuePair<string, string>("sub-delay", "0.5"),
            ],
            options);
    }

    [Fact]
    public void Custom_Options_Come_After_Passthrough_And_Keep_Equals_In_Values()
    {
        var settings = new AetherSettings { Passthrough = true, CustomMpvOptions = "vf=lavfi=[eq=1]" };

        IReadOnlyList<KeyValuePair<string, string>> options = MpvSettingsMap.StartOptions(settings);

        Assert.Equal(["audio-spdif", "vf"], options.Select(o => o.Key));
        Assert.Equal("lavfi=[eq=1]", options[1].Value);
    }

    [Fact]
    public void Custom_Options_Win_Over_The_Settings_They_Name()
    {
        var settings = new AetherSettings { CustomMpvOptions = "--Deband=no\nvolume-max=150\naf=loudnorm", VolumeNormalisation = true };

        MpvRuntimePlan plan = MpvSettingsMap.RuntimePlan(settings, false);

        Assert.DoesNotContain(plan.Properties, p => p.Key == "deband");
        Assert.Contains(plan.Properties, p => p.Key == "scale");
        Assert.False(plan.NormaliseVolume);
    }

    [Fact]
    public void Playback_Option_Defaults_Map_To_Mpv_Defaults()
    {
        Dictionary<string, string> p = Runtime(new AetherSettings());

        Assert.Equal("no", p["deinterlace"]);
        Assert.Equal("-1", p["video-aspect-override"]);
        Assert.Equal("", p["screenshot-directory"]);
        Assert.Equal("png", p["screenshot-format"]);
        Assert.Equal("Aether-%F-%P", p["screenshot-template"]);
        Assert.Equal("yes", p["audio-pitch-correction"]);
        Assert.Equal("auto-safe", p["audio-channels"]);
        Assert.Equal("", p["http-proxy"]);
        Assert.Equal("tcp", p["rtsp-transport"]);
        Assert.Empty(MpvSettingsMap.StartOptions(new AetherSettings()));
        Assert.Null(MpvSettingsMap.RuntimePlan(new AetherSettings(), false).DeinterlaceFilter);
    }

    [Fact]
    public void Playback_Option_Settings_Reach_Their_Properties()
    {
        var settings = new AetherSettings
        {
            Deinterlace = true,
            AspectRatio = "21:9",
            ScreenshotDirectory = @"D:\Shots",
            ScreenshotFormat = "webp",
            ScreenshotTemplate = "shot-%n",
            PitchCorrection = false,
            StereoDownmix = true,
            HttpProxy = "http://proxy:3128",
            RtspTransport = "udp_multicast",
        };

        Dictionary<string, string> p = Runtime(settings);

        Assert.Equal("yes", p["deinterlace"]);
        Assert.Equal("21:9", p["video-aspect-override"]);
        Assert.Equal(@"D:\Shots", p["screenshot-directory"]);
        Assert.Equal("webp", p["screenshot-format"]);
        Assert.Equal("shot-%n", p["screenshot-template"]);
        Assert.Equal("no", p["audio-pitch-correction"]);
        Assert.Equal("stereo", p["audio-channels"]);
        Assert.Equal("http://proxy:3128", p["http-proxy"]);
        Assert.Equal("udp_multicast", p["rtsp-transport"]);
    }

    [Theory]
    [InlineData(true, "yadif", "no", "yadif")]
    [InlineData(true, "bwdif", "no", "bwdif")]
    [InlineData(true, "auto", "yes", null)]
    [InlineData(false, "yadif", "no", null)]
    public void Deinterlacing_Uses_Mpvs_Own_Or_A_Chosen_Filter_Never_Both(bool on, string mode, string property, string? filter)
    {
        MpvRuntimePlan plan = MpvSettingsMap.RuntimePlan(new AetherSettings { Deinterlace = on, DeinterlaceMode = mode }, false);

        Assert.Equal(property, plan.Properties.Single(x => x.Key == "deinterlace").Value);
        Assert.Equal(filter, plan.DeinterlaceFilter);
    }

    [Fact]
    public void Custom_Options_Win_Over_The_New_Live_Settings()
    {
        var settings = new AetherSettings
        {
            Deinterlace = true,
            DeinterlaceMode = "yadif",
            CustomMpvOptions = "video-aspect-override=2\nrtsp-transport=tcp\nvf=hflip",
        };

        MpvRuntimePlan plan = MpvSettingsMap.RuntimePlan(settings, false);

        Assert.DoesNotContain(plan.Properties, p => p.Key is "video-aspect-override" or "rtsp-transport");
        Assert.Null(plan.DeinterlaceFilter);
    }

    [Fact]
    public void Deinterlace_Commands_Touch_Only_Our_Labelled_Filter()
    {
        Assert.Equal([["vf", "remove", "@aether-deint"]], MpvSettingsMap.DeinterlaceCommands(null));
        Assert.Equal(
            [["vf", "remove", "@aether-deint"], ["vf", "add", "@aether-deint:yadif"]],
            MpvSettingsMap.DeinterlaceCommands("yadif"));
    }

    [Fact]
    public void Start_Only_Playback_Options_Are_Added_Only_When_Changed_And_Before_Custom_Options()
    {
        var settings = new AetherSettings
        {
            Passthrough = true,
            NetworkCacheMegabytes = 400,
            RepairAviIndex = true,
            SkipDeblocking = "nonkey",
            CustomMpvOptions = "demuxer-max-bytes=1GiB",
        };

        IReadOnlyList<KeyValuePair<string, string>> options = MpvSettingsMap.StartOptions(settings);

        Assert.Equal(
            [
                new KeyValuePair<string, string>("audio-spdif", "ac3,eac3,dts-hd,truehd"),
                new KeyValuePair<string, string>("demuxer-max-bytes", "400MiB"),
                new KeyValuePair<string, string>("index", "recreate"),
                new KeyValuePair<string, string>("vd-lavc-skiploopfilter", "nonkey"),
                new KeyValuePair<string, string>("demuxer-max-bytes", "1GiB"),
            ],
            options);
        Assert.Empty(MpvSettingsMap.StartOptions(new AetherSettings { SkipDeblocking = "default", RepairAviIndex = false }));
    }

    [Fact]
    public void Sync_Switches_The_Deinterlace_Filter_Only_When_It_Changes()
    {
        (MpvSettingsSync sync, List<string[]> sent) = NewSync();
        sync.Apply(new AetherSettings(), false);
        Assert.DoesNotContain(sent, c => c[0] == "vf");

        sync.Apply(new AetherSettings { Deinterlace = true, DeinterlaceMode = "bwdif" }, false);
        sync.Apply(new AetherSettings { Deinterlace = true, DeinterlaceMode = "bwdif", Brightness = 3 }, false);
        Assert.Equal(2, sent.Count(c => c[0] == "vf"));
        Assert.Equal(["vf", "add", "@aether-deint:bwdif"], sent.Last(c => c[0] == "vf"));

        sync.Apply(new AetherSettings(), false);
        Assert.Equal(["vf", "remove", "@aether-deint"], sent.Last(c => c[0] == "vf"));
    }

    [Fact]
    public void Empty_Custom_Options_Give_Nothing() =>
        Assert.Empty(MpvSettingsMap.CustomOptions(null));

    private static (MpvSettingsSync Sync, List<string[]> Sent) NewSync()
    {
        var sent = new List<string[]>();
        return (new MpvSettingsSync(sent.Add), sent);
    }

    [Fact]
    public void Sync_Sends_Everything_First_Then_Only_What_Changed()
    {
        (MpvSettingsSync sync, List<string[]> sent) = NewSync();
        var settings = new AetherSettings();

        sync.Apply(settings, false);
        int first = sent.Count;
        sync.Apply(settings, false);
        Assert.Equal(first, sent.Count);

        sync.Apply(settings with { Brightness = 12 }, false);

        Assert.Equal(first + 1, sent.Count);
        Assert.Equal(["set", "brightness", "12"], sent[^1]);
    }

    [Fact]
    public void Sync_Reapplies_When_The_Power_Source_Changes_The_Preset()
    {
        (MpvSettingsSync sync, List<string[]> sent) = NewSync();
        var settings = new AetherSettings { BatterySaver = true };
        sync.Apply(settings, false);
        sent.Clear();

        sync.Apply(settings, true);

        Assert.Contains(sent, c => c.SequenceEqual(["set", "scale", "bilinear"]));
        Assert.Contains(sent, c => c.SequenceEqual(["set", "interpolation", "no"]));

        sent.Clear();
        sync.Apply(settings, false);
        Assert.Contains(sent, c => c.SequenceEqual(["set", "scale", "spline36"]));
    }

    [Fact]
    public void Sync_Adds_And_Removes_The_Normalisation_Filter_Only_On_Change()
    {
        (MpvSettingsSync sync, List<string[]> sent) = NewSync();
        sync.Apply(new AetherSettings(), false);
        Assert.DoesNotContain(sent, c => c[0] == "af");

        sync.Apply(new AetherSettings { VolumeNormalisation = true }, false);
        Assert.Equal(2, sent.Count(c => c[0] == "af"));

        sync.Apply(new AetherSettings { VolumeNormalisation = true }, false);
        Assert.Equal(2, sent.Count(c => c[0] == "af"));

        sync.Apply(new AetherSettings(), false);
        Assert.Equal(3, sent.Count(c => c[0] == "af"));
        Assert.Equal(["af", "remove", "@aether-norm"], sent[^1]);
    }

    [Theory]
    [InlineData("white", "#FFFFFF")]
    [InlineData("yellow", "#FFFF00")]
    [InlineData("green", "#00FF00")]
    [InlineData("cyan", "#00FFFF")]
    public void Subtitle_Colour_Maps_To_Hex(string color, string expected) =>
        Assert.Equal(expected, Runtime(new AetherSettings { SubtitleColor = color })["sub-color"]);

    [Theory]
    [InlineData("bottom", "100")]
    [InlineData("raised", "90")]
    [InlineData("top", "10")]
    public void Subtitle_Position_Maps_To_Sub_Pos(string position, string expected) =>
        Assert.Equal(expected, Runtime(new AetherSettings { SubtitlePosition = position })["sub-pos"]);
}
