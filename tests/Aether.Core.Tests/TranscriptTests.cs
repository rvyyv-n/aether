using Aether.Core.Playback.Tracks;

namespace Aether.Core.Tests;

public class TranscriptTests
{
    [Fact]
    public void Srt_Cues_Become_Timed_Lines_With_Tags_Removed()
    {
        const string srt = "﻿1\r\n00:00:01,000 --> 00:00:02,500\r\n<i>Hello</i> there\r\n{\\an8}General\r\n\r\n2\r\n00:01:03,250 --> 00:01:05,000\r\nSecond <b>line</b>\r\n";

        IReadOnlyList<TranscriptLine> lines = Transcript.Parse(srt, ".srt");

        Assert.Equal(2, lines.Count);
        Assert.Equal(new TranscriptLine(1, 2.5, "Hello there General"), lines[0]);
        Assert.Equal(63.25, lines[1].Start);
        Assert.Equal("Second line", lines[1].Text);
    }

    [Fact]
    public void Vtt_Skips_Its_Header_Notes_And_Styles_And_Reads_Short_Times()
    {
        const string vtt = """
            WEBVTT Kind: captions

            NOTE a comment
            that spans lines

            STYLE
            ::cue { color: yellow }

            intro
            00:05.000 --> 00:07.500 align:start position:10%
            <v Ann>Hi</v> <c.loud>you</c>

            01:00:00.000 --> 01:00:01.000
            Late &amp; last
            """;

        IReadOnlyList<TranscriptLine> lines = Transcript.Parse(vtt, ".vtt");

        Assert.Equal([new TranscriptLine(5, 7.5, "Hi you"), new TranscriptLine(3600, 3601, "Late & last")], lines);
    }

    [Fact]
    public void Ass_Takes_The_Text_Field_Even_With_Commas_And_Drops_Overrides_And_Drawings()
    {
        const string ass = """
            [Script Info]
            Title: x

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,not shown
            Dialogue: 0,0:00:02.50,0:00:04.00,Default,,0,0,0,,{\i1}Well,{\i0} hello,\Nworld
            Dialogue: 0,0:00:05.00,0:00:06.00,Sign,,0,0,0,,{\p1}m 0 0 l 10 10{\p0}
            Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,First\hby time
            """;

        IReadOnlyList<TranscriptLine> lines = Transcript.Parse(ass, ".ass");

        Assert.Equal([new TranscriptLine(1, 2, "First by time"), new TranscriptLine(2.5, 4, "Well, hello, world")], lines);
    }

    [Fact]
    public void Broken_Blocks_Are_Skipped_And_Unknown_Types_Give_Nothing()
    {
        Assert.Single(Transcript.Parse("x\n00:00:01,000 --> nonsense\nbad\n\n2\n00:00:03,000 --> 00:00:04,000\ngood\n", ".srt"));
        Assert.Empty(Transcript.Parse("anything", ".sub"));
        Assert.Empty(Transcript.Parse("", ".srt"));
    }

    [Fact]
    public void Search_Keeps_Lines_With_Every_Word_In_Any_Case()
    {
        TranscriptLine[] lines =
        [
            new(1, 2, "Where is the treasure?"),
            new(3, 4, "The TREASURE is buried"),
            new(5, 6, "Nothing here"),
        ];

        Assert.Equal([lines[0], lines[1]], Transcript.Search(lines, "treasure"));
        Assert.Equal([lines[1]], Transcript.Search(lines, "  buried   the "));
        Assert.Equal(lines, Transcript.Search(lines, " "));
    }

    private static MediaTrack Sub(int id, string? codec, string? file = null, bool selected = false, int? index = null) =>
        new(id, TrackKind.Subtitle, null, null, null, 0, false, false, selected, file, Codec: codec, StreamIndex: index);

    [Fact]
    public void Text_Subtitles_Can_Be_Read_And_Pictures_Cannot()
    {
        Assert.True(Transcript.IsText(Sub(1, "subrip")));
        Assert.True(Transcript.IsText(Sub(1, "ass")));
        Assert.True(Transcript.IsText(Sub(1, "webvtt")));
        Assert.True(Transcript.IsText(Sub(1, "mov_text")));
        Assert.True(Transcript.IsText(Sub(1, null, @"C:\v\film.en.srt")));
        Assert.False(Transcript.IsText(Sub(1, "hdmv_pgs_subtitle")));
        Assert.False(Transcript.IsText(Sub(1, "dvd_subtitle")));
        Assert.False(Transcript.IsText(Sub(1, "subrip", @"C:\v\film.idx")));
    }

    [Fact]
    public void The_Transcript_Follows_The_Subtitle_On_Screen_Else_A_File_Else_An_Embedded_Text_Track()
    {
        MediaTrack pictures = Sub(1, "hdmv_pgs_subtitle", selected: true);
        MediaTrack embedded = Sub(2, "subrip", index: 3);
        MediaTrack file = Sub(3, "subrip", @"C:\v\film.srt");
        MediaTrack shown = Sub(4, "ass", selected: true, index: 5);

        Assert.Equal(shown, Transcript.Source(new TrackList([], [embedded, shown])));
        Assert.Equal(file, Transcript.Source(new TrackList([], [pictures, embedded, file])));
        Assert.Equal(embedded, Transcript.Source(new TrackList([], [pictures, embedded])));
        Assert.Null(Transcript.Source(new TrackList([], [pictures])));
    }

    [Fact]
    public void An_Embedded_Track_Is_Pulled_Out_As_Srt_By_Its_Stream_Index() =>
        Assert.Equal(
            ["-v", "error", "-nostdin", "-i", @"C:\v\film.mkv", "-map", "0:3", "-f", "srt", "pipe:1"],
            Transcript.ExtractArguments(@"C:\v\film.mkv", 3));

    [Fact]
    public void The_Track_List_Gives_The_Codec_And_Stream_Index()
    {
        TrackList list = TrackList.Parse("""[{"id":1,"type":"sub","codec":"subrip","ff-index":4}]""");

        Assert.Equal("subrip", list.Subtitles[0].Codec);
        Assert.Equal(4, list.Subtitles[0].StreamIndex);
    }

    [Fact]
    public void The_Line_Playing_Now_Is_The_Last_One_Started()
    {
        TranscriptLine[] lines = [new(1, 2, "a"), new(3, 4, "b"), new(10, 12, "c")];

        Assert.Equal(-1, Transcript.IndexAt(lines, 0.5));
        Assert.Equal(0, Transcript.IndexAt(lines, 1.5));
        Assert.Equal(1, Transcript.IndexAt(lines, 6));
        Assert.Equal(2, Transcript.IndexAt(lines, 100));
    }
}
