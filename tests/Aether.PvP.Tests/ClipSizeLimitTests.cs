using Aether.PvP.Review.Clipping;
using Xunit;

namespace Aether.PvP.Tests;

public class ClipSizeLimitTests
{
    [Fact]
    public void NoLimit_HasNoBytes_AndIsNeverTooSmall()
    {
        Assert.Null(ClipSizeLimit.ToBytes(0));
        Assert.Null(ClipSizeLimit.MinimumIfTooSmall(0, 3600));
    }

    [Fact]
    public void Megabytes_AreMillionsOfBytes() =>
        Assert.Equal(25_000_000L, ClipSizeLimit.ToBytes(25));

    [Fact]
    public void ShortClip_FitsTenMegabytes() =>
        Assert.Null(ClipSizeLimit.MinimumIfTooSmall(10, 30));

    [Fact]
    public void LongClip_IsTooSmallForTenMegabytes_AndReportsTheSmallestSize()
    {
        long? minimum = ClipSizeLimit.MinimumIfTooSmall(10, 600);

        Assert.NotNull(minimum);
        Assert.True(minimum > 10_000_000);
        Assert.Equal(minimum, BitrateCalculator.Plan(10_000_000, 600).MinimumSizeBytes);
    }

    [Fact]
    public void ReportedMinimum_IsAcceptedAsALimit()
    {
        long minimum = ClipSizeLimit.MinimumIfTooSmall(10, 600)!.Value;
        int megabytes = (int)Math.Ceiling(ClipSizeLimit.RoundUpMegabytes(minimum));

        Assert.Null(ClipSizeLimit.MinimumIfTooSmall(megabytes, 600));
    }

    [Fact]
    public void UnknownLength_IsNotTooSmall() =>
        Assert.Null(ClipSizeLimit.MinimumIfTooSmall(10, 0));

    [Theory]
    [InlineData(1_000_000, 1.0)]
    [InlineData(1_000_001, 1.1)]
    [InlineData(12_340_000, 12.4)]
    public void RoundUpMegabytes_RoundsUpToATenth(long bytes, double expected) =>
        Assert.Equal(expected, ClipSizeLimit.RoundUpMegabytes(bytes), 6);
}
