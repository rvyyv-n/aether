using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class HardwareDecodeFallbackTests
{
    [Theory]
    [InlineData("d3d11va", "no")]
    [InlineData("d3d11va-copy", "no")]
    public void HardwareDecoderDroppingToSoftware_IsFallback(string previous, string current) =>
        Assert.True(HardwareDecodeFallback.Occurred(previous, current));

    [Theory]
    [InlineData(null, "no")] // a file that never had a hardware path
    [InlineData("", "no")]
    [InlineData("no", "no")]
    [InlineData("d3d11va", null)] // the decoder was just released, e.g. a new file loading
    [InlineData("d3d11va", "d3d11va-copy")]
    [InlineData(null, "d3d11va")]
    public void EverythingElse_IsNot(string? previous, string? current) =>
        Assert.False(HardwareDecodeFallback.Occurred(previous, current));
}
