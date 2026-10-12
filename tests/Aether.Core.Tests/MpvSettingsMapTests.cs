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
}
