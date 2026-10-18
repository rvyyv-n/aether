using Aether.Core.Playback.Settings;

namespace Aether.Core.Tests;

public class SubtitleLiftTests
{
    [Fact]
    public void Nothing_To_Clear_Keeps_The_Default_Margin() =>
        Assert.Equal("22", SubtitleLift.MarginY("bottom", 0, 720));

    [Fact]
    public void Unknown_Picture_Height_Keeps_The_Default_Margin() =>
        Assert.Equal("22", SubtitleLift.MarginY("bottom", 80, 0));

    [Fact]
    public void Bottom_Scales_The_Clearance_To_The_720_High_Space() =>
        Assert.Equal("160", SubtitleLift.MarginY("bottom", 160, 720));

    [Fact]
    public void A_Taller_Picture_Needs_A_Smaller_Margin() =>
        Assert.Equal("80", SubtitleLift.MarginY("bottom", 120, 1080));

    [Fact]
    public void Raised_Only_Adds_What_Its_Own_Lift_Does_Not_Cover() =>
        Assert.Equal("88", SubtitleLift.MarginY("raised", 160, 720));

    [Fact]
    public void Raised_That_Already_Clears_The_Bar_Keeps_The_Default() =>
        Assert.Equal("22", SubtitleLift.MarginY("raised", 60, 720));

    [Fact]
    public void Top_Is_Never_Lifted() =>
        Assert.Equal("22", SubtitleLift.MarginY("top", 160, 720));

    [Fact]
    public void A_Huge_Clearance_Stops_At_Half_The_Picture() =>
        Assert.Equal("360", SubtitleLift.MarginY("bottom", 5000, 720));
}
