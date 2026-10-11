using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class LaunchArgumentsParsingTests
{
    [Theory]
    [InlineData("video.mp4")]
    [InlineData("video.MP4")]
    [InlineData("video.mkv")]
    [InlineData("video.MKV")]
    [InlineData("video.mov")]
    [InlineData("video.MOV")]
    [InlineData("video.webm")]
    [InlineData("video.WEBM")]
    [InlineData("video.WebM")]
    public void FirstMediaPath_AcceptsAllSupportedExtensions_InAnyCase(string fileName)
    {
        string path = @"C:\media\" + fileName;
        Assert.Equal(path, LaunchArguments.FirstMediaPath([path]));
    }

    [Theory]
    [InlineData("song.mp3")]
    [InlineData("clip.avi")]
    [InlineData("stream.flv")]
    [InlineData("video.wmv")]
    [InlineData("recording.ts")]
    [InlineData("notes.txt")]
    [InlineData("script.ps1")]
    [InlineData("archive.tar.gz")]
    [InlineData("video.mp4.part")]
    [InlineData("video.mkv.bak")]
    [InlineData("videofile")]
    [InlineData("videofile.")]
    public void FirstMediaPath_RejectsUnsupportedExtensions(string fileName)
    {
        string path = @"C:\media\" + fileName;
        Assert.Null(LaunchArguments.FirstMediaPath([path]));
    }

    [Fact]
    public void FirstMediaPath_HandlesQuotedArgumentsWithSpaces()
    {
        string quotedPath = "\"C:\\Users\\User\\Videos\\Highlight Reel 2026.mp4\"";
        string expected = @"C:\Users\User\Videos\Highlight Reel 2026.mp4";

        Assert.Equal(expected, LaunchArguments.FirstMediaPath([quotedPath]));
    }

    [Fact]
    public void FirstMediaPath_TrimsOuterWhitespaceAndQuotes()
    {
        string padded = "   \"D:\\Movies\\Film.mkv\"   ";
        string expected = @"D:\Movies\Film.mkv";

        Assert.Equal(expected, LaunchArguments.FirstMediaPath([padded]));
    }

    [Fact]
    public void FirstMediaPath_DoesNotRequireFileToExistOnDisk()
    {
        // As documented, whether the file exists is for the caller to check;
        // FirstMediaPath performs pure syntactic validation against supported formats.
        string nonExistentPath = Path.Combine(Path.GetTempPath(), "definitely_does_not_exist_xyz123.mkv");
        Assert.False(File.Exists(nonExistentPath));

        string? result = LaunchArguments.FirstMediaPath([nonExistentPath]);
        Assert.Equal(nonExistentPath, result);
    }

    [Fact]
    public void FirstMediaPath_MultipleArguments_PicksFirstSupportedMediaIgnoringOthers()
    {
        string[] args = ["--fullscreen", "--volume=50", @"C:\v1.mkv", @"D:\v2.mp4", @"E:\v3.webm"];
        Assert.Equal(@"C:\v1.mkv", LaunchArguments.FirstMediaPath(args));
    }

    [Fact]
    public void FirstMediaPath_SkipsNullAndWhitespaceElements()
    {
        string?[] args = ["--flag", "-v", "doc.pdf", null, "", "   ", @"C:\match.mov", @"D:\another.mp4"];
        Assert.Equal(@"C:\match.mov", LaunchArguments.FirstMediaPath(args));
    }

    [Fact]
    public void Split_SplitsComplexCommandLineWithQuotesAndSpaces()
    {
        string cmd = "player.exe --fullscreen \"C:\\My Videos\\Intro Match.mp4\" --speed=1.5 \"second.webm\"";
        var parts = LaunchArguments.Split(cmd);

        Assert.Equal(["player.exe", "--fullscreen", @"C:\My Videos\Intro Match.mp4", "--speed=1.5", "second.webm"], parts);
    }

    [Fact]
    public void Split_HandlesMultipleSpacesBetweenArguments()
    {
        string cmd = "arg1    arg2       arg3";
        var parts = LaunchArguments.Split(cmd);

        Assert.Equal(["arg1", "arg2", "arg3"], parts);
    }

    [Fact]
    public void Split_HandlesConsecutiveQuotedArguments()
    {
        string cmd = "\"first arg\" \"second arg\"";
        var parts = LaunchArguments.Split(cmd);

        Assert.Equal(["first arg", "second arg"], parts);
    }

    [Fact]
    public void MediaFormats_Extensions_ContainsExactSupportedSet()
    {
        Assert.Equal([".mp4", ".mkv", ".mov", ".webm"], MediaFormats.Extensions);
    }

    [Theory]
    [InlineData("video.mp4", "MP4")]
    [InlineData("movie.mkv", "MKV")]
    [InlineData("clip.mov", "MOV")]
    [InlineData("stream.webm", "WEBM")]
    [InlineData("noext", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MediaFormats_Label_FormatsExpectedLabel(string? path, string expected)
    {
        Assert.Equal(expected, MediaFormats.Label(path));
    }
}
