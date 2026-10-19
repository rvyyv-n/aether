using System.Globalization;
using Aether.Core.Playback.Tracks;
using Xunit;

namespace Aether.Core.Tests;

public class TrackLabelTests
{
    private const string TrackText = "Track {0}";

    private static MediaTrack Audio(int id, string? lang, string? title = null, string? layout = null, int channels = 0) =>
        new(id, TrackKind.Audio, lang, title, layout, channels, false, false, false, null);

    private static MediaTrack Sub(int id, string? lang, string? title = null, bool sdh = false, bool forced = false, string? file = null) =>
        new(id, TrackKind.Subtitle, lang, title, null, 0, sdh, forced, false, file);

    private static string Label(MediaTrack track) => TrackLabels.Format(track, TrackText, "SDH", "Forced");

    private static string English => CultureInfo.GetCultureInfo("en").DisplayName;

    [Fact]
    public void A_Track_From_A_Web_Address_Is_Named_After_The_Video()
    {
        MediaTrack sub = Sub(3, "en", file: "https://www.youtube.com/api/timedtext?v=abc&lang=en");
        Assert.Equal("Make Your Prompts Dumber", TrackLabels.Format(sub, TrackText, "SDH", "Forced", "  Make Your Prompts Dumber "));
    }

    [Fact]
    public void A_Track_From_A_Web_Address_Falls_Back_To_Its_Language_Without_A_Video_Title()
    {
        MediaTrack sub = Sub(3, "en", file: "https://example.com/videoplayback?expire=1");
        Assert.Equal(English, Label(sub));
    }

    [Fact]
    public void Audio_Shows_Language_And_Channel_Layout()
    {
        Assert.Equal($"{English} · 5.1", Label(Audio(1, "eng", layout: "5.1(side)")));
    }

    [Fact]
    public void Audio_Title_Is_Added_When_It_Says_More_Than_The_Language()
    {
        Assert.Equal($"{English} · Commentary · 2.0", Label(Audio(2, "en", "Commentary", "stereo")));
    }

    [Theory]
    [InlineData("English")]
    [InlineData("eng")]
    [InlineData("ENG")]
    public void A_Title_That_Repeats_The_Language_Is_Left_Out(string title)
    {
        Assert.Equal($"{English} · 2.0", Label(Audio(1, "eng", title, "stereo")));
    }

    [Fact]
    public void A_Track_Without_A_Language_Is_Named_After_Its_Id()
    {
        Assert.Equal("Track 3 · 2.0", Label(Audio(3, null, layout: "stereo")));
        Assert.Equal("Track 4", Label(Sub(4, "")));
    }

    [Fact]
    public void Undetermined_Language_Counts_As_No_Language()
    {
        Assert.Equal("Track 2", Label(Sub(2, "und")));
    }

    [Theory]
    [InlineData("fre", "fr")]
    [InlineData("ger", "de")]
    [InlineData("jpn", "ja")]
    [InlineData("spa", "es")]
    public void Three_Letter_Tags_Resolve_To_The_Language(string tag, string culture)
    {
        Assert.Equal(CultureInfo.GetCultureInfo(culture).DisplayName, TrackLabels.LanguageName(tag));
    }

    [Fact]
    public void An_Unknown_Tag_Is_Shown_As_Written()
    {
        Assert.Equal("zzx", TrackLabels.LanguageName("zzx"));
    }

    [Fact]
    public void Subtitles_Add_Sdh_Or_Forced()
    {
        Assert.Equal($"{English} · SDH", Label(Sub(1, "eng", sdh: true)));
        Assert.Equal($"{English} · Forced", Label(Sub(2, "eng", forced: true)));
        Assert.Equal(English, Label(Sub(3, "eng")));
    }

    [Fact]
    public void A_Subtitle_Title_Sits_Between_Language_And_Flag()
    {
        Assert.Equal($"{English} · Signs · Forced", Label(Sub(1, "eng", "Signs", forced: true)));
        Assert.Equal($"{English} · SDH", Label(Sub(2, "eng", "SDH", sdh: true)));
    }

    [Fact]
    public void An_External_Subtitle_Shows_Its_File_Name()
    {
        Assert.Equal("Film.en.srt", Label(Sub(5, "eng", file: @"C:\Videos\Film.en.srt")));
    }

