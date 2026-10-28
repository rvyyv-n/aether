using Aether.Core.Playback.Tracks;

namespace Aether.Core.Tests;

public class TrackPreferenceTests
{
    private static MediaTrack Audio(int id, string? lang, string? title = null, bool selected = false) =>
        new(id, TrackKind.Audio, lang, title, "stereo", 2, false, false, selected, null);

    private static MediaTrack Sub(int id, string? lang, string? title = null, bool forced = false, bool selected = false, string? file = null) =>
        new(id, TrackKind.Subtitle, lang, title, null, 0, false, forced, selected, file);

    private static TrackList List(params MediaTrack[] tracks) =>
        new(tracks.Where(t => t.Kind == TrackKind.Audio).ToList(), tracks.Where(t => t.Kind == TrackKind.Subtitle).ToList());

    [Fact]
    public void The_Next_File_Gets_The_Same_Languages_By_Their_Own_Ids()
    {
        TrackPreference preference = TrackPreference.From(List(
            Audio(1, "eng"), Audio(2, "jpn", selected: true), Sub(1, "eng", "Full", selected: true), Sub(2, "eng", "Signs", forced: true)));

        var next = List(Audio(1, "jpn"), Audio(2, "eng", selected: true), Sub(1, "eng", "Signs", forced: true, selected: true), Sub(2, "eng", "Full"));

        Assert.Equal([["set", "aid", "1"], ["set", "sid", "2"]], preference.Select(next));
    }

    [Fact]
    public void Title_Breaks_A_Tie_Between_Tracks_Of_One_Language()
    {
        TrackPreference preference = TrackPreference.From(List(Audio(1, "eng", "Commentary", selected: true)));

        var next = List(Audio(4, "eng", "Main", selected: true), Audio(5, "eng", "Commentary"));

        Assert.Equal([["set", "aid", "5"]], preference.Select(next));
    }

    [Fact]
    public void Subtitles_Turned_Off_Stay_Off()
    {
        TrackPreference preference = TrackPreference.From(List(Audio(1, "eng", selected: true), Sub(1, "eng")));

        Assert.Equal([["set", "sid", "no"]], preference.Select(List(Audio(1, "eng", selected: true), Sub(3, "eng", selected: true))));
        Assert.Empty(preference.Select(List(Audio(1, "eng", selected: true), Sub(3, "eng"))));
    }

    [Fact]
    public void A_Language_The_Next_File_Lacks_Leaves_Its_Default()
    {
        TrackPreference preference = TrackPreference.From(List(Audio(1, "fra", selected: true), Sub(1, "fra", selected: true)));

        Assert.Empty(preference.Select(List(Audio(1, "eng", selected: true), Sub(1, "eng", selected: true))));
    }

    [Fact]
    public void An_Untagged_Track_Is_Matched_By_Title()
    {
        TrackPreference preference = TrackPreference.From(List(Audio(2, null, "Director", selected: true)));

        Assert.Equal([["set", "aid", "7"]], preference.Select(List(Audio(6, null, "Main", selected: true), Audio(7, null, "Director"))));
    }

    [Fact]
    public void An_External_Subtitle_File_Is_Not_Carried()
    {
        TrackPreference preference = TrackPreference.From(List(Sub(9, "eng", selected: true, file: @"C:\show\ep1.srt")));

        Assert.Empty(preference.Select(List(Sub(1, "eng"), Sub(2, "jpn", selected: true))));
    }
}
