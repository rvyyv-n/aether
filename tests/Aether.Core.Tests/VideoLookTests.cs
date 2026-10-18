using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Video;
using Xunit;

namespace Aether.Core.Tests;

public class VideoLookTests
{
    private const string Lut = @"C:\Looks\Warm.cube";
    private const string QuotedLut = @"'C\:/Looks/Warm.cube'";

    [Fact]
    public void A_Look_That_Is_Off_Has_No_Filters_And_Leaves_Mpv_Alone()
    {
        var look = new VideoLook();

        Assert.True(look.IsOff);
        Assert.Equal("", look.FilterChain(Lut));
        Assert.Equal("", look.MpvFilter(Lut));
        Assert.Null(look.FfmpegFilter(Lut));
    }

    [Fact]
    public void A_Grade_Becomes_A_Lut_Filter_With_The_Path_Escaped_For_Lavfi()
    {
        var look = new VideoLook("warm");

        Assert.Equal($"lut3d=file={QuotedLut}", look.FilterChain(Lut));
        Assert.Equal($"lavfi=[lut3d=file={QuotedLut}]", look.MpvFilter(Lut));
        Assert.Equal($"lut3d=file={QuotedLut}", look.FfmpegFilter(Lut));
    }

    [Fact]
    public void A_Grade_Whose_File_Is_Gone_Is_Left_Out()
    {
        Assert.Equal("", new VideoLook("warm").FilterChain(null));
        Assert.Equal("cas=strength=0.5", new VideoLook("warm", Sharpen: 50).FilterChain(null));
    }

    [Fact]
    public void A_Quote_In_The_Path_Is_Closed_Escaped_Twice_And_Reopened()
    {
        Assert.Equal(@"'C\:/it'\\\''s/a.cube'",VideoLook.QuoteFilterPath(@"C:\it's\a.cube"));
    }

    [Theory]
    [InlineData(5, "0.05")]
    [InlineData(50, "0.5")]
    [InlineData(100, "1")]
    public void Sharpen_Is_Cas_With_The_Amount_As_A_Fraction(int amount, string strength) =>
        Assert.Equal($"cas=strength={strength}", new VideoLook(Sharpen: amount).FilterChain(null));

    [Theory]
    [InlineData(5, "1.5")]
    [InlineData(40, "12")]
    [InlineData(100, "30")]
    public void Grain_Is_Temporal_Noise_At_Three_Tenths_Of_The_Amount(int amount, string strength) =>
        Assert.Equal($"noise=alls={strength}:allf=t", new VideoLook(Grain: amount).FilterChain(null));

    [Theory]
    [InlineData(DenoiseLevel.Off, "")]
    [InlineData(DenoiseLevel.Light, "hqdn3d=2:1.5:3:2.25")]
    [InlineData(DenoiseLevel.Medium, "hqdn3d=4:3:6:4.5")]
    [InlineData(DenoiseLevel.Strong, "hqdn3d=8:6:12:9")]
    public void Each_Denoise_Level_Is_Its_Own_Hqdn3d_Setting(DenoiseLevel level, string expected)
    {
        var look = new VideoLook(Denoise: level);

        Assert.Equal(expected, look.FilterChain(null));
        Assert.Equal(level == DenoiseLevel.Off, look.IsOff);
        Assert.Equal(expected.Length == 0 ? "" : $"lavfi=[{expected}]", look.MpvFilter(null));
    }

    [Fact]
    public void Denoise_Runs_Before_The_Grade_And_Everything_Else_Follows_In_Order()
    {
        var look = new VideoLook("warm", Sharpen: 30, Grain: 20, Denoise: DenoiseLevel.Medium);

        Assert.Equal($"hqdn3d=4:3:6:4.5,lut3d=file={QuotedLut},cas=strength=0.3,noise=alls=6:allf=t", look.FilterChain(Lut));
    }

