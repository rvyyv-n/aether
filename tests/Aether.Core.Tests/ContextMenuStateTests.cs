using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class ContextMenuStateTests
{
    private static ContextMenuState Playing { get; } = new(EngineReady: true, HasVideo: true, RecentCount: 3, ClipCount: 2, HasWindow: true);

    [Fact]
    public void Playing_With_History_Enables_What_Works_Today()
    {
        Assert.True(Playing.OpenFile);
        Assert.True(Playing.OpenUrl);
        Assert.True(Playing.Recent);
        Assert.True(Playing.Speed);
        Assert.True(Playing.Audio);
        Assert.True(Playing.Subtitles);
        Assert.True(Playing.AlwaysOnTop);
        Assert.True(Playing.Diagnostics);
        Assert.True(Playing.Settings);
        Assert.True(Playing.Clips);
        Assert.True(Playing.Video);
        Assert.True(Playing.CopyFrame);
        Assert.True(Playing.SaveFrame);
    }

    [Fact]
    public void Entries_Whose_Work_Is_Not_Built_Stay_Disabled()
    {
        Assert.False(Playing.PictureInPicture);
        Assert.False(Playing.Stream);
    }

    [Fact]
    public void Without_An_Engine_Nothing_Can_Be_Opened()
    {
        var starting = new ContextMenuState(EngineReady: false, HasVideo: false, RecentCount: 3, ClipCount: 0, HasWindow: true);

        Assert.False(starting.OpenFile);
        Assert.False(starting.OpenUrl);
        Assert.False(starting.Recent);
        Assert.True(starting.Settings);
        Assert.True(starting.Diagnostics);
    }

    [Fact]
    public void Recent_Needs_History_And_Speed_And_Clips_Need_A_Video()
    {
        var idle = new ContextMenuState(EngineReady: true, HasVideo: false, RecentCount: 0, ClipCount: 4, HasWindow: true);

        Assert.False(idle.Recent);
        Assert.False(idle.Speed);
        Assert.False(idle.Audio);
        Assert.False(idle.Subtitles);
        Assert.False(idle.Video);
        Assert.False(idle.CopyFrame);
        Assert.False(idle.SaveFrame);
        Assert.False(idle.Clips);
        Assert.False((Playing with { ClipCount = 0 }).Clips);
    }

    [Fact]
    public void Always_On_Top_Needs_The_Window()
    {
        Assert.False((Playing with { HasWindow = false }).AlwaysOnTop);
    }

    [Theory]
    [InlineData("holiday.mp4", @"D:\Videos\holiday.mp4", "holiday.mp4")]
    [InlineData("", @"D:\Videos\holiday.mp4", @"D:\Videos\holiday.mp4")]
    [InlineData("  ", "https://example.com/v", "https://example.com/v")]
    public void Recent_Label_Falls_Back_To_The_Location(string name, string location, string expected)
    {
        Assert.Equal(expected, ContextMenuState.RecentLabel(name, location));
    }
}
