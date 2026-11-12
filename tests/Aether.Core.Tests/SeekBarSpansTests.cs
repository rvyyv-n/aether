using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class SeekBarSpansTests
{
    [Fact]
    public void A_Part_Of_The_File_Is_Drawn_From_Its_Start_To_Its_End() =>
        Assert.Equal((100d, 50d), SeekBarMath.Span(60, 90, width: 400, duration: 240, minWidth: 2));

    [Fact]
    public void A_Very_Short_Part_Is_Still_Seen() =>
        Assert.Equal((100d, 2d), SeekBarMath.Span(60, 60.1, width: 400, duration: 240, minWidth: 2));

    [Fact]
    public void A_Part_Past_The_End_Is_Cut_At_The_End()
    {
        Assert.Equal((300d, 100d), SeekBarMath.Span(180, 999, width: 400, duration: 240, minWidth: 2));
        Assert.Equal((398d, 2d), SeekBarMath.Span(239.99, 241, width: 400, duration: 240, minWidth: 2));
    }

    [Fact]
    public void Nothing_Is_Drawn_Before_The_Length_Is_Known_Or_After_The_End()
    {
        Assert.Null(SeekBarMath.Span(10, 20, width: 400, duration: 0, minWidth: 2));
        Assert.Null(SeekBarMath.Span(300, 320, width: 400, duration: 240, minWidth: 2));
        Assert.Null(SeekBarMath.Span(10, 20, width: 0, duration: 240, minWidth: 2));
    }
}
