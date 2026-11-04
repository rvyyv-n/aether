using Aether.Core.Playback.Tracks;

namespace Aether.Core.Tests;

public class SubtitleFilesTests
{
    private const string Video = @"D:\Shows\Show.S01E02.1080p.WEB.mkv";

    private static IReadOnlyList<SubtitleFile> Match(string video, params string[] files) =>
        SubtitleFiles.Match(video, files, [video, .. files.Where(f => !SubtitleFiles.IsSubtitle(f))]);

    [Fact]
    public void The_Same_Name_Matches_And_Trailing_Tags_Give_Language_Forced_And_Sdh()
    {
        IReadOnlyList<SubtitleFile> found = Match(Video,
            @"D:\Shows\Show.S01E02.1080p.WEB.srt",
            @"D:\Shows\Show.S01E02.1080p.WEB.en.forced.srt",
            @"D:\Shows\Show.S01E02.1080p.WEB.ja.ass",
            @"D:\Shows\Show.S01E02.1080p.WEB.eng.sdh.vtt");

        Assert.Equal(4, found.Count);
        Assert.Contains(found, f => f.Language is null && !f.Forced && f.Path.EndsWith("WEB.srt", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Language == "en" && f.Forced);
        Assert.Contains(found, f => f.Language == "ja");
        Assert.Contains(found, f => f.Language == "eng" && f.HearingImpaired);
    }

    [Fact]
    public void Spacing_Case_And_Separators_Do_Not_Matter()
    {
        IReadOnlyList<SubtitleFile> found = Match(@"D:\a\My Movie (2020).mkv", @"D:\a\my.movie.2020.es.srt", @"D:\a\MY_MOVIE_2020.SRT");

        Assert.Equal(2, found.Count);
        Assert.Contains(found, f => f.Language == "es");
    }

    [Fact]
    public void The_Episode_Code_Matches_When_The_Rest_Of_The_Name_Differs()
    {
        IReadOnlyList<SubtitleFile> found = Match(Video,
            @"D:\Shows\show s01e02 (subs by someone).en.srt",
            @"D:\Shows\Show.S01E03.en.srt",
            @"D:\Shows\Show.S01E03.mkv");

        SubtitleFile only = Assert.Single(found);
        Assert.Contains("s01e02", only.Path, StringComparison.Ordinal);
        Assert.Equal("en", only.Language);
    }

    [Fact]
    public void A_Lone_Video_Takes_A_Lone_Subtitle_Whatever_It_Is_Called()
    {
        Assert.Single(Match(@"D:\film\movie.mp4", @"D:\film\Subtitles.English.srt"));
        Assert.Empty(Match(@"D:\film\movie.mp4", @"D:\film\Subtitles.English.srt", @"D:\film\other.mp4"));
    }

    [Fact]
    public void Another_Episode_Or_Another_Film_Is_Not_Matched()
    {
        Assert.Empty(Match(Video, @"D:\Shows\Show.S01E20.srt", @"D:\Shows\Show.S01E20.mkv"));
        Assert.Empty(Match(@"D:\a\Alien.mkv", @"D:\a\Aliens.srt", @"D:\a\Aliens.mkv"));
    }

    [Theory]
    [InlineData("movie.en.srt", "en")]
    [InlineData("movie.pt-BR.srt", "pt-BR")]
    [InlineData("movie.zh-Hans.ass", "zh-Hans")]
    [InlineData("movie.720p.srt", null)]
    [InlineData("movie.final.srt", null)]
    [InlineData("movie.srt", null)]
    public void Only_Language_Shaped_Tags_Are_Read_As_A_Language(string name, string? language) =>
        Assert.Equal(language, SubtitleFiles.Tags(name).Language);

    [Fact]
    public void Subtitle_Files_Are_Recognised_By_Extension()
    {
        Assert.True(SubtitleFiles.IsSubtitle(@"C:\x.SRT"));
        Assert.True(SubtitleFiles.IsSubtitle(@"C:\x.vtt"));
        Assert.True(SubtitleFiles.IsSubtitle(@"C:\x.ass"));
        Assert.True(SubtitleFiles.IsSubtitle(@"C:\x.ssa"));
        Assert.True(SubtitleFiles.IsSubtitle(@"C:\x.sub"));
        Assert.False(SubtitleFiles.IsSubtitle(@"C:\x.idx"));
        Assert.False(SubtitleFiles.IsSubtitle(@"C:\x.mkv"));
    }

    [Fact]
    public void Folders_Named_Subs_Or_Subtitles_Are_Looked_In_Too()
    {
        Assert.True(SubtitleFiles.IsSubtitleFolder(@"D:\Shows\Subs"));
        Assert.True(SubtitleFiles.IsSubtitleFolder(@"D:\Shows\subtitles"));
        Assert.False(SubtitleFiles.IsSubtitleFolder(@"D:\Shows\Extras"));
        Assert.Single(Match(Video, @"D:\Shows\Subs\Show.S01E02.1080p.WEB.en.srt"));
    }

    [Fact]
    public void The_Plan_Turns_On_The_Preferred_Language_And_Adds_The_Rest_Off()
    {
        IReadOnlyList<SubtitleFile> found = Match(Video,
            @"D:\Shows\Show.S01E02.1080p.WEB.ja.srt",
            @"D:\Shows\Show.S01E02.1080p.WEB.en.srt",
            @"D:\Shows\Show.S01E02.1080p.WEB.en.forced.srt");

        IReadOnlyList<string[]> commands = SubtitleFiles.Commands(found, preferredLanguage: "en", select: true);

        Assert.Equal(3, commands.Count);
        string[] selected = Assert.Single(commands, c => c[2] == "select");
        Assert.EndsWith("WEB.en.srt", selected[1], StringComparison.Ordinal);
        Assert.All(commands, c => Assert.Equal("sub-add", c[0]));
        Assert.Equal("en", selected[4]);
        Assert.Contains(commands, c => c[2] == "auto" && c[3].Contains("forced", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commands, c => c[2] == "cached");
    }

    [Fact]
    public void With_No_Preferred_Match_An_Untagged_Exact_File_Is_Turned_On_And_Nothing_When_Told_Not_To()
    {
        IReadOnlyList<SubtitleFile> found = Match(Video, @"D:\Shows\Show.S01E02.1080p.WEB.srt", @"D:\Shows\Show.S01E02.1080p.WEB.de.srt");

        Assert.EndsWith("WEB.srt", Assert.Single(SubtitleFiles.Commands(found, "fr", select: true), c => c[2] == "select")[1], StringComparison.Ordinal);
        Assert.DoesNotContain(SubtitleFiles.Commands(found, "fr", select: false), c => c[2] == "select");
    }
}
