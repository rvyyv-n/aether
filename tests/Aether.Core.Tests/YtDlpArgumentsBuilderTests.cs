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
}
