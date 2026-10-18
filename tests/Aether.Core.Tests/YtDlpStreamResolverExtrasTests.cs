using Aether.Core.Playback.Streams;
using Xunit;

namespace Aether.Core.Tests;

public class YtDlpStreamResolverExtrasTests
{
    private static ResolvedStream Parse(string json) =>
        YtDlpStreamResolver.ParseStreamJson(json.ReplaceLineEndings(" "), "https://fallback");

    [Fact]
    public void SplitVideoAndAudio_ReportsBothUrls()
    {
        ResolvedStream s = Parse("""
            {"title":"t","requested_formats":[
              {"format_id":"140","url":"https://x/audio","vcodec":"none","acodec":"mp4a"},
              {"format_id":"137","url":"https://x/video","vcodec":"avc1","acodec":"none"}]}
            """);

        Assert.Equal("https://x/video", s.StreamUrl);
        Assert.Equal("https://x/audio", s.AudioUrl);
        Assert.Equal("137", s.VideoFormatId);
        Assert.Equal("140", s.AudioFormatId);
    }

    [Fact]
    public void CombinedSingleFormat_HasNoAudioUrl()
    {
        ResolvedStream s = Parse("""{"title":"t","url":"https://x/all","format_id":"18"}""");

        Assert.Equal("https://x/all", s.StreamUrl);
        Assert.Null(s.AudioUrl);
        Assert.Equal("18", s.VideoFormatId);
        Assert.Null(s.AudioFormatId);
    }

    [Fact]
    public void Chapters_AreSortedAndMalformedSkipped()
    {
        ResolvedStream s = Parse("""
            {"title":"t","url":"u","chapters":[
              {"start_time":60,"end_time":120,"title":"Second"},
              {"start_time":0,"end_time":60},
              {"start_time":"x","end_time":5,"title":"Bad"},
              {"end_time":9,"title":"NoStart"},
              "junk"]}
            """);

        Assert.NotNull(s.Chapters);
        Assert.Equal(2, s.Chapters!.Count);
        Assert.Equal(new StreamChapter(0, 60, ""), s.Chapters[0]);
        Assert.Equal(new StreamChapter(60, 120, "Second"), s.Chapters[1]);
    }

    [Fact]
    public void Formats_SkipStoryboardsAndMapNoneCodecsToNull()
    {
        ResolvedStream s = Parse("""
            {"title":"t","url":"u","formats":[
              {"format_id":"sb0","url":"https://x/sb","ext":"mhtml","format_note":"storyboard","vcodec":"none","acodec":"none"},
              {"format_id":"140","url":"https://x/a","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","tbr":129.5},
              {"format_id":"137","url":"https://x/v","ext":"mp4","vcodec":"avc1","acodec":"none","height":1080,"width":1920,"fps":30,"http_headers":{"User-Agent":"UA"}},
              {"format_id":"nourl","vcodec":"avc1","acodec":"none"}]}
            """);

        Assert.NotNull(s.Formats);
        Assert.Equal(2, s.Formats!.Count);

        StreamFormat audio = s.Formats[0];
        Assert.Equal("140", audio.FormatId);
        Assert.Null(audio.VideoCodec);
        Assert.Equal("mp4a.40.2", audio.AudioCodec);
        Assert.Equal(129.5, audio.Bitrate);
        Assert.Null(audio.Height);

        StreamFormat video = s.Formats[1];
        Assert.Equal(1080, video.Height);
        Assert.Equal(1920, video.Width);
        Assert.Equal(30, video.Fps);
        Assert.Null(video.AudioCodec);
        Assert.Equal("UA", video.HttpHeaders!["User-Agent"]);
    }

    [Fact]
    public void Subtitles_PreferVttAndSkipLiveChat()
    {
        ResolvedStream s = Parse("""
            {"title":"t","url":"u","subtitles":{
              "en":[{"ext":"srv3","url":"https://x/en.srv3"},{"ext":"srt","url":"https://x/en.srt"},{"ext":"vtt","url":"https://x/en.vtt","name":"English"}],
              "fr":[{"ext":"json3","url":"https://x/fr.json3"}],
              "live_chat":[{"ext":"json","url":"https://x/chat"}]}}
            """);

        StreamSubtitle sub = Assert.Single(s.Subtitles!);
        Assert.Equal("en", sub.Language);
        Assert.Equal("vtt", sub.Ext);
        Assert.Equal("https://x/en.vtt", sub.Url);
        Assert.Equal("English", sub.Name);
        Assert.False(sub.IsAutomatic);
    }

    [Fact]
    public void AutomaticCaptions_KeepOnlyVideoLanguageAndOrig()
    {
        ResolvedStream s = Parse("""
            {"title":"t","url":"u","language":"en","automatic_captions":{
              "en":[{"ext":"vtt","url":"https://x/en"}],
              "de":[{"ext":"vtt","url":"https://x/de"}],
              "fr":[{"ext":"vtt","url":"https://x/fr"}]}}
            """);

        Assert.Equal("en", Assert.Single(s.Subtitles!).Language);
        Assert.All(s.Subtitles!, x => Assert.True(x.IsAutomatic));
    }

    [Fact]
    public void AutomaticCaptions_WithOrig_DropThePlainLanguageDuplicate()
    {
        ResolvedStream s = Parse("""
            {"title":"t","url":"u","language":"en","automatic_captions":{
              "en":[{"ext":"vtt","url":"https://x/en"}],
              "en-orig":[{"ext":"vtt","url":"https://x/en-orig"}],
              "de":[{"ext":"vtt","url":"https://x/de"}]}}
            """);

        Assert.Equal("en-orig", Assert.Single(s.Subtitles!).Language);
    }

    [Fact]
    public void AutomaticCaptions_WithoutVideoLanguage_KeepOnlyOrig()
    {
        ResolvedStream s = Parse("""
            {"title":"t","url":"u","automatic_captions":{
              "en":[{"ext":"vtt","url":"https://x/en"}],
              "en-orig":[{"ext":"vtt","url":"https://x/en-orig"}]}}
            """);

        Assert.Equal("en-orig", Assert.Single(s.Subtitles!).Language);
    }

    [Fact]
    public void AutomaticCaptions_WithoutVideoLanguageOrOrig_KeepNone()
    {
        ResolvedStream s = Parse("""{"title":"t","url":"u","automatic_captions":{"en":[{"ext":"vtt","url":"https://x/en"}]}}""");

        Assert.NotNull(s.Subtitles);
        Assert.Empty(s.Subtitles!);
    }

    [Fact]
    public void MissingKeys_LeaveExtrasNullWithoutThrowing()
    {
        ResolvedStream s = Parse("""{"title":"t","url":"u"}""");

        Assert.Null(s.AudioUrl);
        Assert.Null(s.Chapters);
        Assert.Null(s.Formats);
        Assert.Null(s.Subtitles);
    }

    [Fact]
    public void OddTypedFields_DoNotThrow()
    {
        ResolvedStream s = Parse("""
            {"title":"t","url":"u","chapters":5,"formats":"x","subtitles":[],"automatic_captions":7,
             "requested_formats":[1,{"url":5}]}
            """);

        Assert.Empty(s.Chapters!);
        Assert.Empty(s.Formats!);
        Assert.Null(s.Subtitles);
    }
}
