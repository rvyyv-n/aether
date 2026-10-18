using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class SeekBarChapterSegmentsTests
{
    [Fact]
    public void No_Chapters_Is_One_Whole_Bar() =>
        Assert.Equal([(0d, 400d)], SeekBarMath.ChapterSegments([], 400, 2, 6));

    [Fact]
    public void Each_Start_Cuts_A_Centred_Gap() =>
        Assert.Equal([(0d, 99d), (101d, 299d), (301d, 400d)], SeekBarMath.ChapterSegments([0.25, 0.75], 400, 2, 6));

    [Fact]
    public void Starts_Too_Close_Together_Keep_Only_The_First_Cut() =>
        Assert.Equal([(0d, 99d), (101d, 400d)], SeekBarMath.ChapterSegments([0.25, 0.26, 0.262], 400, 2, 6));

    [Fact]
    public void Starts_Too_Close_To_Either_End_Get_No_Cut() =>
        Assert.Equal([(0d, 400d)], SeekBarMath.ChapterSegments([0.005, 0.995], 400, 2, 6));

    [Fact]
    public void Many_Tiny_Chapters_Never_Leave_Slivers()
    {
        double[] fractions = [.. Enumerable.Range(1, 999).Select(i => i / 1000d)];

        var segments = SeekBarMath.ChapterSegments(fractions, 300, 2, 6);

        Assert.All(segments, s => Assert.True(s.End - s.Start >= 4));
        Assert.Equal(300, segments[^1].End);
    }

    [Fact]
    public void A_Bar_With_No_Width_Has_No_Segments() =>
        Assert.Empty(SeekBarMath.ChapterSegments([0.5], 0, 2, 6));
}