    [Theory]
    [InlineData("off", DenoiseLevel.Off)]
    [InlineData("light", DenoiseLevel.Light)]
    [InlineData("medium", DenoiseLevel.Medium)]
    [InlineData("strong", DenoiseLevel.Strong)]
    [InlineData("nonsense", DenoiseLevel.Off)]
    [InlineData(null, DenoiseLevel.Off)]
    public void Denoise_Names_Round_Trip_And_Unknown_Ones_Mean_Off(string? name, DenoiseLevel expected)
    {
        Assert.Equal(expected, VideoLook.DenoiseFromName(name));
        Assert.Equal(expected, VideoLook.DenoiseFromName(VideoLook.DenoiseName(expected)));
    }

    [Fact]
    public void The_Parts_Come_In_A_Fixed_Order_Whatever_Was_Set_First()
    {
        var look = new VideoLook("warm", Sharpen: 30, Grain: 20);

        Assert.Equal($"lut3d=file={QuotedLut},cas=strength=0.3,noise=alls=6:allf=t", look.FilterChain(Lut));
    }

    [Fact]
    public void Numbers_Use_A_Dot_Whatever_The_Language()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal("cas=strength=0.35", new VideoLook(Sharpen: 35).FilterChain(null));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Values_Outside_The_Sliders_Are_Pulled_Inside()
    {
        var look = new VideoLook("", Sharpen: 250, Grain: -4).Clamped();

        Assert.Equal(new VideoLook(VideoLook.NoGrade, 100, 0), look);
    }

    [Theory]
    [InlineData("auto-safe", true, "d3d11va-copy")]
    [InlineData("d3d11va", true, "d3d11va-copy")]
    [InlineData("d3d11va-copy", true, "d3d11va-copy")]
    [InlineData("no", true, "no")]
    [InlineData("auto-safe", false, "auto-safe")]
    [InlineData("d3d11va", false, "d3d11va")]
    public void A_Hardware_Decoder_Hands_Frames_Back_Only_While_Filters_Run(string configured, bool active, string expected) =>
        Assert.Equal(expected, VideoLook.HardwareDecoder(configured, active));

    [Fact]
    public void The_Look_Is_Off_By_Default_And_Survives_Save_And_Load()
    {
        var storage = new FakeStorage();
        var first = new SettingsStore(storage);
        first.Load();
        Assert.True(VideoLook.FromSettings(first.Current).IsOff);

        first.Set(SettingKeys.LookGrade, "user:My look.cube");
        first.Set(SettingKeys.LookSharpen, 25);
        first.Set(SettingKeys.LookGrain, 10);
        first.Set(SettingKeys.LookDenoise, "strong");
        first.Save();

        var second = new SettingsStore(storage);
        second.Load();
        Assert.Equal(new VideoLook("user:My look.cube", 25, 10, DenoiseLevel.Strong), VideoLook.FromSettings(second.Current));
    }

    [Fact]
    public void The_Look_Settings_Refuse_Values_Outside_Their_Range()
    {
        var store = new SettingsStore(new FakeStorage());
        store.Load();

        Assert.False(store.Set(SettingKeys.LookSharpen, 101));
        Assert.False(store.Set(SettingKeys.LookGrain, -1));
        Assert.True(store.Set(SettingKeys.LookSharpen, 100));
        Assert.False(store.Set(SettingKeys.LookDenoise, "extreme"));
        Assert.True(store.Set(SettingKeys.LookDenoise, "light"));
    }

    [Fact]
    public void Colours_At_Rest_Have_No_Eq_Filter() =>
        Assert.Null(new VideoAdjustments().FfmpegColourFilter());

    [Theory]
    [InlineData(10, 0, 0, 0, "eq=brightness=0.1")]
    [InlineData(0, 50, 0, 0, "eq=contrast=1.5")]
    [InlineData(0, -100, 0, 0, "eq=contrast=0")]
    [InlineData(0, 0, 100, 0, "eq=gamma=8")]
    [InlineData(0, 0, -100, 0, "eq=gamma=0.125")]
    [InlineData(0, 0, 50, 0, "eq=gamma=2.828")]
    [InlineData(0, 0, 0, -100, "eq=saturation=0")]
    [InlineData(0, 0, 0, 25, "eq=saturation=1.25")]
    [InlineData(-30, 20, 10, 40, "eq=brightness=-0.3:contrast=1.2:gamma=1.231:saturation=1.4")]
    public void The_Colour_Sliders_Map_To_Eq_Like_Mpv_Maps_Them(int brightness, int contrast, int gamma, int saturation, string expected) =>
        Assert.Equal(expected, new VideoAdjustments(Brightness: brightness, Contrast: contrast, Gamma: gamma, Saturation: saturation).FfmpegColourFilter());

