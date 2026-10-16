using Aether.Core.Playback.Chapters;
using Aether.Core.Playback.Settings;
using Xunit;

namespace Aether.Core.Tests;

public class ChapterListTests
{
    private static ChapterList Three() => ChapterList.Create([(0, "A"), (60, "B"), (120, "C")]);

    [Fact]
    public void Create_Sorts_Clamps_And_Collapses_Duplicate_Starts()
    {
        ChapterList list = ChapterList.Create([(120, "C"), (-5, "A"), (60, "B"), (60, "B again"), (double.NaN, "Bad")]);

        Assert.Equal([0d, 60d, 120d], list.Items.Select(c => c.Start));
        Assert.Equal("A", list[0].Title);
        Assert.Equal("B", list[1].Title);
    }

    [Fact]
    public void Create_With_Nothing_Gives_The_Empty_List()
    {
        Assert.Equal(0, ChapterList.Create([]).Count);
    }

    [Fact]
    public void EndOf_Is_The_Next_Start_Or_The_Duration()
    {
        ChapterList list = Three();

        Assert.Equal(60, list.EndOf(0, 200));
        Assert.Equal(200, list.EndOf(2, 200));
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(59.9, 0)]
    [InlineData(60, 1)]
    [InlineData(500, 2)]
    public void IndexAt_Finds_The_Chapter_Containing_The_Position(double position, int expected)
    {
        Assert.Equal(expected, Three().IndexAt(position));
    }

    [Fact]
    public void IndexAt_Is_Minus_One_Before_The_First_Chapter_And_When_Empty()
    {
        Assert.Equal(-1, ChapterList.Create([(10, "A")]).IndexAt(5));
        Assert.Equal(-1, ChapterList.Empty.IndexAt(5));
    }

    [Fact]
    public void NextStart_Is_The_Following_Chapter_Or_Null_In_The_Last()
    {
        ChapterList list = Three();

        Assert.Equal(60, list.NextStart(10));
        Assert.Equal(120, list.NextStart(60));
        Assert.Null(list.NextStart(120));
        Assert.Null(ChapterList.Empty.NextStart(10));
    }

    [Fact]
    public void NextStart_Before_The_First_Chapter_Goes_To_The_First()
    {
        Assert.Equal(10, ChapterList.Create([(10, "A"), (20, "B")]).NextStart(2));
    }

    [Fact]
    public void PreviousStart_Restarts_The_Current_Chapter_When_More_Than_Three_Seconds_In()
    {
        Assert.Equal(60, Three().PreviousStart(63.5));
    }

    [Fact]
    public void PreviousStart_Goes_To_The_Chapter_Before_When_Within_Three_Seconds()
    {
        ChapterList list = Three();

        Assert.Equal(0, list.PreviousStart(62));
        Assert.Equal(0, list.PreviousStart(63));
        Assert.Equal(60, list.PreviousStart(122.5));
    }

    [Fact]
    public void PreviousStart_In_The_First_Chapter_Or_Before_It_Goes_To_Zero()
    {
        Assert.Equal(0, Three().PreviousStart(1));
        Assert.Equal(0, Three().PreviousStart(2.9));
        Assert.Equal(0, ChapterList.Create([(10, "A")]).PreviousStart(4));
        Assert.Equal(0, ChapterList.Empty.PreviousStart(40));
    }

    [Fact]
    public void PreviousStart_In_The_First_Chapter_Past_The_Threshold_Goes_To_Its_Start()
    {
        Assert.Equal(10, ChapterList.Create([(10, "A")]).PreviousStart(20));
    }

    [Fact]
    public void Fractions_Skip_A_Chapter_At_Zero_And_Any_At_Or_After_The_End()
    {
        ChapterList list = ChapterList.Create([(0, "A"), (25, "B"), (50, "C"), (100, "D"), (150, "E")]);

        Assert.Equal([0.25, 0.5], list.Fractions(100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    public void Fractions_Are_Empty_Without_A_Duration(double duration)
    {
        Assert.Empty(Three().Fractions(duration));
    }

    [Fact]
    public void Parse_Reads_Mpvs_Chapter_List()
    {
        ChapterList list = ChapterList.Parse("""[{"title":"Intro","time":0.0},{"title":"Main","time":12.5}]""");

        Assert.Equal(2, list.Count);
        Assert.Equal(new Chapter(12.5, "Main"), list[1]);
    }

    [Fact]
    public void Parse_Keeps_Chapters_Without_A_Title_And_Drops_Ones_Without_A_Time()
    {
        ChapterList list = ChapterList.Parse("""[{"time":5},{"title":"No time"},{"title":"x","time":"soon"},3,{"title":"Ok","time":9}]""");

        Assert.Equal([new Chapter(5, ""), new Chapter(9, "Ok")], list.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"time\":1}")]
    [InlineData("[{\"title\":")]
    [InlineData("[]")]
    public void Parse_Gives_An_Empty_List_For_Junk(string? json)
    {
        Assert.Equal(0, ChapterList.Parse(json).Count);
    }

    [Fact]
    public void DisplayTitle_Uses_The_Title_Or_A_Numbered_Fallback()
    {
        ChapterList list = ChapterList.Create([(0, "Cold open"), (10, ""), (20, "  ")]);

        Assert.Equal("Cold open", list.DisplayTitle(0, "Chapter {0}"));
        Assert.Equal("Chapter 2", list.DisplayTitle(1, "Chapter {0}"));
        Assert.Equal("Kapitel 3", list.DisplayTitle(2, "Kapitel {0}"));
    }

    [Fact]
    public void Chapter_Skip_Settings_Have_Their_Defaults()
    {
        var settings = new AetherSettings();

        Assert.False(settings.ChapterAutoSkip);
        Assert.Equal("Sponsor, Intro, Outro", settings.ChapterSkipNames);
    }
}
