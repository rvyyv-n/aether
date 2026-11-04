using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class QueueRowsTests
{
    [Fact]
    public void Rows_Name_Files_By_Their_File_Name_And_Mark_The_Playing_One()
    {
        IReadOnlyList<QueueRow> rows = QueueRows.Build([@"C:\show\e01.mkv", @"C:\show\e02.mkv"], current: 1, filter: null);

        Assert.Equal(["e01.mkv", "e02.mkv"], rows.Select(r => r.Title));
        Assert.Equal([0, 1], rows.Select(r => r.Index));
        Assert.Equal([false, true], rows.Select(r => r.IsPlaying));
    }

    [Fact]
    public void Links_Are_Named_By_Site_And_Path()
    {
        Assert.Equal("youtube.com/watch?v=abc", QueueRows.Title("https://www.youtube.com/watch?v=abc"));
        Assert.Equal("example.com", QueueRows.Title("https://example.com/"));
    }

    [Fact]
    public void Filter_Matches_Any_Part_Of_The_Name_Ignoring_Case_And_Keeps_Queue_Indexes()
    {
        IReadOnlyList<QueueRow> rows = QueueRows.Build(
            [@"C:\a\Intro.mp4", @"C:\a\Episode 1.mp4", @"C:\a\episode 2.mp4"], current: 0, filter: "  EPISODE ");

        Assert.Equal(["Episode 1.mp4", "episode 2.mp4"], rows.Select(r => r.Title));
        Assert.Equal([1, 2], rows.Select(r => r.Index));
        Assert.All(rows, r => Assert.False(r.IsPlaying));
    }

    [Fact]
    public void Filter_Does_Not_Look_At_The_Folder()
    {
        IReadOnlyList<QueueRow> rows = QueueRows.Build([@"C:\episode\clip.mp4"], current: -1, filter: "episode");

        Assert.Empty(rows);
    }

    [Theory]
    [InlineData(RepeatMode.None, RepeatMode.All)]
    [InlineData(RepeatMode.All, RepeatMode.One)]
    [InlineData(RepeatMode.One, RepeatMode.None)]
    public void Repeat_Button_Cycles_Off_All_One(RepeatMode from, RepeatMode to) =>
        Assert.Equal(to, QueueRows.NextRepeat(from));

    [Theory]
    [InlineData(new[] { "a", "b", "c", "d" }, new[] { "b", "c", "a", "d" }, 0, 2)]
    [InlineData(new[] { "a", "b", "c", "d" }, new[] { "d", "a", "b", "c" }, 3, 0)]
    [InlineData(new[] { "a", "b", "c", "d" }, new[] { "a", "c", "b", "d" }, 1, 2)]
    [InlineData(new[] { "a", "b" }, new[] { "b", "a" }, 0, 1)]
    public void A_Drag_Is_Read_Back_As_One_Move(string[] before, string[] after, int from, int to) =>
        Assert.Equal((from, to), QueueRows.FindMove(before, after));

    [Fact]
    public void Same_Order_Is_No_Move() => Assert.Null(QueueRows.FindMove(["a", "b"], ["a", "b"]));

    [Fact]
    public void Two_Moves_Or_Different_Items_Are_Not_One_Move()
    {
        Assert.Null(QueueRows.FindMove(["a", "b", "c", "d"], ["b", "a", "d", "c"]));
        Assert.Null(QueueRows.FindMove(["a", "b"], ["a", "c"]));
        Assert.Null(QueueRows.FindMove(["a", "b"], ["a"]));
    }

    [Fact]
    public void A_Duplicate_Item_Moving_Is_Still_Found()
    {
        object a1 = new(), a2 = new(), b = new();

        Assert.Equal((0, 2), QueueRows.FindMove([a1, b, a2], [b, a2, a1]));
    }
}
