using Aether.Core.Interop.Windowing;
using Xunit;

namespace Aether.Core.Tests;

public class TaskbarThumbButtonsTests
{
    [Theory]
    [InlineData(0u, 16)]
    [InlineData(96u, 16)]
    [InlineData(120u, 20)]
    [InlineData(144u, 24)]
    [InlineData(168u, 28)]
    [InlineData(192u, 32)]
    [InlineData(240u, 40)]
    public void IconSizeForDpi_ScalesAndClampsCleanly(uint dpi, int expectedSize)
    {
        Assert.Equal(expectedSize, TaskbarThumbButtons.IconSizeForDpi(dpi));
    }

    [Theory]
    [InlineData(16, 13)]
    [InlineData(20, 16)]
    [InlineData(24, 19)]
    [InlineData(32, 26)]
    [InlineData(40, 32)]
    public void GlyphFontHeightForIcon_LeavesBreathingRoomInsideBitmap(int iconSize, int expectedHeight)
    {
        int height = TaskbarThumbButtons.GlyphFontHeightForIcon(iconSize);
        Assert.Equal(expectedHeight, height);
        Assert.InRange(height, 12, iconSize);
    }
}