    [Fact]
    public void Colours_Outside_The_Sliders_Are_Pulled_Inside_Before_Mapping() =>
        Assert.Equal("eq=brightness=1:saturation=0", new VideoAdjustments(Brightness: 900, Saturation: -900).FfmpegColourFilter());

    [Fact]
    public void Zoom_Rotation_And_Aspect_Are_Not_Part_Of_The_Eq_Filter() =>
        Assert.Null(new VideoAdjustments(ZoomPercent: 200, Rotation: 90, Aspect: "4:3").FfmpegColourFilter());

    [Fact]
    public void The_Export_Chain_Is_Null_When_Look_And_Colours_Are_At_Rest() =>
        Assert.Null(VideoLook.Off.ExportFilter(Lut, new VideoAdjustments()));

    [Fact]
    public void The_Export_Chain_Is_The_Look_Alone_Or_The_Colours_Alone()
    {
        Assert.Equal("cas=strength=0.5", new VideoLook(Sharpen: 50).ExportFilter(null, new VideoAdjustments()));
        Assert.Equal("eq=contrast=1.1", VideoLook.Off.ExportFilter(Lut, new VideoAdjustments(Contrast: 10)));
    }

    [Fact]
    public void The_Export_Chain_Runs_The_Look_In_Its_Order_Then_The_Colours()
    {
        var look = new VideoLook("warm", Sharpen: 30, Grain: 20, Denoise: DenoiseLevel.Light);

        Assert.Equal(
            $"hqdn3d=2:1.5:3:2.25,lut3d=file={QuotedLut},cas=strength=0.3,noise=alls=6:allf=t,eq=brightness=0.1:saturation=0.8",
            look.ExportFilter(Lut, new VideoAdjustments(Brightness: 10, Saturation: -20)));
    }

    [Fact]
    public void A_Grade_Whose_File_Is_Gone_Leaves_Only_The_Rest_Of_The_Export_Chain() =>
        Assert.Equal("eq=gamma=8", new VideoLook("warm").ExportFilter(null, new VideoAdjustments(Gamma: 100)));

    [Fact]
    public void Eq_Numbers_Use_A_Dot_Whatever_The_Language()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal("eq=brightness=0.15", new VideoAdjustments(Brightness: 15).FfmpegColourFilter());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(@"C:\Looks\Warm.cube", @"'C\:/Looks/Warm.cube'")]
    [InlineData(@"C:\My Looks\Teal look.cube", @"'C\:/My Looks/Teal look.cube'")]
    [InlineData(@"D:\a,b\c;d[e].cube", @"'D\:/a,b/c;d[e].cube'")]
    [InlineData(@"\\server\share\x.cube", @"'//server/share/x.cube'")]
    public void Lut_Paths_Are_Slashed_Colon_Escaped_And_Quoted(string path, string expected) =>
        Assert.Equal(expected, VideoLook.QuoteFilterPath(path));

    [Fact]
    public void The_Export_Filter_Is_Plain_Text_For_Ffmpeg_With_No_Mpv_Wrapper()
    {
        string? chain = new VideoLook("warm").ExportFilter(Lut, new VideoAdjustments(Brightness: 5));

        Assert.DoesNotContain("lavfi", chain);
    }

    [Fact]
    public void Including_The_Look_In_Clips_Is_On_By_Default_And_Survives_Save_And_Load()
    {
        var storage = new FakeStorage();
        var first = new SettingsStore(storage);
        first.Load();
        Assert.True(first.Current.ClipIncludeLook);

        Assert.True(first.Set(SettingKeys.ClipIncludeLook, false));
        first.Save();

        var second = new SettingsStore(storage);
        second.Load();
        Assert.False(second.Current.ClipIncludeLook);
    }

    private sealed class FakeStorage : ISettingsStorage
    {
        public string? Content { get; set; }
        public string? Read() => Content;
        public void Write(string content) => Content = content;
    }
}
