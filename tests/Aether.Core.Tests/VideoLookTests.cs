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
    public void A_Quote_In_The_Path_Is_Closed_Escaped_And_Reopened()
    {
        Assert.Equal(@"'C\:/it'\''s/a.cube'", VideoLook.QuoteFilterPath(@"C:\it's\a.cube"));
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
        first.Save();

        var second = new SettingsStore(storage);
        second.Load();
        Assert.Equal(new VideoLook("user:My look.cube", 25, 10), VideoLook.FromSettings(second.Current));
    }

    [Fact]
    public void The_Look_Settings_Refuse_Values_Outside_Their_Range()
    {
        var store = new SettingsStore(new FakeStorage());
        store.Load();

        Assert.False(store.Set(SettingKeys.LookSharpen, 101));
        Assert.False(store.Set(SettingKeys.LookGrain, -1));
        Assert.True(store.Set(SettingKeys.LookSharpen, 100));
    }

    private sealed class FakeStorage : ISettingsStorage
    {
        public string? Content { get; set; }
        public string? Read() => Content;
        public void Write(string content) => Content = content;
    }
}
