using Aether.Core.Playback.Streams;
using Aether.Core.Playback.Tracks;
using Xunit;

namespace Aether.Core.Tests;

public class LinkSubtitlePlanTests
{
    private static StreamSubtitle Sub(string language, bool automatic = false, string? name = null) =>
        new(language, name, $"https://x/{language}{(automatic ? "-auto" : "")}.vtt", "vtt", automatic);

    [Fact]
    public void Nothing_To_Add_For_Null_Or_Empty()
    {
        Assert.Empty(LinkSubtitlePlan.Create(null, "en"));
        Assert.Empty(LinkSubtitlePlan.Create([], "en"));
    }

    [Fact]
    public void Selects_Only_The_Track_Matching_The_Preferred_Language()
    {
        var plan = LinkSubtitlePlan.Create([Sub("fr"), Sub("en", name: "English")], "en");

        Assert.Equal(["fr", "en"], plan.Select(t => t.Language));
        Assert.Equal([false, true], plan.Select(t => t.Select));
    }

    [Fact]
    public void Selects_Nothing_When_No_Track_Matches()
    {
        var plan = LinkSubtitlePlan.Create([Sub("fr"), Sub("de")], "en");

        Assert.Equal(2, plan.Count);
        Assert.DoesNotContain(plan, t => t.Select);
    }

    [Fact]
    public void Written_Track_Beats_Automatic_One_And_Automatic_Gets_The_Suffix()
    {
        var plan = LinkSubtitlePlan.Create([Sub("en", automatic: true, name: "English"), Sub("en", name: "English")], "en");

        Assert.Equal(["English (auto)", "English"], plan.Select(t => t.Title));
        Assert.Equal([false, true], plan.Select(t => t.Select));
    }

    [Fact]
    public void Automatic_Track_Is_Selected_When_It_Is_The_Only_Match()
    {
        var plan = LinkSubtitlePlan.Create([Sub("en", automatic: true)], "en");

        Assert.True(Assert.Single(plan).Select);
        Assert.Equal("en (auto)", plan[0].Title);
    }

    [Fact]
    public void Automatic_Tracks_For_Other_Languages_Are_Left_Out()
    {
        var plan = LinkSubtitlePlan.Create([Sub("de", automatic: true), Sub("fr")], "en");

        Assert.Equal(["fr"], plan.Select(t => t.Language));
    }

    [Theory]
    [InlineData("en-US", "en", true)]
    [InlineData("EN", "en", true)]
    [InlineData("en-orig", "en, fr", true)]
    [InlineData("fr", "en, fr", true)]
    [InlineData("eng", "en", false)]
    [InlineData("en", "", false)]
    public void Matches_Language_And_Regional_Forms(string language, string preferred, bool expected)
    {
        var plan = LinkSubtitlePlan.Create([Sub(language)], preferred);

        Assert.Equal(expected, plan[0].Select);
    }
}
