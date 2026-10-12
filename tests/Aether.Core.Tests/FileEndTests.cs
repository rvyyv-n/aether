using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class FileEndTests
{
    [Fact]
    public void Reports_Once_When_The_File_Ends()
    {
        var end = new FileEnd();

        Assert.False(end.Reached(false));
        Assert.True(end.Reached(true));
        Assert.False(end.Reached(true));
        Assert.False(end.Reached(true));
    }

    [Fact]
    public void Reports_Again_After_The_End_Is_Left_And_Reached_Again()
    {
        var end = new FileEnd();

        Assert.True(end.Reached(true));
        Assert.False(end.Reached(false));
        Assert.True(end.Reached(true));
    }

    [Fact]
    public void A_File_That_Never_Ends_Reports_Nothing()
    {
        var end = new FileEnd();

        Assert.False(end.Reached(false));
        Assert.False(end.Reached(false));
    }
}
