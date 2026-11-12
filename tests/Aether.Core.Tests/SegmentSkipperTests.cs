using Aether.Core.Playback.Streams;

namespace Aether.Core.Tests;

public class SegmentSkipperTests
{
    private static readonly SponsorSegment Intro = new(0, 7.5, SponsorCategory.Intro);
    private static readonly SponsorSegment Ad = new(30, 60, SponsorCategory.Sponsor);
    private static readonly SponsorSegment Promo = new(60.2, 75, SponsorCategory.SelfPromo);
    private static readonly SponsorSegment Outro = new(200, 212, SponsorCategory.Outro);

    private static SegmentSkipper Skipper(params SponsorSegment[] segments)
    {
        var skipper = new SegmentSkipper();
        skipper.Reset(segments);
        return skipper;
    }

    [Fact]
    public void Playing_Into_A_Segment_Skips_To_Its_End_Once()
    {
        SegmentSkipper skipper = Skipper(Ad);

        Assert.Null(skipper.OnPosition(29.9));
        Assert.Equal(new SegmentSkip(Ad, 60), skipper.OnPosition(30.1));
        Assert.Null(skipper.OnPosition(30.3));
        Assert.Null(skipper.OnPosition(45));
    }

    [Fact]
    public void A_Segment_At_The_Start_Is_Skipped_As_Playback_Begins() =>
        Assert.Equal(new SegmentSkip(Intro, 7.5), Skipper(Intro).OnPosition(0));

    [Fact]
    public void Resuming_Inside_A_Segment_Still_Skips_It() =>
        Assert.Equal(new SegmentSkip(Ad, 60), Skipper(Ad).OnPosition(42));

    [Fact]
    public void A_Segment_The_User_Seeked_Into_Plays()
    {
        SegmentSkipper skipper = Skipper(Ad);

        skipper.OnUserSeek(40);

        Assert.Null(skipper.OnPosition(40.2));
        Assert.Null(skipper.OnPosition(59));
    }

    [Fact]
    public void Back_From_The_Toast_Plays_The_Segment_Without_Skipping_Again()
    {
        SegmentSkipper skipper = Skipper(Ad);
        SegmentSkip skip = skipper.OnPosition(30.1)!.Value;

        skipper.OnUserSeek(skip.Segment.Start);

        Assert.Null(skipper.OnPosition(30.2));
    }

    [Fact]
    public void Segments_That_Touch_Are_Skipped_In_One_Jump()
    {
        SegmentSkipper skipper = Skipper(Ad, Promo);

        Assert.Equal(new SegmentSkip(Ad, 75), skipper.OnPosition(30.1));
        Assert.Null(skipper.OnPosition(75.1));
        Assert.Null(skipper.OnPosition(61));
    }

    [Fact]
    public void Overlapping_Segments_End_Where_The_Last_Ends()
    {
        var longer = new SponsorSegment(40, 90, SponsorCategory.Interaction);

        Assert.Equal(new SegmentSkip(Ad, 90), Skipper(Ad, longer).OnPosition(31));
    }

    [Fact]
    public void Nothing_Is_Skipped_In_The_Last_Moment_Of_A_Segment() =>
        Assert.Null(Skipper(Ad).OnPosition(59.9));

    [Fact]
    public void The_Offer_Names_The_Segment_Playing_Without_Using_It_Up()
    {
        SegmentSkipper skipper = Skipper(Ad, Promo, Outro);

        Assert.Null(skipper.Offer(10));
        Assert.Equal(new SegmentSkip(Ad, 75), skipper.Offer(35));
        Assert.Equal(new SegmentSkip(Ad, 75), skipper.Offer(36));

        skipper.Done(skipper.Offer(36)!.Value);

        Assert.Null(skipper.Offer(37));
        Assert.Null(skipper.Offer(70));
        Assert.Equal(new SegmentSkip(Outro, 212), skipper.Offer(205));
    }

    [Fact]
    public void A_New_Video_Starts_Over()
    {
        SegmentSkipper skipper = Skipper(Ad);
        skipper.OnPosition(31);

        skipper.Reset([Ad]);

        Assert.Equal(new SegmentSkip(Ad, 60), skipper.OnPosition(31));
        skipper.Reset([]);
        Assert.Null(skipper.OnPosition(31));
    }
}
