using Aether.Core.Playback.Tracks;

namespace Aether.Core.Tests;

public class DualSubtitleTests
{
    private const string Both = """
        [
          {"id":1,"type":"audio","selected":true},
          {"id":1,"type":"sub","selected":true,"main-selection":0,"lang":"eng"},
          {"id":2,"type":"sub","selected":true,"main-selection":1,"lang":"jpn"},
          {"id":3,"type":"sub","selected":false,"external":true,"external-filename":"C:\\v\\film.ja.srt"}
        ]
        """;

    [Fact]
    public void The_Second_Selection_Is_The_Secondary_Subtitle_Not_The_Main_One()
    {
        TrackList list = TrackList.Parse(Both);

        Assert.Equal(1, list.SelectedSubtitle?.Id);
        Assert.Equal(2, list.SecondarySubtitle?.Id);
        Assert.False(list.Subtitles[1].Selected);
        Assert.True(list.Subtitles[1].Secondary);
        Assert.False(list.Subtitles[0].Secondary);
    }

    [Fact]
    public void Without_Main_Selection_A_Selected_Track_Is_The_Main_One() =>
        Assert.Equal(1, TrackList.Parse("""[{"id":1,"type":"sub","selected":true}]""").SelectedSubtitle?.Id);

    [Fact]
    public void An_Engine_Restart_Puts_The_Secondary_Subtitle_Back()
    {
        TrackChoices picked = TrackChoices.From(new TrackSnapshot(TrackList.Parse(Both), 0, 0));
        Assert.Equal(2, picked.SecondaryId);

        TrackSnapshot fresh = new(TrackList.Parse("""
            [
              {"id":1,"type":"sub","selected":true,"main-selection":0},
              {"id":2,"type":"sub","selected":false}
            ]
            """), 0, 0);

        Assert.Contains(picked.Select(fresh), c => c.SequenceEqual(["set", "secondary-sid", "2"]));
    }

    [Fact]
    public void A_Secondary_File_Is_Found_Again_By_Name()
    {
        TrackChoices picked = TrackChoices.From(new TrackSnapshot(TrackList.Parse("""
            [
              {"id":1,"type":"sub","selected":true,"main-selection":0},
              {"id":3,"type":"sub","selected":true,"main-selection":1,"external":true,"external-filename":"C:\\v\\film.ja.srt"}
            ]
            """), 0, 0));

        TrackSnapshot fresh = new(TrackList.Parse("""
            [
              {"id":1,"type":"sub","selected":true,"main-selection":0},
              {"id":5,"type":"sub","selected":false,"external":true,"external-filename":"C:/v/film.ja.srt"}
            ]
            """), 0, 0);

        Assert.Contains(picked.Select(fresh), c => c.SequenceEqual(["set", "secondary-sid", "5"]));
    }

    [Fact]
    public void No_Secondary_Turns_One_Off_And_Leaves_None_Alone()
    {
        TrackChoices none = TrackChoices.From(new TrackSnapshot(TrackList.Parse("""[{"id":1,"type":"sub","selected":true,"main-selection":0}]"""), 0, 0));

        Assert.DoesNotContain(none.Select(new TrackSnapshot(TrackList.Parse("""[{"id":1,"type":"sub","selected":true,"main-selection":0}]"""), 0, 0)), c => c[1] == "secondary-sid");
        Assert.Contains(none.Select(new TrackSnapshot(TrackList.Parse(Both), 0, 0)), c => c.SequenceEqual(["set", "secondary-sid", "no"]));
    }
}
