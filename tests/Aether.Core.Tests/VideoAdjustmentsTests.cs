using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Video;
using Xunit;

namespace Aether.Core.Tests;

public class VideoAdjustmentsTests
{
    [Theory]
    [InlineData(100, "0")]
    [InlineData(200, "1")]
    [InlineData(50, "-1")]
    [InlineData(400, "2")]
    [InlineData(25, "-2")]
    public void Zoom_Is_Sent_To_Mpv_As_A_Base_2_Logarithm(int percent, string expected) =>
        Assert.Equal(expected, VideoAdjustments.ZoomForMpv(percent));

    [Fact]
    public void Zoom_Of_110_Percent_Has_A_Dot_Whatever_The_Language()
    {
        Assert.Equal("0.1375", VideoAdjustments.ZoomForMpv(110));
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1000, 400)]
    [InlineData(110.4, 110)]
    public void Zoom_Stays_In_Range(double input, int expected) => Assert.Equal(expected, VideoAdjustments.ClampZoom(input));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(270, 270)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    [InlineData(100, 90)]
    public void Rotation_Lands_On_A_Quarter_Turn(int input, int expected) =>
        Assert.Equal(expected, VideoAdjustments.NormalizeRotation(input));

    [Theory]
    [InlineData(-500, -100)]
    [InlineData(500, 100)]
    [InlineData(12.6, 13)]
    public void Colours_Stay_Between_Minus_And_Plus_100(double input, int expected) =>
        Assert.Equal(expected, VideoAdjustments.ClampColour(input));

    [Fact]
    public void Properties_Use_The_Mpv_Names_In_A_Fixed_Order()
    {
        var adjustments = new VideoAdjustments(200, 90, "16:9", 5, -10, 20, -30);

        Assert.Equal(
            [
                "video-zoom=1",
                "video-rotate=90",
                "video-aspect-override=16:9",
                "brightness=5",
                "contrast=-10",
                "gamma=20",
                "saturation=-30",
            ],
            adjustments.Properties().Select(p => $"{p.Key}={p.Value}"));
    }

    [Fact]
    public void Auto_Aspect_Is_Minus_1_To_Mpv() =>
        Assert.Equal("-1", new VideoAdjustments().Properties().Single(p => p.Key == "video-aspect-override").Value);

    [Fact]
    public void Properties_Clamp_Values_That_Are_Out_Of_Range()
    {
        var wild = new VideoAdjustments(9000, 45, "7:3", 500, -500, 101, -101);

        VideoAdjustments tidy = wild.Clamped();

        Assert.Equal(new VideoAdjustments(400, 0, "auto", 100, -100, 100, -100), tidy);
        Assert.Equal(tidy.Properties(), wild.Properties());
    }

    [Fact]
    public void A_File_Starts_From_The_Aspect_And_Colours_In_Settings()
    {
        var settings = new AetherSettings { AspectRatio = "4:3", Brightness = 10, Contrast = -5, Gamma = 3, Saturation = 7 };

        VideoAdjustments start = VideoAdjustments.FromSettings(settings);

        Assert.Equal(new VideoAdjustments(100, 0, "4:3", 10, -5, 3, 7), start);
    }

    [Fact]
    public void An_Aspect_Ratio_The_Flyout_Does_Not_List_Falls_Back_To_Auto() =>
        Assert.Equal("auto", VideoAdjustments.FromSettings(new AetherSettings { AspectRatio = "5:4" }).Aspect);

    [Fact]
    public void Reset_Colours_Keeps_Zoom_Rotation_And_Aspect()
    {
        var current = new VideoAdjustments(150, 180, "21:9", 40, 40, 40, 40);
        var baseline = new VideoAdjustments(Brightness: 2, Saturation: -4);

        VideoAdjustments reset = current.WithColoursOf(baseline);

        Assert.Equal(new VideoAdjustments(150, 180, "21:9", 2, 0, 0, -4), reset);
    }

    [Theory]
    [InlineData(110, "110%")]
    [InlineData(25, "25%")]
    public void Zoom_Reads_As_A_Percentage(int percent, string expected) =>
        Assert.Equal(expected, VideoAdjustments.FormatZoom(percent));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(12, "+12")]
    [InlineData(-30, "−30")]
    public void Colours_Read_With_Their_Sign(int value, string expected) =>
        Assert.Equal(expected, VideoAdjustments.FormatColour(value));
}
