using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Streams;
using Xunit;

namespace Aether.Core.Tests;

public class YtDlpArgumentsBuilderTests
{
    [Fact]
    public void BuildArguments_DefaultOptions_ContainsDumpJsonAndNoPlaylist()
    {
        var options = new StreamResolutionOptions();
        string args = YtDlpArgumentsBuilder.BuildArguments("https://www.youtube.com/watch?v=dQw4w9WgXcQ", options);

        Assert.Contains("--dump-json", args);
        Assert.Contains("--no-playlist", args);
        Assert.Contains("https://www.youtube.com/watch?v=dQw4w9WgXcQ", args);
        Assert.Contains("-f ", args);
    }

    [Fact]
    public void BuildArguments_WithProxy_IncludesProxyArgument()
    {
        var options = new StreamResolutionOptions(HttpProxy: "http://proxy.example.com:8080");
        string args = YtDlpArgumentsBuilder.BuildArguments("https://youtu.be/test", options);

        Assert.Contains("--proxy", args);
        Assert.Contains("http://proxy.example.com:8080", args);
    }

    [Fact]
    public void BuildArguments_With720pQuality_SetsHeightConstraint()
    {
        var options = new StreamResolutionOptions(StreamingQuality: "720p", PreferGpuDecodableFormats: false);
        string args = YtDlpArgumentsBuilder.BuildArguments("https://youtu.be/test", options);

        Assert.Contains("720", args);
    }

    [Fact]
    public void BuildArguments_WithSelectorOverride_ReplacesSettingsSelector()
    {
        var options = new StreamResolutionOptions(StreamingQuality: "720p", FormatSelectorOverride: "bv*[height<=1080]+ba/b[height<=1080]");
        string args = YtDlpArgumentsBuilder.BuildArguments("https://youtu.be/test", options);

        Assert.Contains("-f \"bv*[height<=1080]+ba/b[height<=1080]\" ", args);
        Assert.DoesNotContain("720", args);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BuildArguments_BlankOverride_UsesSettingsSelector(string? selector)
    {
        var withOverride = new StreamResolutionOptions(StreamingQuality: "720p", FormatSelectorOverride: selector);
        var plain = new StreamResolutionOptions(StreamingQuality: "720p");

        Assert.Equal(
            YtDlpArgumentsBuilder.BuildArguments("https://youtu.be/test", plain),
            YtDlpArgumentsBuilder.BuildArguments("https://youtu.be/test", withOverride));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildArguments_NullOrWhitespaceUrl_ThrowsArgumentException(string? url)
    {
        var options = new StreamResolutionOptions();
        Assert.ThrowsAny<ArgumentException>(() => YtDlpArgumentsBuilder.BuildArguments(url!, options));
    }

    [Fact]
    public void BuildArgumentList_DefaultOptions_IncludesExpectedFlagsInOrder()
    {
        var options = new StreamResolutionOptions();
        IReadOnlyList<string> args = YtDlpArgumentsBuilder.BuildArgumentList("  https://www.youtube.com/watch?v=dQw4w9WgXcQ  ", options);

        string expectedSelector = MpvSettingsMap.YtdlFormat(options.StreamingQuality, options.PreferGpuDecodableFormats);
        Assert.Equal(
            [
                "--dump-json",
                "--no-playlist",
                "--no-warnings",
                "--socket-timeout",
                "15",
                "--extractor-args",
                "youtube:skip=translated_subs",
                "-f",
                expectedSelector,
                "--",
                "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            ],
            args);
    }

    [Fact]
    public void BuildArgumentList_WithProxy_TrimsAndPlacesBeforeSeparator()
    {
        var options = new StreamResolutionOptions(HttpProxy: "  http://127.0.0.1:8080  ");
        var args = YtDlpArgumentsBuilder.BuildArgumentList("https://youtu.be/test", options).ToList();

        int proxyIdx = args.IndexOf("--proxy");
        int sepIdx = args.IndexOf("--");
        Assert.True(proxyIdx > 0);
        Assert.Equal("http://127.0.0.1:8080", args[proxyIdx + 1]);
        Assert.Equal(proxyIdx + 2, sepIdx);
        Assert.Equal("https://youtu.be/test", args[sepIdx + 1]);
    }

    [Theory]
    [InlineData(null, 5, 30)]
    [InlineData(120.0, 5, 30)]
    [InlineData(30.0, 5, 30)]
    [InlineData(10.0, 5, 10)]
    [InlineData(2.0, 1, 2)]
    [InlineData(0.01, 1, 1)]
    public void BuildArgumentList_SocketTimeout_ClampedAndBoundedByTimeout(double? timeoutSeconds, int minExpected, int maxExpected)
    {
        TimeSpan? timeout = timeoutSeconds is { } s ? TimeSpan.FromSeconds(s) : null;
        var args = YtDlpArgumentsBuilder.BuildArgumentList("https://youtu.be/test", new StreamResolutionOptions(Timeout: timeout)).ToList();

        int idx = args.IndexOf("--socket-timeout");
        Assert.True(idx >= 0);
        int actual = int.Parse(args[idx + 1]);
        Assert.InRange(actual, minExpected, maxExpected);
    }

    [Fact]
    public void BuildArgumentList_WithOverride_UsesOverrideWithoutQuotes()
    {
        var options = new StreamResolutionOptions(StreamingQuality: "720p", FormatSelectorOverride: "bv*[height<=1080]+ba");
        var args = YtDlpArgumentsBuilder.BuildArgumentList("https://youtu.be/test", options).ToList();

        int fIdx = args.IndexOf("-f");
        Assert.Equal("bv*[height<=1080]+ba", args[fIdx + 1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildArgumentList_NullOrWhitespaceUrl_ThrowsArgumentException(string? url)
    {
        Assert.ThrowsAny<ArgumentException>(() => YtDlpArgumentsBuilder.BuildArgumentList(url!, new StreamResolutionOptions()));
    }

    [Fact]
    public void BuildArgumentList_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => YtDlpArgumentsBuilder.BuildArgumentList("https://youtu.be/test", null!));
    }
}
