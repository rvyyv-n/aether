using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class LaunchArgumentsTests
{
    [Fact]
    public void FirstMediaPath_PicksTheFirstSupportedFile()
    {
        Assert.Equal(@"C:\v\a.MKV", LaunchArguments.FirstMediaPath(["--flag", "notes.txt", @"C:\v\a.MKV", "b.mp4"]));
    }

    [Fact]
    public void FirstMediaPath_DropsQuotes()
    {
        Assert.Equal(@"C:\my videos\a.mp4", LaunchArguments.FirstMediaPath(["\"C:\\my videos\\a.mp4\""]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("readme.txt")]
    public void FirstMediaPath_ReturnsNullWithoutMedia(string? argument)
    {
        Assert.Null(LaunchArguments.FirstMediaPath([argument]));
        Assert.Null(LaunchArguments.FirstMediaPath([]));
    }

    [Fact]
    public void Split_KeepsQuotedRunsTogether()
    {
        Assert.Equal(["-x", @"C:\my videos\a.mp4", "b.mov"], LaunchArguments.Split("-x \"C:\\my videos\\a.mp4\"  b.mov"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Split_EmptyGivesNothing(string? commandLine) => Assert.Empty(LaunchArguments.Split(commandLine));
}
