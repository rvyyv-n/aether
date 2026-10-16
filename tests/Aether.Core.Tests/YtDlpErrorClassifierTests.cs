using Aether.Core.Playback.Streams;
using Xunit;

namespace Aether.Core.Tests;

public class YtDlpErrorClassifierTests
{
    [Theory]
    [InlineData("ERROR: [youtube] 12345: Private video. Sign in", StreamFailureReason.PrivateVideo)]
    [InlineData("ERROR: This video is private.", StreamFailureReason.PrivateVideo)]
    [InlineData("ERROR: [youtube] Sign in to confirm your age", StreamFailureReason.SignInRequired)]
    [InlineData("ERROR: [youtube] Sign in to confirm you’re not a bot", StreamFailureReason.SignInRequired)]
    [InlineData("ERROR: [youtube] This video requires payment to watch.", StreamFailureReason.SignInRequired)]
    [InlineData("ERROR: [youtube] The uploader has not made this video available in your country", StreamFailureReason.RegionBlocked)]
    [InlineData("ERROR: Video unavailable in your country due to GeoRestricted content", StreamFailureReason.RegionBlocked)]
    [InlineData("ERROR: [youtube] Premieres in 2 hours", StreamFailureReason.LiveNotStarted)]
    [InlineData("ERROR: Live stream will begin in 10 minutes", StreamFailureReason.LiveNotStarted)]
    [InlineData("ERROR: [youtube] Unable to download webpage: getaddrinfo failed", StreamFailureReason.Offline)]
    [InlineData("ERROR: Failed to resolve host www.youtube.com", StreamFailureReason.Offline)]
    [InlineData("ERROR: Network is unreachable", StreamFailureReason.Offline)]
    [InlineData("ERROR: Connection refused", StreamFailureReason.Offline)]
    [InlineData("ERROR: [generic] Unsupported URL: ftp://example.com", StreamFailureReason.SiteNotSupported)]
    [InlineData("ERROR: No video formats found!", StreamFailureReason.SiteNotSupported)]
    [InlineData("ERROR: Some completely unrecognized error message", StreamFailureReason.Unknown)]
    [InlineData("", StreamFailureReason.None)]
    [InlineData(null, StreamFailureReason.None)]
    public void Classify_KnownErrors_ReturnsExpectedReason(string? stderr, StreamFailureReason expected)
    {
        StreamFailureReason reason = YtDlpErrorClassifier.Classify(stderr, stderr is null || stderr.Length == 0 ? 0 : 1);
        Assert.Equal(expected, reason);
    }

    [Fact]
    public void Classify_EmptyErrorWithNonZeroExitCode_ReturnsUnknown()
    {
        StreamFailureReason reason = YtDlpErrorClassifier.Classify(string.Empty, 1);
        Assert.Equal(StreamFailureReason.Unknown, reason);
    }
}
