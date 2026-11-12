using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class FolderPlaylistTests
{
    private const string Dir = @"C:\show\";

    private static string[] Files(params string[] names) => names.Select(n => Dir + n).ToArray();

    [Fact]
    public void Videos_Are_In_Natural_Order()
    {
        Assert.Equal(Files("ep1.mkv", "ep2.mkv", "ep10.mkv"), FolderPlaylist.Videos(Files("ep10.mkv", "ep1.mkv", "ep2.mkv")));
    }

    [Fact]
    public void Other_Formats_Are_Skipped()
    {
        Assert.Equal(Files("a.mp4", "e.MKV"), FolderPlaylist.Videos(Files("a.mp4", "b.txt", "c.srt", "d.avi", "e.MKV")));
    }

    [Fact]
    public void Empty_Folder_Has_No_Videos()
    {
        Assert.Empty(FolderPlaylist.Videos([]));
    }

    [Fact]
    public void Sorting_Uses_The_File_Name_Not_The_Folder()
    {
        Assert.Equal([@"C:.mp4", @"C:.mp4"], FolderPlaylist.Videos([@"C:.mp4", @"C:.mp4"]));
    }

    [Fact]
    public void Dropped_Files_Come_First_In_Natural_Order_Then_Each_Folder()
    {
        var listings = new Dictionary<string, string[]>
        {
            [@"C:\b-season"] = [@"C:\b-season\e2.mkv", @"C:\b-season\notes.txt", @"C:\b-season\e1.mkv"],
            [@"C:\a-season"] = [@"C:\a-season\x.mp4"],
        };

        string[] queue = FolderPlaylist.Dropped(
            Files("ep10.mkv", "readme.txt", "ep2.mkv"), [@"C:\b-season", @"C:\a-season"], folder => listings[folder]);

        Assert.Equal([Dir + "ep2.mkv", Dir + "ep10.mkv", @"C:\a-season\x.mp4", @"C:\b-season\e1.mkv", @"C:\b-season\e2.mkv"], queue);
    }

    [Fact]
    public void Dropping_Nothing_Playable_Gives_An_Empty_Queue()
    {
        Assert.Empty(FolderPlaylist.Dropped(Files("a.txt"), [@"C:\empty"], _ => []));
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
