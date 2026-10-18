using Aether.Core.Playback.Streams;

namespace Aether.Core.Tests;

public sealed class YtDlpVersionsTests
{
    [Theory]
    [InlineData("2026.07.04", "2026.06.30", 1)]
    [InlineData("2026.07.04", "2026.07.04", 0)]
    [InlineData("2026.07.04.1", "2026.07.04", 1)]
    [InlineData("2025.12.31", "2026.01.01", -1)]
    [InlineData("2026.07.04", null, 1)]
    [InlineData(null, "2026.07.04", -1)]
    [InlineData("garbage", "2026.07.04", -1)]
    [InlineData(null, "", 0)]
    public void Compare_OrdersDateVersions(string? a, string? b, int sign)
    {
        Assert.Equal(sign, Math.Sign(YtDlpVersions.Compare(a, b)));
    }

    [Fact]
    public void Choose_TakesTheNewestCopy()
    {
        string? chosen = YtDlpVersions.Choose([("downloaded", "2026.07.04"), ("bundled", "2026.06.01"), ("path", "2026.07.10")]);

        Assert.Equal("path", chosen);
    }

    [Fact]
    public void Choose_PrefersTheEarlierCopyOnATie()
    {
        string? chosen = YtDlpVersions.Choose([("downloaded", "2026.07.04"), ("bundled", "2026.07.04")]);

        Assert.Equal("downloaded", chosen);
    }

    [Fact]
    public void Choose_FallsBackOnOrderWhenNoVersionIsKnown()
    {
        Assert.Equal("downloaded", YtDlpVersions.Choose([("downloaded", null), ("bundled", null)]));
    }

    [Fact]
    public void Choose_PrefersAKnownVersionOverAnUnknownOne()
    {
        Assert.Equal("bundled", YtDlpVersions.Choose([("downloaded", null), ("bundled", "2026.01.01")]));
    }

    [Fact]
    public void Choose_IsNullWithoutCandidates()
    {
        Assert.Null(YtDlpVersions.Choose([]));
    }
}
