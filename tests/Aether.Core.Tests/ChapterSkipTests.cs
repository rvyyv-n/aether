using Aether.Core.Playback.Chapters;
using Xunit;

namespace Aether.Core.Tests;

public class ChapterSkipTests
{
    private static readonly string[] Names = ["Sponsor", "Intro", "Outro"];

    private static ChapterSkipper Skipper(ChapterList chapters, params string[] names)
    {
        var skipper = new ChapterSkipper();
        skipper.Reset(chapters, names.Length == 0 ? Names : names);
        return skipper;
    }

    [Fact]
    public void Parse_Splits_On_Commas_And_Lines_Trims_And_Drops_Empties_And_Repeats()
    {
        IReadOnlyList<string> names = ChapterSkipRules.Parse(" Sponsor ,intro\r\nOutro,, SPONSOR\n\n  ");

        Assert.Equal(["Sponsor", "intro", "Outro"], names);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , \n ")]
    public void Parse_Gives_Nothing_For_Blank_Text(string? text)
    {
        Assert.Empty(ChapterSkipRules.Parse(text));
    }

    [Theory]
    [InlineData("Sponsor: NordVPN", true)]
    [InlineData("SPONSOR segment", true)]
    [InlineData("The Sponsor", true)]
    [InlineData("Sponsor", true)]
    [InlineData("Sponsorship talk", false)]
    [InlineData("Introduction to calculus", false)]
    [InlineData("Intro", true)]
    [InlineData("Intro (cold open)", true)]
    [InlineData("Re-Intro", true)]
    [InlineData("Not an outro_x", false)]
    [InlineData("", false)]
    public void Matches_Whole_Words_Only(string title, bool expected)
    {
        Assert.Equal(expected, ChapterSkipRules.Matches(title, Names));
    }

    [Fact]
    public void Matches_A_Word_Sequence_And_Treats_Regex_Characters_Literally()
    {
        Assert.True(ChapterSkipRules.Matches("Brought to you by Foo", ["to you by"]));
        Assert.True(ChapterSkipRules.Matches("Ad (sponsored)", ["(sponsored)"]));
        Assert.False(ChapterSkipRules.Matches("Adx", ["A.x"]));
    }

    [Fact]
    public void Skips_A_Matching_Chapter_When_Playback_Enters_It()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "Sponsor: X"), (130, "More story")]));

        Assert.Null(skipper.OnPosition(0.5, 300));
        Assert.Null(skipper.OnPosition(99.9, 300));
        ChapterSkipDecision? decision = skipper.OnPosition(100.2, 300);

        Assert.Equal(new ChapterSkipDecision(130, "Sponsor: X"), decision);
    }

    [Fact]
    public void Does_Not_Skip_A_Chapter_That_Does_Not_Match()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "Sponsorship talk")]));

        Assert.Null(skipper.OnPosition(1, 300));
        Assert.Null(skipper.OnPosition(100.1, 300));
    }

    [Fact]
    public void Skips_Each_Chapter_At_Most_Once()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "Sponsor"), (130, "More")]));

        skipper.OnPosition(1, 300);
        Assert.NotNull(skipper.OnPosition(100.1, 300));
        Assert.Null(skipper.OnPosition(131, 300));

        // Watching it again after jumping back (without telling the skipper) must not skip it again.
        Assert.Null(skipper.OnPosition(50, 300));
        Assert.Null(skipper.OnPosition(100.2, 300));
    }

    [Fact]
    public void Staying_Inside_The_Chapter_Does_Not_Skip_Again()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "Sponsor"), (130, "More")]));

        skipper.OnPosition(1, 300);
        Assert.NotNull(skipper.OnPosition(100.1, 300));
        Assert.Null(skipper.OnPosition(100.6, 300));
    }

    [Fact]
    public void A_User_Seek_Into_A_Matching_Chapter_Plays_It()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "Sponsor"), (130, "More")]));

        skipper.OnPosition(1, 300);
        skipper.OnUserSeek(105);

        Assert.Null(skipper.OnPosition(105, 300));
        Assert.Null(skipper.OnPosition(110, 300));
    }

    [Fact]
    public void A_User_Seek_To_A_Normal_Chapter_Does_Not_Stop_Later_Skips()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "More"), (200, "Sponsor"), (230, "End")]));

        skipper.OnPosition(1, 300);
        skipper.OnUserSeek(150);
        Assert.Null(skipper.OnPosition(150, 300));

        Assert.Equal(new ChapterSkipDecision(230, "Sponsor"), skipper.OnPosition(200.1, 300));
    }

    [Fact]
    public void An_Intro_At_The_Start_Is_Skipped_When_Playback_Begins_Inside_It()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Intro"), (45, "Episode")]));

        Assert.Equal(new ChapterSkipDecision(45, "Intro"), skipper.OnPosition(0, 600));
    }

    [Fact]
    public void An_Intro_Is_Still_Skipped_At_Up_To_One_Second()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Intro"), (45, "Episode")]));

        Assert.NotNull(skipper.OnPosition(1, 600));
    }

    [Fact]
    public void Resuming_Midway_Into_A_Matching_Chapter_Does_Not_Skip_It()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "Sponsor"), (130, "More")]));

        Assert.Null(skipper.OnPosition(110, 300));
        Assert.Null(skipper.OnPosition(111, 300));
    }

    [Fact]
    public void An_Outro_Running_To_The_End_Targets_The_Duration()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (280, "Outro")]));

        skipper.OnPosition(1, 300);

        Assert.Equal(new ChapterSkipDecision(300, "Outro"), skipper.OnPosition(280.1, 300));
    }

    [Fact]
    public void A_Chapter_Ending_Within_A_Second_Of_The_End_Targets_The_Duration()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "Sponsor"), (299.5, "Credits")]));

        skipper.OnPosition(1, 300);

        Assert.Equal(300, skipper.OnPosition(100.1, 300)!.Value.Target);
    }

    [Fact]
    public void The_Last_Chapter_Is_Not_Skipped_While_The_Duration_Is_Unknown()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (280, "Outro")]));

        skipper.OnPosition(1, 0);

        Assert.Null(skipper.OnPosition(280.1, 0));
    }

    [Fact]
    public void Back_To_Back_Matching_Chapters_Are_Skipped_In_One_Go_Reporting_The_First_Title()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "Intro"), (110, "Sponsor"), (140, "Show")]));

        skipper.OnPosition(1, 300);

        Assert.Equal(new ChapterSkipDecision(140, "Intro"), skipper.OnPosition(100.1, 300));
        Assert.Null(skipper.OnPosition(140.5, 300));
    }

    [Fact]
    public void Nothing_Is_Skipped_When_There_Are_No_Names()
    {
        var skipper = new ChapterSkipper();
        skipper.Reset(ChapterList.Create([(0, "Intro"), (45, "Episode")]), []);

        Assert.Null(skipper.OnPosition(0, 600));
    }

    [Fact]
    public void Nothing_Is_Skipped_Without_Chapters()
    {
        var skipper = Skipper(ChapterList.Empty);

        Assert.Null(skipper.OnPosition(0, 600));
        Assert.Null(skipper.OnPosition(30, 600));
    }

    [Fact]
    public void Position_Before_The_First_Chapter_Is_Ignored()
    {
        var skipper = Skipper(ChapterList.Create([(10, "Intro"), (50, "Show")]));

        Assert.Null(skipper.OnPosition(2, 600));
        Assert.Equal(new ChapterSkipDecision(50, "Intro"), skipper.OnPosition(10.1, 600));
    }

    [Fact]
    public void Reset_Forgets_What_Was_Skipped()
    {
        ChapterList chapters = ChapterList.Create([(0, "Intro"), (45, "Episode")]);
        var skipper = Skipper(chapters);
        Assert.NotNull(skipper.OnPosition(0, 600));

        skipper.Reset(chapters, Names);

        Assert.NotNull(skipper.OnPosition(0, 600));
    }

    [Fact]
    public void A_Position_Exactly_On_The_Boundary_Belongs_To_The_New_Chapter()
    {
        var skipper = Skipper(ChapterList.Create([(0, "Story"), (100, "Sponsor"), (130, "More")]));

        skipper.OnPosition(99.99, 300);

        Assert.Equal(130, skipper.OnPosition(100, 300)!.Value.Target);
    }
}
