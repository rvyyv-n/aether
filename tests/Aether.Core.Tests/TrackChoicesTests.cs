using Aether.Core.Playback.Tracks;
using Xunit;

namespace Aether.Core.Tests;

public class TrackChoicesTests
{
    private static MediaTrack Audio(int id, bool selected) =>
        new(id, TrackKind.Audio, "eng", null, "stereo", 2, false, false, selected, null);

    private static MediaTrack Sub(int id, bool selected, string? file = null) =>
        new(id, TrackKind.Subtitle, "eng", null, null, 0, false, false, selected, file);

    private static TrackSnapshot Snapshot(MediaTrack[] audio, MediaTrack[] subs, double audioDelay = 0, double subDelay = 0) =>
        new(new TrackList(audio, subs), audioDelay, subDelay);

    [Fact]
    public void From_Reads_The_Selected_Tracks_Files_And_Delays()
    {
        TrackSnapshot snapshot = Snapshot(
            [Audio(1, false), Audio(2, true)],
            [Sub(1, false), Sub(2, true, @"C:\Videos\Film.srt")],
            audioDelay: 0.2,
            subDelay: -0.5);

        TrackChoices choices = TrackChoices.From(snapshot);

        Assert.Equal(2, choices.AudioId);
        Assert.Null(choices.SubtitleId);
        Assert.Equal(@"C:\Videos\Film.srt", choices.SubtitleFile);
        Assert.Equal([@"C:\Videos\Film.srt"], choices.ExternalFiles);
        Assert.Equal(0.2, choices.AudioDelay);
        Assert.Equal(-0.5, choices.SubtitleDelay);
    }

    [Fact]
    public void Nothing_Is_Sent_When_The_New_Engine_Already_Matches()
    {
        TrackSnapshot snapshot = Snapshot([Audio(1, true)], [Sub(1, true)], 0.1, 0.2);

        Assert.Empty(TrackChoices.From(snapshot).Select(snapshot));
    }

    [Fact]
    public void The_Chosen_Audio_And_Embedded_Subtitle_Come_Back()
    {
        TrackChoices choices = TrackChoices.From(Snapshot([Audio(1, false), Audio(2, true)], [Sub(1, false), Sub(2, true)]));
        TrackSnapshot fresh = Snapshot([Audio(1, true), Audio(2, false)], [Sub(1, true), Sub(2, false)]);

        IReadOnlyList<string[]> commands = choices.Select(fresh);

        Assert.Equal([["set", "aid", "2"], ["set", "sid", "2"]], commands);
    }

    [Fact]
    public void Subtitles_That_Were_Off_Are_Turned_Off_Again()
    {
        TrackChoices choices = TrackChoices.From(Snapshot([Audio(1, true)], [Sub(1, false)]));
        TrackSnapshot fresh = Snapshot([Audio(1, true)], [Sub(1, true)]);

        Assert.Equal([["set", "sid", "no"]], choices.Select(fresh));
    }

    [Fact]
    public void A_Subtitle_File_Is_Found_Again_By_Name_Not_By_Id()
    {
        TrackChoices choices = TrackChoices.From(Snapshot([Audio(1, true)], [Sub(1, false), Sub(2, true, @"C:\Videos\Film.srt")]));
        // The restart loaded the file first, so it has another id now, and mpv spells the path with slashes.
        TrackSnapshot fresh = Snapshot([Audio(1, true)], [Sub(1, false, "C:/Videos/film.srt"), Sub(2, true)]);

        Assert.Equal([["set", "sid", "1"]], choices.Select(fresh));
        Assert.Empty(choices.MissingFiles(fresh.Tracks));
    }

    [Fact]
    public void Files_That_Are_Not_Loaded_Yet_Are_Added_Again()
    {
        TrackChoices choices = TrackChoices.From(Snapshot(
            [Audio(1, true)], [Sub(1, false, @"C:\Videos\Film.en.srt"), Sub(2, true, @"C:\Videos\Extra.srt")]));
        TrackList fresh = new([Audio(1, true)], [Sub(1, false, @"C:\Videos\Film.en.srt")]);

        Assert.Equal([@"C:\Videos\Extra.srt"], choices.MissingFiles(fresh));
    }

    [Fact]
    public void A_Track_The_New_File_Lacks_Is_Left_Alone()
    {
        TrackChoices choices = new(AudioId: 5, SubtitleId: 7, SubtitleFile: null, 0, 0, []);
        TrackSnapshot fresh = Snapshot([Audio(1, true)], [Sub(1, true)]);

        Assert.Empty(choices.Select(fresh));
    }

    [Fact]
    public void Delays_Are_Set_Only_When_They_Differ()
    {
        TrackChoices choices = new(1, null, null, AudioDelay: 0.3, SubtitleDelay: -1.5, []);
        TrackSnapshot fresh = Snapshot([Audio(1, true)], [], audioDelay: 0.3, subDelay: 0);

        Assert.Equal([["set", "sub-delay", "-1.5"]], choices.Select(fresh));
    }
}
