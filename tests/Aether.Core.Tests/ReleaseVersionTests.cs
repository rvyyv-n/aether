using Aether.Core.Playback.Updates;

namespace Aether.Core.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("V10.0.4", 10, 0, 4, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("1.3.0-beta.2", 1, 3, 0, "beta.2")]
    [InlineData("1.3.0+build5", 1, 3, 0, "")]
    [InlineData(" v2.0.0-rc.1+abc ", 2, 0, 0, "rc.1")]
    public void TryParse_ReadsTagsAndVersions(string text, int major, int minor, int patch, string prerelease)
    {
        Assert.True(ReleaseVersion.TryParse(text, out ReleaseVersion v));
        Assert.Equal(new ReleaseVersion(major, minor, patch, prerelease), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-a..b")]
    [InlineData("-1.2.3")]
    [InlineData("99999999999.0.0")]
    public void TryParse_RejectsAnythingElse(string? text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("1.2.4", "1.2.3")]
    [InlineData("1.10.0", "1.9.9")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.9")]
    [InlineData("1.0.0-beta.11", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta", "1.0.0-alpha.5")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1")]
    public void IsNewer_FollowsSemVerPrecedence(string newer, string older)
    {
        Assert.True(ReleaseVersion.IsNewer(newer, older));
        Assert.False(ReleaseVersion.IsNewer(older, newer));
    }

    [Fact]
    public void IsNewer_IsFalseForEqualOrUnreadableVersions()
    {
        Assert.False(ReleaseVersion.IsNewer("1.2.3", "v1.2.3"));
        Assert.False(ReleaseVersion.IsNewer("1.2.3+a", "1.2.3+b"));
        Assert.False(ReleaseVersion.IsNewer("nightly", "1.0.0"));
        Assert.False(ReleaseVersion.IsNewer("2.0.0", null));
    }

    [Fact]
    public void ToString_DropsTheTagPrefixAndBuildData()
    {
        ReleaseVersion.TryParse("v1.2.3-beta.1+x", out ReleaseVersion v);
        Assert.Equal("1.2.3-beta.1", v.ToString());
    }
}