    [Theory]
    [InlineData("mono", 0, "1.0")]
    [InlineData("stereo", 0, "2.0")]
    [InlineData("5.1(side)", 0, "5.1")]
    [InlineData("5.1", 0, "5.1")]
    [InlineData("7.1", 0, "7.1")]
    [InlineData("2.1", 0, "2.1")]
    [InlineData("quad", 0, "4.0")]
    [InlineData("5.0(side)", 0, "5.0")]
    [InlineData(null, 6, "5.1")]
    [InlineData("unknown-layout", 2, "2.0")]
    [InlineData(null, 0, null)]
    public void Channel_Layouts(string? layout, int count, string? expected)
    {
        Assert.Equal(expected, TrackLabels.ChannelLayout(layout, count));
    }
}

public class TrackListTests
{
    private const string Json = """
        [
          {"id":1,"type":"video","selected":true,"codec":"h264"},
          {"id":1,"type":"audio","selected":true,"lang":"eng","title":"Surround","demux-channels":"5.1(side)","demux-channel-count":6,"default":true},
          {"id":2,"type":"audio","selected":false,"lang":"jpn","demux-channels":"stereo"},
          {"id":1,"type":"sub","selected":false,"lang":"eng","hearing-impaired":true},
          {"id":2,"type":"sub","selected":true,"external":true,"external-filename":"C:\\Videos\\Film.srt","forced":true}
        ]
        """;

    [Fact]
    public void Parse_Splits_Audio_And_Subtitles_And_Skips_Video()
    {
        TrackList list = TrackList.Parse(Json);

        Assert.Equal([1, 2], list.Audio.Select(t => t.Id));
        Assert.Equal([1, 2], list.Subtitles.Select(t => t.Id));
    }

    [Fact]
    public void Parse_Reads_The_Fields_The_Labels_Need()
    {
        TrackList list = TrackList.Parse(Json);

        MediaTrack first = list.Audio[0];
        Assert.True(first.Selected);
        Assert.Equal("eng", first.Language);
        Assert.Equal("Surround", first.Title);
        Assert.Equal("5.1(side)", first.ChannelLayout);
        Assert.Equal(6, first.ChannelCount);
        Assert.False(list.Audio[1].Selected);
        Assert.True(list.Subtitles[0].HearingImpaired);
        Assert.Equal(@"C:\Videos\Film.srt", list.Subtitles[1].ExternalFile);
        Assert.True(list.Subtitles[1].Forced);
        Assert.Equal(2, list.SelectedSubtitle?.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1, \"x\", {\"type\":\"audio\"}]")]
    public void Parse_Gives_An_Empty_List_For_Anything_Unreadable(string? json)
    {
        TrackList list = TrackList.Parse(json);

        Assert.Empty(list.Audio);
        Assert.Empty(list.Subtitles);
    }
}

public class TrackDelayTests
{
    [Fact]
    public void Steps_Stay_On_The_Tenth_Of_A_Second_Grid()
    {
        double value = 0;
        for (int i = 0; i < 3; i++)
        {
            value = TrackDelay.Stepped(value, +1);
        }

        Assert.Equal(0.3, value);
        Assert.Equal(-0.1, TrackDelay.Stepped(0, -1));
    }

    [Fact]
    public void Steps_Stop_At_The_Limit()
    {
        Assert.Equal(TrackDelay.Limit, TrackDelay.Stepped(TrackDelay.Limit, +1));
        Assert.Equal(-TrackDelay.Limit, TrackDelay.Stepped(-TrackDelay.Limit, -1));
    }

    [Theory]
    [InlineData(0, "0.0 s")]
    [InlineData(0.3, "+0.3 s")]
    [InlineData(-1.25, "−1.2 s")]
    [InlineData(-0.04, "0.0 s")]
    public void Format_Shows_A_Sign_Only_When_There_Is_A_Delay(double seconds, string expected)
    {
        using var _ = new CultureScope("en-US");

        Assert.Equal(expected, TrackDelay.Format(seconds));
    }

    [Fact]
    public void ForMpv_Uses_A_Dot()
    {
        using var _ = new CultureScope("de-DE");

        Assert.Equal("-0.3", TrackDelay.ForMpv(-0.3));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
