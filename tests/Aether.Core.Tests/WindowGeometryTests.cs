using System;
using Aether.Core.Interop.Windowing;
using Xunit;

namespace Aether.Core.Tests;

public class WindowGeometryTests
{
    private static readonly PixelRect Work1080 = new(0, 0, 1920, 1032); // 1080p minus the taskbar

    [Fact]
    public void FitClient_SmallVideo_OpensAtNativeSize()
    {
        Assert.Equal((1280, 720), WindowGeometry.FitClient(1280, 720, Work1080, 14, 7, 0.9, 640));
    }

    [Fact]
    public void FitClient_LargeVideo_ScalesDownIntoTheWorkArea()
    {
        // 2560x1440 into 90% of 1920x1032 less the frame: height limits (921.8 / 1440, rounded).
        (int width, int height) = WindowGeometry.FitClient(2560, 1440, Work1080, 14, 7, 0.9, 640);
        Assert.Equal(922, height);
        Assert.Equal(1639, width);
        Assert.InRange(Math.Abs((double)width / height - 16.0 / 9), 0, 0.002);
    }

    [Fact]
    public void FitClient_TinyVideo_GrowsToTheMinimumWidth()
    {
        Assert.Equal((640, 480), WindowGeometry.FitClient(320, 240, Work1080, 14, 7, 0.9, 640));
    }

    [Fact]
    public void FitClient_MinimumWidthNeverOverflowsTheWorkArea()
    {
        // A very tall video: growing to the minimum width would overflow the height, so the fit wins.
        (int width, int height) = WindowGeometry.FitClient(100, 2000, Work1080, 0, 0, 0.9, 640);
        Assert.True(height <= 929);
        Assert.True(width < 640);
    }

    [Fact]
    public void FitClient_RejectsEmptyVideo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowGeometry.FitClient(0, 720, Work1080, 0, 0, 0.9, 640));
    }

    [Fact]
    public void Recentre_KeepsTheCentre()
    {
        Assert.Equal((660, 316), WindowGeometry.Recentre(new PixelRect(460, 216, 1460, 816), 600, 400, Work1080));
    }

    [Fact]
    public void Recentre_ClampsIntoTheWorkArea()
    {
        Assert.Equal((0, 0), WindowGeometry.Recentre(new PixelRect(-500, -500, 100, 100), 800, 600, Work1080));
        Assert.Equal((1120, 432), WindowGeometry.Recentre(new PixelRect(1800, 1000, 1900, 1030), 800, 600, Work1080));
    }

    [Theory]
    [InlineData(SizingEdge.Right, 100, 100, 1014, 613)]  // client 900 wide → 506 high, plus the 7 px frame
    [InlineData(SizingEdge.Left, 100, 100, 1014, 613)]
    [InlineData(SizingEdge.Bottom, 100, 100, 1014, 613)]
    public void ConstrainToAspect_SideDrags(SizingEdge edge, int left, int top, int right, int bottom)
    {
        // Client 900x? at 16:9 with a 14x7 frame.
        PixelRect proposed = edge == SizingEdge.Bottom
            ? new PixelRect(100, 100, 2000, 100 + 506 + 7)
            : new PixelRect(100, 100, 100 + 900 + 14, 900);
        PixelRect result = WindowGeometry.ConstrainToAspect(proposed, edge, 16.0 / 9, 14, 7);
        Assert.Equal(new PixelRect(left, top, right, bottom), result);
    }

    [Fact]
    public void ConstrainToAspect_TopDrag_MovesTheRightSide()
    {
        PixelRect result = WindowGeometry.ConstrainToAspect(new PixelRect(100, 50, 400, 50 + 450 + 7), SizingEdge.Top, 16.0 / 9, 14, 7);
        Assert.Equal(new PixelRect(100, 50, 100 + 800 + 14, 507), result);
    }

    [Fact]
    public void ConstrainToAspect_TopLeftCorner_KeepsTheBottomRightCorner()
    {
        // Pointer asks for a wider window: width drives, the top edge moves.
        PixelRect result = WindowGeometry.ConstrainToAspect(new PixelRect(0, 300, 1614, 807), SizingEdge.TopLeft, 16.0 / 9, 14, 7);
        Assert.Equal(new PixelRect(0, 807 - 900 - 7, 1614, 807), result);
    }

    [Fact]
    public void ConstrainToAspect_BottomLeftCorner_TallerRequestMovesTheLeftEdge()
    {
        // Client 400x900 at 16:9: height drives, so width becomes 1600 and the left edge moves.
        PixelRect result = WindowGeometry.ConstrainToAspect(new PixelRect(1000, 0, 1414, 907), SizingEdge.BottomLeft, 16.0 / 9, 14, 7);
        Assert.Equal(new PixelRect(1414 - 1600 - 14, 0, 1414, 907), result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ConstrainToAspect_NoAspect_LeavesTheRectangle(double aspect)
    {
        PixelRect proposed = new(10, 20, 500, 400);
        Assert.Equal(proposed, WindowGeometry.ConstrainToAspect(proposed, SizingEdge.Right, aspect, 14, 7));
    }

    [Fact]
    public void ConstrainToAspect_DegenerateClient_LeavesTheRectangle()
    {
        PixelRect proposed = new(10, 20, 20, 25);
        Assert.Equal(proposed, WindowGeometry.ConstrainToAspect(proposed, SizingEdge.Right, 1.5, 14, 7));
    }
}
