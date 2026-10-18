using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class FolderPlaylistTests
{
    private const string Dir = @"C:\show\";

    private static string[] Files(params string[] names) => names.Select(n => Dir + n).ToArray();

    [Fact]
    public void Natural_Order_Puts_Ep2_Before_Ep10()
    {
        string[] files = Files("ep10.mkv", "ep1.mkv", "ep2.mkv");

        Assert.Equal(Dir + "ep2.mkv", FolderPlaylist.NextAfter(Dir + "ep1.mkv", files));
        Assert.Equal(Dir + "ep10.mkv", FolderPlaylist.NextAfter(Dir + "ep2.mkv", files));
    }

    [Fact]
    public void Last_File_Has_No_Next()
    {
        Assert.Null(FolderPlaylist.NextAfter(Dir + "ep10.mkv", Files("ep10.mkv", "ep2.mkv")));
    }

    [Fact]
    public void Other_Formats_Are_Skipped()
    {
        string[] files = Files("a.mp4", "b.txt", "c.srt", "d.avi", "e.MKV");

        Assert.Equal(Dir + "e.MKV", FolderPlaylist.NextAfter(Dir + "a.mp4", files));
    }

    [Fact]
    public void Current_File_Is_Found_Ignoring_Case()
    {
        Assert.Equal(Dir + "b.mp4", FolderPlaylist.NextAfter(Dir.ToUpperInvariant() + "A.MP4", Files("a.mp4", "b.mp4")));
    }

    [Fact]
    public void File_Missing_From_The_Listing_Has_No_Next()
    {
        Assert.Null(FolderPlaylist.NextAfter(Dir + "gone.mp4", Files("a.mp4", "b.mp4")));
    }

    [Fact]
    public void Empty_Folder_Has_No_Next()
    {
        Assert.Null(FolderPlaylist.NextAfter(Dir + "a.mp4", []));
    }

    [Fact]
    public void Previous_Goes_Back_In_The_Same_Order()
    {
        string[] files = Files("ep10.mkv", "ep1.mkv", "ep2.mkv");

        Assert.Equal(Dir + "ep2.mkv", FolderPlaylist.PreviousBefore(Dir + "ep10.mkv", files));
        Assert.Equal(Dir + "ep1.mkv", FolderPlaylist.PreviousBefore(Dir + "ep2.mkv", files));
        Assert.Null(FolderPlaylist.PreviousBefore(Dir + "ep1.mkv", files));
    }

    [Fact]
    public void Neighbours_Are_Missing_At_The_Ends_And_For_Unlisted_Files()
    {
        string[] files = Files("a.mp4", "b.mp4", "c.mp4");

        Assert.Equal(new FolderNeighbours(null, Dir + "b.mp4"), FolderPlaylist.NeighboursOf(Dir + "a.mp4", files));
        Assert.Equal(new FolderNeighbours(Dir + "a.mp4", Dir + "c.mp4"), FolderPlaylist.NeighboursOf(Dir + "b.mp4", files));
        Assert.Equal(new FolderNeighbours(Dir + "b.mp4", null), FolderPlaylist.NeighboursOf(Dir + "c.mp4", files));
        Assert.Equal(default, FolderPlaylist.NeighboursOf(Dir + "gone.mp4", files));
        Assert.Equal(default, FolderPlaylist.NeighboursOf(Dir + "a.mp4", Files("a.mp4")));
    }

    [Fact]
    public void Sorting_Uses_The_File_Name_Not_The_Folder()
    {
        string[] files = [@"C:\z\a.mp4", @"C:\z\b.mp4"];

        Assert.Equal(@"C:\z\b.mp4", FolderPlaylist.NextAfter(@"C:\z\a.mp4", files));
    }

    [Theory]
    [InlineData("ep2", "ep10", -1)]
    [InlineData("ep10", "ep2", 1)]
    [InlineData("ep02", "ep2", -1)]
    [InlineData("Alpha", "beta", -1)]
    [InlineData("A", "a", -1)]
    [InlineData("a1", "a1b", -1)]
    [InlineData("1", "a", -1)]
    [InlineData("file99999999999999999999999", "file100000000000000000000000", -1)]
    public void Natural_Order_Compares_Like_Explorer(string x, string y, int sign)
    {
        Assert.Equal(sign, Math.Sign(NaturalOrder.Instance.Compare(x, y)));
        Assert.Equal(-sign, Math.Sign(NaturalOrder.Instance.Compare(y, x)));
    }

    [Fact]
    public void Natural_Order_Treats_Identical_Names_As_Equal()
    {
        Assert.Equal(0, NaturalOrder.Instance.Compare("ep1.mp4", "ep1.mp4"));
        Assert.True(NaturalOrder.Instance.Compare(null, "a") < 0);
    }
}
