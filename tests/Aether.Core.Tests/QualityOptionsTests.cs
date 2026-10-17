using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Streams;
using Xunit;

namespace Aether.Core.Tests;

public class QualityOptionsTests
{
    private static StreamFormat Video(int height, double? fps = 30, string codec = "avc1") =>
        new($"v{height}-{codec}", height, height * 16 / 9, fps, codec, null, "mp4", null, "https://x/v", null);

    private static StreamFormat Audio(string codec = "mp4a") =>
        new("a1", null, null, null, "none", codec, "m4a", null, "https://x/a", null);

    private static string[] Keys(IReadOnlyList<QualityOption> options) => options.Select(o => o.Key).ToArray();

    [Fact]
    public void From_NullOrEmpty_OnlyAuto()
    {
        Assert.Equal(["auto"], Keys(QualityOptions.From(null)));
        Assert.Equal(["auto"], Keys(QualityOptions.From([])));
    }

    [Fact]
    public void From_OrdersHighestFirstAndDedupesCodecs()
    {
        var options = QualityOptions.From([Video(720), Video(1080), Video(1080, 30, "vp9"), Video(1080, 29.97, "av01"), Video(480)]);

        Assert.Equal(["auto", "h1080", "h720", "h480"], Keys(options));
        Assert.Equal(["Auto", "1080p", "720p", "480p"], options.Select(o => o.Label).ToArray());
    }

    [Fact]
    public void From_HighFpsGetsOwnEntryAboveSameHeight()
    {
        var options = QualityOptions.From([Video(1080, 30), Video(1080, 60), Video(720, 49), Video(720, 50), Video(360, null)]);

        Assert.Equal(["auto", "h1080f60", "h1080", "h720f60", "h720", "h360"], Keys(options));
        Assert.Equal("1080p60", options[1].Label);
        Assert.Equal("720p60", options[3].Label);
    }

    [Fact]
    public void From_AudioOnlyAppendedLast()
    {
        var options = QualityOptions.From([Audio(), Video(720)]);

        Assert.Equal(["auto", "h720", "audio"], Keys(options));
        Assert.Equal("Audio only", options[^1].Label);
    }

    [Fact]
    public void From_OnlyAudio_AutoAndAudio()
    {
        Assert.Equal(["auto", "audio"], Keys(QualityOptions.From([Audio()])));
    }

    [Fact]
    public void From_IgnoresFormatsWithNeitherStream()
    {
        var empty = new StreamFormat("x", null, null, null, "none", "none", null, null, "u", null);
        Assert.Equal(["auto"], Keys(QualityOptions.From([empty])));
    }

    [Fact]
    public void FormatSelector_Values()
    {
        var options = QualityOptions.From([Video(1080, 60), Video(1080), Video(720), Audio()]);

        Assert.Null(options[0].FormatSelector());
        Assert.Equal("bv*[height<=1080][fps>=50]+ba/b[height<=1080][fps>=50]/bv*[height<=1080]+ba/b[height<=1080]", options[1].FormatSelector());
        Assert.Equal("bv*[height<=1080]+ba/b[height<=1080]", options[2].FormatSelector());
        Assert.Equal("bv*[height<=720]+ba/b[height<=720]", options[3].FormatSelector());
        Assert.Equal("ba/b", options[4].FormatSelector());
    }

    [Fact]
    public void Resolve_ExactMatch()
    {
        var options = QualityOptions.From([Video(1080), Video(720), Audio()]);

        Assert.Equal("h720", QualityOptions.Resolve("h720", options).Key);
        Assert.Equal("audio", QualityOptions.Resolve("audio", options).Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("auto")]
    [InlineData("garbage")]
    [InlineData("h")]
    [InlineData("hf60")]
    [InlineData("audio")]
    public void Resolve_EmptyUnknownOrMissingAudio_Auto(string? saved)
    {
        var options = QualityOptions.From([Video(1080)]);
        Assert.Equal("auto", QualityOptions.Resolve(saved, options).Key);
    }

    [Fact]
    public void Resolve_PicksNearestLower()
    {
        var options = QualityOptions.From([Video(1080), Video(720), Video(360)]);

        Assert.Equal("h1080", QualityOptions.Resolve("h1440", options).Key);
        Assert.Equal("h720", QualityOptions.Resolve("h900", options).Key);
        Assert.Equal("h360", QualityOptions.Resolve("h480", options).Key);
    }

    [Fact]
    public void Resolve_BelowEverythingAvailable_Auto()
    {
        var options = QualityOptions.From([Video(720)]);
        Assert.Equal("auto", QualityOptions.Resolve("h480", options).Key);
    }

    [Fact]
    public void Resolve_HighFpsFallsBackToSameHeightOrLower()
    {
        Assert.Equal("h1080", QualityOptions.Resolve("h1080f60", QualityOptions.From([Video(1080), Video(720, 60)])).Key);
        Assert.Equal("h1080f60", QualityOptions.Resolve("h1080", QualityOptions.From([Video(1080, 60), Video(720)])).Key);
        Assert.Equal("h720f60", QualityOptions.Resolve("h1440f60", QualityOptions.From([Video(720, 60), Video(720)])).Key);
    }

    [Fact]
    public void Resolve_OnlyAudioAvailable_VideoKeyGivesAuto()
    {
        Assert.Equal("auto", QualityOptions.Resolve("h720", QualityOptions.From([Audio()])).Key);
    }

    [Fact]
    public void Setting_DefaultsToAutoAndRoundTrips()
    {
        Assert.Equal("", new AetherSettings().StreamQualityChoice);
        Assert.Equal("streaming.qualityChoice", SettingKeys.StreamQualityChoice);
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("auto", "auto")]
    [InlineData("nonsense", "auto")]
    [InlineData("h720", "h720")]
    [InlineData("h1080f60", "h1080f60")]
    [InlineData("audio", "audio")]
    public void ForKey_RebuildsTheRememberedOption(string? key, string expected)
    {
        Assert.Equal(expected, QualityOptions.ForKey(key).Key);
    }

    [Fact]
    public void ForKey_SelectorMatchesTheListedOption()
    {
        var listed = QualityOptions.From([Video(1080, 60), Video(720)]);

        Assert.Equal(listed[1].FormatSelector(), QualityOptions.ForKey("h1080f60").FormatSelector());
        Assert.Equal(listed[2].FormatSelector(), QualityOptions.ForKey("h720").FormatSelector());
        Assert.Equal("Audio only", QualityOptions.ForKey("audio").Label);
    }

    [Fact]
    public void FirstResolveSelector_AutoLeavesTheStreamingSetting()
    {
        Assert.Null(QualityOptions.FirstResolveSelector("", "bv*+ba/b"));
        Assert.Null(QualityOptions.FirstResolveSelector("auto", "bv*+ba/b"));
    }

    [Fact]
    public void FirstResolveSelector_FallsBackToTheStreamingSettingWhenNothingIsThatSmall()
    {
        Assert.Equal("bv*[height<=480]+ba/b[height<=480]/bv*+ba/b", QualityOptions.FirstResolveSelector("h480", "bv*+ba/b"));
    }
}
